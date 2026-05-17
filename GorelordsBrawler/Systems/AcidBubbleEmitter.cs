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
	/// One <see cref="ParticleEmitter"/> on an entity that we re-position every
	/// spawn to a random x along the visible surface line. We use the burst
	/// API (<see cref="ParticleEmitter.Emit"/>) instead of continuous
	/// auto-emission so we can control the per-spawn position — Nez's
	/// continuous mode spawns at the entity's transform and our entity
	/// would have to teleport between every particle.
	///
	/// Render layer: <see cref="GameConstants.Rendering.HitboxRenderLayer"/>
	/// (in front of the liquid post-process). Same convention as
	/// <see cref="HitParticleManager"/> — protects the player SpriteAnimator
	/// from any cross-renderable state corruption (see the PR #3 history).
	/// </summary>
	public class AcidBubbleEmitter : SceneComponent
	{
		private readonly AcidSurface _acid;
		private readonly int _mapWidth;
		private readonly int _mapHeight;

		private static readonly System.Random _rng = new System.Random(0xBBB1);

		private Entity          _entity;
		private ParticleEmitter _emitter;
		private Sprite          _bubbleSprite;
		private float           _spawnAccum;

		public AcidBubbleEmitter(AcidSurface acid, int mapWidth, int mapHeight)
		{
			_acid      = acid;
			_mapWidth  = mapWidth;
			_mapHeight = mapHeight;
		}

		public override void OnEnabled()
		{
			// Reuse the soft-disc texture-gen helper from the liquid renderer
			// — same falloff curve, just smaller scale. Cheap, no extra asset.
			_bubbleSprite = new Sprite(FluidRenderer.CreateSoftDiscTexture(16));

			_entity = Scene.CreateEntity("acid-bubbles");
			var config = new ParticleEmitterConfig
			{
				EmitterType              = ParticleEmitterType.Gravity,
				MaxParticles             = (uint)FluidConfig.BubbleMaxParticles,
				Speed                    = FluidConfig.BubbleRiseSpeed,
				SpeedVariance            = FluidConfig.BubbleRiseSpeedVar,
				Angle                    = -90f,                 // straight up
				AngleVariance            = 15f,
				Gravity                  = new Vector2(0f, -40f),
				ParticleLifespan         = FluidConfig.BubbleLifespan,
				ParticleLifespanVariance = FluidConfig.BubbleLifespanVar,
				StartColor               = new Color((byte)180, (byte)255, (byte)160, (byte)210),
				FinishColor              = new Color((byte)200, (byte)255, (byte)140, (byte)0),
				StartParticleSize        = FluidConfig.BubbleStartSize,
				FinishParticleSize       = FluidConfig.BubbleFinishSize,
				SimulateInWorldSpace     = true,
				Sprite                   = _bubbleSprite,
				SourcePositionVariance   = new Vector2(4f, 1f),
				BlendFuncSource          = Blend.SourceAlpha,
				BlendFuncDestination     = Blend.InverseSourceAlpha,
			};
			_emitter = new ParticleEmitter(config, playOnAwake: false);
			_emitter.RenderLayer = GameConstants.Rendering.HitboxRenderLayer;
			_emitter.LayerDepth  = 0f;
			_entity.AddComponent(_emitter);
		}

		public override void Update()
		{
			if (_acid == null || _emitter == null) return;

			float dt = Time.DeltaTime;
			_spawnAccum += dt * FluidConfig.BubbleSpawnsPerSec;
			while (_spawnAccum >= 1f)
			{
				_spawnAccum -= 1f;
				float marginX = 20f;
				float spanX   = GameConstants.Arena.InnerRight - GameConstants.Arena.InnerLeft - marginX * 2f;
				float x       = GameConstants.Arena.InnerLeft + marginX + (float)_rng.NextDouble() * spanX;

				// Use the per-column wet-cell query, not the volumetric
				// CurrentLevel estimate. CurrentLevel drops below mapHeight the
				// moment the inlet fires — long before any particles are
				// visually settled at that column — which caused bubbles to
				// appear rising out of the bare floor before the acid was even
				// on screen. GetSurfaceLevelAtX returns mapHeight when the
				// column has no wet cells, so we just skip those columns.
				float surfaceY = _acid.GetSurfaceLevelAtX(x);
				if (surfaceY >= _mapHeight) continue;

				_entity.Transform.Position = new Vector2(x, surfaceY);
				_emitter.Emit(1);
			}
		}
	}
}
