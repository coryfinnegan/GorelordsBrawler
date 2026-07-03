using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards
{
	/// <summary>
	/// A platform the acid EATS hole-by-hole — "swiss cheese" destructible
	/// terrain. The classic destructible-2D-terrain technique (Worms / Cortex
	/// Command — store the solid as a grid of cells, clear cells on contact,
	/// rebuild collision from what survives; see GameDev.net "destructible 2D
	/// terrain" and Envato Tuts+ "Coding Destructible Pixel Terrain").
	///
	/// Each frame it asks the acid where its metaball surface overlaps this
	/// slab and clears the touched cells. The visual is per-cell quads (so the
	/// holes are visible); the collision is a small set of BoxColliders rebuilt
	/// from the surviving solid runs (so a player walks/falls through the holes).
	/// When every cell is gone the entity destroys itself.
	///
	/// Used for BOTH the static refuge tiers and the floating drop-logs — the
	/// host (DissolvingTier / DynamicPlatform) owns spawning/floating; this
	/// owns "is solid here, and erode on contact."
	/// </summary>
	public class ErodibleSurface : Component, IUpdatable
	{
		public readonly float Width;
		public readonly float Height;

		/// <summary>Fired once when the last cell erodes (entity about to be destroyed).</summary>
		public Action OnFullyEroded;

		/// <summary>Fraction of cells still solid (1 → 0). Host reads it for feel/gating.</summary>
		public float SolidFraction => _totalCells > 0 ? (float)_solidCount / _totalCells : 0f;

		// Surviving-hull vertical extent, LOCAL to the entity center (px). A
		// floating host reads these so buoyancy tracks the wood that still
		// exists: with the nominal Height, a bottom-eaten log kept floating at
		// full-hull depth — feeding fresh wood to the waterline non-stop AND
		// visually hovering above the water once its wet rows were gone.
		public float SolidTopLocalY    { get; private set; }
		public float SolidBottomLocalY { get; private set; }
		public float SolidHeight => MathF.Max(0f, SolidBottomLocalY - SolidTopLocalY);

		private readonly AcidSurface _acid;
		private readonly float _passesPerSec;   // per-surface erosion rate (tier vs log — AcidConfig)
		private readonly int _cols;
		private readonly int _rows;
		private readonly float _cellW;
		private readonly float _cellH;
		private readonly bool[] _solid;          // [row * cols + col]
		private readonly bool[] _eaten;          // per-pass scratch (mark, then remove)
		// Consecutive wet-pass count per cell — the DWELL filter. A cell only
		// erodes once its own center has been wet ErosionDwellPasses passes in
		// a row, so flickering spray (the corner streams' impact froth) can't
		// chew a slab the standing surface hasn't reached.
		private readonly byte[] _wetStreak;
		private int _solidCount;
		private readonly int _totalCells;

		private readonly List<Collider> _colliders = new();
		// Cached merged rectangles (cell-space) for the renderer — recomputed
		// only when cells change, so Render is cheap and the platform draws as
		// clean solid chunks with clean holes, not a shimmer of tiny quads.
		private readonly List<Rectangle> _mergedRects = new();
		private readonly Color _baseColor;

		// Per-frame erosion cadence: accumulate "erosion passes" so the carve
		// rate is frame-independent. Each pass eats EVERY perimeter cell that
		// currently touches acid (contact-based), so the holes grow inward from
		// wherever the metaball actually laps the platform.
		private float _passAccum;

		// Phase E: optional slab texture. The renderer maps it PROPORTIONALLY
		// onto the slab's full area (each merged rect samples its own sub-rect),
		// so erosion reveals holes through a stable image instead of the
		// pattern swimming as cells vanish. Null → flat baseColor (greybox).
		private readonly Texture2D _texture;
		internal Texture2D SlabTexture => _texture;

		public ErodibleSurface(AcidSurface acid, float width, float height, Color baseColor,
			float passesPerSec, Texture2D texture = null)
		{
			_acid         = acid;
			Width         = width;
			Height        = height;
			_baseColor    = baseColor;
			_passesPerSec = passesPerSec;
			_texture      = texture;

			_cols  = Math.Max(1, (int)MathF.Round(width  / AcidConfig.ErosionCellSize));
			_rows  = Math.Max(1, (int)MathF.Round(height / AcidConfig.ErosionCellSize));
			_cellW = width  / _cols;
			_cellH = height / _rows;
			_solid = new bool[_cols * _rows];
			_eaten = new bool[_cols * _rows];
			_wetStreak = new byte[_cols * _rows];
			for (int i = 0; i < _solid.Length; i++)
			{
				_solid[i] = true;
			}
			_totalCells = _solid.Length;
			_solidCount = _totalCells;
			SolidTopLocalY    = -height * 0.5f;
			SolidBottomLocalY =  height * 0.5f;
		}

		public override void OnAddedToEntity()
		{
			Entity.AddComponent(new ErodibleRenderer(this));
			RebuildGeometry();
		}

		// ── Cell helpers (local space: origin at the slab's top-left) ─────────
		internal int Cols => _cols;
		internal int Rows => _rows;
		internal float CellW => _cellW;
		internal float CellH => _cellH;
		internal Color BaseColor => _baseColor;
		internal bool IsSolid(int col, int row) => _solid[row * _cols + col];

		/// <summary>Local top-left of the slab relative to the entity center.</summary>
		internal Vector2 LocalTopLeft => new Vector2(-Width * 0.5f, -Height * 0.5f);

		public void Update()
		{
			ErodeFromAcid();
		}

		private void ErodeFromAcid()
		{
			if (_solidCount == 0)
			{
				return;
			}

			// Frame-independent carve cadence. One "pass" eats every perimeter
			// cell touching acid this tick; the per-surface rate (ctor — tier vs
			// log, see AcidConfig) sets how fast the front advances. Divided by
			// TimeScale so debug-fast (compressed durations) erodes
			// proportionally faster, like the drain.
			_passAccum += (_passesPerSec / AcidConfig.TimeScale()) * Time.DeltaTime;
			int passes = (int)_passAccum;
			if (passes <= 0)
			{
				return;
			}
			_passAccum -= passes;

			bool changed = false;
			for (int p = 0; p < passes && _solidCount > 0; p++)
			{
				changed |= ErodeOnePass();
			}

			if (changed)
			{
				RebuildGeometry();
				if (_solidCount == 0)
				{
					OnFullyEroded?.Invoke();
					Entity.Destroy();
				}
			}
		}

		/// <summary>
		/// One contact-erosion pass: every SOLID cell that is on the perimeter
		/// (touches empty space or a slab edge) AND has been IMMERSED — its own
		/// center wet for <see cref="AcidConfig.ErosionDwellPasses"/> consecutive
		/// passes — is removed. This is the user's mental model literally — "the
		/// metaball touches the platform here, so this spot gets eaten" — so the
		/// holes grow inward from exactly the faces the acid laps (top, sides,
		/// and the underside of a submerged slab), reading as the acid chewing
		/// the platform rather than a level-line sweeping it.
		///
		/// The dwell requirement is the froth guard: the corner streams' impact
		/// spray flickers cells wet for a pass at a time hundreds of px above
		/// the pool, and without dwell it dissolved tiers the standing surface
		/// hadn't reached (2026-07-02 probe). Standing laps and crest washes are
		/// continuously wet and pass the filter one pass late.
		///
		/// Marks the cells first, removes after, so each cell is judged against
		/// THIS pass's perimeter (no cascade within one pass — that's what
		/// successive passes are for).
		/// </summary>
		private bool ErodeOnePass()
		{
			var topLeft = Entity.Transform.Position + LocalTopLeft;
			bool any = false;

			for (int row = 0; row < _rows; row++)
			{
				for (int col = 0; col < _cols; col++)
				{
					int idx = row * _cols + col;
					if (!_solid[idx])
					{
						continue;
					}

					// Which faces are exposed? (Also the perimeter test.)
					bool up    = row == 0         || !_solid[(row - 1) * _cols + col];
					bool down  = row == _rows - 1 || !_solid[(row + 1) * _cols + col];
					bool left  = col == 0         || !_solid[row * _cols + col - 1];
					bool right = col == _cols - 1 || !_solid[row * _cols + col + 1];
					if (!(up || down || left || right))
					{
						_wetStreak[idx] = 0;
						continue;
					}

					// Sample just beyond each EXPOSED face — where water sits if
					// that face is immersed. The cell's own center can never be
					// wet (colliders keep particles out of the slab, and a
					// grid-aligned 32 px tier's occupancy cells are entirely
					// interior). The DENSE test (IsAcidBodyAt) requires a body
					// of liquid at the face, not stray spray — the corner
					// streams' impact geysers held boolean wetness for whole
					// seconds and chewed tiers 100+ px above the pool.
					// Direction-aware body contact + the dwell streak =
					// "this face is held under acid."
					float wx = topLeft.X + (col + 0.5f) * _cellW;
					float wy = topLeft.Y + (row + 0.5f) * _cellH;
					bool faceWet =
						   (up    && _acid.IsAcidBodyAt(wx, wy - _cellH))
						|| (down  && _acid.IsAcidBodyAt(wx, wy + _cellH))
						|| (left  && _acid.IsAcidBodyAt(wx - _cellW, wy))
						|| (right && _acid.IsAcidBodyAt(wx + _cellW, wy));

					if (faceWet)
					{
						if (_wetStreak[idx] < byte.MaxValue)
						{
							_wetStreak[idx]++;
						}
						if (_wetStreak[idx] >= AcidConfig.ErosionDwellPasses)
						{
							_eaten[idx] = true;
							any = true;
						}
					}
					else
					{
						_wetStreak[idx] = 0;
					}
				}
			}

			if (!any)
			{
				return false;
			}

			for (int i = 0; i < _eaten.Length; i++)
			{
				if (_eaten[i])
				{
					_eaten[i] = false;
					_solid[i] = false;
					_solidCount--;
				}
			}
			return true;
		}

		/// <summary>
		/// Rebuild colliders from surviving cells via GREEDY RECTANGLE MERGING
		/// (same algorithm Nez's TMX GetCollisionRectangles uses): claim the
		/// widest unclaimed solid run on a row, extend it downward while the
		/// full width stays solid, emit one box. An intact slab is exactly ONE
		/// collider; a swiss-cheesed one is a handful. This matters beyond the
		/// broadphase: the fluid's FluidCollider.Project iterates every AABB in
		/// the wet region PER PARTICLE — per-column boxes at a 4 px cell size
		/// would put ~48 rects per slab into that inner loop.
		/// </summary>
		private void RebuildGeometry()
		{
			// Greedy-merge surviving cells into the fewest rectangles (cell-space
			// Rectangles), then build a BoxCollider per rect AND cache the rects
			// for the renderer. Computing once and sharing keeps both the
			// per-particle collider loop and the draw call cheap, and the
			// renderer draws clean solid chunks (not a shimmer of 4 px quads).
			_mergedRects.Clear();
			var claimed = new bool[_solid.Length];

			for (int row = 0; row < _rows; row++)
			{
				for (int col = 0; col < _cols; col++)
				{
					int idx = row * _cols + col;
					if (!_solid[idx] || claimed[idx])
					{
						continue;
					}

					int w = 1;
					while (col + w < _cols && _solid[idx + w] && !claimed[idx + w])
					{
						w++;
					}

					int h = 1;
					bool canGrow = true;
					while (canGrow && row + h < _rows)
					{
						int below = (row + h) * _cols + col;
						for (int k = 0; k < w; k++)
						{
							if (!_solid[below + k] || claimed[below + k])
							{
								canGrow = false;
								break;
							}
						}
						if (canGrow)
						{
							h++;
						}
					}

					for (int r = 0; r < h; r++)
					{
						for (int k = 0; k < w; k++)
						{
							claimed[(row + r) * _cols + col + k] = true;
						}
					}

					_mergedRects.Add(new Rectangle(col, row, w, h));
				}
			}

			// Surviving-hull vertical extent for the host's buoyancy (free from
			// the merged rects). If nothing survives, keep the last span — the
			// entity is about to destroy itself anyway.
			if (_mergedRects.Count > 0)
			{
				int minRow = int.MaxValue, maxRowEnd = 0;
				for (int i = 0; i < _mergedRects.Count; i++)
				{
					if (_mergedRects[i].Y < minRow)
					{
						minRow = _mergedRects[i].Y;
					}
					int end = _mergedRects[i].Y + _mergedRects[i].Height;
					if (end > maxRowEnd)
					{
						maxRowEnd = end;
					}
				}
				SolidTopLocalY    = LocalTopLeft.Y + minRow * _cellH;
				SolidBottomLocalY = LocalTopLeft.Y + maxRowEnd * _cellH;
			}

			// Rebuild colliders from the merged rects.
			for (int i = 0; i < _colliders.Count; i++)
			{
				_colliders[i].Entity?.RemoveComponent(_colliders[i]);
			}
			_colliders.Clear();

			var topLeft = LocalTopLeft;   // collider offsets are entity-local
			foreach (var r in _mergedRects)
			{
				float bw = r.Width  * _cellW;
				float bh = r.Height * _cellH;
				float cx = topLeft.X + r.X * _cellW + bw * 0.5f;
				float cy = topLeft.Y + r.Y * _cellH + bh * 0.5f;

				var collider = new BoxCollider(bw, bh) { LocalOffset = new Vector2(cx, cy) };
				collider.PhysicsLayer = PhysicsLayers.Platforms;
				collider.ShouldColliderScaleAndRotateWithTransform = false;
				Entity.AddComponent(collider);
				_colliders.Add(collider);
			}
		}

		/// <summary>Cached merged solid rectangles (cell-space) for the renderer.</summary>
		internal IReadOnlyList<Rectangle> MergedRects => _mergedRects;
	}

	/// <summary>Draws an <see cref="ErodibleSurface"/>'s surviving area as merged rects.</summary>
	internal class ErodibleRenderer : RenderableComponent
	{
		private readonly ErodibleSurface _surface;
		public override float Width  => _surface.Width;
		public override float Height => _surface.Height;

		public ErodibleRenderer(ErodibleSurface surface) => _surface = surface;

		public override void Render(Batcher batcher, Camera camera)
		{
			var topLeft = Entity.Transform.Position + _surface.LocalTopLeft;
			var rects = _surface.MergedRects;
			var tex = _surface.SlabTexture;
			for (int i = 0; i < rects.Count; i++)
			{
				var r = rects[i];
				var rect = new Rectangle(
					(int)MathF.Floor(topLeft.X + r.X * _surface.CellW),
					(int)MathF.Floor(topLeft.Y + r.Y * _surface.CellH),
					(int)MathF.Ceiling(r.Width  * _surface.CellW),
					(int)MathF.Ceiling(r.Height * _surface.CellH));

				if (tex == null)
				{
					batcher.DrawRect(rect, _surface.BaseColor);
					continue;
				}

				// Phase E: sample the sub-rect of the slab texture that this
				// merged rect covers (proportional mapping over the WHOLE slab)
				// — the image stays anchored while the acid carves it.
				var src = new Rectangle(
					(int)MathF.Floor(r.X / (float)_surface.Cols * tex.Width),
					(int)MathF.Floor(r.Y / (float)_surface.Rows * tex.Height),
					(int)MathF.Ceiling(r.Width  / (float)_surface.Cols * tex.Width),
					(int)MathF.Ceiling(r.Height / (float)_surface.Rows * tex.Height));
				batcher.Draw(tex, rect, src, Color.White);
			}
		}
	}
}
