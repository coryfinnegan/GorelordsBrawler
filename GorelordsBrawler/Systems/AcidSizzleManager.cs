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

		[Inspectable, Range(0f, 200f)]
		public float RiseSpeed        = 55f;

		[Inspectable, Range(0.1f, 2f)]
		public float Lifespan         = 0.55f;

		// ── Refs / state ──────────────────────────────────────────────────────
		private readonly AcidSurface   _acid;
		private readonly ContactHazard _hazard;
		private readonly int           _mapHeight;

		private Entity[]                 _poolEntities;
		private ParticleEmitter[]        _emitters;
		private ParticleEmitterConfig[]  _configs;
		private Sprite                   _puffSprite;
		private int                      _nextSlot;
		private bool                     _subscribed;

		public AcidSizzleManager(AcidSurface acid, ContactHazard hazard, int mapHeight)
		{
			_acid      = acid;
			_hazard    = hazard;
			_mapHeight = mapHeight;
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
				cfg.Speed              = RiseSpeed;
				cfg.ParticleLifespan   = Lifespan;
				cfg.StartParticleSize  = StartSize;
				cfg.FinishParticleSize = FinishSize;
			}
		}

		private void OnDamageApplied(Entity player)
		{
			if (player == null) return;

			// Place the puff at the player's contact point.
			//
			// Naive approach (GetSurfaceLevelAtX → use that Y) is wrong: the
			// occupancy grid returns the TOPMOST wet cell in the column, so
			// if any acid has splashed on a higher platform sharing the
			// player's x-column the puff appears way above the player —
			// reads as "burning a ghost up there" instead of "burning this
			// player here."
			//
			// Right rule: never place the puff above the player. Clamp to
			// max(player.Y, surfaceY) so the puff is either at the acid
			// surface (if it's at/below the player's body — normal wading
			// case) or right at the player (if the topmost wet cell is on
			// some unrelated splash overhead, or no wet cells exist yet).
			float x        = player.Transform.Position.X;
			float puffY    = player.Transform.Position.Y;
			float surfaceY = _acid.GetSurfaceLevelAtX(x);
			if (surfaceY < _mapHeight && surfaceY > puffY)
			{
				puffY = surfaceY;
			}

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
				AngleVariance            = 35f,                   // wide puff cone
				Gravity                  = new Vector2(0f, -25f), // slight upward push (rising smoke)
				ParticleLifespan         = Lifespan,
				ParticleLifespanVariance = 0.15f,
				StartColor               = new Color((byte)255, (byte)250, (byte)200, (byte)240),
				FinishColor              = new Color((byte)200, (byte)255, (byte)140, (byte)0),
				StartParticleSize        = StartSize,
				FinishParticleSize       = FinishSize,
				SimulateInWorldSpace     = true,
				Sprite                   = _puffSprite,
				SourcePositionVariance   = new Vector2(8f, 2f),
				BlendFuncSource          = Blend.SourceAlpha,
				BlendFuncDestination     = Blend.InverseSourceAlpha,
			};
		}
	}
}
