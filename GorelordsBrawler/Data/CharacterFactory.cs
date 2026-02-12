using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components;
using GorelordsBrawler.Components.Abilities;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Data
{
	public static class CharacterFactory
	{
		public static Entity Create(Scene scene, string characterType, InputProfile input, Vector2 spawnPosition)
		{
			var data = CharacterLoader.Load(scene, characterType);

			var entity = scene.CreateEntity(characterType);
			entity.Transform.Position = spawnPosition;

			// Identity + body (always present)
			var stats = new CharacterStats
			{
				name = data.name,
				description = data.description,
				maxHp = data.maxHp,
				bodyWidth = data.bodyWidth,
				bodyHeight = data.bodyHeight,
				colorR = data.colorR,
				colorG = data.colorG,
				colorB = data.colorB,
			};
			entity.AddComponent(stats);

			// Renderer + physics collider
			var renderer = entity.AddComponent(new PrototypeSpriteRenderer(stats.bodyWidth, stats.bodyHeight));
			renderer.SetColor(stats.BodyColor);

			var collider = entity.AddComponent(new BoxCollider(stats.bodyWidth, stats.bodyHeight));
			collider.PhysicsLayer = PhysicsLayers.Player;
			collider.CollidesWithLayers = PhysicsLayers.Platforms;

			entity.AddComponent(new Mover());

			// Movement (always present)
			entity.AddComponent(data.movement);
			entity.AddComponent(new PhysicsBody());
			entity.AddComponent(new WalkAbility(input));
			entity.AddComponent(new JumpAbility(input));

			// Hurtbox + Health (always present)
			var hurtboxCollider = entity.AddComponent(new BoxCollider(stats.bodyWidth, stats.bodyHeight));
			hurtboxCollider.PhysicsLayer = PhysicsLayers.Hurtbox;
			hurtboxCollider.CollidesWithLayers = PhysicsLayers.Hitbox;
			hurtboxCollider.IsTrigger = true;

			entity.AddComponent(new Health { MaxHp = stats.maxHp, CurrentHp = stats.maxHp });
			entity.AddComponent(new Hurtbox());
			entity.AddComponent(new HealthBar());
			entity.AddComponent(new RespawnHandler(spawnPosition));

			// Data-driven abilities — attach based on what the JSON provides
			if (data.melee != null)
			{
				entity.AddComponent(data.melee);
				entity.AddComponent(new MeleeAttack(input));
			}

			if (data.projectile != null)
			{
				entity.AddComponent(data.projectile);
				entity.AddComponent(new ProjectileAttack(input));
			}

			return entity;
		}
	}
}
