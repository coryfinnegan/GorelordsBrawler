using System;
using Nez;
using Nez.Sprites;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Drives sprite animation state from character velocity and grounded state.
	///
	/// Priority (highest first):
	///   attack / attack_left — plays to completion via OnAnimationCompletedEvent;
	///                          restores entity scale when done
	///   jump                 — airborne; speed driven by |Velocity.Y| / JumpSpeed
	///   landing window       — brief Pause() on last jump frame after touching ground
	///   run / run_left       — grounded and moving; speed driven by |Velocity.X| / MoveSpeed
	///   idle / idle_left     — grounded and still
	///
	/// Directional variants (idle_left, run_left, attack_left) play when facing left and the
	/// animation exists in the atlas, overriding the global FlipX to avoid double-mirroring.
	///
	/// Attack scale: if SpriteData.AttackSpriteScale > 0, the entity's transform scale is
	/// temporarily changed during the attack animation and restored on completion.  Use this
	/// when the attack atlas frames have different pixel dimensions than the main atlas.
	/// </summary>
	public class LocomotionAnimator : Component, IUpdatable
	{
		private PhysicsBody _body;
		private MovementStats _movement;
		private SpriteAnimator _animator;
		private SpriteData _spriteData;
		private MeleeAttack _meleeAttack;

		private bool _hasJumpAnim;
		private bool _hasJumpLeftAnim;
		private bool _hasIdleLeftAnim;
		private bool _hasRunLeftAnim;
		private bool _hasAttackFromIdleAnim;
		private bool _hasAttackFromIdleLeftAnim;
		private bool _hasAttackFromRunAnim;
		private bool _hasAttackFromRunLeftAnim;

		private bool _wasGrounded;
		private float _landingTimer;

		// Attack lifecycle — independent of hitbox cooldown timing.
		private float _defaultScale;
		private bool _isPlayingAttack;   // true until OnAnimationCompletedEvent fires
		private bool _prevIsAttacking;   // for rising-edge detection on MeleeAttack.IsAttacking

		/// <summary>True while an attack animation is playing (cleared by OnAnimationCompletedEvent).</summary>
		public bool IsPlayingAttack => _isPlayingAttack;

		public override void OnAddedToEntity()
		{
			_body = Entity.GetComponent<PhysicsBody>();
			_movement = Entity.GetComponent<MovementStats>();
			_animator = Entity.GetComponent<SpriteAnimator>();
			_spriteData = Entity.GetComponent<SpriteData>();
			_meleeAttack = Entity.GetComponent<MeleeAttack>();

			_hasJumpAnim     = _animator.Animations.ContainsKey(GameConstants.Animations.Jump);
			_hasJumpLeftAnim = _animator.Animations.ContainsKey(GameConstants.Animations.JumpLeft);
			_hasIdleLeftAnim = _animator.Animations.ContainsKey(GameConstants.Animations.IdleLeft);
			_hasRunLeftAnim = _animator.Animations.ContainsKey(GameConstants.Animations.RunLeft);
			_hasAttackFromIdleAnim     = _animator.Animations.ContainsKey(GameConstants.Animations.AttackFromIdle);
			_hasAttackFromIdleLeftAnim = _animator.Animations.ContainsKey(GameConstants.Animations.AttackFromIdleLeft);
			_hasAttackFromRunAnim      = _animator.Animations.ContainsKey(GameConstants.Animations.AttackFromRun);
			_hasAttackFromRunLeftAnim  = _animator.Animations.ContainsKey(GameConstants.Animations.AttackFromRunLeft);

			UpdateOrder = GameConstants.Physics.LocomotionAnimatorUpdateOrder;
			_wasGrounded = _body.Grounded;
			_defaultScale = Entity.Transform.Scale.X;

			_animator.OnAnimationCompletedEvent += OnAnimationCompleted;
		}

		public override void OnRemovedFromEntity()
		{
			_animator.OnAnimationCompletedEvent -= OnAnimationCompleted;
		}

		private void OnAnimationCompleted(string animationName)
		{
			if (animationName == GameConstants.Animations.AttackFromIdle ||
				animationName == GameConstants.Animations.AttackFromIdleLeft ||
				animationName == GameConstants.Animations.AttackFromRun ||
				animationName == GameConstants.Animations.AttackFromRunLeft)
			{
				_isPlayingAttack = false;
				Entity.Transform.SetScale(_defaultScale);
			}
		}

		public void Update()
		{
			// If an attack animation is actively playing, preserve its FlipX and bail out early.
			// _prevIsAttacking is updated here so the rising-edge detector stays correct when
			// the animation ends and a new attack can be triggered.
			if (_isPlayingAttack)
			{
				_wasGrounded = _body.Grounded;
				if (_meleeAttack != null)
				{
					_prevIsAttacking = _meleeAttack.IsAttacking;
				}
				return;
			}

			// Flip sprite to face the correct direction (directional variants override below)
			_animator.FlipX = _body.FacingDirection < 0;

			// ── Attack (highest priority) ─────────────────────────────────────────
			var hasAnyAttackAnim = _hasAttackFromIdleAnim || _hasAttackFromIdleLeftAnim ||
			                       _hasAttackFromRunAnim  || _hasAttackFromRunLeftAnim;
			if (hasAnyAttackAnim && _meleeAttack != null)
			{
				var isAttacking = _meleeAttack.IsAttacking;
				var attackJustStarted = isAttacking && !_prevIsAttacking;
				_prevIsAttacking = isAttacking;

				if (attackJustStarted)
				{
					var wasMoving  = Math.Abs(_body.Velocity.X) > 0.1f;
					var facingLeft = _body.FacingDirection < 0;

					// Pick the best available directional variant.
					// animHasLeft = true means the chosen sprite is a left-facing render;
					// it must not be double-mirrored when the character faces left.
					string animName;
					bool animHasLeft;
					if (wasMoving)
					{
						if (facingLeft && _hasAttackFromRunLeftAnim)
						{
							animName = GameConstants.Animations.AttackFromRunLeft;
							animHasLeft = true;
						}
						else if (_hasAttackFromRunAnim)
						{
							animName = GameConstants.Animations.AttackFromRun;
							animHasLeft = false;
						}
						else
						{
							// Only left-facing available — mirror it to face right
							animName = GameConstants.Animations.AttackFromRunLeft;
							animHasLeft = true;
						}
					}
					else
					{
						if (facingLeft && _hasAttackFromIdleLeftAnim)
						{
							animName = GameConstants.Animations.AttackFromIdleLeft;
							animHasLeft = true;
						}
						else if (_hasAttackFromIdleAnim)
						{
							animName = GameConstants.Animations.AttackFromIdle;
							animHasLeft = false;
						}
						else
						{
							// Only left-facing available — mirror it to face right
							animName = GameConstants.Animations.AttackFromIdleLeft;
							animHasLeft = true;
						}
					}

					// Global FlipX (set at top of Update) is correct for right-facing anims.
					// For left-facing anims: no flip when facing left, flip when facing right.
					if (animHasLeft)
					{
						_animator.FlipX = !facingLeft;
					}

					_animator.Speed = _spriteData != null && _spriteData.AttackAnimSpeed > 0
						? _spriteData.AttackAnimSpeed
						: 1.0f;

					if (_spriteData != null && _spriteData.AttackSpriteScale > 0f)
					{
						Entity.Transform.SetScale(_spriteData.AttackSpriteScale);
					}

					_animator.Play(animName, SpriteAnimator.LoopMode.ClampForever);
					_isPlayingAttack = true;
				}

				if (_isPlayingAttack)
				{
					_wasGrounded = _body.Grounded;
					return;
				}
			}

			// ── Locomotion ────────────────────────────────────────────────────────
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
				// Speed must be set before Play() so SetFrame() computes a valid FrameTimeLeft.
				var jumpAnimSpeed = _spriteData != null ? _spriteData.JumpAnimSpeed : 1.0f;
				_animator.Speed = (Math.Abs(_body.Velocity.Y) / _movement.JumpSpeed) * jumpAnimSpeed;

				var useJumpLeft = _hasJumpLeftAnim && _body.FacingDirection < 0;
				var jumpAnim    = useJumpLeft ? GameConstants.Animations.JumpLeft : GameConstants.Animations.Jump;

				if (useJumpLeft)
				{
					_animator.FlipX = false;
				}

				if (justLeftGround || !_animator.IsAnimationActive(jumpAnim))
				{
					_animator.Play(jumpAnim, SpriteAnimator.LoopMode.ClampForever);
				}
			}
			else if (_landingTimer > 0f && _hasJumpAnim)
			{
				// Landing window — freeze on the last jump frame for a brief squash beat.
				// Use Pause() rather than Speed=0 to avoid corrupting FrameTimeLeft on the
				// next Play() call (SetFrame computes FrameTimeLeft = 1/(fps*Speed), so
				// Speed=0 would produce Infinity and permanently stall the next animation).
				if (_hasJumpLeftAnim && _body.FacingDirection < 0)
				{
					_animator.FlipX = false;
				}
				_animator.Pause();
			}
			else
			{
				// Grounded — idle or run.
				// Speed must be set before Play() so SetFrame() computes a valid FrameTimeLeft.
				var isMoving = Math.Abs(_body.Velocity.X) > 0.1f;

				if (isMoving)
				{
					var normalizedSpeed = Math.Abs(_body.Velocity.X) / _movement.MoveSpeed;
					var runAnimSpeed = _spriteData != null ? _spriteData.RunAnimSpeed : 1.0f;
					_animator.Speed = normalizedSpeed * runAnimSpeed;

					var useRunLeft = _hasRunLeftAnim && _body.FacingDirection < 0;
					if (useRunLeft)
					{
						// Left-facing run sprite — no mirror needed; override the global FlipX.
						_animator.FlipX = false;
						if (!_animator.IsAnimationActive(GameConstants.Animations.RunLeft))
						{
							_animator.Play(GameConstants.Animations.RunLeft);
						}
					}
					else
					{
						if (!_animator.IsAnimationActive(GameConstants.Animations.Run))
						{
							_animator.Play(GameConstants.Animations.Run);
						}
					}
				}
				else
				{
					_animator.Speed = 1.0f;
					var useDedicatedLeft = _hasIdleLeftAnim && _body.FacingDirection < 0;
					if (useDedicatedLeft)
					{
						// Left-facing idle sprite — no mirror needed; override the global FlipX.
						_animator.FlipX = false;
						if (!_animator.IsAnimationActive(GameConstants.Animations.IdleLeft))
						{
							_animator.Play(GameConstants.Animations.IdleLeft);
						}
					}
					else
					{
						if (!_animator.IsAnimationActive(GameConstants.Animations.Idle))
						{
							_animator.Play(GameConstants.Animations.Idle);
						}
					}
				}
			}
		}
	}
}
