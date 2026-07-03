using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.Tiled;
using GorelordsBrawler.Components;
using GorelordsBrawler.Components.Abilities;
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
			var combatEffects = AddSceneComponent(new CombatEffectsManager());
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
			// Phase A "Sump": seed a resting pool in the central basin so the acid
			// is present and dangerous from t=0 (the Calm phase) — long before the
			// scripted rise. Deferred internally until the sim exists.
			acidSurface.PreFill(
				GameConstants.Hazards.BasinLeftX,
				GameConstants.Hazards.BasinRightX,
				GameConstants.Hazards.BasinRestTopY,
				GameConstants.Hazards.BasinFloorY);
			var contactHazard = acidEntity.AddComponent(new ContactHazard());
			// Phase B: base rate is the SURFACE chip; depth scales it up per-player
			// via the closure below. (Replaced the old flat 4 dps drip.)
			contactHazard.DamagePerSecond = GameConstants.Hazards.AcidSurfaceDps;
			contactHazard.GetBounds = acidSurface.GetDamageBounds;
			var spawner = AddSceneComponent(new PlatformSpawner(mw, mh));
			// Phase C: the looping Calm→Rise→Scramble→Surge→Drain machine with
			// per-loop escalation and the terminal FinalFlood at the time cap.
			// Geometry/timing all live in AcidConfig (the Phase-A hand-sync debt
			// paid: nothing derives from the old normalized platform array).
			var phaseManager = AddSceneComponent(new AcidPhaseManager(acidSurface));
			// The log population target escalates with the loop (2 → 4): as the
			// static tiers dissolve, the arena transforms into a debris field
			// instead of emptying out.
			spawner.LoopProvider = () => phaseManager.Loop;

			// Dissolvable refuge tiers (functional-test decision: the acid EATS
			// the arena as it climbs). Each TMX "tiers" object becomes a solid
			// ledge that burns away when the rising surface reaches it. The LOW
			// pair gates the log spawner: drop-logs begin the moment the first
			// footing is eaten — platforms arrive BECAUSE the acid took the
			// ground, not on a timer.
			int tiersAlive = 0, lowTiersAlive = 0;
			var tierGroup = tiledMap.GetObjectGroup("tiers");
			if (tierGroup != null)
			{
				foreach (var obj in tierGroup.Objects)
				{
					string rank = "";
					if (obj.Properties != null)
					{
						obj.Properties.TryGetValue("rank", out rank);
					}
					var tierEntity = CreateEntity($"tier-{obj.Name}");
					tierEntity.Transform.Position = new Vector2(
						obj.X + obj.Width * 0.5f, obj.Y + obj.Height * 0.5f);
					var tier = tierEntity.AddComponent(new DissolvingPlatform(
						acidSurface, obj.Width, obj.Height, rank));

					tiersAlive++;
					bool isLow = rank == "low";
					if (isLow)
					{
						lowTiersAlive++;
					}
					tier.OnDissolved = () => tiersAlive--;
					// Debris starts falling once the low pair is MOSTLY chewed —
					// full erosion of the last crumbs lags the visible
					// destruction, and the logs should arrive while the acid is
					// still visibly eating the first footing.
					if (isLow)
					{
						tier.OnMostlyEroded = () =>
						{
							if (--lowTiersAlive == 0)
							{
								spawner.StartSpawning(acidSurface);
							}
						};
					}
				}
			}
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
			foreach (var slot in playerManager.GetActiveSlots())
			{
				slot.PlayerEntity.AddComponent(new SubmersionFeel(acidSurface));
				// Phase B escape mechanic: mash jump to stroke up out of the acid.
				// Needs the slot's InputProfile (same one Walk/JumpAbility use).
				slot.PlayerEntity.AddComponent(new SwimAbility(slot.Input));

				// Phase C flood-safe respawn: pick the spawn at the MOMENT of
				// respawn, from the lowest candidate whose column is dry — so a
				// death during a flood comes back on the surviving refuge, not
				// inside the acid. Closure pattern: RespawnHandler stays
				// hazard-agnostic, the scene installs the acid-aware policy.
				var respawnHandler = slot.PlayerEntity.GetComponent<RespawnHandler>();
				if (respawnHandler != null)
				{
					respawnHandler.SafeSpawnProvider = () => PickSafeSpawn(acidSurface);
				}
			}

			// Phase B depth-scaled lethality: the acid bites harder the deeper a
			// body is. The closure reads each player's SubmersionFeel state
			// (refreshed at UpdateOrder -10, before this hazard's Update) and maps
			// depth through the pure CombatMath curve. Surface lap = 1× (base
			// chip); fully submerged = AcidDeepDpsMult× → a fast melt that's still
			// escapable by swimming. Tuning lives in GameConstants.Hazards.
			//
			// NOT submerged = 0× — i.e. immune. This matters: ContactHazard's
			// GetBounds is the coarse AABB of ALL wet cells, and with the Sump's
			// central pour + splashes that rectangle can span dry bank ground a
			// player is standing on. Pre-Phase-B they'd take phantom chip damage
			// for being inside the box without touching acid. SubmersionFeel's
			// per-column local-surface query is the precise per-player contact
			// test, so the AABB is now just the broadphase and this closure is
			// the narrow-phase — damage tracks the fluid's actual shape (pillar 1).
			contactHazard.DamageScaleForEntity = entity =>
			{
				var feel = entity.GetComponent<SubmersionFeel>();
				if (feel == null)
				{
					return 1f;   // non-player / untracked entity: generic base rate
				}
				if (!feel.IsSubmerged)
				{
					return 0f;   // inside the wet AABB but not actually in acid
				}
				return Combat.CombatMath.AcidDpsMultiplier(
					feel.SubmergedDepth,
					GameConstants.Hazards.AcidDeepDpsMult,
					GameConstants.Hazards.AcidFullSubmergeDepth);
			};

