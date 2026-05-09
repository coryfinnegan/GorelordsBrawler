using System;
using Microsoft.Xna.Framework;
using Nez;
using Nez.Sprites;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Components.Abilities;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Replaces MeleeAttack as the central attack component. Reads directional input
	/// at attack time to select from a full moveset (ground normals + aerials).
	///
	/// Falls back gracefully: missing attack slots are skipped, and legacy characters
	/// with only a Jab (converted from MeleeStats) get the same behavior as before.
	/// </summary>
	public class CombatController : Component, IUpdatable
	{
		private readonly InputProfile _input;
		private AttackMoveSet _moveSet;
		private PhysicsBody _body;
		private Hitstun _hitstun;
		private SpriteAnimator _animator;
		private WeaponSocketData _socketData;
		private SpriteData _spriteData;
		private CharacterStats _stats;
		private float _cooldownTimer;
		private float _hitboxTimer;
		private float _attackBufferTimer;
		private float _heavyBufferTimer;
		private Entity _hitboxEntity;
		private bool _attackAnimActive;
		private Vector2? _lastSocketOffset;
		private LedgeHangAbility _ledge;
		private CrouchAbility _crouch;

		/// <summary>The currently active attack definition, or null when not attacking.</summary>
		public AttackDefinition CurrentAttack { get; private set; }

		/// <summary>True from the moment the attack triggers until cooldown expires.</summary>
		public bool IsAttacking => _cooldownTimer > 0;

		/// <summary>
		/// The animation suffix of the current attack (e.g. "Jab").
		/// Null when using legacy LeftHand/RightHand animations or not attacking.
		/// </summary>
		public string CurrentAnimSuffix { get; private set; }

		/// <summary>
		/// Which attack animation set to play next (0 = LeftHand, 1 = RightHand).
		/// Only used for legacy Jab animations (AnimationSuffix == null).
		/// </summary>
		public int AttackIndex { get; private set; } = 0;

		/// <summary>
		/// True when the current attack uses the legacy LeftHand/RightHand animation system.
		/// LocomotionAnimator checks this to decide which animation path to use.
		/// </summary>
		public bool IsLegacyAttack { get; private set; }

		/// <summary>
		/// Incremented each time a new attack triggers. LocomotionAnimator compares this
		/// against its last-seen value to detect new attacks reliably, even when two
		/// attacks trigger back-to-back in the same frame (avoiding missed rising edges).
		/// </summary>
		public int AttackSequence { get; private set; }

		/// <summary>
		/// True when the current attack was initiated while airborne.
		/// WalkAbility uses this to kill momentum when an aerial attack lands.
		/// </summary>
		public bool IsAerialAttack { get; private set; }

		public CombatController(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_moveSet    = Entity.GetComponent<AttackMoveSet>();
			_body       = Entity.GetComponent<PhysicsBody>();
			_hitstun    = Entity.GetComponent<Hitstun>();
			_animator   = Entity.GetComponent<SpriteAnimator>();
			_socketData = Entity.GetComponent<WeaponSocketData>();
			_spriteData = Entity.GetComponent<SpriteData>();
			_stats      = Entity.GetComponent<CharacterStats>();
		}

		public override void OnRemovedFromEntity()
		{
			_attackAnimActive = false;
			DestroyHitbox();
		}

		/// <summary>
		/// Called by LocomotionAnimator when the attack animation completes.
		/// </summary>
		public void OnAttackAnimationCompleted()
		{
			_attackAnimActive = false;
			CurrentAttack = null;
			CurrentAnimSuffix = null;
			IsLegacyAttack = false;
			IsAerialAttack = false;
			DestroyHitbox();
		}

		public void Update()
		{
			// Lazy resolve: components may be added after CombatController
			if (_ledge == null)
				_ledge = Entity.GetComponent<LedgeHangAbility>();
			if (_crouch == null)
				_crouch = Entity.GetComponent<CrouchAbility>();

			_cooldownTimer -= Time.DeltaTime;

			if (_hitboxEntity != null && !_attackAnimActive)
			{
				DestroyHitbox();
			}

			// Suppress attacks while on a ledge (LedgeHangAbility handles attack-release)
			if (_ledge != null && _ledge.IsOnLedge)
			{
				return;
			}

			// Buffer light attack (Attack button) and heavy attack (Special button)
			if (_input.Attack.IsPressed)
				_attackBufferTimer = GameConstants.Combat.AttackBufferWindow;
			else
				_attackBufferTimer -= Time.DeltaTime;

			if (_input.Special.IsPressed)
				_heavyBufferTimer = GameConstants.Combat.AttackBufferWindow;
			else
				_heavyBufferTimer -= Time.DeltaTime;

			var isStunned = _hitstun != null && _hitstun.IsActive;
			if (_cooldownTimer <= 0 && !_attackAnimActive && !isStunned)
			{
				AttackDefinition attack = null;

				// Heavy has priority; falls back to light
				if (_heavyBufferTimer > 0)
					attack = SelectHeavyAttack();

				if (attack == null && _attackBufferTimer > 0)
					attack = SelectAttack();

				if (attack != null)
				{
					_attackBufferTimer = 0;
					_heavyBufferTimer = 0;
					DestroyHitbox();
					_lastSocketOffset = null;
					_attackAnimActive = true;
					AttackSequence++;
					CurrentAttack = attack;
					CurrentAnimSuffix = attack.AnimationSuffix;
					IsLegacyAttack = string.IsNullOrEmpty(attack.AnimationSuffix);

					if (IsLegacyAttack)
						AttackIndex = (AttackIndex + 1) % 2;

					_cooldownTimer = attack.Cooldown;

					bool useFrameMode = attack.ActiveStartFrame >= 0 && _animator != null;
					if (!useFrameMode)
						SpawnHitbox(attack);
				}
			}

			if (CurrentAttack == null)
			{
				return;
			}

			bool useFrameRange = CurrentAttack.ActiveStartFrame >= 0 && _animator != null;

			// Frame-range mode: manage hitbox by animation frame
			if (useFrameRange && _attackAnimActive)
			{
				int frame = _animator.CurrentFrame;
				bool inWindow = frame >= CurrentAttack.ActiveStartFrame && frame <= CurrentAttack.ActiveEndFrame;
				if (inWindow)
				{
					if (_hitboxEntity == null)
					{
						SpawnHitbox(CurrentAttack);
					}
					else
					{
						TrackHitboxPosition();
					}
				}
				else if (_hitboxEntity != null)
				{
					DestroyHitbox();
				}
			}

			// Timer mode: manage hitbox lifetime and track position each frame
			if (!useFrameRange && _hitboxEntity != null)
			{
				_hitboxTimer -= Time.DeltaTime;
				if (_hitboxTimer <= 0)
				{
					DestroyHitbox();
				}
				else
				{
					TrackHitboxPosition();
				}
			}
		}

		private AttackDefinition SelectAttack()
		{
			if (_body.Grounded)
			{
				IsAerialAttack = false;
				if (_crouch != null && _crouch.IsCrouching && _moveSet.CrouchAttack != null)
					return _moveSet.CrouchAttack;
				return _moveSet.Jab;
			}
			else
			{
				// Aerial attack consumes the aerial action (shared with double jump)
				if (!_body.HasAerialAction)
				{
					return null;
				}

				_body.HasAerialAction = false;
				IsAerialAttack = true;
				return _moveSet.NeutralAir ?? _moveSet.Jab;
			}
		}

		private AttackDefinition SelectHeavyAttack()
		{
			if (_moveSet.Heavy == null || !_body.Grounded)
				return null;
			IsAerialAttack = false;
			return _moveSet.Heavy;
		}

		private void SpawnHitbox(AttackDefinition attack)
		{
			_lastSocketOffset = null;
			_hitboxEntity = Entity.Scene.CreateEntity(GameConstants.EntityNames.MeleeHitbox);

			TrackHitboxPosition();

			var hitboxCollider = _hitboxEntity.AddComponent(
				new BoxCollider(attack.HitboxWidth, attack.HitboxHeight));
			hitboxCollider.PhysicsLayer = PhysicsLayers.Hitbox;
			hitboxCollider.CollidesWithLayers = PhysicsLayers.Hurtbox;
			hitboxCollider.IsTrigger = true;

			_hitboxEntity.AddComponent(new AttackData
			{
				OwnerEntity = Entity,
				Damage = attack.Damage,
				KnockbackForce = attack.KnockbackForce,
				KnockbackAngle = attack.KnockbackAngle,
				FacingDirection = _body.FacingDirection,
				HitstunDuration = attack.HitstunDuration,
			});

			_hitboxTimer = attack.HitboxDuration;
		}

		private void TrackHitboxPosition()
		{
			_hitboxEntity.Transform.Position = ComputeHitboxCenter();
		}

		private Vector2 ComputeHitboxCenter()
		{
			if (_socketData != null && _animator != null)
			{
				var animName = _animator.CurrentAnimationName;
				int frame = _animator.CurrentFrame;
				var socket = _socketData.GetSocket(animName, frame);
				if (socket.HasValue)
				{
					float scale = (_spriteData != null && _spriteData.AttackSpriteScale > 0f)
						? _spriteData.AttackSpriteScale
						: (_spriteData?.SpriteScale ?? 1f);
					float bodyHalfH = _stats?.BodyHeight / 2f ?? 0f;
					float dx = (socket.Value.X - _socketData.FrameWidth / 2f) * scale * _body.FacingDirection;
					float dy = bodyHalfH + (socket.Value.Y - _socketData.FrameHeight) * scale;
					_lastSocketOffset = new Vector2(dx, dy);
					return Entity.Transform.Position + _lastSocketOffset.Value;
				}

				if (_lastSocketOffset.HasValue)
				{
					return Entity.Transform.Position + _lastSocketOffset.Value;
				}
			}

			// Fallback: static offset from the current AttackDefinition
			if (CurrentAttack != null)
			{
				return Entity.Transform.Position
					+ new Vector2(CurrentAttack.HitboxOffsetX * _body.FacingDirection, CurrentAttack.HitboxOffsetY);
			}

			return Entity.Transform.Position;
		}

		private void DestroyHitbox()
		{
			if (_hitboxEntity != null)
			{
				_hitboxEntity.Destroy();
				_hitboxEntity = null;
			}
		}
	}
}
