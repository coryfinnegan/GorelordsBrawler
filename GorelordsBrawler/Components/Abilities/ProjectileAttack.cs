using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Components.Abilities
{
	public class ProjectileAttack : Component, IUpdatable
	{
		private readonly InputProfile _input;
		private ProjectileStats _stats;
		private PhysicsBody _body;
		private CharacterStats _charStats;
		private float _cooldownTimer;

		public ProjectileAttack(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_stats = Entity.GetComponent<ProjectileStats>();
			_body = Entity.GetComponent<PhysicsBody>();
			_charStats = Entity.GetComponent<CharacterStats>();
		}

		public void Update()
		{
			_cooldownTimer -= Time.DeltaTime;

			if (_input.Attack.IsPressed && _cooldownTimer <= 0)
			{
				_cooldownTimer = _stats.cooldown;
				SpawnProjectile();
			}
		}

		private void SpawnProjectile()
		{
			var facing = _body.FacingDirection;
			var spawnOffset = new Vector2(_charStats.bodyWidth * 0.5f * facing, 0);
			var spawnPos = Entity.Transform.Position + spawnOffset;

			var projectile = Entity.Scene.CreateEntity(GameConstants.EntityNames.Projectile);
			projectile.Transform.Position = spawnPos;

			var renderer = projectile.AddComponent(
				new PrototypeSpriteRenderer(_stats.width, _stats.height));
			renderer.SetColor(_charStats.BodyColor);
			renderer.RenderLayer = GameConstants.Rendering.HitboxRenderLayer;

			var collider = projectile.AddComponent(new BoxCollider(_stats.width, _stats.height));
			collider.PhysicsLayer = PhysicsLayers.Hitbox;
			collider.CollidesWithLayers = PhysicsLayers.Hurtbox;
			collider.IsTrigger = true;

			projectile.AddComponent<ProjectileMover>();

			projectile.AddComponent(new AttackData
			{
				OwnerEntity = Entity,
				Damage = _stats.damage,
				KnockbackForce = _stats.knockbackForce,
				KnockbackAngle = _stats.KnockbackAngle,
				FacingDirection = facing
			});

			var velocity = new Vector2(_stats.speed * facing, 0);
			projectile.AddComponent(new Projectile(velocity, _stats.maxLifetime));
		}
	}
}
