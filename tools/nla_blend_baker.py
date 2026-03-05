"""
NLA Blend Baker — Blender add-on
=================================
Automates the idle→attack NLA blend workflow to fix pose-mismatch stutter:

  1. Temporarily builds two NLA tracks: idle (bottom) and attack (top).
  2. Sets a blend_in on the attack strip so it smoothly fades in from the idle pose
     over the first N frames — eliminating the snap from the reference T-pose.
  3. Bakes the attack frame range to a new, clean action.
  4. Normalises keyframe positions to start at frame 1.
  5. Removes the temporary NLA strips and restores any previously-muted tracks.

Workflow:
  - View3D ▸ N-panel ▸ Anim Tools ▸ NLA Blend Baker
  - Select armature, pick idle + attack actions, set blend frames.
  - Click "Bake Blend" → a new action appears in bpy.data.actions.
  - Use that action in the Sprite Baker addon (set Export Name = "attack").

Requirements: Blender 3.2+ (uses context.temp_override).
"""

bl_info = {
    "name": "NLA Blend Baker",
    "author": "GorelordsBrawler Tools",
    "version": (1, 0, 0),
    "blender": (3, 2, 0),
    "location": "View3D > Sidebar > Anim Tools",
    "description": (
        "Blend idle→attack in the NLA editor to fix pose-mismatch stutter, "
        "then bake the result to a clean action ready for sprite export"
    ),
    "category": "Animation",
}

import bpy
from bpy.types import Operator, Panel, PropertyGroup
from bpy.props import IntProperty, PointerProperty, StringProperty


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _find_nla_area(context):
    """Return (area, window) pointing at an open NLA editor, or (None, None)."""
    for window in context.window_manager.windows:
        for area in window.screen.areas:
            if area.type == 'NLA_EDITOR':
                return area, window
    return None, None


def _run_nla_bake(context, obj, frame_start, frame_end):
    """
    Execute bpy.ops.nla.bake over [frame_start, frame_end].

    Tries in order:
      1. Existing NLA editor area (preferred — no UI disruption).
      2. Temporarily converts another area to NLA_EDITOR.
      3. Bare call without override (last resort; may fail with 'wrong context').
    """
    bake_kwargs = dict(
        frame_start=int(frame_start),
        frame_end=int(frame_end),
        step=1,
        only_selected=False,
        visual_keying=True,       # capture the full evaluated (NLA-blended) pose
        clear_constraints=False,
        clear_parents=False,
        use_current_action=False, # always create a fresh action
        clean_curves=False,
        bake_types={'POSE'},
    )

    nla_area, window = _find_nla_area(context)

    if nla_area is not None:
        for region in nla_area.regions:
            if region.type == 'WINDOW':
                with context.temp_override(window=window, area=nla_area, region=region):
                    bpy.ops.nla.bake(**bake_kwargs)
                return True

    # No NLA editor open — temporarily repurpose another area
    priority = ('VIEW_3D', 'DOPESHEET_EDITOR', 'PROPERTIES', 'OUTLINER')
    for area_type in priority:
        for area in context.screen.areas:
            if area.type == area_type:
                old_type = area.type
                area.type = 'NLA_EDITOR'
                try:
                    for region in area.regions:
                        if region.type == 'WINDOW':
                            with context.temp_override(area=area, region=region):
                                bpy.ops.nla.bake(**bake_kwargs)
                            return True
                finally:
                    area.type = old_type

    # Last resort — Blender may raise a context error
    bpy.ops.nla.bake(**bake_kwargs)
    return True


# ---------------------------------------------------------------------------
# Properties
# ---------------------------------------------------------------------------

class NLABlend_Props(PropertyGroup):
    idle_action: PointerProperty(
        type=bpy.types.Action,
        name="Source Action",
        description=(
            "Action that provides the starting pose context — typically whatever "
            "animation plays before the target (e.g. idle, run, jump-land)"
        ),
    )
    attack_action: PointerProperty(
        type=bpy.types.Action,
        name="Target Action",
        description=(
            "Action to blend into. Its first 'Blend Frames' will smoothly "
            "transition from the source pose instead of snapping to a reference pose"
        ),
    )
    blend_frames: IntProperty(
        name="Blend In Frames",
        description=(
            "Number of frames over which source fades out and target fades in at the START. "
            "8–12 works well for most animations; increase if the start still looks abrupt"
        ),
        default=8,
        min=1,
        max=60,
    )
    blend_out_frames: IntProperty(
        name="Blend Out Frames",
        description=(
            "Number of frames at the END of the attack animation over which the character "
            "fades back to the source pose. 0 = no blend-out (hard cut back to source). "
            "8–12 gives a smooth return; increase if the ending still looks abrupt"
        ),
        default=0,
        min=0,
        max=60,
    )
    output_name: StringProperty(
        name="Output Action",
        description=(
            "Name for the baked result action. "
            "Use this as the Export Name in the Sprite Baker"
        ),
        default="Blended",
    )


# ---------------------------------------------------------------------------
# Operator
# ---------------------------------------------------------------------------

