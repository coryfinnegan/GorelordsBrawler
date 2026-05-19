using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.Tiled;
using GorelordsBrawler.Components;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Components.Hazards.Fluid;
using GorelordsBrawler.Components.PostProcessors;
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
			AddSceneComponent(new PauseManager());
			AddSceneComponent(new CombatEffectsManager());
			AddSceneComponent(new HitParticleManager());
			var playerManager = AddSceneComponent(new PlayerManager());

			// Phase 3 see-through-acid: render every active player's CURRENT
			// sprite frame to a dedicated RT with Color.White tint. The RT's
			// alpha channel is a pixel-perfect silhouette consumed by liquid.fx
			// for the "show player through the acid" effect — pixel-perfect
			// instead of bounding-rect-approximated, so the see-through region
			// follows the actual sprite shape and animation pose.
			var playerMaskRenderer = new PlayerMaskRenderer(
				GameConstants.Rendering.PlayerMaskRendererOrder, playerManager);
			AddRenderer(playerMaskRenderer);

			// Liquid post-process is wired AFTER PlayerMaskRenderer exists
			// because it samples the mask RT every frame as the player-presence
			// signal in the shader.
			AddPostProcessor(new LiquidPostProcessor(
				GameConstants.Rendering.LiquidPostProcessorOrder,
				liquidEffect,
				liquidFieldRenderer,
				playerMaskRenderer,
				bodyColor: new Color((byte)45, (byte)180, (byte)40, (byte)255),
				edgeColor: new Color((byte)150, (byte)255, (byte)90, (byte)255)));

			// Phase 4 deadly-polish: chromatic-aberration pulse on acid damage
			// + HP-driven radial vignette. Runs at order 10 so it composites
			// AFTER the liquid pass (order 0) — the vignette/CA apply to the
			// FINAL image including the green acid. Driving logic lives on the
			// DamageFeedbackController entity created after BrawlerCamera below
			// (it needs the camera ref for shake). The post-processor itself is
			// added here so its order slot is fixed and any later mutations to
			// the post-process stack don't accidentally reshuffle priorities.
			var damageFeedbackEffectPath = Path.Combine(
				System.AppDomain.CurrentDomain.BaseDirectory,
				"Content", "Effects", "damage_feedback.mgfxo");
			var damageFeedbackEffect = new Effect(Core.GraphicsDevice,
				File.ReadAllBytes(damageFeedbackEffectPath));
			var damageFeedbackPost = AddPostProcessor(new DamageFeedbackPostProcessor(
				GameConstants.Rendering.DamageFeedbackPostProcessorOrder,
				damageFeedbackEffect));
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
			// Hosted on its own entity (rather than as a SceneComponent) so
			// the [Inspectable] tuning knobs (SpawnsPerSec, StartSize, etc.)
			// appear in the Nez runtime inspector under this entity.
			var bubbleEntity = CreateEntity("acid-bubbles");
			bubbleEntity.AddComponent(new AcidBubbleEmitter(acidSurface, mw, mh));

			// Phase 2 deadly-polish: per-player burn feedback when standing in
			// acid. Subscribes to ContactHazard.OnDamageApplied and fires a
			// yellow smoke puff at the contact point + a HitFlash on the
			// damaged player. Hosted on its own entity (same convention as
			// AcidBubbleEmitter) so the [Inspectable] tuning knobs appear in
			// the Nez runtime inspector under this entity.
			var sizzleEntity = CreateEntity("acid-sizzle");
			sizzleEntity.AddComponent(new AcidSizzleManager(acidSurface, contactHazard));

			// Phase 3 deadly-polish: in-acid presence — give each player a
			// SubmersionFeel component that flips their PhysicsBody to
			// reduced-gravity + drag while submerged. Wired here (not in
			// CharacterFactory) because the AcidSurface dependency only
			// exists at the scene level; CharacterFactory stays hazard-
			// agnostic. The visibility half of Phase 3 lives in liquid.fx
			// and is driven by the LiquidPostProcessor above — nothing to
			// wire per-player for that part.
			foreach (var player in playerManager.GetActivePlayers())
			{
				player.AddComponent(new SubmersionFeel(acidSurface));
			}

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
			// Static view: lock the camera to map center at fit-to-map zoom.
			// The acid-rising-follow + player-zoom motion was reading as a
			// "blip" tied to the acid surface (it tracked the volumetric
			// CurrentLevel estimate, which jumps as soon as the inlet fires
			// long before any particles are visually on screen). Shake from
			// CombatEffectsManager etc. still applies on top of the locked
			// position. Targets and acid-surface coupling are intentionally
			// dropped so nothing pulls the view around.
			brawlerCam.Static = true;

			// Phase 4 deadly-polish: spin up the controller AFTER brawlerCam
			// because it holds a ref to call AddShake on each acid damage
			// tick. Hosted on its own entity (same convention as
			// AcidBubbleEmitter / AcidSizzleManager) so its [Inspectable]
			// tunables appear in the Nez runtime inspector. The post-processor
			// it drives was already AddPostProcessor'd above; this controller
			// just writes per-frame uniforms into it.
			var damageFeedbackEntity = CreateEntity("damage-feedback");
			damageFeedbackEntity.AddComponent(new DamageFeedbackController(
				damageFeedbackPost, playerManager, contactHazard, brawlerCam));

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
