using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.Particles;
using Nez.Textures;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Components.Hazards.Fluid;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	/// <summary>
	/// Visual polish for the acid hazard. Manages three pools of
	/// <see cref="ParticleEmitter"/>s:
	///
	///   • <b>Splash</b> — bright green droplets sprayed up + outward when
	///     something hits the surface (stream impact, falling platform,
	///     player landing in). Triggered by <see cref="AcidSurface.Disturb"/>
	///     plus periodic auto-splashes at the inlet's floor impact points.
	///
	///   • <b>Bubble</b> — slow-rising semi-transparent dots that pop at
	///     random points along the visible acid surface. Sells the "this is
	///     gross, eating away at things" vibe.
	///
	///   • <b>Smoke</b> — gray-yellow rising puffs spawned when the acid
	///     damages a player (hooks <see cref="ContactHazard.OnDamageApplied"/>).
	///     Reads as "your character is being burned right now."
	///
	/// Pool size of <see cref="PoolSize"/> per kind = up to that many
	/// simultaneous effects of each type without allocating mid-frame. We round-
	/// robin slot assignment — interrupting a still-playing emitter is fine
	/// because Emit() doesn't reset existing particles.
	/// </summary>
	public class AcidEffectsManager : SceneComponent
	{
		private const int   PoolSize           = 12;
		private const float BubbleSpawnRate    = 10f;   // bubbles/sec — more, smaller, frequent
		private const float CornerSplashEvery  = 0.35f; // sec between auto-splashes at cascade impacts

		private readonly AcidSurface   _acid;
		private readonly ContactHazard _hazard;
		private readonly int           _mapWidth;
		private readonly int           _mapHeight;

		private static readonly System.Random _rng = new System.Random(0xACE1D);

		// Shared procedural particle texture (white soft disc) — tinted per-config.
		private Sprite _discSprite;

		// Pools
		private ParticleEmitter[] _smokeEmitters;
		private ParticleEmitter[] _splashEmitters;
		private Entity[]          _smokeEntities;
		private Entity[]          _splashEntities;
		private int _nextSmokeSlot;
		private int _nextSplashSlot;

		// Continuous bubble emitter follows the surface — single instance, not pooled.
		private Entity          _bubbleEntity;
		private ParticleEmitter _bubbleEmitter;
		private ParticleEmitterConfig _bubbleConfig;

		// Bookkeeping
		private float _bubbleAccum;
		private float _splashAccum;
		private float _inletLeftX;
		private float _inletRightX;

		public AcidEffectsManager(AcidSurface acid, ContactHazard hazard, int mapWidth, int mapHeight)
		{
			_acid      = acid;
			_hazard    = hazard;
			_mapWidth  = mapWidth;
			_mapHeight = mapHeight;

			// The bottom platforms' outer edges are where the inlet cascade lands
			// on the floor pool — auto-splash there to sell continuous pouring.
			float minBotCy = 0f;
			(float cx, float cy, float w) botL = (0.313f, 0.820f, 0.125f);
			(float cx, float cy, float w) botR = (0.688f, 0.820f, 0.125f);
			foreach (var p in GameConstants.Hazards.Platforms)
			{
				if (p.cy <= minBotCy) continue;
				minBotCy = p.cy;
			}
			foreach (var p in GameConstants.Hazards.Platforms)
			{
				if (p.cy != minBotCy) continue;
				if (p.cx < 0.5f) botL = p;
				else             botR = p;
			}
			_inletLeftX  = (botL.cx - botL.w * 0.5f) * mapWidth;
			_inletRightX = (botR.cx + botR.w * 0.5f) * mapWidth;
		}

		public override void OnEnabled()
		{
			_discSprite = new Sprite(FluidRenderer.CreateSoftDiscTexture(16));

			_smokeEmitters  = new ParticleEmitter[PoolSize];
			_splashEmitters = new ParticleEmitter[PoolSize];
			_smokeEntities  = new Entity[PoolSize];
			_splashEntities = new Entity[PoolSize];

			for (int i = 0; i < PoolSize; i++)
			{
				_smokeEntities[i]  = BuildPooledEmitter($"acid-smoke-{i}",  BuildSmokeConfig(),  out _smokeEmitters[i]);
				_splashEntities[i] = BuildPooledEmitter($"acid-splash-{i}", BuildSplashConfig(), out _splashEmitters[i]);
			}

			// One always-on continuous bubble emitter. We move it along the
			// surface as the pool rises by retargeting its entity position from
			// Update(); each EmitOnce burst happens at the moved position.
			_bubbleEntity = Scene.CreateEntity("acid-bubbles");
			_bubbleConfig = BuildBubbleConfig();
			_bubbleEmitter = new ParticleEmitter(_bubbleConfig, playOnAwake: false);
			_bubbleEmitter.RenderLayer = GameConstants.Rendering.DefaultRenderLayer;
			_bubbleEmitter.LayerDepth  = 0f;
			_bubbleEntity.AddComponent(_bubbleEmitter);

			// Hook contact damage → smoke
			if (_hazard != null)
			{
				_hazard.OnDamageApplied += OnPlayerBurned;
			}
		}

		public override void OnDisabled()
		{
			if (_hazard != null)
			{
				_hazard.OnDamageApplied -= OnPlayerBurned;
			}
		}

		private Entity BuildPooledEmitter(string name, ParticleEmitterConfig config, out ParticleEmitter emitter)
		{
			var entity = Scene.CreateEntity(name);
			emitter = new ParticleEmitter(config, playOnAwake: false);
			emitter.RenderLayer = GameConstants.Rendering.DefaultRenderLayer;
			emitter.LayerDepth  = 0f;
			entity.AddComponent(emitter);
			return entity;
		}

		// ──────────────────────────────────────────────────────────────────
		// Update — periodic bubble + auto-splash drivers
		// ──────────────────────────────────────────────────────────────────

		public override void Update()
		{
			if (_acid == null || !_acid.IsRising) return;

			float dt = Time.DeltaTime;
			float surfaceY = _acid.CurrentLevel;
			if (surfaceY >= _mapHeight) return;   // pool empty / off-screen

			// Bubbles at random surface positions. We use the smoothed pool
			// level (CurrentLevel), not GetSurfaceLevelAtX — the latter also
			// reports falling-stream particles in the column above the pool,
			// so bubbles would spawn near the top of the screen.
			_bubbleAccum += dt * BubbleSpawnRate;
			while (_bubbleAccum >= 1f)
			{
				_bubbleAccum -= 1f;
				float x = (float)_rng.NextDouble() * (GameConstants.Arena.InnerRight - GameConstants.Arena.InnerLeft - 40f)
				          + GameConstants.Arena.InnerLeft + 20f;
				_bubbleEntity.Transform.Position = new Vector2(x, surfaceY);
				_bubbleEmitter.Emit(1);
			}

			// Periodic splashes at the two cascade impact points (also clamped
			// to CurrentLevel so they don't fire off-screen at the top).
			_splashAccum += dt;
			if (_splashAccum >= CornerSplashEvery)
			{
				_splashAccum = 0f;
				SpawnSplash(new Vector2(_inletLeftX,  surfaceY));
				SpawnSplash(new Vector2(_inletRightX, surfaceY));
			}
		}

		// ──────────────────────────────────────────────────────────────────
		// Public effect API — other systems can poke these to spawn one-offs
		// ──────────────────────────────────────────────────────────────────

		/// <summary>Burst of splash droplets at a world point.</summary>
		public void SpawnSplash(Vector2 worldPos, int count = 12)
		{
			if (_splashEmitters == null) return;
			int slot = _nextSplashSlot;
			_nextSplashSlot = (_nextSplashSlot + 1) % PoolSize;
			_splashEntities[slot].Transform.Position = worldPos;
			_splashEmitters[slot].Emit(count);
		}

		/// <summary>Rising smoke puff at a world point (used for burn-on-contact).</summary>
		public void SpawnSmoke(Vector2 worldPos, int count = 6)
		{
			if (_smokeEmitters == null) return;
			int slot = _nextSmokeSlot;
			_nextSmokeSlot = (_nextSmokeSlot + 1) % PoolSize;
			_smokeEntities[slot].Transform.Position = worldPos;
			_smokeEmitters[slot].Emit(count);
		}

		// ──────────────────────────────────────────────────────────────────
		// Damage hook
		// ──────────────────────────────────────────────────────────────────

		private void OnPlayerBurned(Entity victim, Vector2 contactPoint, int damage)
		{
			// Vent smoke at the pool's body surface (not GetSurfaceLevelAtX,
			// which also reports falling-stream particles at column tops —
			// would put smoke off-screen at y≈30 above the inlet). Cap at the
			// player's Y so we don't fire smoke off the bottom of a deep
			// player who is below the pool surface.
			float poolY = _acid != null ? _acid.CurrentLevel : contactPoint.Y;
			float ventY = Math.Min(contactPoint.Y, poolY);
			int count   = 6 + Math.Min(damage, 4);
			SpawnSmoke(new Vector2(contactPoint.X, ventY), count);
		}

		// ──────────────────────────────────────────────────────────────────
		// Emitter config builders
		// ──────────────────────────────────────────────────────────────────

		private ParticleEmitterConfig BuildSplashConfig()
		{
			// Bright sparking droplets that arc up + outward. Additive so they
			// pop against the green pool below — reads as "splash of light."
			return new ParticleEmitterConfig
			{
				EmitterType              = ParticleEmitterType.Gravity,
				MaxParticles             = 80,
				Speed                    = 260f,
				SpeedVariance            = 100f,
				Angle                    = -90f,
				AngleVariance            = 55f,
				Gravity                  = new Vector2(0f, 1200f),
				ParticleLifespan         = 0.6f,
				ParticleLifespanVariance = 0.18f,
				StartColor               = new Color((byte)200, (byte)255, (byte)140, (byte)220),
				FinishColor              = new Color((byte)90,  (byte)200, (byte)40,  (byte)0),
				StartParticleSize        = 8f,
				FinishParticleSize       = 2f,
				SimulateInWorldSpace     = true,
				Sprite                   = _discSprite,
				SourcePositionVariance   = new Vector2(6f, 2f),
				BlendFuncSource          = Blend.SourceAlpha,
				BlendFuncDestination     = Blend.One,        // additive: visible above the bright pool
			};
		}

		private ParticleEmitterConfig BuildBubbleConfig()
		{
			// Slow-rising, bright-yellow-green bubbles. Live longer so they
			// rise visibly above the pool surface. Additive blending makes them
			// pop against the dark green pool body.
			return new ParticleEmitterConfig
			{
				EmitterType              = ParticleEmitterType.Gravity,
				MaxParticles             = 256,
				Speed                    = 22f,
				SpeedVariance            = 12f,
				Angle                    = -90f,
				AngleVariance            = 25f,
				Gravity                  = new Vector2(0f, -60f),
				ParticleLifespan         = 1.4f,
				ParticleLifespanVariance = 0.3f,
				StartColor               = new Color((byte)220, (byte)255, (byte)160, (byte)200),
				FinishColor              = new Color((byte)180, (byte)230, (byte)100, (byte)0),
				StartParticleSize        = 6f,
				FinishParticleSize       = 12f,
				SimulateInWorldSpace     = true,
				Sprite                   = _discSprite,
				SourcePositionVariance   = new Vector2(10f, 2f),
				BlendFuncSource          = Blend.SourceAlpha,
				BlendFuncDestination     = Blend.One,        // additive bubbles glow against the pool
			};
		}

		private ParticleEmitterConfig BuildSmokeConfig()
		{
			// Smoke off a burning player. Starts hot acid-orange then cools
			// to dim gray — reads unambiguously as "you are being burned."
			// Alpha-blended (not additive) so it OCCLUDES the green pool
			// instead of glowing — that's what makes it look like smoke
			// versus glow. Fast initial rise so the puff escapes above the
			// pool surface immediately.
			return new ParticleEmitterConfig
			{
				EmitterType              = ParticleEmitterType.Gravity,
				MaxParticles             = 192,
				Speed                    = 140f,
				SpeedVariance            = 40f,
				Angle                    = -90f,
				AngleVariance            = 22f,
				Gravity                  = new Vector2(0f, -160f),
				ParticleLifespan         = 1.6f,
				ParticleLifespanVariance = 0.4f,
				StartColor               = new Color((byte)255, (byte)170, (byte)50,  (byte)255),
				FinishColor              = new Color((byte)90,  (byte)90,  (byte)90,  (byte)0),
				StartParticleSize        = 12f,
				FinishParticleSize       = 38f,
				SimulateInWorldSpace     = true,
				Sprite                   = _discSprite,
				SourcePositionVariance   = new Vector2(10f, 4f),
				BlendFuncSource          = Blend.SourceAlpha,
				BlendFuncDestination     = Blend.InverseSourceAlpha,
			};
		}
	}
}