class NLABlend_OT_bake(Operator):
    bl_idname = "nla_blend.bake"
    bl_label = "Bake Blend"
    bl_description = (
        "Set up temporary NLA strips (idle below, attack above with blend-in), "
        "bake the result to a new action, then clean up"
    )
    bl_options = {'REGISTER', 'UNDO'}

    def _cleanup(self, anim, track_idle, track_atk, saved_mutes, scene,
                 saved_fs, saved_fe):
        """Remove temp tracks, restore mutes and scene frame range."""
        try:
            anim.nla_tracks.remove(track_atk)
        except Exception:
            pass
        try:
            anim.nla_tracks.remove(track_idle)
        except Exception:
            pass
        for track, was_muted in saved_mutes.items():
            track.mute = was_muted
        scene.frame_start = saved_fs
        scene.frame_end   = saved_fe

    def execute(self, context):  # noqa: C901  (complexity OK for a single operator)
        props = context.scene.nla_blend_props
        obj   = context.active_object

        # ── Validate ─────────────────────────────────────────────────────────
        if obj is None or obj.type != 'ARMATURE':
            self.report({'ERROR'}, "Active object must be an armature")
            return {'CANCELLED'}

        idle_act = props.idle_action
        atk_act  = props.attack_action

        if not idle_act:
            self.report({'ERROR'}, "Select a Source action")
            return {'CANCELLED'}
        if not atk_act:
            self.report({'ERROR'}, "Select a Target action")
            return {'CANCELLED'}
        if idle_act is atk_act:
            self.report({'ERROR'}, "Source and target actions must be different")
            return {'CANCELLED'}

        # ── Prepare animation data ────────────────────────────────────────────
        if not obj.animation_data:
            obj.animation_data_create()
        anim = obj.animation_data

        # Mute all existing NLA tracks so they don't interfere with our temp setup
        saved_mutes = {track: track.mute for track in anim.nla_tracks}
        for track in anim.nla_tracks:
            track.mute = True

        # ── Calculate world-space frame layout ────────────────────────────────
        #
        #   world frames:
        #     [idle_ws ──────────────── idle_we]
        #                   [atk_ws ─── atk_we]
        #                   |←blend→|
        #
        #   The attack strip starts 'blend' frames before the idle strip ends.
        #   Its blend_in = blend causes NLA to cross-fade from idle → attack
        #   over those overlap frames — eliminating the reference-pose snap.
        #
        idle_fr0 = idle_act.frame_range[0]
        idle_fr1 = idle_act.frame_range[1]
        idle_len = idle_fr1 - idle_fr0          # length in frames (float)

        atk_fr0  = atk_act.frame_range[0]
        atk_fr1  = atk_act.frame_range[1]
        atk_len  = atk_fr1 - atk_fr0

        blend    = float(props.blend_frames)

        idle_ws  = 1.0
        idle_we  = idle_ws + idle_len

        atk_ws   = idle_we - blend             # attack starts 'blend' frames early
        atk_we   = atk_ws  + atk_len

        # ── Clear active action before bake ──────────────────────────────────
        # If anim.action is set, Blender treats it as "tweak mode" and evaluates
        # it on top of the NLA strips, overriding our blend result.  Clear it so
        # the bake captures purely the NLA-evaluated pose.
        original_action = anim.action
        anim.action = None

        # ── Create temp NLA tracks ────────────────────────────────────────────
        # Track added first = index 0 = BOTTOM = lower priority.
        # Track added second = higher index = TOP = higher priority.
        # NLA evaluates bottom→top, so attack (top) overrides idle (bottom).

        track_idle = anim.nla_tracks.new()
        track_idle.name = "_nla_blend_idle_tmp"
        strip_idle = track_idle.strips.new("_idle_tmp", int(idle_ws), idle_act)
        strip_idle.frame_start        = idle_ws
        strip_idle.frame_end          = idle_we
        strip_idle.action_frame_start = idle_fr0
        strip_idle.action_frame_end   = idle_fr1
        strip_idle.blend_type         = 'REPLACE'
        strip_idle.blend_in           = 0.0
        strip_idle.blend_out          = 0.0
        strip_idle.extrapolation      = 'HOLD'   # freeze idle pose before/after strip

        track_atk = anim.nla_tracks.new()
        track_atk.name = "_nla_blend_atk_tmp"
        strip_atk = track_atk.strips.new("_atk_tmp", int(atk_ws), atk_act)
        strip_atk.frame_start        = atk_ws
        strip_atk.frame_end          = atk_we
        strip_atk.action_frame_start = atk_fr0
        strip_atk.action_frame_end   = atk_fr1
        blend_out = float(props.blend_out_frames)

        strip_atk.blend_type         = 'REPLACE'
        strip_atk.blend_in           = blend      # ← cross-fades FROM source at start
        strip_atk.blend_out          = blend_out  # ← cross-fades BACK to source at end

        # ── Save and set scene frame range for bake ───────────────────────────
        saved_fs = context.scene.frame_start
        saved_fe = context.scene.frame_end
        context.scene.frame_start = int(atk_ws)
        context.scene.frame_end   = int(atk_we)

        # Force the depsgraph to pick up the new NLA tracks before baking
        bpy.context.view_layer.update()

        # ── Bake (pose mode + visual_keying captures NLA-evaluated pose) ──────
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode='POSE')
        bpy.ops.pose.select_all(action='SELECT')

        try:
            _run_nla_bake(context, obj, int(atk_ws), int(atk_we))
        except Exception as exc:
            self.report({'ERROR'}, f"NLA bake failed: {exc}")
            anim.action = original_action
            self._cleanup(anim, track_idle, track_atk, saved_mutes,
                          context.scene, saved_fs, saved_fe)
            bpy.ops.object.mode_set(mode='OBJECT')
            return {'CANCELLED'}

        # ── Collect the baked action before restoring state ───────────────────
        baked = anim.action

        # ── Restore NLA tracks, scene state, and original active action ───────
        self._cleanup(anim, track_idle, track_atk, saved_mutes,
                      context.scene, saved_fs, saved_fe)
        anim.action = original_action   # prevents viewport jumping after bake

        if not baked or baked is original_action:
            self.report({'WARNING'},
                "Bake completed but the active action slot is empty — "
                "check the Action Editor"
            )
            bpy.ops.object.mode_set(mode='OBJECT')
            return {'FINISHED'}

        # ── Normalise keyframes to start at frame 1 ───────────────────────────
        # The baked action has keyframes at world frames [atk_ws … atk_we].
        # Shift everything back so frame atk_ws → frame 1, making the action
        # portable and matching what the Sprite Baker expects.
        offset = atk_ws - 1.0
        if abs(offset) > 0.001:
            for fc in baked.fcurves:
                for kp in fc.keyframe_points:
                    kp.co.x           -= offset
                    kp.handle_left.x  -= offset
                    kp.handle_right.x -= offset

        # ── Rename ────────────────────────────────────────────────────────────
        baked.name = props.output_name

        bpy.ops.object.mode_set(mode='OBJECT')

        out_info = f"{int(blend_out)}-frame blend-out" if blend_out > 0 else "no blend-out"
        self.report(
            {'INFO'},
            f"Baked '{props.output_name}': {int(atk_len)} frames, "
            f"{int(blend)}-frame blend-in, {out_info} "
            f"('{idle_act.name}' → '{atk_act.name}')"
        )
        return {'FINISHED'}


