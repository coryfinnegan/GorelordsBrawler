using System;
using Microsoft.Xna.Framework;
using Nez;
using Nez.Sprites;
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
				Name = data.Name,
				Description = data.Description,
				MaxHp = data.MaxHp,
				BodyWidth = data.BodyWidth,
				BodyHeight = data.BodyHeight,
				ColorR = data.ColorR,
				ColorG = data.ColorG,
				ColorB = data.ColorB,
			};
			entity.AddComponent(stats);

			// Renderer — packed sprite atlas if available, colored rectangle fallback otherwise
			if (data.Sprite != null && TryCreateSpriteAnimator(entity, data.Sprite, stats))
			{
				entity.AddComponent(data.Sprite);
				entity.AddComponent(new LocomotionAnimator());
			}
			else
			{
				var renderer = entity.AddComponent(new PrototypeSpriteRenderer(stats.BodyWidth, stats.BodyHeight));
				renderer.SetColor(stats.BodyColor);
			}

			var collider = entity.AddComponent(new BoxCollider(stats.BodyWidth, stats.BodyHeight));
			collider.PhysicsLayer = PhysicsLayers.Player;
			collider.CollidesWithLayers = PhysicsLayers.Platforms;
			// Entity scale drives sprite size only — collider dimensions are explicit world-space values
			collider.ShouldColliderScaleAndRotateWithTransform = false;

			entity.AddComponent(new Mover());

			// Movement (always present)
			entity.AddComponent(data.Movement);
			entity.AddComponent(new PhysicsBody());
			entity.AddComponent(new WalkAbility(input));
			entity.AddComponent(new JumpAbility(input));

			// Hurtbox + Health (always present)
			var hurtboxCollider = entity.AddComponent(new BoxCollider(stats.BodyWidth, stats.BodyHeight));
			hurtboxCollider.PhysicsLayer = PhysicsLayers.Hurtbox;
			hurtboxCollider.CollidesWithLayers = PhysicsLayers.Hitbox;
			hurtboxCollider.IsTrigger = true;
			hurtboxCollider.ShouldColliderScaleAndRotateWithTransform = false;

			entity.AddComponent(new Health { MaxHp = stats.MaxHp, CurrentHp = stats.MaxHp });
			entity.AddComponent(new Hurtbox());
			entity.AddComponent(new HealthBar());
			entity.AddComponent(new RespawnHandler(spawnPosition));

			// Data-driven abilities — attach based on what the JSON provides
			if (data.Melee != null)
			{
				entity.AddComponent(data.Melee);
				entity.AddComponent(new MeleeAttack(input));
			}

			if (data.Projectile != null)
			{
				entity.AddComponent(data.Projectile);
				entity.AddComponent(new ProjectileAttack(input));
			}

			return entity;
		}

		private static bool TryCreateSpriteAnimator(Entity entity, SpriteData spriteData, CharacterStats stats)
		{
			try
			{
				var atlas = SpriteAtlasLoader.ParseSpriteAtlas(spriteData.AtlasPath, premultiplyAlpha: true);

				var animator = new SpriteAnimator();
				animator.AddAnimationsFromAtlas(atlas);

				// Load any additional atlases (e.g. attack/hurt animations packed separately)
				if (spriteData.ExtraAtlasPaths != null)
				{
					foreach (var extraPath in spriteData.ExtraAtlasPaths)
					{
						try
						{
							var extraAtlas = SpriteAtlasLoader.ParseSpriteAtlas(extraPath, premultiplyAlpha: true);
							animator.AddAnimationsFromAtlas(extraAtlas);
						}
						catch (Exception e)
						{
							Debug.Warn("Failed to load extra atlas '{0}': {1}", extraPath, e.Message);
						}
					}
				}

				// Atlas origin is bottom-center (0.5, 1.0) so sprite renders upward
				// from its position. Shift it down by half body height to align
				// the sprite's feet with the bottom of the centered collider.
				animator.LocalOffset = new Vector2(0, stats.BodyHeight / 2f);

				animator.Play(GameConstants.Animations.Idle);
				entity.AddComponent(animator);

				// Visual scale -- independent of physics body dimensions.
				// LocalOffset above aligns the sprite feet to the collider bottom using
				// BodyHeight (world units), which remains correct at any SpriteScale.
				entity.Transform.SetScale(spriteData.SpriteScale);

				return true;
			}
			catch (Exception e)
			{
				Debug.Warn("Failed to load sprite atlas '{0}', falling back to rectangle: {1}",
					spriteData.AtlasPath, e.Message);
				return false;
			}
		}
	}
}
