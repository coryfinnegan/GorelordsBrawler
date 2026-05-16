using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards.Fluid
{
	/// <summary>
	/// Renders each fluid particle as a soft-alpha disc through Nez's Batcher.
	/// Overlapping discs naturally fuse into smooth, blobby "metaball-ish"
	/// shapes instead of the chunky-square look that flat quads gave us.
	///
	/// Two passes per particle, both sharing one generated disc texture so the
	/// whole liquid body batches into one draw call per pass:
	///
	///   1. Body            — saturated green disc at full DiscSpriteRadius
	///                        with alpha blend. With ~3× physics-radius discs,
	///                        neighbouring particles overlap heavily and merge
	///                        into a smooth blob.
	///   2. Surface sparkle — additive hot-green highlight, ONLY drawn on
	///                        particles within ±SurfaceBandHalfHeight of the
	///                        pool's current surface. Sells "this is glowing
	///                        acid, do not touch." Critically NOT applied to
	///                        the falling stream — additive on the densely-
	///                        packed stream would blow out to white.
	/// </summary>
	public sealed class FluidRenderer : RenderableComponent
	{
		// 32-px texture: more source pixels means the bilinear filter has a
		// nicer falloff to work with when we scale up to ~24-px world rendering.
		private const int   DiscTexSize           = 32;
		private const float SurfaceBandHalfHeight = 12f; // px on either side of CurrentLevel that counts as surface

		private readonly FluidSimulation _sim;
		private readonly AcidSurface     _surface;
		private readonly int _mapWidth;
		private readonly int _mapHeight;
		private readonly Color _body;
		private readonly Color _sparkle;

		private Texture2D _discTex;
		private Vector2   _discOrigin;
		private float     _bodyScale;
		private float     _sparkleScale;

		public override RectangleF Bounds => new RectangleF(0, 0, _mapWidth, _mapHeight);

		public FluidRenderer(FluidSimulation sim, AcidSurface surface,
			int mapWidth, int mapHeight, Color tint)
		{
			_sim       = sim;
			_surface   = surface;
			_mapWidth  = mapWidth;
			_mapHeight = mapHeight;

			// Body: more opaque than the source tint so overlapping discs fill
			// in to a solid look instead of leaving the under-surface visible.
			_body = new Color(tint.R, tint.G, tint.B, (byte)230);

			// Sparkle: hot acid-green, low alpha so a handful of overlapping
			// surface particles brighten the surface without nuking to white.
			_sparkle = new Color((byte)120, (byte)255, (byte)80, (byte)80);

			RenderLayer = GameConstants.Rendering.DefaultRenderLayer;
			LayerDepth  = 0f; // after the TiledMap, before characters
		}

		public override void OnAddedToEntity()
		{
			_discTex      = CreateSoftDiscTexture(DiscTexSize);
			_discOrigin   = new Vector2(DiscTexSize * 0.5f, DiscTexSize * 0.5f);
			_bodyScale    = (FluidConfig.DiscSpriteRadius * 2f) / DiscTexSize;
			// Sparkle is smaller — a highlight, not a body.
			_sparkleScale = _bodyScale * 0.6f;
		}

		public override void Render(Batcher batcher, Camera camera)
		{
			if (_sim == null || _sim.Count == 0)
			{
				return;
			}

			int count    = _sim.Count;
			var px       = _sim.Px;
			var py       = _sim.Py;
			float surfY  = _surface != null ? _surface.CurrentLevel : float.MaxValue;
			float bandLo = surfY - SurfaceBandHalfHeight;
			float bandHi = surfY + SurfaceBandHalfHeight;

			// ─ Pass 1: body (alpha blend, all particles) ───────────────────
			// Batcher.Begin was already called for us by the renderer with the
			// default state of AlphaBlend, so no switch needed.
			for (int i = 0; i < count; i++)
			{
				batcher.Draw(_discTex, new Vector2(px[i], py[i]), null,
					_body, 0f, _discOrigin, _bodyScale,
					SpriteEffects.None, 0f);
			}

			// ─ Pass 2: surface sparkle (additive, surface band only) ───────
			// Flush the AlphaBlend batch, switch to additive, then restore.
			batcher.FlushBatch();
			var gd = Core.GraphicsDevice;
			var prev = gd.BlendState;
			gd.BlendState = BlendState.Additive;

			for (int i = 0; i < count; i++)
			{
				float y = py[i];
				if (y < bandLo || y > bandHi) continue;
				batcher.Draw(_discTex, new Vector2(px[i], y), null,
					_sparkle, 0f, _discOrigin, _sparkleScale,
					SpriteEffects.None, 0f);
			}

			batcher.FlushBatch();
			gd.BlendState = prev;
		}

		public override void OnRemovedFromEntity()
		{
			_discTex?.Dispose();
			_discTex = null;
		}

		// ──────────────────────────────────────────────────────────────────
		// Procedural soft-disc texture: 1 at center, smooth quadratic falloff
		// to 0 at radius. White texel + per-draw tint = colored blob. Public
		// so AcidEffectsManager can reuse it for particle emitter sprites.
		// ──────────────────────────────────────────────────────────────────
		internal static Texture2D CreateSoftDiscTexture(int size)
		{
			var tex = new Texture2D(Core.GraphicsDevice, size, size);
			var data = new Color[size * size];
			float half = size * 0.5f;

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float dx = (x + 0.5f) - half;
					float dy = (y + 0.5f) - half;
					float d  = MathF.Sqrt(dx * dx + dy * dy) / half;
					float a  = MathHelper.Clamp(1f - d * d, 0f, 1f);
					a = MathF.Pow(a, 1.5f);
					byte alpha = (byte)(a * 255f);
					data[y * size + x] = new Color((byte)255, (byte)255, (byte)255, alpha);
				}
			}
			tex.SetData(data);
			return tex;
		}
	}
}
