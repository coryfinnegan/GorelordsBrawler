using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards.Fluid
{
	/// <summary>
	/// Draws each fluid particle as a soft-alpha disc sprite through Nez's
	/// Batcher (SpriteBatch path). One texture → one draw call regardless of
	/// particle count.
	///
	/// Alpha convention: NON-premultiplied. We rely on the default
	/// BlendState.AlphaBlend that Batcher uses; do NOT switch to premultiplied
	/// without updating the texture-generation step.
	///
	/// Soft additive overlap of adjacent discs is the metaball-ish look. A
	/// dedicated render-target threshold pass (UseMetaballPass) is reserved for
	/// future polish but disabled in this initial implementation.
	/// </summary>
	public sealed class FluidRenderer : RenderableComponent
	{
		private readonly FluidSimulation _sim;
		private readonly int _mapWidth;
		private readonly int _mapHeight;
		private readonly Color _tint;

		private Texture2D _discTexture;
		private Vector2 _origin;
		private float _spriteScale;

		public override RectangleF Bounds => new RectangleF(0, 0, _mapWidth, _mapHeight);

		public FluidRenderer(FluidSimulation sim, int mapWidth, int mapHeight, Color tint)
		{
			_sim       = sim;
			_mapWidth  = mapWidth;
			_mapHeight = mapHeight;
			_tint      = tint;

			RenderLayer = GameConstants.Rendering.DefaultRenderLayer;
			LayerDepth  = 0f; // draw after the tile map, before characters
		}

		public override void OnAddedToEntity()
		{
			const int Size = 16;
			_discTexture = new Texture2D(Core.GraphicsDevice, Size, Size);
			var data = new Color[Size * Size];
			const float half = Size * 0.5f;

			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					float dx = (x + 0.5f) - half;
					float dy = (y + 0.5f) - half;
					float d  = MathF.Sqrt(dx * dx + dy * dy) / half;
					float a  = MathHelper.Clamp(1f - d * d, 0f, 1f);
					a = MathF.Pow(a, 1.5f); // smooth falloff
					byte alpha = (byte)(a * 255f);
					data[y * Size + x] = new Color((byte)255, (byte)255, (byte)255, alpha);
				}
			}
			_discTexture.SetData(data);

			_origin       = new Vector2(half, half);
			_spriteScale  = (FluidConfig.DiscSpriteRadius * 2f) / Size;
		}

		public override void Render(Batcher batcher, Camera camera)
		{
			if (_sim == null || _sim.Count == 0)
			{
				return;
			}

			int count = _sim.Count;
			var px = _sim.Px;
			var py = _sim.Py;
			Vector2 pos;
			for (int i = 0; i < count; i++)
			{
				pos.X = px[i];
				pos.Y = py[i];
				batcher.Draw(
					_discTexture,
					pos,
					null,
					_tint,
					0f,
					_origin,
					_spriteScale,
					SpriteEffects.None,
					0f);
			}
		}

		public override void OnRemovedFromEntity()
		{
			_discTexture?.Dispose();
			_discTexture = null;
		}
	}
}
