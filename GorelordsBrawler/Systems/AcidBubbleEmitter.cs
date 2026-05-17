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
	/// Phase 1 of the acid-deadly-polish-plan: ambient bubbles rising off the
	/// acid surface.
	///
	/// One <see cref="ParticleEmitter"/> on the host entity that we re-position
	/// every spawn to a random x along the visible surface line. Burst-emit
	/// (<see cref="ParticleEmitter.Emit"/>) instead of continuous auto-emission
	/// so we can control the per-spawn position — Nez's continuous mode spawns
	/// at the entity's transform and we'd have to teleport between every particle.
	///
	/// Render layer: <see cref="GameConstants.Rendering.HitboxRenderLayer"/>
	/// (in front of the liquid post-process). Same convention as
	/// <see cref="HitParticleManager"/> — protects the player SpriteAnimator
	/// from any cross-renderable state corruption (see the PR #3 history).
	///
	/// Tunables are instance fields with <c>[Inspectable, Range(...)]</c> so
	/// they can be live-tuned in the Nez runtime inspector. Per-spawn fields
	/// (size, rise speed, lifespan) propagate to the live emitter every frame
	/// by mirroring them onto the shared <see cref="ParticleEmitterConfig"/>
	/// reference — ParticleEmitter reads from this on each <c>Emit()</c>.
	/// SpawnsPerSec is consumed directly in <see cref="Update"/>.
	///
	/// Why a <c>Component</c> and not a <c>SceneComponent</c>: SceneComponents
	/// don't appear in the standard entity-inspector tree, so the inspectable
	/// fields wouldn't be discoverable. Attaching to an entity makes the
	/// sliders show up under the entity in the runtime inspector.
	/// </summary>
	public class AcidBubbleEmitter : Component, IUpdatable
	{
		// ── Live-tunable intensity knobs ──────────────────────────────────────
		[Inspectable, Range(0f, 64f)]
		public float SpawnsPerSec = FluidConfig.BubbleSpawnsPerSec;

		[Inspectable, Range(0.5f, 24f)]
		public float StartSize    = FluidConfig.BubbleStartSize;

		[Inspectable, Range(0.5f, 32f)]
		public float FinishSize   = FluidConfig.BubbleFinishSize;

		[Inspectable, Range(0f, 200f)]
		public float RiseSpeed    = FluidConfig.BubbleRiseSpeed;

		[Inspectable, Range(0.1f, 4f)]
		public float Lifespan     = FluidConfig.BubbleLifespan;

		// ── Refs / state ──────────────────────────────────────────────────────
		private readonly AcidSurface _acid;
		private readonly int         _mapWidth;
		private readonly int         _mapHeight;

		private static readonly System.Random _rng = new System.Random(0xBBB1);

		private ParticleEmitter        _emitter;
		private ParticleEmitterConfig  _config;   // SAME ref the emitter holds
		private Sprite                 _bubbleSprite;
		private float                  _spawnAccum;

		public AcidBubbleEmitter(AcidSurface acid, int mapWidth, int mapHeight)
		{
			_acid      = acid;
			_mapWidth  = mapWidth;
			_mapHeight = mapHeight;
		}

		public override void OnAddedToEntity()
		{
			// Reuse the soft-disc texture-gen helper from the liquid renderer
			// — same falloff curve, just smaller scale. Cheap, no extra asset.
			_bubbleSprite = new Sprite(FluidRenderer.CreateSoftDiscTexture(16));

			_config = new ParticleEmitterConfig
			{
				EmitterType              = ParticleEmitterType.Gravity,
				MaxParticles             = (uint)FluidConfig.BubbleMaxParticles,
				Speed                    = RiseSpeed,
				SpeedVariance            = FluidConfig.BubbleRiseSpeedVar,
				Angle                    = -90f,                 // straight up
				AngleVariance            = 15f,
				Gravity                  = new Vector2(0f, -40f),
				ParticleLifespan         = Lifespan,
				ParticleLifespanVariance = FluidConfig.BubbleLifespanVar,
				StartColor               = new Color((byte)180, (byte)255, (byte)160, (byte)210),
				FinishColor              = new Color((byte)200, (byte)255, (byte)140, (byte)0),
				StartParticleSize        = StartSize,
				FinishParticleSize       = FinishSize,
				SimulateInWorldSpace     = true,
				Sprite                   = _bubbleSprite,
				SourcePositionVariance   = new Vector2(4f, 1f),
				BlendFuncSource          = Blend.SourceAlpha,
				BlendFuncDestination     = Blend.InverseSourceAlpha,
			};
			_emitter = new ParticleEmitter(_config, playOnAwake: false);
			_emitter.RenderLayer = GameConstants.Rendering.HitboxRenderLayer;
			_emitter.LayerDepth  = 0f;
			Entity.AddComponent(_emitter);
		}

		public void Update()
		{
			if (_acid == null || _emitter == null) return;

			// Mirror live-tunable per-spawn fields into the shared config so
			// inspector slider changes take effect on the next Emit(). The
			// emitter holds the same object reference, so this is just a
			// field-by-field copy — no setter cost.
			_config.Speed              = RiseSpeed;
			_config.ParticleLifespan   = Lifespan;
			_config.StartParticleSize  = StartSize;
			_config.FinishParticleSize = FinishSize;

			float dt = Time.DeltaTime;
			_spawnAccum += dt * SpawnsPerSec;
			while (_spawnAccum >= 1f)
			{
				_spawnAccum -= 1f;
				float marginX = 20f;
				float spanX   = GameConstants.Arena.InnerRight - GameConstants.Arena.InnerLeft - marginX * 2f;
				float x       = GameConstants.Arena.InnerLeft + marginX + (float)_rng.NextDouble() * spanX;

				// Per-column wet-cell query, not the volumetric CurrentLevel
				// estimate — see the b9125db commit message for why.
				float surfaceY = _acid.GetSurfaceLevelAtX(x);
				if (surfaceY >= _mapHeight) continue;

				Entity.Transform.Position = new Vector2(x, surfaceY);
				_emitter.Emit(1);
			}
		}
	}
}
