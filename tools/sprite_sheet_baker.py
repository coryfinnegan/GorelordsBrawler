"""
Sprite Sheet Baker — Blender Add-on
====================================
Renders character animations directly to a Nez sprite atlas (atlas.png +
atlas.atlas) in one step.

Workflow
--------
  1. Pick subject (armature) and add actions to bake.
  2. Set frame size, FPS, padding, origin, and output directory.
  3. Click "Bake Atlas" — renders frames, then calls the Nez SpriteAtlasPacker
     via `dotnet run` to produce atlas.png + atlas.atlas.

Install
-------
  Edit > Preferences > Add-ons > Install > point to this file.
  Configure the Packer Project path once (Settings panel) — it's the path to
  SpriteAtlasPacker.Console.csproj inside your Nez submodule.

Non-square frames
-----------------
  Frame Width and Frame Height can differ (e.g. 800×512 for wide attack
  animations).  The packer handles any frame size natively.

Per-action settings
-------------------
  Each action has an optional Name Override (exported animation key, empty =
  action.name) and a per-action FPS (0 = use global FPS).

  Note: the Nez packer assigns a single FPS value per animation strip; if you
  need different FPS per animation the packer is called once per animation so
  each strip gets its own FPS written into the atlas.

Character prefix
----------------
  Set "Character Prefix" in Atlas Settings (e.g. "FutureAxe") to prepend the
  character name to every output filename AND animation key, preventing name
  collisions between characters sharing an output directory.
  e.g. prefix="FutureAxe", atlas_name="Idle" → FutureAxe_Idle.atlas with
  animation key "FutureAxe_Idle".

Weapon sockets
--------------
  "Setup Both Sockets" creates two Empties (WeaponSocketLeft / WeaponSocketRight)
  each parented to the specified hand bone.  During baking the active socket's
  2D screen position is recorded per-frame and written to a .sockets.json sidecar.

  Per-action Socket Override lets each entry in the bake list use a different
  socket (e.g. a right-hand attack uses WeaponSocketRight).

NLA blending
------------
  Use nla_blend_baker.py (Anim Tools panel) to blend transitions like
  Idle→Attack or AttackRight→AttackLeft into clean baked actions first,
  then add those baked actions here to render.
"""

bl_info = {
    "name": "Sprite Sheet Baker",
    "author": "GorelordsBrawler Tools",
    "version": (3, 0, 0),
    "blender": (3, 0, 0),
    "location": "View3D > N Panel > Sprite Baker",
    "description": "Renders armature actions directly to a Nez sprite atlas",
    "category": "Render",
}

import json
import math
import os
import shutil
import subprocess

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


# Animation type suffixes — must match AnimationKeyBuilder property names in the game.
# The identifier IS the suffix; combined with char_prefix → "{prefix}_{suffix}".
ANIM_TYPE_ITEMS = [
    ("CUSTOM",                        "Custom",                          "Use a free-text name or the raw action name"),
    ("Idle",                          "Idle",                            "FutureAxe_Idle"),
    ("IdleFaceLeft",                  "Idle (Face Left)",                "FutureAxe_IdleFaceLeft"),
    ("Run",                           "Run",                             "FutureAxe_Run"),
    ("RunFaceLeft",                   "Run (Face Left)",                 "FutureAxe_RunFaceLeft"),
    ("Jump",                          "Jump",                            "FutureAxe_Jump"),
    ("JumpFaceLeft",                  "Jump (Face Left)",                "FutureAxe_JumpFaceLeft"),
    ("Select",                        "Select",                          "FutureAxe_Select"),
    ("AttackIdleRightHand",           "Attack Idle — Right Hand",        "FutureAxe_AttackIdleRightHand"),
    ("AttackIdleRightHandFaceLeft",   "Attack Idle — Right Hand (Left)", "FutureAxe_AttackIdleRightHandFaceLeft"),
    ("AttackRunRightHand",            "Attack Run — Right Hand",         "FutureAxe_AttackRunRightHand"),
    ("AttackRunRightHandFaceLeft",    "Attack Run — Right Hand (Left)",  "FutureAxe_AttackRunRightHandFaceLeft"),
    ("AttackIdleLeftHand",            "Attack Idle — Left Hand",         "FutureAxe_AttackIdleLeftHand"),
    ("AttackIdleLeftHandFaceLeft",    "Attack Idle — Left Hand (Left)",  "FutureAxe_AttackIdleLeftHandFaceLeft"),
    ("AttackRunLeftHand",             "Attack Run — Left Hand",          "FutureAxe_AttackRunLeftHand"),
    ("AttackRunLeftHandFaceLeft",     "Attack Run — Left Hand (Left)",   "FutureAxe_AttackRunLeftHandFaceLeft"),
]