#if DEBUG
			if (AppSettings.DebugServer)
			{
				var exporter = AddSceneComponent(new GorelordsBrawler.DevTools.DebugStateExporter(playerManager));
				// Acid-specific state — other features should register their own
				// keys the same way so /state stays a union of whatever is on screen.
				exporter.RegisterProvider("acidActive", () => acidSurface.IsRising);
				exporter.RegisterProvider("acidLevel",  () => (int)acidSurface.CurrentLevel);
				// MEASURED standing surface (median basin-center probes) — the
				// oracle tests assert fill ceilings against. acidLevel above is
				// the legacy volumetric ESTIMATE, geometry-blind for a basin
				// pool (it read "below the floor" while the pool sat on banks).
				exporter.RegisterProvider("acidSurfaceY", () => (int)acidSurface.GetStandingSurfaceY());
				exporter.RegisterProvider("acidSpeed",  () => acidSurface.IsRising ? 1 : 0);
				// Automation assertions: particle count proves the pool didn't vanish/respawn,
				// finiteness proves it didn't NaN (the hitstop dt=0 failure mode).
				exporter.RegisterProvider("acidParticleCount", () => acidSurface.ParticleCount);
				exporter.RegisterProvider("acidFinite",        () => acidSurface.AllParticlesFinite());
				// Phase B: the hazard's live damage AABB (the broadphase). E2E uses it to
				// prove the phantom-damage fix — a player INSIDE this box but not
				// submerged must take zero damage.
				exporter.RegisterProvider("acidBoundsLeft",   () => (int)acidSurface.GetDamageBounds().X);
				exporter.RegisterProvider("acidBoundsTop",    () => (int)acidSurface.GetDamageBounds().Y);
				exporter.RegisterProvider("acidBoundsRight",  () => (int)(acidSurface.GetDamageBounds().X + acidSurface.GetDamageBounds().Width));
				exporter.RegisterProvider("acidBoundsBottom", () => (int)(acidSurface.GetDamageBounds().Y + acidSurface.GetDamageBounds().Height));
				// Phase C oracles: the phase machine's observable state, so E2E can
				// assert transition order, escalation, drain recession, and the
				// final flood without screen-scraping.
				exporter.RegisterProvider("acidPhase",      () => phaseManager.Phase.ToString());
				exporter.RegisterProvider("acidLoop",       () => phaseManager.Loop);
				exporter.RegisterProvider("acidSurgeCount", () => acidSurface.SurgeCount);
				exporter.RegisterProvider("acidDraining",   () => phaseManager.IsDraining);
				exporter.RegisterProvider("acidFillCap",    () => acidSurface.ParticleCap);
				exporter.RegisterProvider("tiersRemaining", () => tiersAlive);
				// Combat: true during a hit freeze (TimeScale=0). Lets the acid-survives-a-hit
				// regression prove it actually drove the game through the dt=0 window.
				exporter.RegisterProvider("hitstopActive",     () => combatEffects.IsHitstopActive);
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

		/// <summary>
		/// Flood-aware respawn pick: the LOWEST AcidConfig candidate whose column's
		/// acid surface clears the feet by the clearance margin — respawns stay
		/// near the action until the flood forces them up the tiers. If NOTHING
		/// qualifies (deep storm, refuges gone), spawn ABOVE the live surface at
		/// the last candidate's column instead of inside the acid — the hazard
		/// may doom you, but it never executes you on frame 0 (Brinstar rule:
		/// the stage hazard launches and burns, it doesn't KO outright).
		/// </summary>
		private static Vector2 PickSafeSpawn(AcidSurface acid)
		{
			var candidates = AcidConfig.RespawnPoints;
			foreach (var p in candidates)
			{
				float headY   = p.Y - 24f;
				float feetY   = p.Y + 24f;
				float surface = acid.GetLocalSurfaceLevelAtX(p.X, headY);
				if (surface - feetY >= AcidConfig.RespawnClearancePx)
				{
					return p;
				}
			}

			var last = candidates[candidates.Length - 1];
			float standing = acid.GetStandingSurfaceY();
			// Feet (center + 24) must clear the standing surface by the margin.
			float safeY = standing - AcidConfig.RespawnClearancePx - 24f;
			return new Vector2(last.X, MathF.Min(last.Y, safeY));
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
