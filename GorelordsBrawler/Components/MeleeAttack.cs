using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Components
{
	public class MeleeAttack : Component, IUpdatable
	{
		private readonly InputProfile _input;
		private CharacterStats _stats;
		private PhysicsBody _body;
		private float _cooldownTimer;
		private float _hitboxTimer;
		private Entity _hitboxEntity;

		public MeleeAttack(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_stats = Entity.GetComponent<CharacterStats>();
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
				_cooldownTimer = _stats.attackCooldown;
				SpawnHitbox();
			}
		}

		private void SpawnHitbox()
		{
			float offsetX = _stats.hitboxOffsetX * _body.FacingDirection;

			_hitboxEntity = Entity.Scene.CreateEntity(GameConstants.EntityNames.MeleeHitbox);
			_hitboxEntity.Transform.Position = Entity.Transform.Position + new Vector2(offsetX, 0);

			var hitboxRenderer = _hitboxEntity.AddComponent(
				new PrototypeSpriteRenderer(_stats.hitboxWidth, _stats.hitboxHeight));
			hitboxRenderer.SetColor(Color.Red * GameConstants.Rendering.HitboxColorAlpha);
			hitboxRenderer.RenderLayer = GameConstants.Rendering.HitboxRenderLayer;

			var hitboxCollider = _hitboxEntity.AddComponent(
				new BoxCollider(_stats.hitboxWidth, _stats.hitboxHeight));
			hitboxCollider.PhysicsLayer = PhysicsLayers.Hitbox;
			hitboxCollider.CollidesWithLayers = PhysicsLayers.Hurtbox;
			hitboxCollider.IsTrigger = true;

			_hitboxEntity.AddComponent(new AttackData
			{
				OwnerEntity = Entity,
				Damage = _stats.meleeDamage,
				KnockbackForce = _stats.meleeKnockbackForce,
				KnockbackAngle = _stats.MeleeKnockbackAngle,
				FacingDirection = _body.FacingDirection
			});

			_hitboxTimer = _stats.hitboxDuration;
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
