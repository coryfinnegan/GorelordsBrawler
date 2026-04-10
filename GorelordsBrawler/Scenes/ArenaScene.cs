using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;
using Nez.Tiled;
using GorelordsBrawler.Components;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Input;
using GorelordsBrawler.Systems;
using GorelordsBrawler.Systems.Rules;

namespace GorelordsBrawler.Scenes
{
	public class ArenaScene : BaseScene
	{
		public ArenaScene()
		{
			AddRenderer(new RenderLayerRenderer(0,
				GameConstants.Rendering.DefaultRenderLayer,
				GameConstants.Rendering.HitboxRenderLayer,
				GameConstants.Rendering.HealthBarRenderLayer));

			AddSceneComponent(new PauseManager());
			AddSceneComponent(new CombatEffectsManager());
			AddSceneComponent(new HitParticleManager());
			var playerManager = AddSceneComponent(new PlayerManager());
			var setup = Core.GetGlobalManager<MatchSetupManager>();

			// Load Tiled map
			var tiledMap = Content.LoadTiledMap(GameConstants.Maps.Arena1);
			var mapEntity = CreateEntity("tiled-map");
			var renderer = mapEntity.AddComponent(
				new TiledMapRenderer(tiledMap, GameConstants.Maps.CollisionLayerName));
			renderer.SetLayersToRender("background", "platforms");
			renderer.RenderLayer = GameConstants.Rendering.DefaultRenderLayer;
			renderer.PhysicsLayer = PhysicsLayers.Platforms;

			// Read spawn positions from map object layer (fallback to constants)
			var spawnPositions = ReadSpawnPositions(tiledMap);

			foreach (var selection in setup.Selections)
			{
				var input = InputProfileFactory.CreateFromDevice(selection.Device);
				var spawn = selection.SlotIndex < spawnPositions.Length
					? spawnPositions[selection.SlotIndex]
					: spawnPositions[0];
				playerManager.AddPlayer(selection.SlotIndex, input, selection.CharacterType, spawn);
			}

			var cameraEntity = CreateEntity(GameConstants.EntityNames.Camera);
			var brawlerCam = cameraEntity.AddComponent(new BrawlerCamera());
			brawlerCam.SetMapBounds(tiledMap.WorldWidth, tiledMap.WorldHeight);
			foreach (var player in playerManager.GetActivePlayers())
			{
				brawlerCam.AddTarget(player);
			}

			var ruleset = new StockRuleset();
			AddSceneComponent(new MatchManager(ruleset));
			AddSceneComponent(new MatchHUD(ruleset));
		}

		private static Vector2[] ReadSpawnPositions(TmxMap map)
		{
			var spawnGroup = map.GetObjectGroup("spawns");
			if (spawnGroup == null || spawnGroup.Objects.Count == 0)
			{
				return GameConstants.Arena.FallbackSpawnPositions;
			}

			// Build a dictionary of index -> position from spawn objects
			var spawns = new SortedDictionary<int, Vector2>();
			foreach (var obj in spawnGroup.Objects)
			{
				int index = 0;
				if (obj.Properties != null && obj.Properties.TryGetValue("index", out var indexStr))
				{
					int.TryParse(indexStr, out index);
				}
				spawns[index] = new Vector2(obj.X, obj.Y);
			}

			var result = new Vector2[spawns.Count];
			int i = 0;
			foreach (var kvp in spawns)
			{
				result[i++] = kvp.Value;
			}

			return result;
		}
	}
}
