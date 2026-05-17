using System;
using Nez;

namespace GorelordsBrawler.Components.Hazards.Fluid
{
	/// <summary>
	/// Coarse 2D "wet cell" grid built each frame from the particle positions.
	/// Used to answer surface and damage-bounds queries cheaply without
	/// running through 4000+ particles per call.
	///
	/// CellSize defaults to FluidConfig.GridCellSize (16 px). On a 1280×800 map
	/// that's 80 × 50 = 4000 cells; total memory ≈ 8 KB.
	/// </summary>
	public sealed class FluidOccupancyGrid
	{
		public readonly int Cols;
		public readonly int Rows;
		public readonly int CellSize;
		public readonly int MapWidth;
		public readonly int MapHeight;

		private readonly short[] _counts;        // particles per cell
		private readonly short[] _topRowPerCol;  // top-most wet row in each column (Rows = "dry")
		private int _leftWetCol  = -1;
		private int _rightWetCol = -1;
		private int _topWetRow   = -1;
		private int _bottomWetRow = -1;

		public FluidOccupancyGrid(int mapWidth, int mapHeight, int cellSize)
		{
			MapWidth  = mapWidth;
			MapHeight = mapHeight;
			CellSize  = cellSize;
			Cols      = Math.Max(1, mapWidth  / cellSize);
			Rows      = Math.Max(1, mapHeight / cellSize);
			_counts        = new short[Cols * Rows];
			_topRowPerCol  = new short[Cols];
			Clear();
		}

		public void Clear()
		{
			Array.Clear(_counts, 0, _counts.Length);
			for (int c = 0; c < Cols; c++)
			{
				_topRowPerCol[c] = (short)Rows; // dry sentinel
			}
			_leftWetCol = _rightWetCol = -1;
			_topWetRow = _bottomWetRow = -1;
		}

		/// <summary>Rebuild from current particle positions.</summary>
		public void RebuildFrom(FluidSimulation sim)
		{
			Clear();
			int count = sim.Count;
			int wetThreshold = FluidConfig.WetThreshold;
			for (int i = 0; i < count; i++)
			{
				float x = sim.Px[i];
				float y = sim.Py[i];
				int cx = (int)(x / CellSize);
				int cy = (int)(y / CellSize);
				if (cx < 0 || cx >= Cols || cy < 0 || cy >= Rows)
				{
					continue;
				}
				int idx = cy * Cols + cx;
				short c = ++_counts[idx];

				if (c == wetThreshold)
				{
					// This cell just became wet — update column top and bounds
					if (cy < _topRowPerCol[cx])
					{
						_topRowPerCol[cx] = (short)cy;
					}
					if (_leftWetCol  < 0 || cx < _leftWetCol ) _leftWetCol  = cx;
					if (_rightWetCol < 0 || cx > _rightWetCol) _rightWetCol = cx;
					if (_topWetRow    < 0 || cy < _topWetRow   ) _topWetRow    = cy;
					if (_bottomWetRow < 0 || cy > _bottomWetRow) _bottomWetRow = cy;
				}
			}
		}

		/// <summary>World Y of the topmost wet cell in the column at worldX, or MapHeight if dry.</summary>
		public float GetSurfaceYAt(float worldX)
		{
			int cx = (int)(worldX / CellSize);
			if (cx < 0) cx = 0;
			if (cx >= Cols) cx = Cols - 1;
			short row = _topRowPerCol[cx];
			if (row >= Rows)
			{
				return MapHeight;
			}
			return row * CellSize;
		}

		/// <summary>
		/// World Y of the topmost wet cell in the column at worldX whose row
		/// is at or below the row containing ceilingWorldY. Returns MapHeight
		/// if no wet cell qualifies.
		///
		/// Use this when you need the surface of the acid mass NEAR a specific
		/// reference point (e.g. a player wading in the pool) instead of the
		/// absolute topmost wet cell — splashes on higher platforms in the
		/// same column would otherwise mislead the caller.
		/// </summary>
		public float GetSurfaceYAtBelow(float worldX, float ceilingWorldY)
		{
			int cx = (int)(worldX / CellSize);
			if (cx < 0) cx = 0;
			if (cx >= Cols) cx = Cols - 1;

			int startRow = (int)(ceilingWorldY / CellSize);
			if (startRow < 0)     startRow = 0;
			if (startRow >= Rows) return MapHeight;

			int wetThreshold = FluidConfig.WetThreshold;
			for (int r = startRow; r < Rows; r++)
			{
				if (_counts[r * Cols + cx] >= wetThreshold)
				{
					return r * CellSize;
				}
			}
			return MapHeight;
		}

		/// <summary>Minimum (highest on-screen) surface across the column range covering [leftX, rightX].</summary>
		public float GetMinSurfaceYInRange(float leftX, float rightX)
		{
			int cL = (int)(leftX  / CellSize);
			int cR = (int)(rightX / CellSize);
			if (cL < 0) cL = 0;
			if (cR >= Cols) cR = Cols - 1;
			if (cR < cL) cR = cL;

			short minRow = (short)Rows;
			for (int c = cL; c <= cR; c++)
			{
				if (_topRowPerCol[c] < minRow)
				{
					minRow = _topRowPerCol[c];
				}
			}
			if (minRow >= Rows)
			{
				return MapHeight;
			}
			return minRow * CellSize;
		}

		/// <summary>Bounding rectangle of all wet cells. Empty rect if no wet cells.</summary>
		public RectangleF GetWetBounds()
		{
			if (_leftWetCol < 0)
			{
				return default;
			}
			float x = _leftWetCol * CellSize;
			float y = _topWetRow * CellSize;
			float w = (_rightWetCol - _leftWetCol + 1) * CellSize;
			float h = (_bottomWetRow - _topWetRow + 1) * CellSize;
			return new RectangleF(x, y, w, h);
		}

		/// <summary>Whether any cell is wet.</summary>
		public bool HasWetCells => _leftWetCol >= 0;
	}
}
