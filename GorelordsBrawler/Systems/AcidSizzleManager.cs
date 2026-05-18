using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.Particles;
using Nez.Textures;
using GorelordsBrawler.Components;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Components.Hazards.Fluid;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	/// <summary>
	/// Phase 2 of the acid-deadly-polish-plan: per-player BURN feedback when
	/// they're standing in acid. Two effects, both fired off
	/// <see cref="ContactHazard.OnDamageApplied"/>:
	///
	///   1. A bright yellow-white smoke puff at the contact point on the acid
	///      surface — distinct enough from the passive green bubbles (Phase 1)
	///      to read as "damage right now, here" rather than ambient fizz.
	///   2. The player's existing <see cref="HitFlash"/> component triggers a
	///      red tint so the player visibly takes a hit (same feedback they
	///      already get from melee attacks).
	///
	/// Pool design lifted from <see cref="HitParticleManager"/>: round-robin
	/// of N emitter entities. Per-tick burst count is small because the damage
	/// event fires at ~30 Hz per damaged player and we don't want to drown the
	/// screen — the cap is bounded by particle MaxParticles too.
	///
	/// Hosted as a <see cref="Component"/> on its own entity (not a
	/// <see cref="SceneComponent"/>) so the <c>[Inspectable]</c> tuning knobs
	/// appear in the Nez runtime inspector under the entity — same convention
	/// as <see cref="AcidBubbleEmitter"/>. Per-emit config fields are mirrored
	/// onto the shared <see cref="ParticleEmitterConfig"/> reference each emit
	/// so inspector tweaks take effect on the next puff.
	///
	/// Render layer: <see cref="GameConstants.Rendering.HitboxRenderLayer"/>
	/// — same convention as bubbles + hit particles, in front of the liquid
	/// post-process and safe from cross-renderable state-corruption.
	/// </summary>
	public class AcidSizzleManager : Component, IUpdatable
	{
		private const int PoolSize = 8;

		// ── Live-tunable intensity knobs ──────────────────────────────────────
		[Inspectable, Range(1, 20)]
		public int   ParticlesPerTick = 5;

		[Inspectable, Range(1f, 24f)]
		public float StartSize        = 7f;

		[Inspectable, Range(2f, 40f)]
		public float FinishSize       = 14f;

		// RiseSpeed bumped 55 → 80 + Lifespan bumped 0.55 → 0.75: review feedback
		// asked for puffs to "climb a little higher." Max rise ≈ Speed * Lifespan;
		// these together roughly double the peak plume height (~30 px → ~60 px)
		// while keeping the fade soft.
		[Inspectable, Range(0f, 200f)]
		public float RiseSpeed        = 80f;

		[Inspectable, Range(0.1f, 2f)]
		public float Lifespan         = 0.75f;

		// AngleVariance widens the velocity cone — particles fan out as they
		// rise. Bumped 35 → 55 for the requested "slightly more horizontally
		// spread" look. Particles still aim straight up on average; only the
		// per-particle deviation grows.
		[Inspectable, Range(0f, 90f)]
		public float AngleVariance    = 55f;

		// SpawnSpreadX widens the box of spawn positions (mirrored into
		// ParticleEmitterConfig.SourcePositionVariance.X). Bumped 8 → 14 so
		// the base of the puff is visibly wider at emission, not just at the
		// top of the cone. Y kept tight (2 px) so the puff still hugs the
		// contact line.
		[Inspectable, Range(0f, 40f)]
		public float SpawnSpreadX     = 14f;

		// ── Refs / state ──────────────────────────────────────────────────────
		private readonly AcidSurface   _acid;
		private readonly ContactHazard _hazard;

		private Entity[]                 _poolEntities;
		private ParticleEmitter[]        _emitters;
		private ParticleEmitterConfig[]  _configs;
		private Sprite                   _puffSprite;
		private int                      _nextSlot;
		private bool                     _subscribed;

		public AcidSizzleManager(AcidSurface acid, ContactHazard hazard)
		{
			_acid   = acid;
			_hazard = hazard;
		}

		public override void OnAddedToEntity()
		{
			// Reuse the soft-disc texture-gen helper from the liquid renderer
			// — same falloff curve we use for bubbles, just here it reads as
			// smoke instead of fizz because of the color + velocity profile.
			_puffSprite = new Sprite(FluidRenderer.CreateSoftDiscTexture(16));

			_poolEntities = new Entity[PoolSize];
			_emitters     = new ParticleEmitter[PoolSize];
			_configs      = new ParticleEmitterConfig[PoolSize];

			for (int i = 0; i < PoolSize; i++)
			{
				var entity = Entity.Scene.CreateEntity($"acid-sizzle-{i}");

				var config = BuildConfig();
				_configs[i] = config;

				var emitter = new ParticleEmitter(config, playOnAwake: false);
				emitter.RenderLayer = GameConstants.Rendering.HitboxRenderLayer;
				emitter.LayerDepth  = 0f;
				entity.AddComponent(emitter);

				_poolEntities[i] = entity;
				_emitters[i]     = emitter;
			}

			if (_hazard != null)
			{
				_hazard.OnDamageApplied += OnDamageApplied;
				_subscribed = true;
			}
		}

		public override void OnRemovedFromEntity()
		{
			if (_subscribed && _hazard != null)
			{
				_hazard.OnDamageApplied -= OnDamageApplied;
				_subscribed = false;
			}
		}

		public void Update()
		{
			// Mirror live-tunable fields onto every pooled config so inspector
			// slider changes propagate to the next emit on any slot. Same
			// pattern as AcidBubbleEmitter — Nez ParticleEmitter reads from
			// the config object on each Emit() and we hold the same reference.
			for (int i = 0; i < _configs.Length; i++)
			{
				var cfg = _configs[i];
				cfg.Speed                  = RiseSpeed;
				cfg.ParticleLifespan       = Lifespan;
				cfg.StartParticleSize      = StartSize;
				cfg.FinishParticleSize     = FinishSize;
				cfg.AngleVariance          = AngleVariance;
				// Keep Y spread tight (2 px) — only widening the X axis
				// matters for "wider plume at the contact line."
				cfg.SourcePositionVariance = new Vector2(SpawnSpreadX, 2f);
			}
		}

		private void OnDamageApplied(Entity player)
		{
			if (player == null) return;

			// Place the puff at the LOCAL acid surface near the player —
			// where the body actually meets the air-acid boundary.
			//
			// Earlier cuts:
			//   - Transform.Position.Y → reads as "smoke from the crotch"
			//     for a centered sprite.
			//   - Bounds.Bottom (feet)  → correct contact point but gets
			//     buried in the pool when the player wades deeper than
			//     their ankles; puffs spawn underwater and rarely surface
			//     before their lifespan expires.
			//   - GetSurfaceLevelAtX    → returns the topmost wet cell in
			//     the column, so a splash on a platform overhead misled the
			//     puff way above the player.
			//
			// GetLocalSurfaceLevelAtX scans from the player's HEAD downward
			// and returns the first wet cell. That's the surface at the
			// player's body. Ranges of behaviour:
			//   - shallow wading: surface at knees/ankles → puff at knees
			//   - deep wading:    surface at chest/head   → puff at chest
			//   - fully submerged (head under): query returns surface at
			//     head row (since head is itself wet); puff at head, rises
			//     out of the pool quickly.
			//   - no acid in column yet (corner case): fall back to feet.
			var collider = player.GetComponent<Collider>();
			float x       = player.Transform.Position.X;
			float feetY   = collider != null ? collider.Bounds.Bottom : player.Transform.Position.Y;
			float headY   = collider != null ? collider.Bounds.Top    : player.Transform.Position.Y;
			float surface = _acid.GetLocalSurfaceLevelAtX(x, headY);
			// min: if the local surface is above the feet (typical wading),
			// puff at surface; else surface returned mapHeight (no wet cells
			// in column under the head) → fall through to feet.
			float puffY   = System.Math.Min(surface, feetY);

			int slot = _nextSlot;
			_nextSlot = (_nextSlot + 1) % PoolSize;
			_poolEntities[slot].Transform.Position = new Vector2(x, puffY);
			_emitters[slot].Emit(ParticlesPerTick);

			// Red HitFlash on the player sprite — same feedback they get
			// from melee hits. The component is attached by CharacterFactory
			// so every active player has one; null-conditional covers the
			// missing-component case.
			player.GetComponent<HitFlash>()?.Trigger();
		}

		private ParticleEmitterConfig BuildConfig()
		{
			return new ParticleEmitterConfig
			{
				EmitterType              = ParticleEmitterType.Gravity,
				MaxParticles             = 64,
				Speed                    = RiseSpeed,
				SpeedVariance            = 22f,
				Angle                    = -90f,                  // straight up
				AngleVariance            = AngleVariance,
				Gravity                  = new Vector2(0f, -25f), // slight upward push (rising smoke)
				ParticleLifespan         = Lifespan,
				ParticleLifespanVariance = 0.15f,
				StartColor               = new Color((byte)255, (byte)250, (byte)200, (byte)240),
				FinishColor              = new Color((byte)200, (byte)255, (byte)140, (byte)0),
				StartParticleSize        = StartSize,
				FinishParticleSize       = FinishSize,
				SimulateInWorldSpace     = true,
				Sprite                   = _puffSprite,
				SourcePositionVariance   = new Vector2(SpawnSpreadX, 2f),
				BlendFuncSource          = Blend.SourceAlpha,
				BlendFuncDestination     = Blend.InverseSourceAlpha,
			};
		}
	}
}
