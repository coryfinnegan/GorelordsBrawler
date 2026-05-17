using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards.Fluid
{
	/// <summary>
	/// PASS 1 of the metaball-splat liquid pipeline (see
	/// <c>.claude/skills/nez-liquid-rendering/SKILL.md</c>): draws every
	/// particle as a soft-alpha disc sprite. We do NOT manage blend state
	/// here — this component is rendered exclusively by
	/// <c>LiquidFieldRenderer</c>, which has already opened the Batcher
	/// with <see cref="BlendState.Additive"/> and pointed it at the
	/// field <c>RenderTexture</c>. Overlapping particles' alpha values
	/// accumulate into a "potential field" that the post-process shader
	/// then thresholds into solid liquid.
	///
	/// IMPORTANT — <c>RenderLayer = LiquidRenderLayer</c> is intentionally
	/// NOT listed in the scene's default <see cref="RenderLayerRenderer"/>,
	/// so this component is invisible to the regular scene renderer. Only
	/// the LiquidFieldRenderer picks it up.
	/// </summary>
	public sealed class FluidRenderer : RenderableComponent
	{
		private const int DiscTexSize = 32;

		private readonly FluidSimulation _sim;
		private readonly int _mapWidth;
		private readonly int _mapHeight;
		private readonly Color _splatColor;

		private Texture2D _discTex;
		private Vector2   _discOrigin;
		private float     _splatScale;

		public override RectangleF Bounds => new RectangleF(0, 0, _mapWidth, _mapHeight);

		public FluidRenderer(FluidSimulation sim, int mapWidth, int mapHeight, Color tint)
		{
			_sim       = sim;
			_mapWidth  = mapWidth;
			_mapHeight = mapHeight;
			// Per-particle splat color. The RGB is essentially irrelevant — the
			// shader overrides the body color from a uniform — but we keep the
			// tint here so debug visualisations of the raw field RT have the
			// right hue. What matters for the metaball threshold is the ALPHA
			// channel: full-alpha at the disc center, falling to 0 at the
			// disc edge. The additive blend in pass 1 then accumulates that
			// alpha across overlapping particles.
			_splatColor = tint;

			RenderLayer = GameConstants.Rendering.LiquidRenderLayer;
			LayerDepth  = 0f;
		}

		public override void OnAddedToEntity()
		{
			_discTex    = CreateSoftDiscTexture(DiscTexSize);
			_discOrigin = new Vector2(DiscTexSize * 0.5f, DiscTexSize * 0.5f);
			// Splat radius MUST be > physics radius so neighbouring particles'
			// fields overlap heavily. ~3× physics radius is the sweet spot
			// recommended by GameDev.net "Fluid Rendering with Box2D".
			_splatScale = (FluidConfig.SplatRadius * 2f) / DiscTexSize;
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

			for (int i = 0; i < count; i++)
			{
				batcher.Draw(_discTex, new Vector2(px[i], py[i]), null,
					_splatColor, 0f, _discOrigin, _splatScale,
					SpriteEffects.None, 0f);
			}
		}

		public override void OnRemovedFromEntity()
		{
			_discTex?.Dispose();
			_discTex = null;
		}

		// ──────────────────────────────────────────────────────────────────
		// Soft-alpha disc texture, generated once. Quadratic falloff:
		// (1 − d²)^1.5. Alpha is 1.0 at the centre and 0 at the radius
		// boundary. The additive accumulation depends on this curve for
		// clean potential-field behaviour.
		// ──────────────────────────────────────────────────────────────────
		internal static Texture2D CreateSoftDiscTexture(int size)
		{
			var tex  = new Texture2D(Core.GraphicsDevice, size, size);
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