# Maps each base anim_type to its face-left counterpart.
# Types not listed here (FaceLeft variants, Select, CUSTOM) don't have auto-FaceLeft.
FACE_LEFT_COUNTERPART = {
    "Idle":                "IdleFaceLeft",
    "Run":                 "RunFaceLeft",
    "Jump":                "JumpFaceLeft",
    "AttackIdleRightHand": "AttackIdleRightHandFaceLeft",
    "AttackIdleLeftHand":  "AttackIdleLeftHandFaceLeft",
    "AttackRunRightHand":  "AttackRunRightHandFaceLeft",
    "AttackRunLeftHand":   "AttackRunLeftHandFaceLeft",
}

# Animation types that track the RIGHT hand socket by default.
# All other types track the LEFT hand socket by default.
RIGHT_HAND_ANIM_TYPES = {
    "AttackIdleRightHand",
    "AttackIdleRightHandFaceLeft",
    "AttackRunRightHand",
    "AttackRunRightHandFaceLeft",
}


def _infer_socket(item, props):
    """Return the socket object to use for this action item.

    Priority: per-action socket_object override → inferred from anim_type
    (right-hand types → socket_right, everything else → socket_left).
    """
    if item.socket_object:
        return item.socket_object
    if item.anim_type in RIGHT_HAND_ANIM_TYPES:
        return props.socket_right
    return props.socket_left


_KNOWN_ANIM_TYPES = {item[0] for item in ANIM_TYPE_ITEMS if item[0] != "CUSTOM"}


def _get_atlas_suffix(props):
    """Return the animation-key suffix derived from the atlas name builder dropdowns.

    Attack  + hand + state  → e.g. "AttackIdleLeftHand"
    Movement + type         → e.g. "Idle", "Run", "Jump", "Select"
    Custom                  → props.atlas_name (free text, falls back to "atlas")

    To add a new preset, append an item to the relevant EnumProperty and handle
    it in the MOVEMENT branch below (or add a new branch for a new category).
    """
    cat = props.atlas_category
    if cat == "ATTACK":
        return f"Attack{props.atlas_attack_state}{props.atlas_attack_hand}Hand"
    if cat == "MOVEMENT":
        return props.atlas_movement_type
    # CUSTOM
    return props.atlas_name.strip() or "atlas"


def _on_action_changed(self, context):
    """Auto-select anim_type when an action is picked, based on the action name."""
    if self.action is None:
        return
    name = self.action.name

    # Direct match (action named exactly like a suffix, e.g. "AttackIdleRightHand")
    if name in _KNOWN_ANIM_TYPES:
        self.anim_type = name
        return

    # Match after stripping character prefix (e.g. "FutureAxe_AttackIdleRightHand" → suffix)
    try:
        props = context.scene.sprite_baker
        char_prefix = props.character_prefix.strip()
        if char_prefix and name.startswith(char_prefix + "_"):
            suffix = name[len(char_prefix) + 1:]
            if suffix in _KNOWN_ANIM_TYPES:
                self.anim_type = suffix
                return
    except Exception:
        pass

    self.anim_type = "CUSTOM"


# ══════════════════════════════════════════════════════════════════════════════
# Property Groups
# ══════════════════════════════════════════════════════════════════════════════

