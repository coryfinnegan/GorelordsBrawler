"""
Sprite Sheet Baker V2 — Blender Add-on
=======================================
Renders character animations directly to Nez sprite atlases (atlas.png +
atlas.atlas) in one step.

Workflow
--------
  1. Pick subject (armature), set character prefix and output directory.
  2. Assign weapon sockets and hurtbox zone empties.
  3. Select actions to bake (Add All auto-filters by armature).
  4. Click **Bake All** — each action gets its own atlas + sidecars,
     FaceLeft variants are generated automatically.

Install
-------
  Edit > Preferences > Add-ons > Install > point to this file.
  Configure the Packer Project path once (Settings panel) — it's the path to
  SpriteAtlasPacker.Console.csproj inside your Nez submodule.
"""

bl_info = {
    "name": "Sprite Sheet Baker V2",
    "author": "GorelordsBrawler Tools",
    "version": (4, 0, 0),
    "blender": (3, 0, 0),
    "location": "View3D > N Panel > Sprite Baker",
    "description": "Renders armature actions directly to Nez sprite atlases — one button bake",
    "category": "Render",
}

import json
import math
import os
import shutil
import subprocess
from statistics import mean

import bpy
import bpy_extras.object_utils
from bpy.props import (
    BoolProperty,
    CollectionProperty,
    EnumProperty,
    FloatProperty,
    IntProperty,
    PointerProperty,
    StringProperty,
)
from bpy.types import Operator, Panel, PropertyGroup, UIList


# ══════════════════════════════════════════════════════════════════════════════
# Animation types — the user picks one per action entry.
# Each type determines: atlas suffix, FaceLeft generation, socket hand, hurtbox tracking.
# ══════════════════════════════════════════════════════════════════════════════

# (identifier, label, description)  — identifier becomes the atlas suffix.
ANIM_TYPE_ITEMS = [
    ("Idle",                  "Idle",                         "Standing idle"),
    ("Run",                   "Run",                          "Run cycle"),
    ("Jump",                  "Jump",                         "Jump animation"),
    ("Fall",                  "Fall",                         "Falling animation"),
    ("Land",                  "Land",                         "Landing animation"),
    ("Hurt",                  "Hurt",                         "Hit reaction"),
    ("Death",                 "Death",                        "Death animation"),
    ("Select",                "Select",                       "Character-select screen pose"),
    ("AttackIdleLeftHand",    "Attack Idle — Left Hand",      "Attack from idle (left hand weapon)"),
    ("AttackIdleRightHand",   "Attack Idle — Right Hand",     "Attack from idle (right hand weapon)"),
    ("AttackRunLeftHand",     "Attack Run — Left Hand",       "Attack while running (left hand weapon)"),
    ("AttackRunRightHand",    "Attack Run — Right Hand",      "Attack while running (right hand weapon)"),
    ("AttackJumpLeftHand",    "Attack Jump — Left Hand",      "Attack while jumping (left hand weapon)"),
    ("AttackJumpRightHand",   "Attack Jump — Right Hand",     "Attack while jumping (right hand weapon)"),
]

# Types that auto-generate a FaceLeft atlas variant.
FACE_LEFT_TYPES = {
    "Idle", "Run", "Jump", "Fall", "Land",
    "Hurt", "Death",
    "AttackIdleLeftHand", "AttackIdleRightHand",
    "AttackRunLeftHand", "AttackRunRightHand",
    "AttackJumpLeftHand", "AttackJumpRightHand",
}

# Types that track a weapon socket (attack types).
SOCKET_TYPES = {
    "AttackIdleLeftHand", "AttackIdleRightHand",
    "AttackRunLeftHand", "AttackRunRightHand",
    "AttackJumpLeftHand", "AttackJumpRightHand",
}

# Types that use the RIGHT hand socket. Everything else in SOCKET_TYPES uses left.
RIGHT_HAND_TYPES = {
    "AttackIdleRightHand", "AttackRunRightHand", "AttackJumpRightHand",
}

# Types that skip hurtbox tracking.
SKIP_HURTBOX_TYPES = {"Select", "Hurt", "Death"}

# Zone definitions: (empty_name, props_bone_attr, props_pointer_attr)
_HURTBOX_ZONES = [
    ("HurtboxZone_Head", "hurtbox_head_bone", "hurtbox_head"),
    ("HurtboxZone_Body", "hurtbox_body_bone", "hurtbox_body"),
    ("HurtboxZone_Legs", "hurtbox_legs_bone", "hurtbox_legs"),
]


def _validate_action_bones(action, armature):
    """Check if an action's fcurves reference bones that exist on the armature.

    Returns (matched, total) counts. If matched == 0 the action is likely
    from a different rig.
    """
    if action is None or armature is None or armature.type != "ARMATURE":
        return 0, 0
    bone_names = {b.name for b in armature.data.bones}
    total = 0
    matched = 0
    for fc in action.fcurves:
        dp = fc.data_path
        if dp.startswith('pose.bones["'):
            end = dp.index('"]', 12)
            name = dp[12:end]
            total += 1
            if name in bone_names:
                matched += 1
    return matched, total


# ══════════════════════════════════════════════════════════════════════════════
# Property Groups
# ══════════════════════════════════════════════════════════════════════════════

class SPRITE_ActionItem(PropertyGroup):
    """One entry in the actions-to-bake list — a (type, action) pair."""
    anim_type: EnumProperty(
        name="Type",
        description="Animation type — determines atlas name, FaceLeft, socket, and hurtbox behavior",
        items=ANIM_TYPE_ITEMS,
        default="Idle",
    )
    action: PointerProperty(type=bpy.types.Action)
    enabled: BoolProperty(name="Enabled", default=True)
    fps: IntProperty(
        name="FPS",
        description="Frames per second for this animation (0 = use global FPS)",
        default=0,
        min=0,
        max=120,
    )
    start_frame: IntProperty(
        name="Start",
        description="Override start frame (0 = use action start)",
        default=0,
        min=0,
    )
    end_frame: IntProperty(
        name="End",
        description="Override end frame (0 = use action end)",
        default=0,
        min=0,
    )


