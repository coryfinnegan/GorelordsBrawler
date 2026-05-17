using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards.Fluid
{
	/// <summary>
	/// Renders the fluid as filled solid pools (no per-particle discs) +
	/// airborne droplet sprites for the falling stream.
	///
	/// The "chunky soft-disc" look came from each particle drawing its own
	/// blob. To get true LIQUID we instead:
	///
	///   1. Bucket particles into fine vertical columns. For each column
	///      record min/max Y of any particle in it AND the count.
	///   2. Columns whose count exceeds <see cref="BodyDensityThreshold"/>
	///      are POOLS — fill them with a solid rect from smoothed-min Y
	///      to smoothed-max Y. This catches the floor pool AND any pool
	///      sitting on top of a platform, without the body bleeding into
	///      space below.
	///   3. Columns with too few particles are TREATED AS AIRBORNE — those
	///      particles render as small soft-disc droplets so the falling
	///      stream still reads as discrete drops.
	///   4. Bright thin highlight strip drawn ON the pool surface to give
	///      the liquid a meniscus / sheen.
	///
	/// Everything goes through <see cref="Batcher"/> with default alpha-
	/// blend material — no direct <c>GraphicsDevice</c> state changes
	/// (previously corrupted batcher state and broke the player
	/// SpriteAnimator render on the same layer).
	/// </summary>
	public sealed class FluidRenderer : RenderableComponent
	{
		private const int   DiscTexSize           = 32;
		private const int   ColumnWidth           = 8;     // px per surface bucket
		// A "pool" column needs both:
		//   - enough particles to count as a body
		//   - particles confined to a SHORT Y range — falling streams have
		//     particles spread over hundreds of pixels and should stay
		//     droplets, not become a tall green column.
		private const int   BodyDensityThreshold  = 4;
		// Allow deep pools (up to ~200 px) — falling streams typically span
		// the whole 700+ px from inlet to floor so they're well above this.
		private const float BodyMaxPoolHeight     = 200f;
		private const float SurfaceBleed          = 2f;    // px the body extends past max Y
		private const float SurfaceTopOffset      = 2f;    // px above min Y to start body
		private const float SurfaceHighlightThickness = 3f;

		private readonly FluidSimulation _sim;
		private readonly AcidSurface     _surface;
		private readonly int _mapWidth;
		private readonly int _mapHeight;
		private readonly Color _bodyColor;
		private readonly Color _surfaceHighlight;
		private readonly Color _dropletColor;

		// Per-column state, reused frame to frame.
		private float[] _colMinY;
		private float[] _colMaxY;
		private int[]   _colCount;
		private float[] _colSmoothMinY;
		private float[] _colSmoothMaxY;
		private int     _numCols;

		// Disc texture for airborne droplets.
		private Texture2D _discTex;
		private Vector2   _discOrigin;
		private float     _dropletScale;

		public override RectangleF Bounds => new RectangleF(0, 0, _mapWidth, _mapHeight);

		public FluidRenderer(FluidSimulation sim, AcidSurface surface,
			int mapWidth, int mapHeight, Color tint)
		{
			_sim       = sim;
			_surface   = surface;
			_mapWidth  = mapWidth;
			_mapHeight = mapHeight;

			// Body: full alpha, slightly darker than tint for depth.
			_bodyColor = new Color(
				(byte)Math.Max(0, tint.R - 15),
				(byte)Math.Max(0, tint.G - 10),
				(byte)Math.Max(0, tint.B - 20),
				(byte)255);

			// Surface highlight: a brighter version drawn as a thin strip
			// at the surface. Looks like a wet sheen / meniscus.
			_surfaceHighlight = new Color(
				(byte)Math.Min(255, tint.R + 80),
				(byte)Math.Min(255, tint.G + 50),
				(byte)Math.Min(255, tint.B + 40),
				(byte)255);

			// Airborne droplets: brighter still, semi-transparent.
			_dropletColor = new Color(
				(byte)Math.Min(255, tint.R + 80),
				(byte)Math.Min(255, tint.G + 40),
				(byte)Math.Min(255, tint.B + 50),
				(byte)220);

			RenderLayer = GameConstants.Rendering.DefaultRenderLayer;
			LayerDepth  = 0f;
		}

		public override void OnAddedToEntity()
		{
			_discTex      = CreateSoftDiscTexture(DiscTexSize);
			_discOrigin   = new Vector2(DiscTexSize * 0.5f, DiscTexSize * 0.5f);
			_dropletScale = (FluidConfig.DropletDiscRadius * 2f) / DiscTexSize;

			_numCols       = (_mapWidth + ColumnWidth - 1) / ColumnWidth;
			_colMinY       = new float[_numCols];
			_colMaxY       = new float[_numCols];
			_colCount      = new int[_numCols];
			_colSmoothMinY = new float[_numCols];
			_colSmoothMaxY = new float[_numCols];
		}

		public override void Render(Batcher batcher, Camera camera)
		{
			if (_sim == null || _sim.Count == 0)
			{
				return;
			}

			int count = _sim.Count;
			var px    = _sim.Px;
			var py    = _sim.Py;

			// 1. Bucket particles by column.
			for (int c = 0; c < _numCols; c++)
			{
				_colMinY[c]  = _mapHeight;
				_colMaxY[c]  = 0f;
				_colCount[c] = 0;
			}
			for (int i = 0; i < count; i++)
			{
				int col = (int)(px[i] / ColumnWidth);
				if (col < 0 || col >= _numCols) continue;
				_colCount[col]++;
				float y = py[i];
				if (y < _colMinY[col]) _colMinY[col] = y;
				if (y > _colMaxY[col]) _colMaxY[col] = y;
			}

			// 2. 5-tap box smooth min and max so the surface lerps between
			//    adjacent columns instead of stair-stepping per particle.
			for (int c = 0; c < _numCols; c++)
			{
				int c0 = Math.Max(0, c - 2);
				int c1 = Math.Max(0, c - 1);
				int c3 = Math.Min(_numCols - 1, c + 1);
				int c4 = Math.Min(_numCols - 1, c + 2);
				_colSmoothMinY[c] = (_colMinY[c0] + _colMinY[c1] + _colMinY[c]
				                   + _colMinY[c3] + _colMinY[c4]) * 0.2f;
				_colSmoothMaxY[c] = (_colMaxY[c0] + _colMaxY[c1] + _colMaxY[c]
				                   + _colMaxY[c3] + _colMaxY[c4]) * 0.2f;
			}

			// 3. Body fill — columns that look like pools, not streams.
			//    Both criteria must hold: enough particles, packed shallow.
			for (int c = 0; c < _numCols; c++)
			{
				if (_colCount[c] < BodyDensityThreshold) continue;
				float poolHeight = _colSmoothMaxY[c] - _colSmoothMinY[c];
				if (poolHeight > BodyMaxPoolHeight) continue;   // falling stream — leave as droplets
				float top = _colSmoothMinY[c] - SurfaceTopOffset;
				float bot = _colSmoothMaxY[c] + SurfaceBleed;
				if (bot <= top) continue;
				float x = c * ColumnWidth;
				batcher.DrawRect(x, top, ColumnWidth, bot - top, _bodyColor);
				batcher.DrawRect(x, top, ColumnWidth, SurfaceHighlightThickness, _surfaceHighlight);
			}

			// 4. Droplets — every particle in a non-pool column (either too
			//    sparse OR too tall a Y range) gets rendered as a soft drop.
			for (int i = 0; i < count; i++)
			{
				int col = (int)(px[i] / ColumnWidth);
				if (col < 0 || col >= _numCols) continue;
				if (_colCount[col] >= BodyDensityThreshold &&
				    (_colSmoothMaxY[col] - _colSmoothMinY[col]) <= BodyMaxPoolHeight)
				{
					continue;   // this column rendered as body — particle already covered
				}
				batcher.Draw(_discTex, new Vector2(px[i], py[i]), null,
					_dropletColor, 0f, _discOrigin, _dropletScale,
					SpriteEffects.None, 0f);
			}
		}

		public override void OnRemovedFromEntity()
		{
			_discTex?.Dispose();
			_discTex = null;
		}

		// ──────────────────────────────────────────────────────────────────
		// Procedural soft-disc texture for airborne droplets / particle FX.
		// Public so AcidEffectsManager can reuse it.
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
