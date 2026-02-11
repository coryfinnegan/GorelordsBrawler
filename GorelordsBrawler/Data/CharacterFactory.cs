using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components;
using GorelordsBrawler.Components.Abilities;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Data
{
	public static class CharacterFactory
	{
		public static Entity Create(Scene scene, string characterType, InputProfile input, Vector2 spawnPosition)
		{
			var stats = CharacterLoader.Load(scene, characterType);

			var entity = scene.CreateEntity(characterType);
			entity.Transform.Position = spawnPosition;

			// Stats first so sibling components can find it
			entity.AddComponent(stats);

			var renderer = entity.AddComponent(
				new PrototypeSpriteRenderer(stats.bodyWidth, stats.bodyHeight));
			renderer.SetColor(stats.BodyColor);

			var collider = entity.AddComponent(
				new BoxCollider(stats.bodyWidth, stats.bodyHeight));
			collider.PhysicsLayer = PhysicsLayers.Player;
			collider.CollidesWithLayers = PhysicsLayers.Platforms;

			entity.AddComponent(new Mover());
			entity.AddComponent(new PhysicsBody());

			// Hurtbox (separate trigger collider for receiving damage)
			var hurtboxCollider = entity.AddComponent(new BoxCollider(stats.bodyWidth, stats.bodyHeight));
			hurtboxCollider.PhysicsLayer = PhysicsLayers.Hurtbox;
			hurtboxCollider.CollidesWithLayers = PhysicsLayers.Hitbox;
			hurtboxCollider.IsTrigger = true;

			// Health system
			var health = entity.AddComponent(new Health { MaxHp = stats.maxHp, CurrentHp = stats.maxHp });
			entity.AddComponent(new Hurtbox());
			entity.AddComponent(new HealthBar());
			entity.AddComponent(new RespawnHandler(spawnPosition));

			AttachAbilities(entity, characterType, input);

			return entity;
		}

		private static void AttachAbilities(Entity entity, string characterType, InputProfile input)
		{
			switch (characterType)
			{
				case GameConstants.Characters.Trollborg:
					entity.AddComponent(new WalkAbility(input));
					entity.AddComponent(new JumpAbility(input));
					entity.AddComponent(new MeleeAttack(input));
					break;

				default:
					// Fallback: basic movement for unknown characters
					entity.AddComponent(new WalkAbility(input));
					entity.AddComponent(new JumpAbility(input));
					entity.AddComponent(new MeleeAttack(input));
					break;
			}
		}
	}
}