class SPRITE_Props(PropertyGroup):
    """All Sprite Baker settings stored on the Scene."""

    # ── Subject ──────────────────────────────────────────────────────────────
    subject: PointerProperty(
        name="Subject",
        type=bpy.types.Object,
        description="Armature whose actions will be rendered",
    )
    actions: CollectionProperty(type=SPRITE_ActionItem)
    active_action_index: IntProperty(default=0)

    # ── Frame ─────────────────────────────────────────────────────────────────
    frame_width: IntProperty(
        name="W",
        default=512,
        min=1,
        description="Camera/render width in pixels",
    )
    frame_height: IntProperty(
        name="H",
        default=512,
        min=1,
        description="Camera/render height in pixels",
    )
    target_size: IntProperty(
        name="Target Height",
        default=0,
        min=0,
        description=(
            "Downscale output frames to this height before packing (0 = full render resolution). "
            "Width scales proportionally."
        ),
    )
    transparent: BoolProperty(
        name="Transparent Background",
        default=True,
        description="Render with alpha channel (RGBA PNG)",
    )

    # ── Atlas ─────────────────────────────────────────────────────────────────
    character_prefix: StringProperty(
        name="Character Prefix",
        default="",
        description="Prepended to every atlas filename and animation key (e.g. 'FutureAxe')",
    )
    output_dir: StringProperty(
        name="Output Dir",
        subtype="DIR_PATH",
        default="//sprites/",
        description="Folder where the atlas files are written",
    )
    fps: IntProperty(
        name="FPS",
        default=8,
        min=1,
        max=120,
        description="Default animation framerate (per-action FPS overrides this)",
    )
    padding: IntProperty(
        name="Padding",
        default=2,
        min=0,
        description="Pixels of padding between packed sprites",
    )
    origin_x: FloatProperty(
        name="Origin X",
        default=0.5,
        min=0.0,
        max=1.0,
        description="Sprite origin X (0 = left, 0.5 = center, 1 = right)",
    )
    origin_y: FloatProperty(
        name="Origin Y",
        default=1.0,
        min=0.0,
        max=1.0,
        description="Sprite origin Y (0 = top, 1.0 = bottom)",
    )

    # ── Weapon Sockets ────────────────────────────────────────────────────────
    socket_left: PointerProperty(
        name="Left Hand Socket",
        type=bpy.types.Object,
        description="Empty parented to the left weapon hand bone",
    )
    socket_right: PointerProperty(
        name="Right Hand Socket",
        type=bpy.types.Object,
        description="Empty parented to the right weapon hand bone",
    )
    socket_left_bone: StringProperty(
        name="Left Bone",
        default="mixamorig:LeftHand",
        description="Bone the WeaponSocketLeft empty will be parented to",
    )
    socket_right_bone: StringProperty(
        name="Right Bone",
        default="mixamorig:RightHand",
        description="Bone the WeaponSocketRight empty will be parented to",
    )

    # ── Hurtbox Zones ─────────────────────────────────────────────────────────
    hurtbox_head: PointerProperty(
        name="Head Zone",
        type=bpy.types.Object,
        description="Empty parented to the head/neck bone — tracks the head hurtbox center",
    )
    hurtbox_body: PointerProperty(
        name="Body Zone",
        type=bpy.types.Object,
        description="Empty parented to the spine/chest bone — tracks the torso hurtbox center",
    )
    hurtbox_legs: PointerProperty(
        name="Legs Zone",
        type=bpy.types.Object,
        description="Empty parented to the hips/pelvis bone — tracks the legs hurtbox center",
    )
    hurtbox_head_bone: StringProperty(
        name="Head Bone",
        default="mixamorig:Head",
        description="Bone the HurtboxZone_Head empty will be parented to",
    )
    hurtbox_body_bone: StringProperty(
        name="Body Bone",
        default="mixamorig:Spine1",
        description="Bone the HurtboxZone_Body empty will be parented to",
    )
    hurtbox_legs_bone: StringProperty(
        name="Legs Bone",
        default="mixamorig:Hips",
        description="Bone the HurtboxZone_Legs empty will be parented to",
    )

    # ── Packer ────────────────────────────────────────────────────────────────
    packer_project: StringProperty(
        name="Packer Project",
        subtype="FILE_PATH",
        default="",
        description="Path to SpriteAtlasPacker.Console.csproj (inside Nez submodule)",
    )
    max_atlas_size: IntProperty(
        name="Max Atlas Size",
        default=8192,
        min=512,
        max=16384,
        description="Maximum width and height of the packed atlas in pixels",
    )


# ══════════════════════════════════════════════════════════════════════════════
# UI List
# ══════════════════════════════════════════════════════════════════════════════

class SPRITE_UL_ActionList(UIList):
    def draw_item(self, context, layout, data, item, icon, active_data, active_propname):
        row = layout.row(align=True)
        row.prop(item, "enabled", text="")
        if item.action:
            props = context.scene.sprite_baker
            char_prefix = props.character_prefix.strip()
            suffix = item.anim_type
            label = f"{char_prefix}_{suffix}" if char_prefix else suffix
            type_tag = f"  [{item.action.name}]"
            fps_str = f"  [{item.fps} fps]" if item.fps > 0 else ""
            frames_str = f"  [f{item.start_frame}-{item.end_frame}]" if item.start_frame > 0 or item.end_frame > 0 else ""
            # Warn if action bones don't match subject armature
            armature = props.subject
            warn_icon = "ACTION"
            if armature and armature.type == "ARMATURE":
                matched, total = _validate_action_bones(item.action, armature)
                if total > 0 and matched == 0:
                    warn_icon = "ERROR"
            row.label(text=label + type_tag + fps_str + frames_str, icon=warn_icon)
        else:
            row.label(text="(empty)", icon="ERROR")


# ══════════════════════════════════════════════════════════════════════════════
# Operators — action list management
# ══════════════════════════════════════════════════════════════════════════════

class SPRITE_OT_AddAction(Operator):
    bl_idname = "sprite.add_action"
    bl_label = "Add Action"
    bl_description = "Add a slot; defaults to the subject's current action"

    def execute(self, context):
        props = context.scene.sprite_baker
        item = props.actions.add()
        obj = props.subject
        if obj and obj.animation_data and obj.animation_data.action:
            item.action = obj.animation_data.action
        props.active_action_index = len(props.actions) - 1
        return {"FINISHED"}


