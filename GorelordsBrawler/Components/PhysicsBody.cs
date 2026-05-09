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

			Velocity.Y += gravity * dt;

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
