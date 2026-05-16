using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards.Fluid
{
	/// <summary>
	/// Renders each fluid particle as a small colored quad via PrimitiveBatch
	/// (same path as the legacy WaterRenderer — a proven route through Nez's
	/// rendering pipeline that draws geometry independent of any Sprite cache).
	///
	/// Two passes:
	///   - Outer (full DiscSpriteRadius, tint color) gives the body of each blob.
	///   - Inner (half radius, brighter) hints at depth/highlight.
	///
	/// 6 vertices per particle × 2 passes = 12N. At 4000 particles that's ≤ 48k
	/// vertices per frame — well within PrimitiveBatch's per-batch budget.
	/// </summary>
	public sealed class FluidRenderer : RenderableComponent
	{
		private readonly FluidSimulation _sim;
		private readonly int _mapWidth;
		private readonly int _mapHeight;
		private readonly Color _outer;
		private readonly Color _inner;

		private PrimitiveBatch _primBatch;

		public override RectangleF Bounds => new RectangleF(0, 0, _mapWidth, _mapHeight);

		public FluidRenderer(FluidSimulation sim, int mapWidth, int mapHeight, Color tint)
		{
			_sim       = sim;
			_mapWidth  = mapWidth;
			_mapHeight = mapHeight;
			_outer     = tint;
			_inner     = new Color(
				(int)System.Math.Min(255, tint.R + 60),
				(int)System.Math.Min(255, tint.G + 50),
				(int)System.Math.Min(255, tint.B + 40),
				(int)255);

			RenderLayer = GameConstants.Rendering.DefaultRenderLayer;
			LayerDepth  = 0f; // after the TiledMap, before characters
		}

		public override void OnAddedToEntity()
		{
			// 12 vertices per particle × MaxParticles → up to 60k for 5000 particles.
			_primBatch = new PrimitiveBatch(65536);
		}

		public override void Render(Batcher batcher, Camera camera)
		{
			if (_sim == null || _sim.Count == 0)
			{
				return;
			}

			batcher.FlushBatch();

			var gd = Core.GraphicsDevice;
			gd.BlendState        = BlendState.AlphaBlend;
			gd.DepthStencilState = DepthStencilState.None;
			gd.RasterizerState   = RasterizerState.CullNone;

			Matrix proj = camera.ProjectionMatrix;
			Matrix view = camera.TransformMatrix;
			_primBatch.Begin(ref proj, ref view);

			float rOuter = FluidConfig.DiscSpriteRadius;
			float rInner = rOuter * 0.55f;

			int count = _sim.Count;
			var px = _sim.Px;
			var py = _sim.Py;

			// Outer halo pass
			for (int i = 0; i < count; i++)
			{
				AddQuad(px[i], py[i], rOuter, _outer);
			}

			// Inner core pass
			for (int i = 0; i < count; i++)
			{
				AddQuad(px[i], py[i], rInner, _inner);
			}

			_primBatch.End();
		}

		private void AddQuad(float cx, float cy, float r, Color c)
		{
			float x0 = cx - r, x1 = cx + r;
			float y0 = cy - r, y1 = cy + r;
			_primBatch.AddVertex(new Vector2(x0, y0), c, PrimitiveType.TriangleList);
			_primBatch.AddVertex(new Vector2(x1, y0), c, PrimitiveType.TriangleList);
			_primBatch.AddVertex(new Vector2(x0, y1), c, PrimitiveType.TriangleList);
			_primBatch.AddVertex(new Vector2(x1, y0), c, PrimitiveType.TriangleList);
			_primBatch.AddVertex(new Vector2(x1, y1), c, PrimitiveType.TriangleList);
			_primBatch.AddVertex(new Vector2(x0, y1), c, PrimitiveType.TriangleList);
		}

		public override void OnRemovedFromEntity()
		{
			_primBatch?.Dispose();
			_primBatch = null;
		}
	}
}