class SPRITE_OT_RemoveAction(Operator):
    bl_idname = "sprite.remove_action"
    bl_label = "Remove Action"
    bl_description = "Remove the selected entry from the bake list"

    def execute(self, context):
        props = context.scene.sprite_baker
        if props.actions and 0 <= props.active_action_index < len(props.actions):
            props.actions.remove(props.active_action_index)
            props.active_action_index = max(0, props.active_action_index - 1)
        return {"FINISHED"}


class SPRITE_OT_AddAllActions(Operator):
    bl_idname = "sprite.add_all_actions"
    bl_label = "Add All Actions"
    bl_description = (
        "Add every compatible action to the bake list. "
        "Actions whose bones don't match the subject armature are skipped."
    )

    def execute(self, context):
        props = context.scene.sprite_baker
        armature = props.subject
        existing = {item.action for item in props.actions if item.action}
        added = 0
        skipped = 0
        for action in sorted(bpy.data.actions, key=lambda a: a.name):
            if action not in existing:
                if armature and armature.type == "ARMATURE":
                    matched, total = _validate_action_bones(action, armature)
                    if total > 0 and matched == 0:
                        skipped += 1
                        continue
                item = props.actions.add()
                item.action = action
                added += 1
        msg = f"Added {added} action(s)"
        if skipped > 0:
            msg += f", skipped {skipped} (wrong armature)"
        self.report({"INFO"}, msg)
        return {"FINISHED"}


class SPRITE_OT_ClearActions(Operator):
    bl_idname = "sprite.clear_actions"
    bl_label = "Clear List"
    bl_description = "Remove all entries from the bake list"

    def execute(self, context):
        context.scene.sprite_baker.actions.clear()
        return {"FINISHED"}


class SPRITE_OT_SelectAllActions(Operator):
    bl_idname = "sprite.select_all_actions"
    bl_label = "Select All"
    bl_description = "Enable all actions in the bake list"

    def execute(self, context):
        for item in context.scene.sprite_baker.actions:
            item.enabled = True
        return {"FINISHED"}


class SPRITE_OT_DeselectAllActions(Operator):
    bl_idname = "sprite.deselect_all_actions"
    bl_label = "Deselect All"
    bl_description = "Disable all actions in the bake list"

    def execute(self, context):
        for item in context.scene.sprite_baker.actions:
            item.enabled = False
        return {"FINISHED"}


# ══════════════════════════════════════════════════════════════════════════════
# Operator — auto-detect packer
# ══════════════════════════════════════════════════════════════════════════════

class SPRITE_OT_DetectPacker(Operator):
    bl_idname = "sprite.detect_packer"
    bl_label = "Auto-detect"
    bl_description = "Search for SpriteAtlasPacker.Console.csproj near the blend file"

    def execute(self, context):
        props = context.scene.sprite_baker
        blend_dir = bpy.path.abspath("//")
        found = self._find_packer(blend_dir)
        if found:
            props.packer_project = found
            self.report({"INFO"}, f"Found: {found}")
        else:
            self.report({"WARNING"}, "Packer not found — set the path manually")
        return {"FINISHED"}

    def _find_packer(self, start_dir):
        target = "SpriteAtlasPacker.Console.csproj"
        path = start_dir
        for _ in range(8):
            csproj = self._search_down(path, target)
            if csproj:
                return csproj
            parent = os.path.dirname(path)
            if parent == path:
                break
            path = parent
        return None

    def _search_down(self, root, filename):
        for dirpath, dirnames, filenames in os.walk(root):
            if filename in filenames:
                return os.path.join(dirpath, filename)
            dirnames[:] = [d for d in dirnames if not d.startswith(".")]
        return None


# ══════════════════════════════════════════════════════════════════════════════
# Operator — setup weapon sockets
# ══════════════════════════════════════════════════════════════════════════════

class SPRITE_OT_SetupWeaponSockets(Operator):
    bl_idname = "sprite.setup_weapon_sockets"
    bl_label = "Setup Both Sockets"
    bl_description = (
        "Create 'WeaponSocketLeft' and 'WeaponSocketRight' Empties, parent each "
        "to the specified hand bone, and assign them to the socket fields"
    )

    def execute(self, context):
        props = context.scene.sprite_baker
        armature = props.subject

        if armature is None:
            self.report({"ERROR"}, "Set the Subject armature first")
            return {"CANCELLED"}

        if context.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")

        left_ok = self._setup_socket(context, armature, "WeaponSocketLeft", props.socket_left_bone)
        right_ok = self._setup_socket(context, armature, "WeaponSocketRight", props.socket_right_bone)

        if left_ok:
            props.socket_left = bpy.data.objects["WeaponSocketLeft"]
        if right_ok:
            props.socket_right = bpy.data.objects["WeaponSocketRight"]

        if left_ok and right_ok:
            self.report({"INFO"}, "Both sockets created — position them at the weapon tips")
        elif left_ok or right_ok:
            self.report({"WARNING"}, "One socket failed — check bone names")
        else:
            return {"CANCELLED"}

        return {"FINISHED"}

    def _setup_socket(self, context, armature, empty_name, bone_name):
        bone_name = bone_name.strip()
        if not bone_name:
            self.report({"ERROR"}, f"Bone name is empty for {empty_name}")
            return False
        if bone_name not in armature.data.bones:
            self.report({"ERROR"}, f"Bone '{bone_name}' not found in '{armature.name}' for {empty_name}")
            return False

        empty = bpy.data.objects.get(empty_name)
        if empty is None:
            pose_bone = armature.pose.bones[bone_name]
            bone_head_world = armature.matrix_world @ pose_bone.head
            bpy.ops.object.select_all(action="DESELECT")
            bpy.ops.object.empty_add(type="PLAIN_AXES", location=bone_head_world)
            empty = context.active_object
            empty.name = empty_name
            empty.empty_display_size = 0.05

        bpy.ops.object.select_all(action="DESELECT")
        empty.select_set(True)
        armature.select_set(True)
        context.view_layer.objects.active = armature
        armature.data.bones.active = armature.data.bones[bone_name]
        bpy.ops.object.parent_set(type="BONE")

        bpy.ops.object.select_all(action="DESELECT")
        empty.select_set(True)
        context.view_layer.objects.active = empty
        return True


