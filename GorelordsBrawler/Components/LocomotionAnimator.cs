using System;
using Nez;
using Nez.Sprites;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Drives sprite animation state and playback speed from actual character velocity.
	/// Switches idle/run animations and scales SpriteAnimator.Speed proportionally to
	/// how fast the character is moving relative to their max move speed. This keeps
	/// feet in sync with ground movement at any speed — including during deceleration
	/// from hits or other external forces.
	///
	/// Tune SpriteData.runAnimSpeed in the character JSON to calibrate foot sync:
	/// increase if the walk animation looks too slow, decrease if it spins too fast.
	/// </summary>
	public class LocomotionAnimator : Component, IUpdatable
	{
		private PhysicsBody _body;
		private MovementStats _movement;
		private SpriteAnimator _animator;
		private SpriteData _spriteData;

		public override void OnAddedToEntity()
		{
			_body = Entity.GetComponent<PhysicsBody>();
			_movement = Entity.GetComponent<MovementStats>();
			_animator = Entity.GetComponent<SpriteAnimator>();
			_spriteData = Entity.GetComponent<SpriteData>();
			UpdateOrder = GameConstants.Physics.LocomotionAnimatorUpdateOrder;
		}

		public void Update()
		{
			// Flip sprite to face the correct direction
			_animator.FlipX = _body.FacingDirection < 0;

			var isMoving = Math.Abs(_body.Velocity.X) > 0.1f;

			// Switch between idle and run animations
			var targetAnim = isMoving ? GameConstants.Animations.Run : GameConstants.Animations.Idle;
			if (!_animator.IsAnimationActive(targetAnim))
			{
				_animator.Play(targetAnim);
			}

			// Scale animation speed proportionally to current velocity.
			// At max moveSpeed, normalizedSpeed = 1.0 and Speed = runAnimSpeed.
			// At half speed, animation plays at half rate — feet stay in sync.
			if (isMoving)
			{
				var normalizedSpeed = Math.Abs(_body.Velocity.X) / _movement.MoveSpeed;
				var runAnimSpeed = _spriteData != null ? _spriteData.RunAnimSpeed : 1.0f;
				_animator.Speed = normalizedSpeed * runAnimSpeed;
			}
			else
			{
				_animator.Speed = 1.0f;
			}
		}
	}
}