# ---------------------------------------------------------------------------
# Panel
# ---------------------------------------------------------------------------

class NLABlend_PT_panel(Panel):
    bl_label      = "NLA Blend Baker"
    bl_idname     = "NLA_BLEND_PT_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category   = 'Anim Tools'

    def draw(self, context):
        layout = self.layout
        props  = context.scene.nla_blend_props
        obj    = context.active_object

        if obj is None or obj.type != 'ARMATURE':
            layout.label(text="Select an armature", icon='ERROR')
            return

        # ── Actions ──────────────────────────────────────────────────────────
        col = layout.column(align=True)
        col.label(text="Actions", icon='ACTION')
        col.prop(props, "idle_action")
        col.prop(props, "attack_action")

        # ── Settings ─────────────────────────────────────────────────────────
        layout.separator()
        col = layout.column(align=True)
        col.label(text="Settings", icon='IPO_EASE_IN_OUT')
        col.prop(props, "blend_frames")
        col.prop(props, "blend_out_frames")
        col.prop(props, "output_name")

        # ── Info summary ──────────────────────────────────────────────────────
        idle_act = props.idle_action
        atk_act  = props.attack_action
        can_bake = idle_act is not None and atk_act is not None and idle_act is not atk_act

        if can_bake:
            atk_frames = int(atk_act.frame_range[1] - atk_act.frame_range[0])
            box = layout.box()
            col = box.column(align=True)
            col.label(
                text=f"Target: {atk_frames} frames",
                icon='INFO',
            )
            col.label(
                text=f"Blend-in:  first {props.blend_frames} frames from source",
            )
            if props.blend_out_frames > 0:
                col.label(
                    text=f"Blend-out: last {props.blend_out_frames} frames back to source",
                )
            else:
                col.label(text="Blend-out: none (hard cut at end)")
            col.separator()
            col.label(text=f"Output: '{props.output_name}'", icon='EXPORT')
            col.label(text="  Use in Sprite Baker as Export Name")

        # ── Bake button ───────────────────────────────────────────────────────
        layout.separator()
        row = layout.row()
        row.scale_y = 1.6
        row.enabled = can_bake
        row.operator("nla_blend.bake", icon='RENDER_ANIMATION')


# ---------------------------------------------------------------------------
# Registration
# ---------------------------------------------------------------------------

classes = (
    NLABlend_Props,
    NLABlend_OT_bake,
    NLABlend_PT_panel,
)


def register():
    for cls in classes:
        bpy.utils.register_class(cls)
    bpy.types.Scene.nla_blend_props = bpy.props.PointerProperty(type=NLABlend_Props)


def unregister():
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)
    del bpy.types.Scene.nla_blend_props


if __name__ == "__main__":
    register()