# ══════════════════════════════════════════════════════════════════════════════
# Operator — fix action links
# ══════════════════════════════════════════════════════════════════════════════

class SPRITE_OT_FixActionLinks(Operator):
    bl_idname = "sprite.fix_action_links"
    bl_label = "Fix Action Links"
    bl_description = (
        "Link all compatible actions to the subject armature via NLA stash tracks. "
        "Fixes actions imported with duplicate armatures (Armature.001, etc.) so "
        "they appear in the action browser for your main armature"
    )

    def execute(self, context):
        props = context.scene.sprite_baker
        armature = props.subject

        if armature is None:
            self.report({"ERROR"}, "Set the Subject armature first")
            return {"CANCELLED"}

        if armature.type != "ARMATURE":
            self.report({"ERROR"}, f"'{armature.name}' is not an armature")
            return {"CANCELLED"}

        # Ensure animation data exists on the armature
        if armature.animation_data is None:
            armature.animation_data_create()

        # Remember the current action so we can restore it at the end
        original_action = armature.animation_data.action

        # Collect names of actions already stashed on this armature
        already_stashed = set()
        for track in armature.animation_data.nla_tracks:
            for strip in track.strips:
                if strip.action:
                    already_stashed.add(strip.action.name)
        # Also count the currently assigned action
        if original_action:
            already_stashed.add(original_action.name)

        fixed = 0
        skipped = 0

        for action in bpy.data.actions:
            # Skip actions already linked to this armature
            if action.name in already_stashed:
                continue

            matched, total = _validate_action_bones(action, armature)
            if total == 0:
                # No bone fcurves — not a pose action
                continue
            if matched == 0:
                # Bones don't match this armature at all
                skipped += 1
                continue

            # 1. Set fake user so the action never gets garbage collected
            action.use_fake_user = True

            # 2. Stash into a muted NLA track — this creates a persistent
            #    link from the armature to the action so it shows up in the
            #    action browser dropdown.
            track = armature.animation_data.nla_tracks.new()
            track.name = f"[Stash] {action.name}"
            track.mute = True  # stashed tracks don't interfere with playback
            start_frame = int(action.frame_range[0])
            strip = track.strips.new(action.name, start_frame, action)
            strip.action = action

            fixed += 1

        # Restore the originally active action
        armature.animation_data.action = original_action

        msg = f"Linked {fixed} action(s) to '{armature.name}' via NLA stash"
        if skipped > 0:
            msg += f", skipped {skipped} (different rig)"
        self.report({"INFO"}, msg)
        return {"FINISHED"}


# ══════════════════════════════════════════════════════════════════════════════
# Operator — setup hurtbox zones
# ══════════════════════════════════════════════════════════════════════════════

class SPRITE_OT_SetupHurtboxZones(Operator):
    bl_idname = "sprite.setup_hurtbox_zones"
    bl_label = "Setup Hurtbox Zones"
    bl_description = (
        "Create HurtboxZone_Head, HurtboxZone_Body, and HurtboxZone_Legs Empties, "
        "parent each to the specified bone, and assign them to the zone fields"
    )

    def execute(self, context):
        props = context.scene.sprite_baker
        armature = props.subject

        if armature is None:
            self.report({"ERROR"}, "Set the Subject armature first")
            return {"CANCELLED"}

        if context.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")

        ok_count = 0
        for empty_name, bone_attr, pointer_attr in _HURTBOX_ZONES:
            bone_name = getattr(props, bone_attr)
            if self._setup_zone(context, armature, empty_name, bone_name):
                setattr(props, pointer_attr, bpy.data.objects[empty_name])
                ok_count += 1

        if ok_count == len(_HURTBOX_ZONES):
            self.report({"INFO"}, f"All {ok_count} hurtbox zone empties created and parented")
        elif ok_count > 0:
            self.report({"WARNING"}, f"{ok_count}/{len(_HURTBOX_ZONES)} zones created — check bone names")
        else:
            self.report({"ERROR"}, "No zones created — check bone names")
            return {"CANCELLED"}

        return {"FINISHED"}

    def _setup_zone(self, context, armature, empty_name, bone_name):
        bone_name = bone_name.strip()
        if not bone_name:
            self.report({"ERROR"}, f"Bone name is empty for {empty_name}")
            return False
        if bone_name not in armature.data.bones:
            self.report({"ERROR"}, f"Bone '{bone_name}' not found in '{armature.name}' for {empty_name}")
            return False

        empty = bpy.data.objects.get(empty_name)
        if empty is None:
            pose_bone = armature.pose.bones[bone_name]
            bone_head_world = armature.matrix_world @ pose_bone.head
            bpy.ops.object.select_all(action="DESELECT")
            bpy.ops.object.empty_add(type="PLAIN_AXES", location=bone_head_world)
            empty = context.active_object
            empty.name = empty_name
            empty.empty_display_size = 0.08

        bpy.ops.object.select_all(action="DESELECT")
        empty.select_set(True)
        armature.select_set(True)
        context.view_layer.objects.active = armature
        armature.data.bones.active = armature.data.bones[bone_name]
        bpy.ops.object.parent_set(type="BONE")

        bpy.ops.object.select_all(action="DESELECT")
        empty.select_set(True)
        context.view_layer.objects.active = empty
        return True


# ══════════════════════════════════════════════════════════════════════════════
# Operator — Bake All (V2 one-button bake)
# ══════════════════════════════════════════════════════════════════════════════

