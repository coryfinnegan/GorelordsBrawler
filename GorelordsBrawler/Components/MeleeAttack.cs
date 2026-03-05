using Microsoft.Xna.Framework;
using Nez;
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
		private float _cooldownTimer;
		private float _hitboxTimer;
		private Entity _hitboxEntity;

		/// <summary>True from the moment the attack is triggered until the cooldown expires.</summary>
		public bool IsAttacking => _cooldownTimer > 0;

		public MeleeAttack(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_melee = Entity.GetComponent<MeleeStats>();
			_body = Entity.GetComponent<PhysicsBody>();
		}

		public override void OnRemovedFromEntity()
		{
			DestroyHitbox();
		}

		public void Update()
		{
			_cooldownTimer -= Time.DeltaTime;

			// Handle active hitbox lifetime
			if (_hitboxEntity != null)
			{
				_hitboxTimer -= Time.DeltaTime;
				if (_hitboxTimer <= 0)
					DestroyHitbox();
			}

			// Attack input
			if (_input.Attack.IsPressed && _cooldownTimer <= 0 && _hitboxEntity == null)
			{
				_cooldownTimer = _melee.Cooldown;
				SpawnHitbox();
			}
		}

		private void SpawnHitbox()
		{
			float offsetX = _melee.HitboxOffsetX * _body.FacingDirection;

			_hitboxEntity = Entity.Scene.CreateEntity(GameConstants.EntityNames.MeleeHitbox);
			_hitboxEntity.Transform.Position = Entity.Transform.Position + new Vector2(offsetX, 0);

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
				FacingDirection = _body.FacingDirection
			});

			_hitboxTimer = _melee.HitboxDuration;
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
