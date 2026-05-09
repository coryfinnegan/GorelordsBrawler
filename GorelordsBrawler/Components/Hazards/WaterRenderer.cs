using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards
{
	/// <summary>
	/// Renders the CA fluid grid as a smooth triangle-mesh surface.
	/// Each adjacent pair of columns forms a trapezoid whose top edge interpolates
	/// between the two surface heights, giving smooth appearance despite 32-px grid steps.
	/// LayerDepth = 1.0 renders behind all sprites in the same RenderLayer.
	/// </summary>
	public class WaterRenderer : RenderableComponent
	{
		private readonly int   _mapWidth;
		private readonly int   _mapHeight;
		private readonly Color _surfaceColor;
		private readonly Color _depthColor;

		private float[] _surfaceY;
		private int     _cols;
		private int     _cellSize;

		private PrimitiveBatch _primBatch;

		public override RectangleF Bounds => new RectangleF(0, 0, _mapWidth, _mapHeight);

		public WaterRenderer(int mapWidth, int mapHeight, Color surfaceColor)
		{
			_mapWidth     = mapWidth;
			_mapHeight    = mapHeight;
			_surfaceColor = surfaceColor;
			_depthColor   = new Color(
				(int)(surfaceColor.R * 0.4f),
				(int)(surfaceColor.G * 0.45f),
				(int)(surfaceColor.B * 0.3f),
				230);

			RenderLayer = GameConstants.Rendering.DefaultRenderLayer;
			LayerDepth  = 0f; // insertion order: acid renders after TiledMap but before characters
		}

		public override void OnAddedToEntity()
		{
			// Max vertices: (Cols-2) column pairs × 6 vertices each ≈ 228 for a 40-col map
			_primBatch = new PrimitiveBatch(512);
		}

		/// <summary>
		/// Call once after construction so the renderer holds a reference to the live array.
		/// Because arrays are reference types the renderer always sees the latest frame data
		/// without a per-frame call.
		/// </summary>
		public void SetFluidState(float[] surfaceY, int cols, int cellSize)
		{
			_surfaceY = surfaceY;
			_cols     = cols;
			_cellSize = cellSize;
		}

		public override void Render(Batcher batcher, Camera camera)
		{
			if (_surfaceY == null) return;

			batcher.FlushBatch();

			var gd = Core.GraphicsDevice;
			gd.BlendState        = BlendState.AlphaBlend;
			gd.DepthStencilState = DepthStencilState.None;
			gd.RasterizerState   = RasterizerState.CullNone;

			Matrix proj = camera.ProjectionMatrix;
			Matrix view = camera.TransformMatrix;
			_primBatch.Begin(ref proj, ref view);

			float yBot = _mapHeight;

			for (int c = 1; c < _cols - 2; c++)
			{
				float sL = _surfaceY[c];
				float sR = _surfaceY[c + 1];

				if (sL >= yBot && sR >= yBot) continue;

				float x0 = c       * _cellSize;
				float x1 = (c + 1) * _cellSize;
				float y0 = Math.Min(sL, yBot);
				float y1 = Math.Min(sR, yBot);

				// Triangle 1: top-left → top-right → bottom-left
				_primBatch.AddVertex(new Vector2(x0, y0),  _surfaceColor, PrimitiveType.TriangleList);
				_primBatch.AddVertex(new Vector2(x1, y1),  _surfaceColor, PrimitiveType.TriangleList);
				_primBatch.AddVertex(new Vector2(x0, yBot), _depthColor,  PrimitiveType.TriangleList);

				// Triangle 2: top-right → bottom-right → bottom-left
				_primBatch.AddVertex(new Vector2(x1, y1),  _surfaceColor, PrimitiveType.TriangleList);
				_primBatch.AddVertex(new Vector2(x1, yBot), _depthColor,  PrimitiveType.TriangleList);
				_primBatch.AddVertex(new Vector2(x0, yBot), _depthColor,  PrimitiveType.TriangleList);
			}

			_primBatch.End();
		}

		public override void OnRemovedFromEntity()
		{
			_primBatch?.Dispose();
			_primBatch = null;
		}
	}
}
