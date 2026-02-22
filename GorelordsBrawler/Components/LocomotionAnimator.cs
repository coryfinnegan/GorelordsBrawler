using System;
using Nez;
using Nez.Sprites;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Drives sprite animation state from character velocity and grounded state.
	/// Three states: idle (grounded, not moving), run (grounded, moving), jump (airborne).
	/// Run animation speed scales with horizontal velocity to keep feet in sync.
	/// Jump animation speed scales with vertical velocity — naturally slow at apex,
	/// fast on launch and during fall — plus an 80ms landing hold on the last frame.
	/// </summary>
	public class LocomotionAnimator : Component, IUpdatable
	{
		private PhysicsBody _body;
		private MovementStats _movement;
		private SpriteAnimator _animator;
		private SpriteData _spriteData;
		private bool _hasJumpAnim;
		private bool _wasGrounded;
		private float _landingTimer;

		public override void OnAddedToEntity()
		{
			_body = Entity.GetComponent<PhysicsBody>();
			_movement = Entity.GetComponent<MovementStats>();
			_animator = Entity.GetComponent<SpriteAnimator>();
			_spriteData = Entity.GetComponent<SpriteData>();
			_hasJumpAnim = _animator.Animations.ContainsKey(GameConstants.Animations.Jump);
			UpdateOrder = GameConstants.Physics.LocomotionAnimatorUpdateOrder;
			_wasGrounded = _body.Grounded;
		}

		public void Update()
		{
			// Flip sprite to face the correct direction
			_animator.FlipX = _body.FacingDirection < 0;

			var justLanded = !_wasGrounded && _body.Grounded;
			var justLeftGround = _wasGrounded && !_body.Grounded;
			_wasGrounded = _body.Grounded;

			if (justLanded && _hasJumpAnim)
			{
				_landingTimer = GameConstants.Physics.LandingWindowDuration;
			}

			if (_landingTimer > 0f)
			{
				_landingTimer -= Time.DeltaTime;
			}

			if (!_body.Grounded && _hasJumpAnim)
			{
				// Airborne — restart animation on takeoff, then drive speed via vertical velocity.
				// At apex Velocity.Y ≈ 0 so Speed ≈ 0, giving a natural hang-time pause.
				if (justLeftGround || !_animator.IsAnimationActive(GameConstants.Animations.Jump))
				{
					_animator.Play(GameConstants.Animations.Jump, SpriteAnimator.LoopMode.ClampForever);
				}

				var jumpAnimSpeed = _spriteData != null ? _spriteData.JumpAnimSpeed : 1.0f;
				_animator.Speed = (Math.Abs(_body.Velocity.Y) / _movement.JumpSpeed) * jumpAnimSpeed;
			}
			else if (_landingTimer > 0f && _hasJumpAnim)
			{
				// Landing window — freeze on the last jump frame for a brief squash beat.
				_animator.Speed = 0f;
			}
			else
			{
				// Grounded — idle or run
				var isMoving = Math.Abs(_body.Velocity.X) > 0.1f;
				var targetAnim = isMoving ? GameConstants.Animations.Run : GameConstants.Animations.Idle;

				if (!_animator.IsAnimationActive(targetAnim))
				{
					_animator.Play(targetAnim);
				}

				// Scale run animation speed proportionally to current velocity
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
}
