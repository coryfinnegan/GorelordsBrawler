using System;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards.Fluid
{
	/// <summary>
	/// Static collision world for the fluid: arena walls plus a per-frame snapshot
	/// of platform AABBs harvested from Nez Physics on the Platforms layer.
	///
	/// Production usage:
	///   RebuildFromPhysics() each frame before stepping the simulation.
	/// Test usage:
	///   SetAabbs(...) directly; arena walls are derived from the constructor.
	///
	/// `Project` resolves a single particle out of any overlapping AABB by
	/// pushing along the minimum-penetration axis. `ContactSide` re-queries
	/// after the solver to identify which face the particle settled on, so the
	/// post-solve velocity-update step can apply friction/restitution.
	/// </summary>
	public sealed class FluidCollider
	{
		// ── Static arena walls ────────────────────────────────────────────────
		public readonly float WallLeft;
		public readonly float WallRight;
		public readonly float Floor;
		public readonly float Ceiling;

		// ── Dynamic AABB list (refreshed per frame) ───────────────────────────
		private RectangleF[] _aabbs = new RectangleF[32];
		public int Count { get; private set; }

		// Scratch buffer for Nez broadphase
		// Nez's BoxcastBroadphase returns IEnumerable<Collider>, so we just iterate.

		public FluidCollider(float wallLeft, float wallRight, float ceiling, float floor)
		{
			WallLeft  = wallLeft;
			WallRight = wallRight;
			Ceiling   = ceiling;
			Floor     = floor;
		}

		/// <summary>Direct injection for tests / non-Nez contexts.</summary>
		public void SetAabbs(RectangleF[] aabbs, int count)
		{
			if (_aabbs.Length < count)
			{
				_aabbs = new RectangleF[count];
			}
			for (int i = 0; i < count; i++)
			{
				_aabbs[i] = aabbs[i];
			}
			Count = count;
		}

		/// <summary>
		/// Rebuild the dynamic AABB list from Nez Physics for the given query
		/// region (typically the wet bounding box expanded by 2h).
		/// </summary>
		public void RebuildFromPhysics(in RectangleF queryArea)
		{
			Count = 0;
			var hits = Physics.BoxcastBroadphase(queryArea, PhysicsLayers.Platforms);
			foreach (var c in hits)
			{
				if (Count >= _aabbs.Length)
				{
					Array.Resize(ref _aabbs, _aabbs.Length * 2);
				}
				_aabbs[Count++] = c.Bounds;
			}
		}

		/// <summary>
		/// Push a particle of given radius out of any overlapping wall or AABB
		/// along the minimum-penetration axis. Called inside each solver iteration.
		/// </summary>
		public void Project(ref float x, ref float y, float radius)
		{
			// Walls — clamp first
			if (x < WallLeft  + radius) x = WallLeft  + radius;
			if (x > WallRight - radius) x = WallRight - radius;
			if (y > Floor     - radius) y = Floor     - radius;
			if (y < Ceiling   + radius) y = Ceiling   + radius;

			for (int i = 0; i < Count; i++)
			{
				ref var a = ref _aabbs[i];
				float left   = a.X;
				float right  = a.X + a.Width;
				float top    = a.Y;
				float bottom = a.Y + a.Height;

				if (x + radius <= left || x - radius >= right ||
				    y + radius <= top  || y - radius >= bottom)
				{
					continue;
				}

				float penLeft   = (x + radius) - left;
				float penRight  = right  - (x - radius);
				float penTop    = (y + radius) - top;
				float penBottom = bottom - (y - radius);

				// Find minimum
				float minPen = penLeft;
				int side = 3;            // 3 = left face
				if (penRight < minPen)   { minPen = penRight;  side = 4; }
				if (penTop < minPen)     { minPen = penTop;    side = 1; }
				if (penBottom < minPen)  { minPen = penBottom; side = 2; }

				switch (side)
				{
					case 1: y -= penTop;     break;   // push up out of top face
					case 2: y += penBottom;  break;   // push down out of bottom
					case 3: x -= penLeft;    break;   // push left
					case 4: x += penRight;   break;   // push right
				}
			}
		}

		/// <summary>
		/// After solver convergence, identify which face (if any) the particle is
		/// touching. Used for velocity damping. Returns 0 = none, 1 = top, 2 = bottom,
		/// 3 = left, 4 = right.
		/// </summary>
		public int ContactSide(float x, float y, float radius)
		{
			// Wall contacts
			if (y >= Floor     - radius - 0.1f) return 1; // resting on floor — top face contact
			if (y <= Ceiling   + radius + 0.1f) return 2;
			if (x <= WallLeft  + radius + 0.1f) return 3;
			if (x >= WallRight - radius - 0.1f) return 4;

			for (int i = 0; i < Count; i++)
			{
				ref var a = ref _aabbs[i];
				float left   = a.X;
				float right  = a.X + a.Width;
				float top    = a.Y;
				float bottom = a.Y + a.Height;

				// Within horizontal span and right at top?
				if (x > left - radius && x < right + radius)
				{
					if (Math.Abs((y + radius) - top   ) < 0.5f) return 1;
					if (Math.Abs((y - radius) - bottom) < 0.5f) return 2;
				}
				if (y > top - radius && y < bottom + radius)
				{
					if (Math.Abs((x + radius) - left ) < 0.5f) return 3;
					if (Math.Abs((x - radius) - right) < 0.5f) return 4;
				}
			}
			return 0;
		}
	}
}
