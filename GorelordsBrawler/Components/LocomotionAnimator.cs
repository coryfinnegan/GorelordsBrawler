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
	///   attack (LeftHand / RightHand) — plays to completion via OnAnimationCompletedEvent;
	///                                   restores entity scale when done; alternates via MeleeAttack.AttackIndex
	///   jump                          — airborne; speed driven by |Velocity.Y| / JumpSpeed
	///   landing window                — brief Pause() on last jump frame after touching ground
	///   run / run_FaceLeft            — grounded and moving; speed driven by |Velocity.X| / MoveSpeed
	///   idle / idle_FaceLeft          — grounded and still
	///
	/// Directional variants (IdleFaceLeft, RunFaceLeft, attack FaceLeft) play when facing left
	/// and the animation exists in the atlas, overriding the global FlipX to avoid double-mirroring.
	///
	/// Animation keys are built via AnimationKeyBuilder using SpriteData.AnimPrefix so they are
	/// character-specific (e.g. "FutureAxe_Idle") without any hardcoding here.
	/// </summary>
	public class LocomotionAnimator : Component, IUpdatable
	{
		private PhysicsBody _body;
		private MovementStats _movement;
		private SpriteAnimator _animator;
		private SpriteData _spriteData;
		private MeleeAttack _meleeAttack;

		// Built from SpriteData.AnimPrefix — all animation key lookups go through this.
		private AnimationKeyBuilder _keys;

		// Existence flags — set once in OnAddedToEntity based on what the atlas actually contains.
		private bool _hasJumpAnim;
		private bool _hasJumpFaceLeftAnim;
		private bool _hasIdleFaceLeftAnim;
		private bool _hasRunFaceLeftAnim;

		// LeftHand attack existence flags
		private bool _hasAttackIdleLeftHandAnim;
		private bool _hasAttackIdleLeftHandFaceLeftAnim;
		private bool _hasAttackRunLeftHandAnim;
		private bool _hasAttackRunLeftHandFaceLeftAnim;

		// RightHand attack existence flags (mirrored action)
		private bool _hasAttackIdleRightHandAnim;
		private bool _hasAttackIdleRightHandFaceLeftAnim;
		private bool _hasAttackRunRightHandAnim;
		private bool _hasAttackRunRightHandFaceLeftAnim;

		private bool _wasGrounded;
		private float _landingTimer;

		// Attack lifecycle — independent of hitbox cooldown timing.
		private float _defaultScale;
		private bool _isPlayingAttack;   // true until OnAnimationCompletedEvent fires
		private bool _prevIsAttacking;   // for rising-edge detection on MeleeAttack.IsAttacking
		private bool _pendingAttack;     // new attack triggered while animation was still playing

		/// <summary>True while an attack animation is playing (cleared by OnAnimationCompletedEvent).</summary>
		public bool IsPlayingAttack => _isPlayingAttack;

		public override void OnAddedToEntity()
		{
			_body        = Entity.GetComponent<PhysicsBody>();
			_movement    = Entity.GetComponent<MovementStats>();
			_animator    = Entity.GetComponent<SpriteAnimator>();
			_spriteData  = Entity.GetComponent<SpriteData>();
			_meleeAttack = Entity.GetComponent<MeleeAttack>();

			_keys = new AnimationKeyBuilder(_spriteData?.AnimPrefix ?? "");

			_hasJumpAnim           = _animator.Animations.ContainsKey(_keys.Jump);
			_hasJumpFaceLeftAnim   = _animator.Animations.ContainsKey(_keys.JumpFaceLeft);
			_hasIdleFaceLeftAnim   = _animator.Animations.ContainsKey(_keys.IdleFaceLeft);
			_hasRunFaceLeftAnim    = _animator.Animations.ContainsKey(_keys.RunFaceLeft);

			_hasAttackIdleLeftHandAnim            = _animator.Animations.ContainsKey(_keys.AttackIdleLeftHand);
			_hasAttackIdleLeftHandFaceLeftAnim     = _animator.Animations.ContainsKey(_keys.AttackIdleLeftHandFaceLeft);
			_hasAttackRunLeftHandAnim             = _animator.Animations.ContainsKey(_keys.AttackRunLeftHand);
			_hasAttackRunLeftHandFaceLeftAnim      = _animator.Animations.ContainsKey(_keys.AttackRunLeftHandFaceLeft);

			_hasAttackIdleRightHandAnim           = _animator.Animations.ContainsKey(_keys.AttackIdleRightHand);
			_hasAttackIdleRightHandFaceLeftAnim    = _animator.Animations.ContainsKey(_keys.AttackIdleRightHandFaceLeft);
			_hasAttackRunRightHandAnim            = _animator.Animations.ContainsKey(_keys.AttackRunRightHand);
			_hasAttackRunRightHandFaceLeftAnim     = _animator.Animations.ContainsKey(_keys.AttackRunRightHandFaceLeft);

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
			// Check if the completed animation is any attack animation for this character
			if (_keys != null)
			{
				foreach (var key in _keys.AllAttackAnims)
				{
					if (animationName == key)
					{
						_isPlayingAttack = false;
						Entity.Transform.SetScale(_defaultScale);
						return;
					}
				}
			}
		}

		public void Update()
		{
			if (_isPlayingAttack)
			{
				_wasGrounded = _body.Grounded;
				if (_meleeAttack != null)
				{
					var isAttackingNow = _meleeAttack.IsAttacking;
					if (isAttackingNow && !_prevIsAttacking)
						_pendingAttack = true;
					_prevIsAttacking = isAttackingNow;
				}
				return;
			}

			// Flip sprite to face the correct direction (directional variants override below)
			_animator.FlipX = _body.FacingDirection < 0;

			// ── Attack (highest priority) ─────────────────────────────────────────
			var hasAnyLeftHandAnim  = _hasAttackIdleLeftHandAnim  || _hasAttackIdleLeftHandFaceLeftAnim  ||
			                          _hasAttackRunLeftHandAnim   || _hasAttackRunLeftHandFaceLeftAnim;
			var hasAnyRightHandAnim = _hasAttackIdleRightHandAnim || _hasAttackIdleRightHandFaceLeftAnim ||
			                          _hasAttackRunRightHandAnim  || _hasAttackRunRightHandFaceLeftAnim;
			var hasAnyAttackAnim    = hasAnyLeftHandAnim || hasAnyRightHandAnim;

			if (hasAnyAttackAnim && _meleeAttack != null)
			{
				var isAttacking = _meleeAttack.IsAttacking;
				var attackJustStarted = (isAttacking && !_prevIsAttacking) || _pendingAttack;
				_pendingAttack = false;
				_prevIsAttacking = isAttacking;

				if (attackJustStarted)
				{
					var wasMoving  = Math.Abs(_body.Velocity.X) > 0.1f;
					var facingLeft = _body.FacingDirection < 0;

					// Use right-hand animation set if: AttackIndex == 1 AND right-hand anims exist
					var useRightHand = _meleeAttack.AttackIndex == 1 && hasAnyRightHandAnim;

					string animName;
					bool animHasLeft;

					if (wasMoving)
					{
						if (useRightHand)
						{
							if (facingLeft && _hasAttackRunRightHandFaceLeftAnim)
							{
								animName = _keys.AttackRunRightHandFaceLeft;
								animHasLeft = true;
							}
							else if (_hasAttackRunRightHandAnim)
							{
								animName = _keys.AttackRunRightHand;
								animHasLeft = false;
							}
							else
							{
								animName = _keys.AttackRunRightHandFaceLeft;
								animHasLeft = true;
							}
						}
						else
						{
							if (facingLeft && _hasAttackRunLeftHandFaceLeftAnim)
							{
								animName = _keys.AttackRunLeftHandFaceLeft;
								animHasLeft = true;
							}
							else if (_hasAttackRunLeftHandAnim)
							{
								animName = _keys.AttackRunLeftHand;
								animHasLeft = false;
							}
							else
							{
								animName = _keys.AttackRunLeftHandFaceLeft;
								animHasLeft = true;
							}
						}
					}
					else
					{
						if (useRightHand)
						{
							if (facingLeft && _hasAttackIdleRightHandFaceLeftAnim)
							{
								animName = _keys.AttackIdleRightHandFaceLeft;
								animHasLeft = true;
							}
							else if (_hasAttackIdleRightHandAnim)
							{
								animName = _keys.AttackIdleRightHand;
								animHasLeft = false;
							}
							else
							{
								animName = _keys.AttackIdleRightHandFaceLeft;
								animHasLeft = true;
							}
						}
						else
						{
							if (facingLeft && _hasAttackIdleLeftHandFaceLeftAnim)
							{
								animName = _keys.AttackIdleLeftHandFaceLeft;
								animHasLeft = true;
							}
							else if (_hasAttackIdleLeftHandAnim)
							{
								animName = _keys.AttackIdleLeftHand;
								animHasLeft = false;
							}
							else
							{
								animName = _keys.AttackIdleLeftHandFaceLeft;
								animHasLeft = true;
							}
						}
					}

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
			var justLanded     = !_wasGrounded && _body.Grounded;
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
				var jumpAnimSpeed = _spriteData != null ? _spriteData.JumpAnimSpeed : 1.0f;
				_animator.Speed = (Math.Abs(_body.Velocity.Y) / _movement.JumpSpeed) * jumpAnimSpeed;

				var useJumpFaceLeft = _hasJumpFaceLeftAnim && _body.FacingDirection < 0;
				var jumpAnim        = useJumpFaceLeft ? _keys.JumpFaceLeft : _keys.Jump;

				if (useJumpFaceLeft)
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
				if (_hasJumpFaceLeftAnim && _body.FacingDirection < 0)
				{
					_animator.FlipX = false;
				}
				_animator.Pause();
			}
			else
			{
				var isMoving = Math.Abs(_body.Velocity.X) > 0.1f;

				if (isMoving)
				{
					var normalizedSpeed = Math.Abs(_body.Velocity.X) / _movement.MoveSpeed;
					var runAnimSpeed    = _spriteData != null ? _spriteData.RunAnimSpeed : 1.0f;
					_animator.Speed = normalizedSpeed * runAnimSpeed;

					var useRunFaceLeft = _hasRunFaceLeftAnim && _body.FacingDirection < 0;
					if (useRunFaceLeft)
					{
						_animator.FlipX = false;
						if (!_animator.IsAnimationActive(_keys.RunFaceLeft))
						{
							_animator.Play(_keys.RunFaceLeft);
						}
					}
					else
					{
						if (!_animator.IsAnimationActive(_keys.Run))
						{
							_animator.Play(_keys.Run);
						}
					}
				}
				else
				{
					_animator.Speed = 1.0f;
					var useIdleFaceLeft = _hasIdleFaceLeftAnim && _body.FacingDirection < 0;
					if (useIdleFaceLeft)
					{
						_animator.FlipX = false;
						if (!_animator.IsAnimationActive(_keys.IdleFaceLeft))
						{
							_animator.Play(_keys.IdleFaceLeft);
						}
					}
					else
					{
						if (!_animator.IsAnimationActive(_keys.Idle))
						{
							_animator.Play(_keys.Idle);
						}
					}
				}
			}
		}
	}
}