class SPRITE_OT_BakeAll(Operator):
    """Bake all enabled actions — each action produces its own atlas + sidecars."""
    bl_idname = "sprite.bake_all"
    bl_label = "Bake All"
    bl_description = (
        "Render all enabled actions. Each action gets its own atlas file. "
        "FaceLeft variants, socket tracking, and hurtbox zones are handled automatically."
    )

    def execute(self, context):
        props = context.scene.sprite_baker
        scene = context.scene
        render = scene.render
        obj = props.subject

        errors = self._validate(props, obj)
        if errors:
            for e in errors:
                self.report({"ERROR"}, e)
            return {"CANCELLED"}

        char_prefix = props.character_prefix.strip()
        output_dir = bpy.path.abspath(props.output_dir)
        os.makedirs(output_dir, exist_ok=True)

        enabled_items = [i for i in props.actions if i.enabled and i.action]

        # Save original render settings
        orig = {
            "frame":       scene.frame_current,
            "filepath":    render.filepath,
            "res_x":       render.resolution_x,
            "res_y":       render.resolution_y,
            "res_pct":     render.resolution_percentage,
            "format":      render.image_settings.file_format,
            "color_mode":  render.image_settings.color_mode,
            "transparent": render.film_transparent,
            "use_ext":     render.use_file_extension,
            "action":      obj.animation_data.action,
        }

        if props.target_size > 0:
            render.resolution_y = props.target_size
            render.resolution_x = round(props.target_size * props.frame_width / props.frame_height)
        else:
            render.resolution_x = props.frame_width
            render.resolution_y = props.frame_height
        render.resolution_percentage = 100
        render.image_settings.file_format = "PNG"
        render.image_settings.color_mode = "RGBA"
        render.film_transparent = props.transparent
        render.use_file_extension = True

        baked_res_x = render.resolution_x
        baked_res_y = render.resolution_y

        # Compute zone sizes once from the first action that has all 3 zones
        computed_zone_sizes = None
        total_actions = len(enabled_items)
        failed = 0

        try:
            for action_idx, item in enumerate(enabled_items):
                action = item.action
                anim_type = item.anim_type  # explicit type chosen by user
                atlas_name = f"{char_prefix}_{anim_type}" if char_prefix else anim_type
                anim_fps = item.fps if item.fps > 0 else props.fps

                self.report({"INFO"}, f"[{action_idx + 1}/{total_actions}] Baking '{atlas_name}' ({action.name})...")

                # Determine socket object from the type
                track_socket = anim_type in SOCKET_TYPES
                socket_obj = None
                if track_socket:
                    if anim_type in RIGHT_HAND_TYPES:
                        socket_obj = props.socket_right
                    else:
                        socket_obj = props.socket_left

                track_hurtboxes = anim_type not in SKIP_HURTBOX_TYPES

                # Collect hurtbox empties
                hurtbox_empties = []
                if track_hurtboxes:
                    for empty_name, _bone_attr, pointer_attr in _HURTBOX_ZONES:
                        zone_obj = getattr(props, pointer_attr)
                        if zone_obj is not None:
                            zone_name = empty_name.replace("HurtboxZone_", "")
                            hurtbox_empties.append((zone_name, zone_obj))

                # Create temp dirs for this action
                tmp_dir = os.path.join(output_dir, f"_tmp_{atlas_name}")
                if os.path.exists(tmp_dir):
                    shutil.rmtree(tmp_dir)
                os.makedirs(tmp_dir)

                try:
                    # Render face-right
                    socket_positions, hurtbox_zones, origin_x = self._render_action(
                        context, obj, action, atlas_name, tmp_dir,
                        socket_obj=socket_obj,
                        track_socket=track_socket,
                        hurtbox_empties=hurtbox_empties,
                        track_hurtboxes=track_hurtboxes,
                        start_frame_override=item.start_frame,
                        end_frame_override=item.end_frame,
                    )

                    # Compute zone sizes from distances on first valid action
                    if computed_zone_sizes is None and hurtbox_zones:
                        computed_zone_sizes = self._compute_zone_sizes_from_distances(hurtbox_zones)

                    # Write sidecars
                    sockets_dict = {atlas_name: socket_positions}
                    self._write_sockets_sidecar(output_dir, atlas_name, baked_res_x, baked_res_y, sockets_dict)

                    hurtboxes_dict = {}
                    if hurtbox_zones:
                        hurtboxes_dict[atlas_name] = hurtbox_zones
                    self._write_hurtboxes_sidecar(
                        output_dir, atlas_name, baked_res_x, baked_res_y,
                        hurtboxes_dict, origin_x, computed_zone_sizes)

                    # Pack atlas
                    atlas_png = os.path.join(output_dir, f"{atlas_name}.png")
                    atlas_map = os.path.join(output_dir, f"{atlas_name}.atlas")
                    packer_ok = self._call_packer(
                        bpy.path.abspath(props.packer_project),
                        tmp_dir, atlas_png, atlas_map, anim_fps, props)
                    if not packer_ok:
                        self.report({"WARNING"}, f"Packer failed for: {atlas_name}")
                        failed += 1

                    # Render FaceLeft variant if applicable
                    if anim_type in FACE_LEFT_TYPES:
                        fl_atlas_name = f"{atlas_name}FaceLeft"
                        fl_tmp_dir = os.path.join(output_dir, f"_tmp_{fl_atlas_name}")
                        if os.path.exists(fl_tmp_dir):
                            shutil.rmtree(fl_tmp_dir)
                        os.makedirs(fl_tmp_dir)

                        self.report({"INFO"}, f"  → FaceLeft variant: {fl_atlas_name}")
                        fl_positions, _fl_zones, _fl_origin = self._render_action(
                            context, obj, action, fl_atlas_name, fl_tmp_dir,
                            socket_obj=None,
                            track_socket=False,
                            hurtbox_empties=[],
                            track_hurtboxes=False,
                            rotation_z_offset=math.pi,
                            start_frame_override=item.start_frame,
                            end_frame_override=item.end_frame,
                        )

                        fl_png = os.path.join(output_dir, f"{fl_atlas_name}.png")
                        fl_map = os.path.join(output_dir, f"{fl_atlas_name}.atlas")
                        fl_ok = self._call_packer(
                            bpy.path.abspath(props.packer_project),
                            fl_tmp_dir, fl_png, fl_map, anim_fps, props)
                        if not fl_ok:
                            self.report({"WARNING"}, f"Packer failed for: {fl_atlas_name}")
                            failed += 1

                        shutil.rmtree(fl_tmp_dir, ignore_errors=True)

                except Exception as exc:
                    self.report({"WARNING"}, f"Failed to bake '{atlas_name}': {exc}")
                    failed += 1
                finally:
                    shutil.rmtree(tmp_dir, ignore_errors=True)

        finally:
            scene.frame_set(orig["frame"])
            render.filepath = orig["filepath"]
            render.resolution_x = orig["res_x"]
            render.resolution_y = orig["res_y"]
            render.resolution_percentage = orig["res_pct"]
            render.image_settings.file_format = orig["format"]
            render.image_settings.color_mode = orig["color_mode"]
            render.film_transparent = orig["transparent"]
            render.use_file_extension = orig["use_ext"]
            obj.animation_data.action = orig["action"]

        if failed == 0:
            self.report({"INFO"}, f"All {total_actions} action(s) baked successfully → {output_dir}")
        else:
            self.report({"WARNING"}, f"{total_actions - failed}/{total_actions} succeeded, {failed} failed")

        return {"FINISHED"}

    # ── Validation ────────────────────────────────────────────────────────────

    def _validate(self, props, obj):
        errors = []
        if obj is None:
            errors.append("No subject object selected")
        elif obj.animation_data is None:
            errors.append("Subject has no animation data")

        if not any(i.enabled and i.action for i in props.actions):
            errors.append("No enabled actions in the bake list")

        if not props.packer_project:
            errors.append("Packer Project path is not set (Settings panel)")
        elif not os.path.exists(bpy.path.abspath(props.packer_project)):
            errors.append(f"Packer project not found: {props.packer_project}")

        return errors

    # ── Core render ───────────────────────────────────────────────────────────

    def _render_action(self, context, obj, action, anim_name, tmp_dir,
                       socket_obj=None, track_socket=True,
                       hurtbox_empties=None, track_hurtboxes=True,
                       rotation_z_offset=0.0,
                       start_frame_override=0, end_frame_override=0):
        """Render each frame of an action to tmp_dir/{anim_name}/frame_NNNN.png.

        Returns (socket_positions, hurtbox_zones, origin_x).
        """
        if hurtbox_empties is None:
            hurtbox_empties = []

        scene = context.scene
        render = scene.render

        obj.animation_data.action = action
        frame_start = start_frame_override if start_frame_override > 0 else int(action.frame_range[0])
        frame_end = end_frame_override if end_frame_override > 0 else int(action.frame_range[1])

        anim_dir = os.path.join(tmp_dir, anim_name)
        os.makedirs(anim_dir, exist_ok=True)

        socket_positions = []
        origin_x = None
        hurtbox_zones = {name: [] for name, _ in hurtbox_empties}

        for idx, frame_num in enumerate(range(frame_start, frame_end + 1)):
            scene.frame_set(frame_num)

            if rotation_z_offset != 0.0:
                obj.rotation_euler[2] += rotation_z_offset
                context.view_layer.update()

            depsgraph = context.evaluated_depsgraph_get() if (
                (track_socket and socket_obj is not None) or hurtbox_empties
            ) and scene.camera is not None else None

            # Record weapon socket position
            if track_socket and socket_obj is not None and depsgraph is not None:
                socket_eval = socket_obj.evaluated_get(depsgraph)
                ndc = bpy_extras.object_utils.world_to_camera_view(
                    scene, scene.camera, socket_eval.matrix_world.translation
                )
                if ndc.z > 0.0:
                    px = ndc.x * render.resolution_x
                    py = (1.0 - ndc.y) * render.resolution_y
                    socket_positions.append([round(px, 2), round(py, 2)])
                else:
                    socket_positions.append(None)
            else:
                socket_positions.append(None)

            # Project armature origin on first frame (for FaceLeft mirror pivot)
            if origin_x is None and depsgraph is not None and track_hurtboxes and hurtbox_empties:
                obj_eval = obj.evaluated_get(depsgraph)
                origin_ndc = bpy_extras.object_utils.world_to_camera_view(
                    scene, scene.camera, obj_eval.matrix_world.translation)
                if origin_ndc.z > 0.0:
                    origin_x = round(origin_ndc.x * render.resolution_x, 2)

            # Record hurtbox zone positions
            if depsgraph is not None:
                for zone_name, zone_obj in hurtbox_empties:
                    zone_eval = zone_obj.evaluated_get(depsgraph)
                    ndc = bpy_extras.object_utils.world_to_camera_view(
                        scene, scene.camera, zone_eval.matrix_world.translation
                    )
                    if ndc.z > 0.0:
                        px = ndc.x * render.resolution_x
                        py = (1.0 - ndc.y) * render.resolution_y
                        hurtbox_zones[zone_name].append([round(px, 2), round(py, 2)])
                    else:
                        hurtbox_zones[zone_name].append(None)
            else:
                for zone_name, _ in hurtbox_empties:
                    hurtbox_zones[zone_name].append(None)

            render.filepath = os.path.join(anim_dir, f"{anim_name}_{idx:04d}")
            bpy.ops.render.render(write_still=True)

            if rotation_z_offset != 0.0:
                obj.rotation_euler[2] -= rotation_z_offset

        frame_count = frame_end - frame_start + 1
        print(f"  {anim_name}: {frame_count} frames → {anim_dir}")
        return socket_positions, hurtbox_zones, origin_x

    # ── Zone sizing ───────────────────────────────────────────────────────────

    def _compute_zone_sizes_from_distances(self, hurtbox_zones):
        """Derive zone pixel dimensions from average distances between zone centers.

        Uses inter-zone Y distances to determine proportional sizes:
        - Head height = avg distance from head to body center
        - Body height = 80% of head-to-body distance (compact torso)
        - Legs height = 2.5x body-to-legs distance (extends to feet)
        - Widths are proportional to heights.
        """
        head_positions = hurtbox_zones.get("Head", [])
        body_positions = hurtbox_zones.get("Body", [])
        legs_positions = hurtbox_zones.get("Legs", [])

        if not head_positions or not body_positions or not legs_positions:
            print("  Zone sizing: not all 3 zones present — skipping auto-size")
            return None

        head_body_dists = []
        body_legs_dists = []

        num_frames = min(len(head_positions), len(body_positions), len(legs_positions))
        for i in range(num_frames):
            h = head_positions[i]
            b = body_positions[i]
            l = legs_positions[i]
            if h is not None and b is not None:
                head_body_dists.append(b[1] - h[1])
            if b is not None and l is not None:
                body_legs_dists.append(l[1] - b[1])

        if not head_body_dists or not body_legs_dists:
            print("  Zone sizing: insufficient frame data — skipping auto-size")
            return None

        avg_hb = mean(head_body_dists)
        avg_bl = mean(body_legs_dists)

        head_h = max(avg_hb, 10)
        body_h = max(avg_hb * 0.8, 10)
        legs_h = max(avg_bl * 2.0, 10)

        head_w = head_h * 0.75
        body_w = body_h * 1.2
        legs_w = legs_h * 0.7

        result = {
            "Head": {"width": round(head_w), "height": round(head_h)},
            "Body": {"width": round(body_w), "height": round(body_h)},
            "Legs": {"width": round(legs_w), "height": round(legs_h)},
        }

        for name, dims in result.items():
            print(f"  Auto zone size — {name}: {dims['width']}×{dims['height']} px")
        return result

    # ── Sidecar writers ───────────────────────────────────────────────────────

    def _write_sockets_sidecar(self, output_dir, base_name, res_x, res_y, sockets_dict):
        """Write a .sockets.json sidecar if any socket positions exist."""
        socket_anims = {
            name: positions
            for name, positions in sockets_dict.items()
            if any(p is not None for p in positions)
        }
        if not socket_anims:
            return
        sockets_data = {
            "frame_width":  res_x,
            "frame_height": res_y,
            "animations":   socket_anims,
        }
        sockets_path = os.path.join(output_dir, f"{base_name}.sockets.json")
        with open(sockets_path, "w") as f:
            json.dump(sockets_data, f, indent=2)
        print(f"  Socket sidecar written: {sockets_path}")

    def _write_hurtboxes_sidecar(self, output_dir, base_name, res_x, res_y,
                                 hurtboxes_dict, origin_x=None,
                                 zone_sizes=None):
        """Write a .hurtboxes.json sidecar for per-frame hurtbox zone positions."""
        hurtbox_anims = {}
        for anim_name, zones in hurtboxes_dict.items():
            filtered = {
                zone: positions
                for zone, positions in zones.items()
                if any(p is not None for p in positions)
            }
            if filtered:
                hurtbox_anims[anim_name] = filtered
        if not hurtbox_anims:
            return

        # Build zone size metadata from auto-computed sizes
        zones_meta = {}
        if zone_sizes:
            for zone_name, dims in zone_sizes.items():
                has_data = any(zone_name in zones for zones in hurtbox_anims.values())
                if has_data:
                    zones_meta[zone_name] = dims
        else:
            # Fallback: use sensible defaults
            for zone_name in ("Head", "Body", "Legs"):
                has_data = any(zone_name in zones for zones in hurtbox_anims.values())
                if has_data:
                    zones_meta[zone_name] = {"width": 24, "height": 24}

        hurtboxes_data = {
            "frame_width": res_x,
            "frame_height": res_y,
            "zones": zones_meta,
            "animations": hurtbox_anims,
        }
        if origin_x is not None:
            hurtboxes_data["origin_x"] = origin_x
        hurtboxes_path = os.path.join(output_dir, f"{base_name}.hurtboxes.json")
        with open(hurtboxes_path, "w") as f:
            json.dump(hurtboxes_data, f, indent=2)
        print(f"  Hurtbox sidecar written: {hurtboxes_path}")

    # ── Packer ────────────────────────────────────────────────────────────────

    def _call_packer(self, packer_project, frames_dir, atlas_png, atlas_map, fps, props):
        max_size = props.max_atlas_size
        cmd = [
            "dotnet", "run",
            "--project", packer_project,
            "--no-launch-profile",
            "--",
            f"-image:{atlas_png}",
            f"-map:{atlas_map}",
            f"-fps:{fps}",
            f"-pad:{props.padding}",
            f"-originX:{props.origin_x}",
            f"-originY:{props.origin_y}",
            f"-mw:{max_size}",
            f"-mh:{max_size}",
            frames_dir,
        ]
        print(f"\n  Running packer: {' '.join(cmd)}")
        result = subprocess.run(cmd, capture_output=True, text=True)

        if result.stdout.strip():
            print(f"  Packer stdout:\n    {result.stdout.strip()}")
        if result.stderr.strip():
            print(f"  Packer stderr:\n    {result.stderr.strip()}")

        if result.returncode != 0 or not os.path.exists(atlas_png):
            print(f"  Packer exit code: {result.returncode}")
            print(f"  Atlas PNG exists: {os.path.exists(atlas_png)}")
            return False

        size_kb = os.path.getsize(atlas_png) / 1024
        print(f"  Atlas: {size_kb:.0f} KB → {atlas_png}")
        return True


