#if DEBUG
using System.Collections.Generic;
using System.IO;
using Nez;
using Nez.Console;
using Nez.Persistence;
using GorelordsBrawler.Components;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.DevTools
{
	public static class DebugCommands
	{
		/// <summary>
		/// Registers the F5 keybinding for quick-saving stats.
		/// Call from scene setup or game initialization.
		/// </summary>
		public static void RegisterKeybindings()
		{
			DebugConsole.BindActionToFunctionKey(5, () => SaveAllPlayerStats());
		}

		[Command("save-stats", "Saves all player character stats to their JSON files (F5)")]
		static void SaveStatsCommand()
		{
			SaveAllPlayerStats();
		}

		static void SaveAllPlayerStats()
		{
			var scene = Core.Scene;
			if (scene == null)
			{
				Nez.Debug.Log("No active scene");
				return;
			}

			var entities = scene.FindEntitiesWithTag(0); // default tag
			int saved = 0;

			// Check all known character types
			foreach (var characterType in GameConstants.Characters.All)
			{
				var entity = scene.FindEntity(characterType);
				if (entity == null)
				{
					continue;
				}

				var stats = entity.GetComponent<CharacterStats>();
				if (stats == null)
				{
					continue;
				}

				if (SaveCharacterStats(entity, characterType))
				{
					saved++;
				}
			}

			if (saved > 0)
			{
				Nez.Debug.Log($"Saved stats for {saved} character(s)");
			}
			else
			{
				Nez.Debug.Log("No player characters found in scene");
			}
		}

		static bool SaveCharacterStats(Entity entity, string characterType)
		{
			var stats = entity.GetComponent<CharacterStats>();
			var movement = entity.GetComponent<MovementStats>();
			var melee = entity.GetComponent<MeleeStats>();
			var projectile = entity.GetComponent<ProjectileStats>();
			var sprite = entity.GetComponent<SpriteData>();

			// Build a clean data structure — avoids serializing Component base class fields
			var data = new Dictionary<string, object>
			{
				{ "Name", stats.Name },
				{ "Description", stats.Description },
				{ "MaxHp", stats.MaxHp },
				{ "BodyWidth", stats.BodyWidth },
				{ "BodyHeight", stats.BodyHeight },
				{ "ColorR", stats.ColorR },
				{ "ColorG", stats.ColorG },
				{ "ColorB", stats.ColorB },
			};

			if (movement != null)
			{
				var movementData = new Dictionary<string, object>
				{
					{ "MoveSpeed", movement.MoveSpeed },
					{ "JumpSpeed", movement.JumpSpeed },
					{ "Gravity", movement.Gravity },
					{ "FallMultiplier", movement.FallMultiplier },
					{ "ShortHopMultiplier", movement.ShortHopMultiplier },
					{ "CoyoteTime", movement.CoyoteTime },
				};
				data["Movement"] = movementData;
			}

			if (melee != null)
			{
				var meleeData = new Dictionary<string, object>
				{
					{ "Damage", melee.Damage },
					{ "KnockbackForce", melee.KnockbackForce },
					{ "KnockbackAngleX", melee.KnockbackAngleX },
					{ "KnockbackAngleY", melee.KnockbackAngleY },
					{ "HitboxWidth", melee.HitboxWidth },
					{ "HitboxHeight", melee.HitboxHeight },
					{ "HitboxOffsetX", melee.HitboxOffsetX },
					{ "Cooldown", melee.Cooldown },
					{ "HitboxDuration", melee.HitboxDuration },
				};
				data["Melee"] = meleeData;
			}

			if (projectile != null)
			{
				var projectileData = new Dictionary<string, object>
				{
					{ "Speed", projectile.Speed },
					{ "Width", projectile.Width },
					{ "Height", projectile.Height },
					{ "Damage", projectile.Damage },
					{ "KnockbackForce", projectile.KnockbackForce },
					{ "KnockbackAngleX", projectile.KnockbackAngleX },
					{ "KnockbackAngleY", projectile.KnockbackAngleY },
					{ "MaxLifetime", projectile.MaxLifetime },
					{ "Cooldown", projectile.Cooldown },
				};
				data["Projectile"] = projectileData;
			}

			if (sprite != null)
			{
				var spriteData = new Dictionary<string, object>
				{
					{ "AtlasPath", sprite.AtlasPath },
					{ "RunAnimSpeed", sprite.RunAnimSpeed },
				};
				data["Sprite"] = spriteData;
			}

			var json = Json.ToJson(data, true);
			var path = GameConstants.ContentPaths.CharactersFolder + characterType + GameConstants.ContentPaths.JsonExtension;

			File.WriteAllText(path, json);
			Nez.Debug.Log($"Saved {characterType} -> {path}");
			return true;
		}
	}
}
#endif
