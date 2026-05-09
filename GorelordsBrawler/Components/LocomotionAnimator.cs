using System;
using Nez;
using Nez.Sprites;
using GorelordsBrawler.Components.Abilities;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Drives sprite animation state from character velocity and grounded state.
	///
	/// Priority (highest first):
	///   attack  — plays to completion; supports both legacy (LeftHand/RightHand) and
	///             new dynamic animations via CombatController.CurrentAnimSuffix
	///   hurt    — during hitstun
	///   ledge   — LedgeIdle (hanging) or LedgeClimb (pulling up)
	///   jump    — airborne; speed driven by |Velocity.Y| / JumpSpeed
	///   landing — brief Pause() on last jump frame after touching ground
	///   run     — grounded and moving; speed driven by |Velocity.X| / MoveSpeed
	///   idle    — grounded and still
	///
	/// Directional variants (FaceLeft suffix) play when facing left and the animation
	/// exists in the atlas, overriding global FlipX to avoid double-mirroring.
	/// </summary>
	public class LocomotionAnimator : Component, IUpdatable
	{
		private PhysicsBody _body;
		private MovementStats _movement;
		private SpriteAnimator _animator;
		private SpriteData _spriteData;
		private CombatController _combat;
		private Hitstun _hitstun;
		private LedgeHangAbility _ledge;
		private CrouchAbility _crouch;

		private AnimationKeyBuilder _keys;

		// Existence flags — set once in OnAddedToEntity based on what the atlas contains.
		private bool _hasJumpAnim;
		private bool _hasJumpFaceLeftAnim;
		private bool _hasIdleFaceLeftAnim;
		private bool _hasRunFaceLeftAnim;

		// Legacy LeftHand attack existence flags (used when CombatController.IsLegacyAttack)
		private bool _hasAttackIdleLeftHandAnim;
		private bool _hasAttackIdleLeftHandFaceLeftAnim;
		private bool _hasAttackRunLeftHandAnim;
		private bool _hasAttackRunLeftHandFaceLeftAnim;
		private bool _hasAttackIdleRightHandAnim;
		private bool _hasAttackIdleRightHandFaceLeftAnim;
		private bool _hasAttackRunRightHandAnim;
		private bool _hasAttackRunRightHandFaceLeftAnim;

		// Hurt existence flags
		private bool _hasHurtAnim;
		private bool _hasHurtFaceLeftAnim;

		// Crouch existence flags
		private bool _hasCrouchIdleAnim;
		private bool _hasCrouchIdleFaceLeftAnim;
		private bool _hasCrouchRunAnim;
		private bool _hasCrouchRunFaceLeftAnim;

		// Ledge existence flags
		private bool _hasLedgeIdleAnim;
		private bool _hasLedgeIdleFaceLeftAnim;
		private bool _hasLedgeClimbAnim;
		private bool _hasLedgeClimbFaceLeftAnim;

		private bool _wasGrounded;
		private float _landingTimer;

		// Attack lifecycle
		private float _defaultScale;
		private bool _isPlayingAttack;
		private int _lastAttackSequence;

		// The animation name currently playing for an attack (used for completion detection)
		private string _currentAttackAnim;

		// Ledge climb lifecycle
		private bool _isPlayingClimb;
		private string _currentClimbAnim;

		/// <summary>True while an attack animation is playing.</summary>
		public bool IsPlayingAttack => _isPlayingAttack;

		public override void OnAddedToEntity()
		{
			_body       = Entity.GetComponent<PhysicsBody>();
			_movement   = Entity.GetComponent<MovementStats>();
			_animator   = Entity.GetComponent<SpriteAnimator>();
			_spriteData = Entity.GetComponent<SpriteData>();
			_hitstun    = Entity.GetComponent<Hitstun>();

			_keys = new AnimationKeyBuilder(_spriteData?.AnimPrefix ?? "");

			_hasJumpAnim         = _animator.Animations.ContainsKey(_keys.Jump);
			_hasJumpFaceLeftAnim = _animator.Animations.ContainsKey(_keys.JumpFaceLeft);
			_hasIdleFaceLeftAnim = _animator.Animations.ContainsKey(_keys.IdleFaceLeft);
			_hasRunFaceLeftAnim  = _animator.Animations.ContainsKey(_keys.RunFaceLeft);

			_hasAttackIdleLeftHandAnim           = _animator.Animations.ContainsKey(_keys.AttackIdleLeftHand);
			_hasAttackIdleLeftHandFaceLeftAnim   = _animator.Animations.ContainsKey(_keys.AttackIdleLeftHandFaceLeft);
			_hasAttackRunLeftHandAnim            = _animator.Animations.ContainsKey(_keys.AttackRunLeftHand);
			_hasAttackRunLeftHandFaceLeftAnim    = _animator.Animations.ContainsKey(_keys.AttackRunLeftHandFaceLeft);

			_hasAttackIdleRightHandAnim          = _animator.Animations.ContainsKey(_keys.AttackIdleRightHand);
			_hasAttackIdleRightHandFaceLeftAnim  = _animator.Animations.ContainsKey(_keys.AttackIdleRightHandFaceLeft);
			_hasAttackRunRightHandAnim           = _animator.Animations.ContainsKey(_keys.AttackRunRightHand);
			_hasAttackRunRightHandFaceLeftAnim   = _animator.Animations.ContainsKey(_keys.AttackRunRightHandFaceLeft);

			_hasHurtAnim         = _animator.Animations.ContainsKey(_keys.Hurt);
			_hasHurtFaceLeftAnim = _animator.Animations.ContainsKey(_keys.HurtFaceLeft);

			_hasCrouchIdleAnim         = _animator.Animations.ContainsKey(_keys.CrouchIdle);
			_hasCrouchIdleFaceLeftAnim = _animator.Animations.ContainsKey(_keys.CrouchIdleFaceLeft);
			_hasCrouchRunAnim          = _animator.Animations.ContainsKey(_keys.CrouchRun);
			_hasCrouchRunFaceLeftAnim  = _animator.Animations.ContainsKey(_keys.CrouchRunFaceLeft);

			_hasLedgeIdleAnim         = _animator.Animations.ContainsKey(_keys.LedgeIdle);
			_hasLedgeIdleFaceLeftAnim = _animator.Animations.ContainsKey(_keys.LedgeIdleFaceLeft);
			_hasLedgeClimbAnim         = _animator.Animations.ContainsKey(_keys.LedgeClimb);
			_hasLedgeClimbFaceLeftAnim = _animator.Animations.ContainsKey(_keys.LedgeClimbFaceLeft);

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
			if (_isPlayingAttack && animationName == _currentAttackAnim)
			{
				_isPlayingAttack = false;
				_currentAttackAnim = null;
				Entity.Transform.SetScale(_defaultScale);
				_combat?.OnAttackAnimationCompleted();
			}

			if (_isPlayingClimb && animationName == _currentClimbAnim)
			{
				_isPlayingClimb = false;
				_currentClimbAnim = null;
				_ledge?.OnClimbAnimationCompleted();
			}
		}

		public void Update()
		{
			// Lazy resolve: components may be added after LocomotionAnimator
			if (_combat == null)
				_combat = Entity.GetComponent<CombatController>();
			if (_ledge == null)
				_ledge = Entity.GetComponent<LedgeHangAbility>();
			if (_crouch == null)
				_crouch = Entity.GetComponent<CrouchAbility>();

			// ── Attack (highest priority) ─────────────────────────────────────────
			if (_combat != null && _combat.AttackSequence != _lastAttackSequence)
			{
				// New attack detected — interrupt any playing animation and start fresh
				_lastAttackSequence = _combat.AttackSequence;
				_isPlayingAttack = false;
				_currentAttackAnim = null;
				Entity.Transform.SetScale(_defaultScale);

				_animator.FlipX = _body.FacingDirection < 0;

				string animName = null;

				if (_combat.IsLegacyAttack)
				{
					animName = SelectLegacyAttackAnim();
				}
				else if (_combat.CurrentAnimSuffix != null)
				{
					animName = SelectDynamicAttackAnim(_combat.CurrentAnimSuffix);
				}

				if (animName != null)
				{
					_animator.Speed = _spriteData != null && _spriteData.AttackAnimSpeed > 0
						? _spriteData.AttackAnimSpeed
						: 1.0f;

					if (_spriteData != null && _spriteData.AttackSpriteScale > 0f)
					{
						Entity.Transform.SetScale(_spriteData.AttackSpriteScale);
					}

					_animator.Play(animName, SpriteAnimator.LoopMode.ClampForever);
					_isPlayingAttack = true;
					_currentAttackAnim = animName;
				}
			}

			if (_isPlayingAttack)
			{
				_wasGrounded = _body.Grounded;
				return;
			}

			// Flip sprite to face the correct direction
			_animator.FlipX = _body.FacingDirection < 0;

			// ── Hurt (between attack and jump) ───────────────────────────────────
			if (_hasHurtAnim && _hitstun != null && _hitstun.IsActive)
			{
				var useHurtFaceLeft = _hasHurtFaceLeftAnim && _body.FacingDirection < 0;
				var hurtAnim = useHurtFaceLeft ? _keys.HurtFaceLeft : _keys.Hurt;

				if (useHurtFaceLeft)
				{
					_animator.FlipX = false;
				}

				if (!_animator.IsAnimationActive(hurtAnim))
				{
					_animator.Speed = 1.0f;
					_animator.Play(hurtAnim, SpriteAnimator.LoopMode.ClampForever);
				}

				_wasGrounded = _body.Grounded;
				return;
			}

			// ── Ledge (between hurt and jump) ────────────────────────────────────
			if (_ledge != null && _ledge.IsOnLedge)
			{
				if (_ledge.IsClimbing)
				{
					var useFaceLeft = _hasLedgeClimbFaceLeftAnim && _body.FacingDirection < 0;

					// FaceLeft animations are separate sprites — keep FlipX=false every frame
					// so the global FlipX=true set above doesn't mirror them each tick.
					if (useFaceLeft)
					{
						_animator.FlipX = false;
					}

					if (!_isPlayingClimb)
					{
						string climbAnim;

						if (_hasLedgeClimbAnim || _hasLedgeClimbFaceLeftAnim)
						{
							climbAnim = useFaceLeft ? _keys.LedgeClimbFaceLeft : _keys.LedgeClimb;
						}
						else
						{
							// Fallback: no climb animation — complete immediately
							_ledge.OnClimbAnimationCompleted();
							_wasGrounded = _body.Grounded;
							return;
						}

						_animator.Speed = _spriteData != null && _spriteData.LedgeClimbAnimSpeed > 0f
							? _spriteData.LedgeClimbAnimSpeed
							: 1.0f;
						_animator.Play(climbAnim, SpriteAnimator.LoopMode.ClampForever);
						_isPlayingClimb = true;
						_currentClimbAnim = climbAnim;
					}
				}
				else
				{
					// Hanging — play LedgeIdle or fallback to frozen jump frame
					_isPlayingClimb = false;
					_currentClimbAnim = null;

					var useFaceLeft = _hasLedgeIdleFaceLeftAnim && _body.FacingDirection < 0;

					if (_hasLedgeIdleAnim || _hasLedgeIdleFaceLeftAnim)
					{
						var ledgeAnim = useFaceLeft ? _keys.LedgeIdleFaceLeft : _keys.LedgeIdle;
						if (useFaceLeft)
						{
							_animator.FlipX = false;
						}

						if (!_animator.IsAnimationActive(ledgeAnim))
						{
							_animator.Speed = 1.0f;
							_animator.Play(ledgeAnim, SpriteAnimator.LoopMode.ClampForever);
						}
					}
					else if (_hasJumpAnim)
					{
						// Fallback: frozen jump frame as visual hang
						var useJumpFaceLeft = _hasJumpFaceLeftAnim && _body.FacingDirection < 0;
						var jumpAnim = useJumpFaceLeft ? _keys.JumpFaceLeft : _keys.Jump;
						if (useJumpFaceLeft)
						{
							_animator.FlipX = false;
						}

						if (!_animator.IsAnimationActive(jumpAnim))
						{
							_animator.Play(jumpAnim, SpriteAnimator.LoopMode.ClampForever);
						}
						_animator.Speed = 0f;
						_animator.Pause();
					}
				}

				_wasGrounded = _body.Grounded;
				return;
			}

			// Clear climb state when not on ledge
			if (_isPlayingClimb)
			{
				_isPlayingClimb = false;
				_currentClimbAnim = null;
			}

			// ── Crouch ────────────────────────────────────────────────────────────
			if (_crouch != null && _crouch.IsCrouching && (_hasCrouchIdleAnim || _hasCrouchIdleFaceLeftAnim || _hasCrouchRunAnim || _hasCrouchRunFaceLeftAnim))
			{
				var isMoving = System.Math.Abs(_body.Velocity.X) > 0.1f;
				var facingLeft = _body.FacingDirection < 0;

				if (isMoving && (_hasCrouchRunAnim || _hasCrouchRunFaceLeftAnim))
				{
					var useFaceLeft = _hasCrouchRunFaceLeftAnim && facingLeft;
					var anim = useFaceLeft ? _keys.CrouchRunFaceLeft : _keys.CrouchRun;
					if (useFaceLeft) _animator.FlipX = false;

					var normalizedSpeed = System.Math.Max(System.Math.Abs(_body.Velocity.X) / _movement.MoveSpeed, 0.3f);
					_animator.Speed = normalizedSpeed * (_spriteData != null ? _spriteData.RunAnimSpeed : 1.0f);

					PlayLooped(anim);
				}
				else
				{
					var useFaceLeft = _hasCrouchIdleFaceLeftAnim && facingLeft;
					var anim = useFaceLeft ? _keys.CrouchIdleFaceLeft : _keys.CrouchIdle;
					if (useFaceLeft) _animator.FlipX = false;

					_animator.Speed = 1.0f;
					PlayLooped(anim);
				}

				_wasGrounded = _body.Grounded;
				return;
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
					var normalizedSpeed = Math.Max(Math.Abs(_body.Velocity.X) / _movement.MoveSpeed, 0.3f);
					var runAnimSpeed    = _spriteData != null ? _spriteData.RunAnimSpeed : 1.0f;
					_animator.Speed = normalizedSpeed * runAnimSpeed;

					var useRunFaceLeft = _hasRunFaceLeftAnim && _body.FacingDirection < 0;
					if (useRunFaceLeft)
					{
						_animator.FlipX = false;
						PlayLooped(_keys.RunFaceLeft);
					}
					else
					{
						PlayLooped(_keys.Run);
					}
				}
				else
				{
					_animator.Speed = 1.0f;
					var useIdleFaceLeft = _hasIdleFaceLeftAnim && _body.FacingDirection < 0;
					if (useIdleFaceLeft)
					{
						_animator.FlipX = false;
						PlayLooped(_keys.IdleFaceLeft);
					}
					else
					{
						PlayLooped(_keys.Idle);
					}
				}
			}
		}

		/// <summary>
		/// Plays a looping animation while preserving the normalized frame position of
		/// the outgoing animation. Switching from Run to CrouchRun (or any locomotion
		/// pair) picks up at the same point in the cycle rather than snapping to frame 0.
		/// </summary>
		private void PlayLooped(string animName)
		{
			if (_animator.IsAnimationActive(animName))
				return;

			float progress = _animator.FrameCount > 0
				? (float)_animator.CurrentFrame / _animator.FrameCount
				: 0f;

			_animator.Play(animName);

			if (progress > 0f && _animator.FrameCount > 1)
			{
				int frame = (int)(progress * _animator.FrameCount);
				_animator.SetFrame(Math.Min(frame, _animator.FrameCount - 1));
			}
		}

		/// <summary>
		/// Selects an attack animation using the new dynamic suffix system.
		/// Tries "{Prefix}_{Suffix}FaceLeft" when facing left, falls back to "{Prefix}_{Suffix}",
		/// then falls back to any existing legacy attack animation as a final fallback.
		/// </summary>
		private string SelectDynamicAttackAnim(string suffix)
		{
			var facingLeft = _body.FacingDirection < 0;
			var prefix = _spriteData?.AnimPrefix ?? "";
			var baseName = string.IsNullOrEmpty(prefix) ? suffix : string.Concat(prefix, "_", suffix);
			var faceLeftName = baseName + "FaceLeft";

			// Try FaceLeft variant when facing left
			if (facingLeft && _animator.Animations.ContainsKey(faceLeftName))
			{
				_animator.FlipX = false;
				return faceLeftName;
			}

			// Try the base animation
			if (_animator.Animations.ContainsKey(baseName))
			{
				return baseName;
			}

			// Graceful fallback: use any existing legacy attack animation so that
			// new attack types (SideTilt, UpTilt, etc.) still visually play something
			// until dedicated animations are created.
			return SelectLegacyAttackAnim();
		}

		/// <summary>
		/// Selects an attack animation from the legacy LeftHand/RightHand system.
		/// Returns null if no legacy attack animations exist.
		/// </summary>
		private string SelectLegacyAttackAnim()
		{
			var hasAnyLeftHandAnim  = _hasAttackIdleLeftHandAnim  || _hasAttackIdleLeftHandFaceLeftAnim  ||
			                          _hasAttackRunLeftHandAnim   || _hasAttackRunLeftHandFaceLeftAnim;
			var hasAnyRightHandAnim = _hasAttackIdleRightHandAnim || _hasAttackIdleRightHandFaceLeftAnim ||
			                          _hasAttackRunRightHandAnim  || _hasAttackRunRightHandFaceLeftAnim;

			if (!hasAnyLeftHandAnim && !hasAnyRightHandAnim)
			{
				return null;
			}

			var wasMoving  = Math.Abs(_body.Velocity.X) > 0.1f;
			var facingLeft = _body.FacingDirection < 0;

			var useRightHand = _combat != null && _combat.AttackIndex == 1 && hasAnyRightHandAnim;

			if (wasMoving)
			{
				if (useRightHand)
				{
					if (facingLeft && _hasAttackRunRightHandFaceLeftAnim)
					{
						_animator.FlipX = false;
						return _keys.AttackRunRightHandFaceLeft;
					}
					if (_hasAttackRunRightHandAnim)
					{
						return _keys.AttackRunRightHand;
					}
					_animator.FlipX = !facingLeft;
					return _keys.AttackRunRightHandFaceLeft;
				}
				else
				{
					if (facingLeft && _hasAttackRunLeftHandFaceLeftAnim)
					{
						_animator.FlipX = false;
						return _keys.AttackRunLeftHandFaceLeft;
					}
					if (_hasAttackRunLeftHandAnim)
					{
						return _keys.AttackRunLeftHand;
					}
					_animator.FlipX = !facingLeft;
					return _keys.AttackRunLeftHandFaceLeft;
				}
			}
			else
			{
				if (useRightHand)
				{
					if (facingLeft && _hasAttackIdleRightHandFaceLeftAnim)
					{
						_animator.FlipX = false;
						return _keys.AttackIdleRightHandFaceLeft;
					}
					if (_hasAttackIdleRightHandAnim)
					{
						return _keys.AttackIdleRightHand;
					}
					_animator.FlipX = !facingLeft;
					return _keys.AttackIdleRightHandFaceLeft;
				}
				else
				{
					if (facingLeft && _hasAttackIdleLeftHandFaceLeftAnim)
					{
						_animator.FlipX = false;
						return _keys.AttackIdleLeftHandFaceLeft;
					}
					if (_hasAttackIdleLeftHandAnim)
					{
						return _keys.AttackIdleLeftHand;
					}
					_animator.FlipX = !facingLeft;
					return _keys.AttackIdleLeftHandFaceLeft;
				}
			}
		}
	}
}