# ══════════════════════════════════════════════════════════════════════════════
# Panels
# ══════════════════════════════════════════════════════════════════════════════

class _Base:
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Sprite Baker"


class SPRITE_PT_CharacterSetup(_Base, Panel):
    bl_label = "Character Setup"
    bl_idname = "SPRITE_PT_CharacterSetup"

    def draw(self, context):
        layout = self.layout
        props = context.scene.sprite_baker

        layout.prop(props, "subject")
        layout.prop(props, "character_prefix")
        layout.prop(props, "output_dir")

        row = layout.row(align=True)
        row.prop(props, "frame_width")
        row.prop(props, "frame_height")

        layout.prop(props, "target_size")
        if props.target_size > 0:
            out_w = round(props.target_size * props.frame_width / props.frame_height)
            layout.label(text=f"Output: {out_w}×{props.target_size} px per frame", icon="INFO")

        layout.prop(props, "transparent")

        col = layout.column(align=True)
        col.prop(props, "fps")
        col.prop(props, "padding")

        row = layout.row(align=True)
        row.prop(props, "origin_x")
        row.prop(props, "origin_y")

        layout.prop(props, "max_atlas_size")

        layout.separator()
        layout.label(text="Nez SpriteAtlasPacker:")
        row = layout.row(align=True)
        row.prop(props, "packer_project", text="")
        row.operator("sprite.detect_packer", text="", icon="VIEWZOOM")

        if props.packer_project:
            path = bpy.path.abspath(props.packer_project)
            if not os.path.exists(path):
                layout.label(text="Packer not found at this path", icon="ERROR")
            else:
                layout.label(text="Packer found", icon="CHECKMARK")


