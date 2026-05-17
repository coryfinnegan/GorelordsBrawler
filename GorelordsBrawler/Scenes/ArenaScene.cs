using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.Tiled;
using GorelordsBrawler.Components;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Components.Hazards.Fluid;
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

			// ── Liquid metaball pipeline ──────────────────────────────────
			// Renderer that splats every particle (on LiquidRenderLayer) into
			// its own RenderTexture with additive blend. Runs at order -10 so
			// it goes BEFORE the default RenderLayerRenderer above, meaning
			// by the time the PostProcessor runs we have a fully populated
			// field RT. See .claude/skills/nez-liquid-rendering/SKILL.md.
			var liquidFieldRenderer = new LiquidFieldRenderer(
				GameConstants.Rendering.LiquidFieldRendererOrder);
			AddRenderer(liquidFieldRenderer);

			// Threshold post-process — composites the field RT over the scene
			// via the liquid.fx shader. .mgfxo is the precompiled bytecode
			// (mgfxc Content/Effects/liquid.fx ... /Profile:OpenGL); shipped
			// in Content/ via the project's existing copy-to-output glob.
			var effectPath = Path.Combine(
				System.AppDomain.CurrentDomain.BaseDirectory,
				"Content", "Effects", "liquid.mgfxo");
			var liquidEffect = new Effect(Core.GraphicsDevice, File.ReadAllBytes(effectPath));
			AddPostProcessor(new LiquidPostProcessor(
				GameConstants.Rendering.LiquidPostProcessorOrder,
				liquidEffect,
				liquidFieldRenderer,
				bodyColor: new Color((byte)45, (byte)180, (byte)40, (byte)255),
				edgeColor: new Color((byte)150, (byte)255, (byte)90, (byte)255)));

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

			// Hazard system
			int mw = tiledMap.WorldWidth, mh = tiledMap.WorldHeight;
			var acidEntity = CreateEntity("acid");
			var acidSurface = acidEntity.AddComponent(new AcidSurface(mw, mh, tiledMap));
			var contactHazard = acidEntity.AddComponent(new ContactHazard());
			contactHazard.DamagePerSecond = GameConstants.Hazards.AcidDamagePerSec;
			contactHazard.GetBounds = acidSurface.GetDamageBounds;
			var spawner = AddSceneComponent(new PlatformSpawner(mw, mh));
			AddSceneComponent(new AcidPhaseManager(acidSurface, spawner, mw, mh));
			// Phase 1 deadly-polish: ambient bubbles rising from the surface.
			AddSceneComponent(new AcidBubbleEmitter(acidSurface, mw, mh));

#if DEBUG
			if (AppSettings.DebugServer)
			{
				var exporter = AddSceneComponent(new GorelordsBrawler.DevTools.DebugStateExporter(playerManager));
				// Acid-specific state — other features should register their own
				// keys the same way so /state stays a union of whatever is on screen.
				exporter.RegisterProvider("acidActive", () => acidSurface.IsRising);
				exporter.RegisterProvider("acidLevel",  () => (int)acidSurface.CurrentLevel);
				exporter.RegisterProvider("acidSpeed",  () => acidSurface.IsRising ? 1 : 0);
			}
#endif

			var cameraEntity = CreateEntity(GameConstants.EntityNames.Camera);
			var brawlerCam = cameraEntity.AddComponent(new BrawlerCamera());
			brawlerCam.SetMapBounds(tiledMap.WorldWidth, tiledMap.WorldHeight);
			brawlerCam.SetAcidSurface(acidSurface);
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
