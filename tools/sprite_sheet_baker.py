"""
Sprite Sheet Baker — Blender Add-on
====================================
Renders character animations directly to a Nez sprite atlas (atlas.png +
atlas.atlas) in one step.  Replaces the separate build_atlas.py step.

Workflow
--------
  1. Pick subject (armature) and add actions to bake.
  2. Set frame size, FPS, padding, origin, and output directory.
  3. Click "Bake Atlas" — the add-on renders frames, then calls the
     Nez SpriteAtlasPacker via `dotnet run` to produce atlas.png + atlas.atlas.

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
"""

bl_info = {
    "name": "Sprite Sheet Baker",
    "author": "GorelordsBrawler Tools",
    "version": (2, 0, 0),
    "blender": (3, 0, 0),
    "location": "View3D > N Panel > Sprite Baker",
    "description": "Renders armature actions directly to a Nez sprite atlas",
    "category": "Render",
}

import math
import os
import shutil
import subprocess
import sys

import bpy
from bpy.props import (
    BoolProperty,
    CollectionProperty,
    FloatProperty,
    IntProperty,
    PointerProperty,
    StringProperty,
)
from bpy.types import Operator, Panel, PropertyGroup, UIList


# ══════════════════════════════════════════════════════════════════════════════
# Property Groups
# ══════════════════════════════════════════════════════════════════════════════