class SPRITE_PT_Sockets(_Base, Panel):
    bl_label = "Sockets"
    bl_idname = "SPRITE_PT_Sockets"

    def draw(self, context):
        layout = self.layout
        props = context.scene.sprite_baker

        row = layout.row(align=True)
        row.prop(props, "socket_right", text="Right Hand")
        row = layout.row(align=True)
        row.prop(props, "socket_left", text="Left Hand")
        col = layout.column(align=True)
        col.prop(props, "socket_right_bone", text="R Bone")
        col.prop(props, "socket_left_bone", text="L Bone")
        layout.operator("sprite.setup_weapon_sockets", text="Setup Both Sockets", icon="BONE_DATA")


class SPRITE_PT_HurtboxZones(_Base, Panel):
    bl_label = "Hurtbox Zones"
    bl_idname = "SPRITE_PT_HurtboxZones"

    def draw(self, context):
        layout = self.layout
        props = context.scene.sprite_baker

        row = layout.row(align=True)
        row.prop(props, "hurtbox_head", text="Head")
        row = layout.row(align=True)
        row.prop(props, "hurtbox_body", text="Body")
        row = layout.row(align=True)
        row.prop(props, "hurtbox_legs", text="Legs")
        col = layout.column(align=True)
        col.prop(props, "hurtbox_head_bone", text="Head Bone")
        col.prop(props, "hurtbox_body_bone", text="Body Bone")
        col.prop(props, "hurtbox_legs_bone", text="Legs Bone")
        layout.operator("sprite.setup_hurtbox_zones", text="Setup Hurtbox Zones", icon="BONE_DATA")
        layout.label(text="Zone sizes are auto-computed from distances", icon="INFO")


