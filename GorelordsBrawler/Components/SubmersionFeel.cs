using System;
using Nez;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Per-player fluid-medium state + feel. Attaches to each player entity in
	/// <c>ArenaScene</c> after the <see cref="AcidSurface"/> exists. Each frame:
	///
	///   1. Asks AcidSurface for the local BODY surface Y at the player's
	///      column (<see cref="AcidSurface.GetBodySurfaceLevelAtX"/> — the
	///      dense-occupancy waterline). The threshold-1 droplet query used
	///      pre-escape-fix let stray spray at head height fake a full-body
	///      depth reading — the same failure class that ratcheted the drop-
	///      logs into the air, and the reason the old breach jump almost
	///      never fired.
	///   2. While submerged, writes the fluid medium onto the sibling
	///      <see cref="PhysicsBody"/>: buoyancy replaces gravity (Smash-style
	///      float-to-surface — see GameConstants.Hazards.AcidBuoyancyAccel)
	///      plus LinearDrag momentum bleed. It also BANKS the aerial action
	///      (water refreshes your air jump, like landing) and clears fast-
	///      fall — so a jump press in the brief dry bob-window at the surface
	///      still produces a full-strength double jump instead of nothing.
	///   3. Otherwise restores the dry-land defaults. Single-writer
	///      assumption — nothing else mutates these fields in the current
	///      design.
	///
	/// <see cref="IsSubmerged"/> is exposed for other systems that want
	/// to read the same state (HUD, future visibility tweaks etc.). It's
	/// also what the liquid shader uses to decide which players to
	/// "see-through" — read by <c>LiquidPostProcessor</c> each frame.
	///
	/// All knobs are <c>[Inspectable, Range(...)]</c> so you can tune live
	/// without rebuilds — same convention as the rest of the acid systems.
	/// </summary>
	public class SubmersionFeel : Component, IUpdatable
	{
		// ── Live-tunable feel knobs ──────────────────────────────────────────
		[Inspectable, Range(0f, 3000f)]
		public float BuoyancyAccel        = GameConstants.Hazards.AcidBuoyancyAccel;

		[Inspectable, Range(0f, 600f)]
		public float BuoyantMaxRiseSpeed  = GameConstants.Hazards.AcidBuoyantMaxRiseSpeed;

		[Inspectable, Range(0f, 4f)]
		public float SubmergedDrag         = 0.5f;

		// ── Refs / state ─────────────────────────────────────────────────────
		private readonly AcidSurface _acid;
		private PhysicsBody          _body;
		private Collider             _collider;

		public bool IsSubmerged { get; private set; }

		/// <summary>
		/// How far the body's FEET are below the local acid surface, in pixels
		/// (0 when dry or only touching the surface). This is the single source of
		/// depth truth for Phase B: ContactHazard reads it for depth-scaled damage
		/// and SwimAbility reads <see cref="IsSubmerged"/> to gate strokes. Measured
		/// at the feet (collider bottom) so it grows smoothly as a body sinks —
		/// matching "the deeper you're launched, the worse it bites."
		/// </summary>
		public float SubmergedDepth { get; private set; }

		public SubmersionFeel(AcidSurface acid)
		{
			_acid = acid;
		}

		public override void OnAddedToEntity()
		{
			_body     = Entity.GetComponent<PhysicsBody>();
			_collider = Entity.GetComponent<Collider>();
			// Run BEFORE the abilities (Walk/Jump/Swim, UpdateOrder 0) and before
			// PhysicsBody (100) so IsSubmerged/SubmergedDepth are fresh when
			// SwimAbility and ContactHazard read them this same frame. Negative
			// order = earliest. (ContactHazard lives on the acid entity, not the
			// player, but reads this via the per-player closure in ArenaScene.)
			UpdateOrder = -10;
		}

		public void Update()
		{
			if (_acid == null || _body == null || _collider == null) return;

			float x      = Entity.Transform.Position.X;
			float headY  = _collider.Bounds.Top;
			float feetY  = _collider.Bounds.Bottom;

			// Body-surface query: topmost DENSELY wet cell at this column at or
			// below the head row. Ignores splashes on higher platforms sharing
			// the column AND stray spray droplets (the threshold-1 query read
			// those as "the surface" and corrupted every depth-gated system).
			float localSurface = _acid.GetBodySurfaceLevelAtX(x, headY);
			IsSubmerged = localSurface < feetY;
			// Depth = how far feet are below the surface (>=0). When dry, feetY <=
			// localSurface so this clamps to 0.
			SubmergedDepth = IsSubmerged ? (feetY - localSurface) : 0f;

			if (IsSubmerged)
			{
				_body.BuoyancyAccel        = BuoyancyAccel;
				_body.BuoyancyMaxRiseSpeed = BuoyantMaxRiseSpeed;
				_body.LinearDrag           = SubmergedDrag;
				// Water banks the aerial action and cancels fast-fall, exactly
				// like landing — this is what makes the surface bob safe: a
				// press in a momentarily-dry frame is a full double jump via
				// JumpAbility instead of a dead input (the old 2-frame luck
				// window found by the DeepKnockIn E2E trace).
				_body.HasAerialAction = true;
				_body.FastFalling     = false;
			}
			else
			{
				_body.BuoyancyAccel        = 0f;
				_body.BuoyancyMaxRiseSpeed = 0f;
				_body.LinearDrag           = 0f;
			}
		}
	}
}