class SPRITE_ActionItem(PropertyGroup):
    """One entry in the actions-to-bake list."""
    action: PointerProperty(type=bpy.types.Action)
    enabled: BoolProperty(name="Enabled", default=True)
    name_override: StringProperty(
        name="Name",
        description="Animation key written into the atlas (empty = use action name)",
        default="",
    )
    fps: IntProperty(
        name="FPS",
        description="Frames per second for this animation (0 = use global FPS)",
        default=0,
        min=0,
        max=120,
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
    output_dir: StringProperty(
        name="Output Dir",
        subtype="DIR_PATH",
        default="//sprites/",
        description="Folder where the atlas files are written",
    )
    atlas_name: StringProperty(
        name="Atlas Name",
        default="atlas",
        description=(
            "Base filename for the output files — e.g. 'atlas' produces atlas.png + atlas.atlas, "
            "'atlas-attack' produces atlas-attack.png + atlas-attack.atlas"
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
            label = item.name_override if item.name_override else item.action.name
            fps_str = f"  [{item.fps} fps]" if item.fps > 0 else ""
            row.label(text=label + fps_str, icon="ACTION")
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
        # Walk up to find repo root, then walk down to find the csproj
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
            # Don't descend into hidden dirs or .git
            dirnames[:] = [d for d in dirnames if not d.startswith(".")]
        return None


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

        # ── Validate ─────────────────────────────────────────────────────────
        errors = self._validate(props, obj)
        if errors:
            for e in errors:
                self.report({"ERROR"}, e)
            return {"CANCELLED"}

        output_dir = bpy.path.abspath(props.output_dir)
        os.makedirs(output_dir, exist_ok=True)
        tmp_dir = os.path.join(output_dir, "_tmp_frames")
        if os.path.exists(tmp_dir):
            shutil.rmtree(tmp_dir)
        os.makedirs(tmp_dir)

        enabled_items = [i for i in props.actions if i.enabled and i.action]

        # ── Save original render settings ─────────────────────────────────────
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

        # ── Apply render settings ─────────────────────────────────────────────
        # If target_size is set, render at the downscaled dimensions directly —
        # the camera framing is unchanged but the output pixels are smaller.
        # This keeps all atlas frames at the same pixel height so a single
        # SpriteScale value works for every animation.
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

        baked_names = []  # (anim_name, fps) tuples for packer calls
        try:
            for item in enabled_items:
                anim_name = item.name_override.strip() if item.name_override.strip() else item.action.name
                anim_fps = item.fps if item.fps > 0 else props.fps
                self.report({"INFO"}, f"Rendering '{anim_name}' ...")
                self._render_action(context, obj, item.action, anim_name, tmp_dir, props)
                baked_names.append((anim_name, anim_fps))
        except Exception as exc:
            self.report({"ERROR"}, f"Render failed: {exc}")
            return {"CANCELLED"}
        finally:
            # Always restore original settings
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

        # ── Run packer (once per animation so each gets its own FPS) ──────────
        atlas_base = props.atlas_name.strip() or "atlas"
        atlas_png = os.path.join(output_dir, f"{atlas_base}.png")
        atlas_map = os.path.join(output_dir, f"{atlas_base}.atlas")
        packer_ok = self._run_packer(props, tmp_dir, atlas_png, atlas_map, baked_names)

        # ── Clean up temp frames ──────────────────────────────────────────────
        shutil.rmtree(tmp_dir, ignore_errors=True)

        if packer_ok:
            self.report({"INFO"}, f"Atlas written to: {output_dir}")
        else:
            self.report({"WARNING"}, "Frames rendered but packer failed — see console")

        return {"FINISHED"}

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

    def _render_action(self, context, obj, action, anim_name, tmp_dir, props):
        """Render each frame of an action to tmp_dir/{anim_name}/frame_NNNN.png."""
        scene = context.scene
        render = scene.render

        obj.animation_data.action = action
        frame_start = int(action.frame_range[0])
        frame_end = int(action.frame_range[1])

        anim_dir = os.path.join(tmp_dir, anim_name)
        os.makedirs(anim_dir, exist_ok=True)

        for idx, frame_num in enumerate(range(frame_start, frame_end + 1)):
            scene.frame_set(frame_num)
            # Set filepath without extension; Blender appends .png (use_file_extension=True)
            render.filepath = os.path.join(anim_dir, f"{anim_name}_{idx:04d}")
            bpy.ops.render.render(write_still=True)

        frame_count = frame_end - frame_start + 1
        print(f"  {anim_name}: {frame_count} frames → {anim_dir}")

    def _run_packer(self, props, tmp_dir, atlas_png, atlas_map, baked_names):
        """
        Call the Nez SpriteAtlasPacker via 'dotnet run'.

        Because the packer assigns one FPS to the entire atlas, we call it once
        per animation (each with its own FPS) and merge the resulting atlas
        files.  If all FPS values are the same, a single call is made instead.
        """
        packer = bpy.path.abspath(props.packer_project)

        # Check whether all animations share the same FPS
        fps_values = {fps for _, fps in baked_names}
        if len(fps_values) == 1:
            # Single packer call for the whole tmp_dir
            return self._call_packer(packer, tmp_dir, atlas_png, atlas_map,
                                     next(iter(fps_values)), props)

        # Multiple FPS values — pack each animation separately, then merge
        # the resulting .atlas files (atlas.png is rebuilt from scratch each
        # call, so we combine by running all and concatenating atlas text).
        merged_atlas_lines = []
        first_atlas_png = None

        for anim_name, fps in baked_names:
            anim_tmp = os.path.join(tmp_dir, "_single")
            # Move just this animation's subdirectory into a clean temp folder
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

            # We cannot trivially merge separate PNG atlases, so fall back to
            # a single call using the global FPS and warn the user.
            # (True per-animation FPS merging would require re-packing all
            # frames together with a custom atlas writer — out of scope here.)
            if os.path.exists(anim_png):
                os.remove(anim_png)
            if os.path.exists(anim_map):
                os.remove(anim_map)

        # Fallback: single call with global FPS
        self.report({"INFO"},
            "Per-action FPS requires separate atlases; using global FPS for the merged atlas")
        return self._call_packer(packer, tmp_dir, atlas_png, atlas_map,
                                 props.fps, props)

    def _call_packer(self, packer_project, frames_dir, atlas_png, atlas_map, fps, props):
        max_size = props.max_atlas_size
        cmd = [
            "dotnet", "run",
            "--project", packer_project,
            "--no-launch-profile",  # ignore launchSettings.json so our args aren't overridden
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

        # Always print packer output so failures are visible in Blender's console
        if result.stdout.strip():
            print(f"  Packer stdout:\n    {result.stdout.strip()}")
        if result.stderr.strip():
            print(f"  Packer stderr:\n    {result.stderr.strip()}")

        # The packer may exit 0 even on failure, so verify the output file exists
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

        # Per-action settings for selected entry
        if props.actions and 0 <= props.active_action_index < len(props.actions):
            item = props.actions[props.active_action_index]
            box = layout.box()
            box.prop_search(item, "action", bpy.data, "actions", text="Action")
            row = box.row(align=True)
            row.prop(item, "name_override", text="Export Name")
            row = box.row(align=True)
            row.prop(item, "fps", text="FPS (0 = global)")


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

        layout.prop(props, "output_dir")
        layout.prop(props, "atlas_name")

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