class SPRITE_PT_Actions(_Base, Panel):
    bl_label = "Actions"
    bl_idname = "SPRITE_PT_Actions"

    def draw(self, context):
        layout = self.layout
        props = context.scene.sprite_baker

        if props.subject and props.subject.type == "ARMATURE":
            layout.operator("sprite.fix_action_links", icon="LINK_BLEND")

        layout.separator()

        row = layout.row()
        row.template_list(
            "SPRITE_UL_ActionList", "",
            props, "actions",
            props, "active_action_index",
            rows=8,
        )
        col = row.column(align=True)
        col.operator("sprite.add_action", text="", icon="ADD")
        col.operator("sprite.remove_action", text="", icon="REMOVE")
        col.separator()
        col.operator("sprite.add_all_actions", text="", icon="LINENUMBERS_ON")
        col.operator("sprite.clear_actions", text="", icon="TRASH")

        row = layout.row(align=True)
        row.operator("sprite.select_all_actions", icon="CHECKBOX_HLT")
        row.operator("sprite.deselect_all_actions", icon="CHECKBOX_DEHLT")

        # Per-action details for selected entry
        if props.actions and 0 <= props.active_action_index < len(props.actions):
            item = props.actions[props.active_action_index]
            box = layout.box()
            box.prop(item, "anim_type", text="Type")
            box.prop_search(item, "action", bpy.data, "actions", text="Action")
            if item.action:
                char_prefix = props.character_prefix.strip()
                atlas_name = f"{char_prefix}_{item.anim_type}" if char_prefix else item.anim_type
                box.label(text=f"Atlas: {atlas_name}.*", icon="INFO")
                if item.anim_type in FACE_LEFT_TYPES:
                    box.label(text=f"+ {atlas_name}FaceLeft.*", icon="LOOP_FORWARDS")
                if item.anim_type in SOCKET_TYPES:
                    is_right = item.anim_type in RIGHT_HAND_TYPES
                    socket_name = "Right Hand" if is_right else "Left Hand"
                    sock_obj = props.socket_right if is_right else props.socket_left
                    if sock_obj:
                        box.label(text=f"Socket: {sock_obj.name} ({socket_name})", icon="EMPTY_AXIS")
                    else:
                        box.label(text=f"Socket: {socket_name} (NOT SET!)", icon="ERROR")
                # Warn if action bones don't match subject armature
                if props.subject and props.subject.type == "ARMATURE":
                    matched, total = _validate_action_bones(item.action, props.subject)
                    if total > 0 and matched == 0:
                        box.label(text="Bones don't match armature!", icon="ERROR")
            box.prop(item, "fps", text="FPS (0 = global)")
            row = box.row(align=True)
            row.prop(item, "start_frame", text="Start (0 = auto)")
            row.prop(item, "end_frame", text="End (0 = auto)")

        enabled = [i for i in props.actions if i.enabled and i.action]
        layout.separator()
        col = layout.column()
        col.scale_y = 2.0
        col.operator("sprite.bake_all", icon="RENDER_ANIMATION",
                     text=f"BAKE ALL ({len(enabled)} actions)")


# ══════════════════════════════════════════════════════════════════════════════
# Registration
# ══════════════════════════════════════════════════════════════════════════════

_classes = (
    SPRITE_ActionItem,
    SPRITE_Props,
    SPRITE_UL_ActionList,
    SPRITE_OT_AddAction,
    SPRITE_OT_RemoveAction,
    SPRITE_OT_AddAllActions,
    SPRITE_OT_ClearActions,
    SPRITE_OT_SelectAllActions,
    SPRITE_OT_DeselectAllActions,
    SPRITE_OT_FixActionLinks,
    SPRITE_OT_DetectPacker,
    SPRITE_OT_SetupWeaponSockets,
    SPRITE_OT_SetupHurtboxZones,
    SPRITE_OT_BakeAll,
    SPRITE_PT_CharacterSetup,
    SPRITE_PT_Sockets,
    SPRITE_PT_HurtboxZones,
    SPRITE_PT_Actions,
)


def register():
    for cls in _classes:
        bpy.utils.register_class(cls)
    bpy.types.Scene.sprite_baker = PointerProperty(type=SPRITE_Props)


def unregister():
    for cls in reversed(_classes):
        bpy.utils.unregister_class(cls)
    del bpy.types.Scene.sprite_baker


if __name__ == "__main__":
    register()
