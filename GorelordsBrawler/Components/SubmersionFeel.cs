using System;
using Nez;
using GorelordsBrawler.Components.Hazards;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Phase 3 of the acid-deadly-polish-plan: per-player fluid-medium feel.
	/// Attaches to each player entity in <c>ArenaScene</c> after the
	/// <see cref="AcidSurface"/> exists. Each frame:
	///
	///   1. Asks AcidSurface for the LOCAL surface Y at the player's column
	///      (the helper added in PR #6 for sizzle puff placement — scans
	///      down from the head row, ignores splashes on higher platforms).
	///   2. If the player's collider bottom is below that surface (body is
	///      in acid), writes the submerged values onto the sibling
	///      <see cref="PhysicsBody"/>: reduced GravityScale (~0.45) and
	///      non-zero LinearDrag (~0.5). Result: slower fall + momentum
	///      bleeds, "syrupy" syrup-like in-water feel without any swim or
	///      float behaviour (no upward force, no animation hooks).
	///   3. Otherwise restores the dry-land defaults (1.0 / 0.0). Single-
	///      writer assumption — nothing else mutates these fields in the
	///      current design.
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
		[Inspectable, Range(0f, 1f)]
		public float SubmergedGravityScale = 0.45f;

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

			// Local-surface query (PR #6 helper): topmost wet cell at this
			// column at or below the head row. Avoids the bug where a splash
			// on a higher platform sharing this column would be returned as
			// "the surface."
			float localSurface = _acid.GetLocalSurfaceLevelAtX(x, headY);
			IsSubmerged = localSurface < feetY;
			// Depth = how far feet are below the surface (>=0). When dry, feetY <=
			// localSurface so this clamps to 0.
			SubmergedDepth = IsSubmerged ? (feetY - localSurface) : 0f;

			if (IsSubmerged)
			{
				_body.GravityScale = SubmergedGravityScale;
				_body.LinearDrag   = SubmergedDrag;
			}
			else
			{
				_body.GravityScale = 1f;
				_body.LinearDrag   = 0f;
			}
		}
	}
}