class SPRITE_ActionItem(PropertyGroup):
    """One entry in the actions-to-bake list."""
    action: PointerProperty(type=bpy.types.Action, update=_on_action_changed)
    enabled: BoolProperty(name="Enabled", default=True)
    anim_type: EnumProperty(
        name="Animation Type",
        description=(
            "Game animation type — determines the exported atlas key. "
            "Select the matching type and the key is built automatically as "
            "{CharacterPrefix}_{AnimationType}. Use 'Custom' for anything not in the list."
        ),
        items=ANIM_TYPE_ITEMS,
        default="CUSTOM",
    )
    name_override: StringProperty(
        name="Custom Name",
        description="Animation key written into the atlas when type is Custom (empty = use action name)",
        default="",
    )
    render_face_left: BoolProperty(
        name="Also render Face-Left",
        description=(
            "Render this action a second time with the subject rotated 180° Z, "
            "producing the FaceLeft atlas variant automatically in the same bake pass. "
            "Only applicable to types that have a FaceLeft counterpart."
        ),
        default=False,
    )
    fps: IntProperty(
        name="FPS",
        description="Frames per second for this animation (0 = use global FPS)",
        default=0,
        min=0,
        max=120,
    )
    socket_object: PointerProperty(
        name="Socket Override",
        type=bpy.types.Object,
        description=(
            "Socket Empty to track for this specific action. "
            "Overrides the global left-hand socket. "
            "Use this to assign WeaponSocketRight for right-hand attack actions."
        ),
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
        description="Camera/render width in pixels — sets the aspect ratio of each frame",
    )
    frame_height: IntProperty(
        name="H",
        default=512,
        min=1,
        description="Camera/render height in pixels — sets the aspect ratio of each frame",
    )
    target_size: IntProperty(
        name="Target Height",
        default=0,
        min=0,
        description=(
            "Downscale output frames to this height before packing (0 = use full render resolution). "
            "Width scales proportionally. Match this to the main atlas targetSize so all "
            "animations share the same pixel dimensions and the same SpriteScale works for all."
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
        description=(
            "Prepended to every atlas filename AND animation key to prevent name collisions. "
            "e.g. 'FutureAxe' + Idle => FutureAxe_Idle.atlas with key 'FutureAxe_Idle'. "
            "Leave empty for no prefix."
        ),
    )
    output_dir: StringProperty(
        name="Output Dir",
        subtype="DIR_PATH",
        default="//sprites/",
        description="Folder where the atlas files are written",
    )

    # Atlas name builder — determines the output filename suffix.
    # Combine character_prefix + computed suffix → {prefix}_{suffix}.*
    atlas_category: EnumProperty(
        name="Category",
        description="Top-level animation category",
        items=[
            ("ATTACK",   "Attack",   "Melee or ranged attack animation"),
            ("MOVEMENT", "Movement", "Locomotion / stance animation"),
            ("CUSTOM",   "Custom",   "Free-text name — use for anything not in the list"),
        ],
        default="MOVEMENT",
    )
    atlas_attack_hand: EnumProperty(
        name="Hand",
        description="Which hand holds the weapon for this attack",
        items=[
            ("Left",  "Left Hand",  ""),
            ("Right", "Right Hand", ""),
        ],
        default="Left",
    )
    atlas_attack_state: EnumProperty(
        name="State",
        description="Character state during the attack",
        items=[
            ("Idle", "From Idle", "Attack launched while standing"),
            ("Run",  "From Run",  "Attack launched while running"),
        ],
        default="Idle",
    )
    atlas_movement_type: EnumProperty(
        name="Type",
        description="Locomotion / stance type",
        items=[
            ("Idle",   "Idle",   ""),
            ("Run",    "Run",    ""),
            ("Jump",   "Jump",   ""),
            ("Select", "Select", "Character-select screen pose"),
        ],
        default="Idle",
    )
    # Kept for CUSTOM category — free-text fallback
    atlas_name: StringProperty(
        name="Custom Name",
        default="atlas",
        description=(
            "Output name suffix when Category is set to Custom. "
            "e.g. 'Idle' with prefix 'FutureAxe' produces FutureAxe_Idle.atlas"
        ),
    )
    fps: IntProperty(
        name="FPS",
        default=8,
        min=1,
        max=120,
        description="Default animation framerate written into the atlas (per-action FPS overrides this)",
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
        description=(
            "Empty parented to the left weapon hand bone. "
            "Used as the default socket for actions without a Socket Override. "
            "Its 2D screen position is recorded each frame and written to a .sockets.json sidecar."
        ),
    )
    socket_right: PointerProperty(
        name="Right Hand Socket",
        type=bpy.types.Object,
        description=(
            "Empty parented to the right weapon hand bone. "
            "Automatically assigned to mirrored actions created by 'Mirror & Bake'."
        ),
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
        description="Maximum width and height of the packed atlas in pixels (increase if packer fails with many/large frames)",
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
            if item.anim_type != "CUSTOM":
                label = f"{char_prefix}_{item.anim_type}" if char_prefix else item.anim_type
            else:
                label = item.name_override.strip() if item.name_override.strip() else item.action.name
                if char_prefix and not label.startswith(char_prefix + "_"):
                    label = f"{char_prefix}_{label}"
            fps_str = f"  [{item.fps} fps]" if item.fps > 0 else ""
            if item.socket_object:
                sock_str = f"  [sock: {item.socket_object.name}]"
            elif item.anim_type in RIGHT_HAND_ANIM_TYPES:
                sock_str = "  [R hand]"
            else:
                sock_str = "  [L hand]" if props.socket_left or props.socket_right else ""
            fl_str = "  →FL" if item.render_face_left and item.anim_type in FACE_LEFT_COUNTERPART else ""
            row.label(text=label + fps_str + sock_str + fl_str, icon="ACTION")
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
    bl_description = "Add every action in this .blend file to the bake list"

    def execute(self, context):
        props = context.scene.sprite_baker
        existing = {item.action for item in props.actions if item.action}
        added = 0
        for action in sorted(bpy.data.actions, key=lambda a: a.name):
            if action not in existing:
                item = props.actions.add()
                item.action = action
                added += 1
        self.report({"INFO"}, f"Added {added} action(s)")
        return {"FINISHED"}


class SPRITE_OT_ClearActions(Operator):
    bl_idname = "sprite.clear_actions"
    bl_label = "Clear List"
    bl_description = "Remove all entries from the bake list"

    def execute(self, context):
        context.scene.sprite_baker.actions.clear()
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
# Operator — setup weapon sockets (both hands)
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

        left_ok  = self._setup_socket(context, armature, "WeaponSocketLeft",  props.socket_left_bone)
        right_ok = self._setup_socket(context, armature, "WeaponSocketRight", props.socket_right_bone)

        if left_ok:
            props.socket_left  = bpy.data.objects["WeaponSocketLeft"]
        if right_ok:
            props.socket_right = bpy.data.objects["WeaponSocketRight"]

        if left_ok and right_ok:
            self.report({"INFO"},
                "Both sockets created — move WeaponSocketLeft to the axe tip on the left hand, "
                "WeaponSocketRight on the right hand")
        elif left_ok or right_ok:
            self.report({"WARNING"}, "One socket failed — check bone names in the fields above")
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
# Operator — bake atlas
# ══════════════════════════════════════════════════════════════════════════════

class SPRITE_OT_BakeAtlas(Operator):
    """Render all enabled actions and pack directly to a Nez sprite atlas."""
    bl_idname = "sprite.bake_atlas"
    bl_label = "Bake Atlas"
    bl_description = (
        "Render all enabled actions to individual frames, then call the "
        "Nez SpriteAtlasPacker to produce atlas.png + atlas.atlas"
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

        # Build effective atlas base name (with optional character prefix)
        char_prefix = props.character_prefix.strip()
        atlas_suffix = _get_atlas_suffix(props)
        atlas_base = f"{char_prefix}_{atlas_suffix}" if char_prefix else atlas_suffix

        output_dir = bpy.path.abspath(props.output_dir)
        os.makedirs(output_dir, exist_ok=True)
        tmp_dir = os.path.join(output_dir, "_tmp_frames")
        fl_tmp_dir = os.path.join(output_dir, "_tmp_fl_frames")
        for d in (tmp_dir, fl_tmp_dir):
            if os.path.exists(d):
                shutil.rmtree(d)
            os.makedirs(d)

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

        # Capture the rendered frame size NOW — the finally block restores the original
        # resolution before we write the sidecar, so we must save it here.
        baked_res_x = render.resolution_x
        baked_res_y = render.resolution_y

        baked_names = []     # (anim_name, fps) tuples for main atlas packer call
        baked_sockets = {}   # anim_name -> list of [x,y] or None per frame  (main atlas only)
        fl_baked = []        # (fl_name, fps, positions) — each gets its own separate atlas

        try:
            for item in enabled_items:
                if item.anim_type != "CUSTOM":
                    anim_name = f"{char_prefix}_{item.anim_type}" if char_prefix else item.anim_type
                else:
                    anim_name = item.name_override.strip() if item.name_override.strip() else item.action.name
                    if char_prefix and not anim_name.startswith(char_prefix + "_"):
                        anim_name = f"{char_prefix}_{anim_name}"
                anim_fps = item.fps if item.fps > 0 else props.fps

                self.report({"INFO"}, f"Rendering '{anim_name}' ...")
                # Never record socket data for FaceLeft animations — the game always
                # looks up face-right socket data for FaceLeft anim names, so a
                # FaceLeft sidecar would cause a double-flip of the hitbox position.
                is_face_left = item.anim_type != "CUSTOM" and item.anim_type.endswith("FaceLeft")
                socket_positions = self._render_action(
                    context, obj, item, anim_name, tmp_dir, props,
                    track_socket=not is_face_left)
                baked_names.append((anim_name, anim_fps))
                baked_sockets[anim_name] = socket_positions

                # Auto FaceLeft: rotate subject 180° Z and render into a separate atlas
                if item.render_face_left and item.anim_type in FACE_LEFT_COUNTERPART:
                    fl_type = FACE_LEFT_COUNTERPART[item.anim_type]
                    fl_name = f"{char_prefix}_{fl_type}" if char_prefix else fl_type
                    self.report({"INFO"}, f"Rendering '{fl_name}' (auto face-left) ...")
                    fl_positions = self._render_action(
                        context, obj, item, fl_name, fl_tmp_dir, props,
                        rotation_z_offset=math.pi,
                        track_socket=False,  # game looks up face-right data for face-left anims
                    )
                    fl_baked.append((fl_name, anim_fps, fl_positions))

        except Exception as exc:
            self.report({"ERROR"}, f"Render failed: {exc}")
            return {"CANCELLED"}
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

        # ── Main atlas ────────────────────────────────────────────────────────
        self._write_sockets_sidecar(output_dir, atlas_base, baked_res_x, baked_res_y, baked_sockets)

        atlas_png = os.path.join(output_dir, f"{atlas_base}.png")
        atlas_map = os.path.join(output_dir, f"{atlas_base}.atlas")
        packer_ok = self._run_packer(props, tmp_dir, atlas_png, atlas_map, baked_names)

        # ── Separate atlas per face-left animation ─────────────────────────
        # fl_tmp_dir contains one subdir per face-left animation — pass it
        # directly to the packer so each gets its own atlas file.
        for fl_name, fl_fps, fl_positions in fl_baked:
            fl_sockets = {fl_name: fl_positions}
            self._write_sockets_sidecar(output_dir, fl_name, baked_res_x, baked_res_y, fl_sockets)

            fl_png = os.path.join(output_dir, f"{fl_name}.png")
            fl_map = os.path.join(output_dir, f"{fl_name}.atlas")
            # Each face-left animation has its own subdir in fl_tmp_dir.
            # Create a per-animation staging dir so the packer only sees one animation.
            fl_staging = os.path.join(fl_tmp_dir, f"_stage_{fl_name}")
            os.makedirs(fl_staging, exist_ok=True)
            fl_frames_dst = os.path.join(fl_staging, fl_name)
            if not os.path.exists(fl_frames_dst):
                shutil.copytree(os.path.join(fl_tmp_dir, fl_name), fl_frames_dst)
            fl_ok = self._call_packer(
                bpy.path.abspath(props.packer_project),
                fl_staging, fl_png, fl_map, fl_fps, props,
            )
            if not fl_ok:
                self.report({"WARNING"}, f"Packer failed for face-left atlas: {fl_name}")
                packer_ok = False

        shutil.rmtree(tmp_dir, ignore_errors=True)
        shutil.rmtree(fl_tmp_dir, ignore_errors=True)

        if packer_ok:
            self.report({"INFO"}, f"Atlas written to: {output_dir}")
        else:
            self.report({"WARNING"}, "Frames rendered but packer failed — see console")

        return {"FINISHED"}

    def _write_sockets_sidecar(self, output_dir, base_name, res_x, res_y, sockets_dict):
        """Write a .sockets.json sidecar for the given animation dict, if any positions exist."""
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

    # ── Helpers ───────────────────────────────────────────────────────────────

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

    def _render_action(self, context, obj, item, anim_name, tmp_dir, props,
                       rotation_z_offset=0.0, track_socket=True):
        """Render each frame of an action to tmp_dir/{anim_name}/frame_NNNN.png.

        Returns a list of socket positions (one per frame): each entry is
        [x, y] in rendered pixels (top-left origin) or None if the socket is
        off-screen or not configured.

        rotation_z_offset — extra Z rotation (radians) applied to obj every frame
        after scene.frame_set() re-evaluates the dependency graph.  Used for the
        auto face-left pass (math.pi).
        """
        scene = context.scene
        render = scene.render

        action = item.action
        obj.animation_data.action = action
        frame_start = int(action.frame_range[0])
        frame_end = int(action.frame_range[1])

        anim_dir = os.path.join(tmp_dir, anim_name)
        os.makedirs(anim_dir, exist_ok=True)

        # Per-action socket override → inferred from anim_type → left socket fallback
        socket_obj = _infer_socket(item, props)

        socket_positions = []

        for idx, frame_num in enumerate(range(frame_start, frame_end + 1)):
            scene.frame_set(frame_num)

            # Apply Z rotation offset AFTER frame_set() so the depsgraph
            # re-evaluation doesn't undo our change (needed for face-left pass).
            if rotation_z_offset != 0.0:
                obj.rotation_euler[2] += rotation_z_offset
                context.view_layer.update()

            # Record the weapon socket position in 2D screen space.
            # Only reject if the socket is behind the camera (ndc.z <= 0).
            # We intentionally allow coords outside [0, resolution] — the weapon
            # can swing past the frame edge and the game's hitbox math still works.
            # For face-left passes (track_socket=False) we skip recording entirely
            # because the game looks up face-right socket data for face-left animations,
            # so this sidecar would never be used.
            if track_socket and socket_obj is not None and scene.camera is not None:
                depsgraph = context.evaluated_depsgraph_get()
                socket_eval = socket_obj.evaluated_get(depsgraph)
                ndc = bpy_extras.object_utils.world_to_camera_view(
                    scene, scene.camera, socket_eval.matrix_world.translation
                )
                if ndc.z > 0.0:
                    px = ndc.x * render.resolution_x
                    py = (1.0 - ndc.y) * render.resolution_y  # flip Y: Blender NDC Y=0 at bottom
                    socket_positions.append([round(px, 2), round(py, 2)])
                else:
                    socket_positions.append(None)
            else:
                socket_positions.append(None)

            render.filepath = os.path.join(anim_dir, f"{anim_name}_{idx:04d}")
            bpy.ops.render.render(write_still=True)

            # Restore rotation so the next frame_set() starts from the clean value.
            if rotation_z_offset != 0.0:
                obj.rotation_euler[2] -= rotation_z_offset

        frame_count = frame_end - frame_start + 1
        print(f"  {anim_name}: {frame_count} frames → {anim_dir}")
        return socket_positions

    def _run_packer(self, props, tmp_dir, atlas_png, atlas_map, baked_names):
        packer = bpy.path.abspath(props.packer_project)

        fps_values = {fps for _, fps in baked_names}
        if len(fps_values) == 1:
            return self._call_packer(packer, tmp_dir, atlas_png, atlas_map,
                                     next(iter(fps_values)), props)

        merged_atlas_lines = []
        for anim_name, fps in baked_names:
            anim_tmp = os.path.join(tmp_dir, "_single")
            if os.path.exists(anim_tmp):
                shutil.rmtree(anim_tmp)
            os.makedirs(anim_tmp)
            src_dir = os.path.join(tmp_dir, anim_name)
            if os.path.isdir(src_dir):
                shutil.copytree(src_dir, os.path.join(anim_tmp, anim_name))

            anim_png = atlas_png + f".{anim_name}.tmp.png"
            anim_map = atlas_map + f".{anim_name}.tmp.atlas"
            ok = self._call_packer(packer, anim_tmp, anim_png, anim_map, fps, props)
            if not ok:
                return False

            if os.path.exists(anim_png):
                os.remove(anim_png)
            if os.path.exists(anim_map):
                os.remove(anim_map)

        self.report({"INFO"},
            "Per-action FPS requires separate atlases; using global FPS for the merged atlas")
        return self._call_packer(packer, tmp_dir, atlas_png, atlas_map, props.fps, props)

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


class SPRITE_PT_Subject(_Base, Panel):
    bl_label = "Subject & Actions"
    bl_idname = "SPRITE_PT_Subject"

    def draw(self, context):
        layout = self.layout
        props = context.scene.sprite_baker

        layout.prop(props, "subject")

        layout.separator()
        layout.label(text="Actions to bake:")

        row = layout.row()
        row.template_list(
            "SPRITE_UL_ActionList", "",
            props, "actions",
            props, "active_action_index",
            rows=5,
        )
        col = row.column(align=True)
        col.operator("sprite.add_action", text="", icon="ADD")
        col.operator("sprite.remove_action", text="", icon="REMOVE")
        col.separator()
        col.operator("sprite.add_all_actions", text="", icon="LINENUMBERS_ON")
        col.operator("sprite.clear_actions", text="", icon="TRASH")

        # Weapon socket setup
        layout.separator()
        box = layout.box()
        box.label(text="Weapon Sockets", icon="EMPTY_AXIS")
        row = box.row(align=True)
        row.prop(props, "socket_left", text="Left Hand")
        row = box.row(align=True)
        row.prop(props, "socket_right", text="Right Hand")
        col = box.column(align=True)
        col.prop(props, "socket_left_bone", text="L Bone")
        col.prop(props, "socket_right_bone", text="R Bone")
        box.operator("sprite.setup_weapon_sockets", text="Setup Both Sockets", icon="BONE_DATA")

        # Per-action settings for selected entry
        if props.actions and 0 <= props.active_action_index < len(props.actions):
            item = props.actions[props.active_action_index]
            box = layout.box()
            box.prop_search(item, "action", bpy.data, "actions", text="Action")
            box.prop(item, "anim_type", text="Type")
            if item.anim_type == "CUSTOM":
                box.prop(item, "name_override", text="Custom Name")
            else:
                char_prefix = props.character_prefix.strip()
                export_key = f"{char_prefix}_{item.anim_type}" if char_prefix else item.anim_type
                box.label(text=f"Key: {export_key}", icon="INFO")
            if item.anim_type in FACE_LEFT_COUNTERPART:
                row = box.row(align=True)
                row.prop(item, "render_face_left")
                if item.render_face_left:
                    char_prefix = props.character_prefix.strip()
                    fl_type = FACE_LEFT_COUNTERPART[item.anim_type]
                    fl_key = f"{char_prefix}_{fl_type}" if char_prefix else fl_type
                    box.label(text=f"Also bakes: {fl_key}", icon="INFO")

            row = box.row(align=True)
            row.prop(item, "fps", text="FPS (0 = global)")
            box.prop(item, "socket_object", text="Socket Override")
            if not item.socket_object:
                inferred = _infer_socket(item, props)
                if inferred:
                    box.label(text=f"Auto socket: {inferred.name}", icon="EMPTY_AXIS")
                elif item.anim_type in RIGHT_HAND_ANIM_TYPES:
                    box.label(text="Auto socket: Right Hand (not set!)", icon="ERROR")
                else:
                    box.label(text="Auto socket: Left Hand (not set!)", icon="ERROR")


class SPRITE_PT_FrameSettings(_Base, Panel):
    bl_label = "Frame Settings"
    bl_idname = "SPRITE_PT_FrameSettings"

    def draw(self, context):
        layout = self.layout
        props = context.scene.sprite_baker

        col = layout.column(align=True)
        col.label(text="Camera size (sets aspect ratio):")
        row = col.row(align=True)
        row.prop(props, "frame_width")
        row.prop(props, "frame_height")

        layout.separator()
        col2 = layout.column(align=True)
        col2.prop(props, "target_size")
        if props.target_size > 0:
            out_w = round(props.target_size * props.frame_width / props.frame_height)
            col2.label(
                text=f"Output: {out_w}×{props.target_size} px per frame",
                icon="INFO",
            )
        else:
            col2.label(text=f"Output: {props.frame_width}×{props.frame_height} px per frame (full res)")

        layout.prop(props, "transparent")


class SPRITE_PT_AtlasSettings(_Base, Panel):
    bl_label = "Atlas Settings"
    bl_idname = "SPRITE_PT_AtlasSettings"

    def draw(self, context):
        layout = self.layout
        props = context.scene.sprite_baker

        # Character prefix
        layout.prop(props, "character_prefix")

        layout.separator()
        layout.prop(props, "output_dir")

        # ── Atlas name builder ─────────────────────────────────────────────
        box = layout.box()
        box.label(text="Animation Type", icon="ACTION")
        box.prop(props, "atlas_category", text="")
        if props.atlas_category == "ATTACK":
            row = box.row(align=True)
            row.prop(props, "atlas_attack_hand", expand=True)
            row = box.row(align=True)
            row.prop(props, "atlas_attack_state", expand=True)
        elif props.atlas_category == "MOVEMENT":
            box.prop(props, "atlas_movement_type", expand=True)
        else:
            box.prop(props, "atlas_name", text="Name")

        # Show the computed output filename
        char_prefix = props.character_prefix.strip()
        suffix = _get_atlas_suffix(props)
        effective = f"{char_prefix}_{suffix}" if char_prefix else suffix
        box.label(text=f"Output: {effective}.*", icon="INFO")

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


class SPRITE_PT_Bake(_Base, Panel):
    bl_label = "Bake"
    bl_idname = "SPRITE_PT_Bake"

    def draw(self, context):
        layout = self.layout
        props = context.scene.sprite_baker

        enabled = [i for i in props.actions if i.enabled and i.action]

        col = layout.column()
        col.scale_y = 1.6
        col.operator("sprite.bake_atlas", icon="RENDER_ANIMATION")

        layout.separator()
        layout.label(text=f"{len(enabled)} / {len(props.actions)} action(s) queued")

        if props.frame_width != props.frame_height:
            layout.label(
                text=f"Non-square frames: {props.frame_width}×{props.frame_height}",
                icon="INFO",
            )


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
    SPRITE_OT_DetectPacker,
    SPRITE_OT_SetupWeaponSockets,
    SPRITE_OT_BakeAtlas,
    SPRITE_PT_Subject,
    SPRITE_PT_FrameSettings,
    SPRITE_PT_AtlasSettings,
    SPRITE_PT_Bake,
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
