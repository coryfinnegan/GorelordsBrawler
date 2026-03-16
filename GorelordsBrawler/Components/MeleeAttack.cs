using Microsoft.Xna.Framework;
using Nez;
using Nez.Sprites;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Components
{
	public class MeleeAttack : Component, IUpdatable
	{
		private readonly InputProfile _input;
		private MeleeStats _melee;
		private PhysicsBody _body;
		private Hitstun _hitstun;
		private SpriteAnimator _animator;
		private WeaponSocketData _socketData;
		private SpriteData _spriteData;
		private CharacterStats _stats;
		private float _cooldownTimer;
		private float _hitboxTimer;
		private float _attackBufferTimer;
		private Entity _hitboxEntity;
		// Cached offset from entity center → last valid socket position.
		// When a frame has no socket data (null in sidecar), we hold this offset
		// instead of snapping to the static MeleeStats fallback.
		private Vector2? _lastSocketOffset;

		/// <summary>True from the moment the attack is triggered until the cooldown expires.</summary>
		public bool IsAttacking => _cooldownTimer > 0;

		/// <summary>
		/// Which attack animation set to play next (0 = LeftHand, 1 = RightHand).
		/// Toggles on each fresh attack so successive presses alternate hands.
		/// Read by LocomotionAnimator to select the correct animation variant.
		/// </summary>
		public int AttackIndex { get; private set; } = 0;

		public MeleeAttack(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_melee      = Entity.GetComponent<MeleeStats>();
			_body       = Entity.GetComponent<PhysicsBody>();
			_hitstun    = Entity.GetComponent<Hitstun>();
			_animator   = Entity.GetComponent<SpriteAnimator>();
			_socketData = Entity.GetComponent<WeaponSocketData>();
			_spriteData = Entity.GetComponent<SpriteData>();
			_stats      = Entity.GetComponent<CharacterStats>();
		}

		public override void OnRemovedFromEntity()
		{
			DestroyHitbox();
		}

		public void Update()
		{
			_cooldownTimer -= Time.DeltaTime;

			// Destroy hitbox when the attack window closes
			if (_hitboxEntity != null && _cooldownTimer <= 0)
			{
				DestroyHitbox();
			}

			// Buffer attack input — forgives presses up to AttackBufferWindow ms early
			if (_input.Attack.IsPressed)
			{
				_attackBufferTimer = GameConstants.Combat.AttackBufferWindow;
			}
			else
			{
				_attackBufferTimer -= Time.DeltaTime;
			}

			// Frame-range mode: hitbox is spawned and destroyed by animation frame index.
			// Timer mode: hitbox is spawned immediately and lives for HitboxDuration.
			bool useFrameMode = _melee.ActiveStartFrame >= 0 && _animator != null;

			// Fire attack when buffer is active, cooldown ready, and not stunned
			var isStunned = _hitstun != null && _hitstun.IsActive;
			if (_attackBufferTimer > 0 && _cooldownTimer <= 0 && _hitboxEntity == null && !isStunned)
			{
				_attackBufferTimer = 0;
				// Toggle hand BEFORE setting cooldown so LocomotionAnimator reads the new value
				// in the same frame when it checks IsAttacking rising-edge.
				AttackIndex = (AttackIndex + 1) % 2;
				_cooldownTimer = _melee.Cooldown;

				if (!useFrameMode)
				{
					// Timer mode: spawn hitbox immediately
					SpawnHitbox();
				}
				// Frame-range mode: frame tracking below handles spawning
			}

			// Frame-range mode: manage hitbox by animation frame
			if (useFrameMode && _cooldownTimer > 0)
			{
				int frame = _animator.CurrentFrame;
				bool inWindow = frame >= _melee.ActiveStartFrame && frame <= _melee.ActiveEndFrame;
				if (inWindow)
				{
					if (_hitboxEntity == null)
					{
						SpawnHitbox();
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
			if (!useFrameMode && _hitboxEntity != null)
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

		private void SpawnHitbox()
		{
			_lastSocketOffset = null; // reset so we don't carry offset from a previous attack
			_hitboxEntity = Entity.Scene.CreateEntity(GameConstants.EntityNames.MeleeHitbox);

			// Position from socket data (or static fallback) — also used for tracking each frame
			TrackHitboxPosition();

			var hitboxRenderer = _hitboxEntity.AddComponent(
				new PrototypeSpriteRenderer(_melee.HitboxWidth, _melee.HitboxHeight));
			hitboxRenderer.SetColor(Color.Red * GameConstants.Rendering.HitboxColorAlpha);
			hitboxRenderer.RenderLayer = GameConstants.Rendering.HitboxRenderLayer;

			var hitboxCollider = _hitboxEntity.AddComponent(
				new BoxCollider(_melee.HitboxWidth, _melee.HitboxHeight));
			hitboxCollider.PhysicsLayer = PhysicsLayers.Hitbox;
			hitboxCollider.CollidesWithLayers = PhysicsLayers.Hurtbox;
			hitboxCollider.IsTrigger = true;

			_hitboxEntity.AddComponent(new AttackData
			{
				OwnerEntity = Entity,
				Damage = _melee.Damage,
				KnockbackForce = _melee.KnockbackForce,
				KnockbackAngle = _melee.KnockbackAngle,
				FacingDirection = _body.FacingDirection,
				HitstunDuration = _melee.HitstunDuration,
			});

			_hitboxTimer = _melee.HitboxDuration; // used only in timer mode
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
					// Convert frame pixel coords to world offset from entity center.
					// Sprite origin is bottom-center (0.5, 1.0), so:
					//   frame bottom-center in pixels = (FrameWidth/2, FrameHeight)
					//   socket offset from bottom-center = (sx - W/2, sy - H)
					//   in world units (at SpriteScale): multiply by scale
					//   plus entity-to-sprite-bottom offset = BodyHeight/2
					float scale = (_spriteData != null && _spriteData.AttackSpriteScale > 0f)
						? _spriteData.AttackSpriteScale
						: (_spriteData?.SpriteScale ?? 1f);
					float bodyHalfH = _stats?.BodyHeight / 2f ?? 0f;
					float dx = (socket.Value.X - _socketData.FrameWidth  / 2f) * scale * _body.FacingDirection;
					float dy = bodyHalfH + (socket.Value.Y - _socketData.FrameHeight) * scale;
					_lastSocketOffset = new Vector2(dx, dy);
					return Entity.Transform.Position + _lastSocketOffset.Value;
				}

				// Null socket for this frame (sidecar gap) — hold the last known position
				// rather than snapping to the static MeleeStats offset.
				if (_lastSocketOffset.HasValue)
				{
					return Entity.Transform.Position + _lastSocketOffset.Value;
				}
			}

			// Fallback: static offset from MeleeStats (no socket data at all)
			return Entity.Transform.Position
				+ new Vector2(_melee.HitboxOffsetX * _body.FacingDirection, _melee.HitboxOffsetY);
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
