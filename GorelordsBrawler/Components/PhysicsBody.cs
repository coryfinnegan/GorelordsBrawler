using System;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	public class PhysicsBody : Component, IUpdatable
	{
		public Vector2 Velocity;
		public bool Grounded;
		public int FacingDirection = 1;

		/// <summary>
		/// True while the character is rising and the jump button is still held.
		/// Set by JumpAbility on launch, cleared when button is released or apex is reached.
		/// </summary>
		public bool JumpHeld;

		/// <summary>
		/// Time since the character was last grounded. Used for coyote time.
		/// </summary>
		public float TimeSinceGrounded;

		/// <summary>
		/// True when the player has activated fast fall (tap down while airborne and falling).
		/// Multiplies gravity by FastFallMultiplier. Reset on landing.
		/// </summary>
		public bool FastFalling;

		/// <summary>
		/// True while the character has an aerial action available (double jump OR aerial attack).
		/// Set on initial jump, consumed by whichever comes first. Reset on landing.
		/// </summary>
		public bool HasAerialAction;

		/// <summary>
		/// When true, PhysicsBody skips all physics processing (gravity, movement, collision).
		/// Used by LedgeHangAbility to freeze the character in place during ledge hang/climb.
		/// </summary>
		public bool SuspendPhysics;

		/// <summary>
		/// Multiplier applied to the final per-frame gravity. 1.0 = dry-land
		/// physics (no change). Set below 1 (e.g. 0.45) to simulate reduced
		/// gravity inside a fluid medium — see <c>SubmersionFeel</c>. Set above
		/// 1 for "heavy" zones. Composes after the existing fall/short-hop/
		/// fast-fall multipliers so those still feel right; only the final
		/// magnitude is scaled.
		/// </summary>
		public float GravityScale = 1f;

		/// <summary>
		/// Per-second linear velocity damping. 0 = no drag (default, dry land).
		/// Each frame velocity is multiplied by (1 - clamp(LinearDrag * dt, 0, 1)),
		/// bleeding both axes uniformly — used by <c>SubmersionFeel</c> to give
		/// "syrupy" momentum loss while submerged. Applied AFTER ability
		/// systems write Velocity, BEFORE gravity adds, so the player's input
		/// still takes effect but isn't preserved frame-to-frame in fluid.
		/// </summary>
		public float LinearDrag = 0f;

		/// <summary>
		/// Upward acceleration (px/s²) while immersed in a fluid. When &gt; 0 it
		/// REPLACES gravity entirely (including the fall/short-hop/fast-fall
		/// multipliers — those are air-feel rules): the body decelerates a plunge
		/// and then floats upward, Smash-style. Set each frame by
		/// <c>SubmersionFeel</c>; 0 (default) = dry land, normal gravity.
		/// </summary>
		public float BuoyancyAccel = 0f;

		/// <summary>
		/// Cap on the upward speed the buoyancy itself can produce. Faster
		/// upward motion from another source (a jump) is left untouched —
		/// buoyancy simply stops contributing above the cap, it never brakes.
		/// </summary>
		public float BuoyancyMaxRiseSpeed = 0f;

		private Mover _mover;
		private MovementStats _movement;

		public override void OnAddedToEntity()
		{
			_mover = Entity.GetComponent<Mover>();
			_movement = Entity.GetComponent<MovementStats>();
			UpdateOrder = GameConstants.Physics.PhysicsBodyUpdateOrder;
		}

		public void Update()
		{
			if (SuspendPhysics)
			{
				return;
			}

			var dt = Math.Min(Time.DeltaTime, GameConstants.Physics.MaxDeltaTime);

			// Track time since last grounded (for coyote time)
			if (Grounded)
			{
				TimeSinceGrounded = 0f;
			}
			else
			{
				TimeSinceGrounded += dt;
			}

			// Reset aerial state on landing
			if (Grounded)
			{
				FastFalling = false;
				HasAerialAction = true;
			}

			// Apply linear drag (fluid medium support). Runs BEFORE gravity so
			// gravity's per-frame contribution to Velocity.Y isn't immediately
			// damped away — the integrated velocity is what drag bleeds. Both
			// axes share the same coefficient; if jumping out of acid ends up
			// feeling mushy this can be split to X-only.
			if (LinearDrag > 0f)
			{
				float dampen = 1f - MathHelper.Clamp(LinearDrag * dt, 0f, 1f);
				Velocity *= dampen;
			}

			if (BuoyancyAccel > 0f)
			{
				// Fluid medium: buoyancy replaces gravity outright. Accelerate
				// upward only while below the rise cap (up is negative Y), and
				// never past it — but a faster ascent from a jump passes through
				// untouched, so a submerged jump keeps its full launch speed.
				if (Velocity.Y > -BuoyancyMaxRiseSpeed)
				{
					Velocity.Y = MathHelper.Max(
						-BuoyancyMaxRiseSpeed, Velocity.Y - BuoyancyAccel * dt);
				}
			}
			else
			{
				// Gravity with variable multipliers for game feel:
				// - Fast falling: even higher gravity for aggressive aerial approaches
				// - Falling: higher gravity for snappy descent
				// - Rising with jump released: higher gravity for short hops
				// - Rising with jump held: normal gravity for full jump arc
				var gravity = _movement.Gravity;
				if (FastFalling)
				{
					gravity *= _movement.FallMultiplier * _movement.FastFallMultiplier;
				}
				else if (Velocity.Y > 0)
				{
					gravity *= _movement.FallMultiplier;
				}
				else if (Velocity.Y < 0 && !JumpHeld)
				{
					gravity *= _movement.ShortHopMultiplier;
				}

				// GravityScale is the LAST multiplier — composes after the
				// game-feel multipliers above so e.g. fast-falling underwater
				// still fast-falls (just from a smaller base).
				gravity *= GravityScale;

				Velocity.Y += gravity * dt;
			}

			// Move with collision
			var motion = Velocity * dt;
			var collided = _mover.Move(motion, out var collisionResult);

			if (collided)
			{
				if (collisionResult.Normal.Y < GameConstants.Physics.GroundNormalThreshold)
				{
					Grounded = true;
					Velocity.Y = 0;
				}

				if (collisionResult.Normal.Y > GameConstants.Physics.CeilingNormalThreshold)
				{
					Velocity.Y = 0;
				}
			}
			else
			{
				Grounded = false;
			}
		}
	}
}
