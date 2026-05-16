using System;
using Microsoft.Xna.Framework;

namespace GorelordsBrawler.Components.Hazards.Fluid
{
	/// <summary>
	/// Position-Based Fluids (PBF) solver — Macklin & Müller, ACM TOG 2013.
	///
	/// Particle data is stored as parallel float arrays (SoA, not AoS):
	///   - Each PBF stage touches a different subset (density reads Pred only;
	///     ΔP reads Pred+Lambda, writes DP). SoA keeps unused fields out of L1.
	///   - Trivially SIMD-amenable later if profiling demands.
	///
	/// Alive particles are kept densely packed at indices [0, Count). Despawn
	/// swaps with the last alive — O(1) without a free-list.
	///
	/// Public surface is intentionally tight: Spawn, Step, ApplyImpulseInRadius,
	/// and read-only access to Count and the position arrays.
	/// </summary>
	public sealed class FluidSimulation
	{
		// ── Particle state (parallel arrays) ───────────────────────────────────
		public readonly float[] Px;
		public readonly float[] Py;
		public readonly float[] Vx;
		public readonly float[] Vy;
		public readonly float[] PredX;
		public readonly float[] PredY;

		private readonly float[] _lambda;
		private readonly float[] _dpx;
		private readonly float[] _dpy;

		public int Count { get; private set; }
		public int Capacity { get; }

		// ── World ─────────────────────────────────────────────────────────────
		private readonly float _worldLeft, _worldRight, _worldTop, _worldBottom;

		// ── Kernel coefficients (precomputed) ─────────────────────────────────
		private readonly float _h;
		private readonly float _h2;        // h²
		private readonly float _poly6Coef;
		private readonly float _spikyCoef; // for ∇W magnitude

		// Self-density contribution (W_poly6(0))
		private readonly float _wSelf;

		// sCorr precomputed denominator W_poly6(Δq)
		private readonly float _wDq;

		/// <summary>Rest density ρ₀ — calibrated at construction from hex packing.</summary>
		public float RestDensity { get; private set; }

		// ── Spatial hash ──────────────────────────────────────────────────────
		private readonly int _cols;
		private readonly int _rows;
		private readonly int _cellSize;
		private readonly int[] _cellStart;   // length cols*rows + 1
		private readonly int[] _cellIdx;     // length Capacity
		private readonly int[] _cellOf;      // length Capacity — particle's cell id

		// Scratch buffer for fast collision-side detection after solver
		private readonly int[] _lastHitSide; // 0=none, 1=top, 2=bottom, 3=left, 4=right

		// RNG for inlet jitter and small perturbations
		private readonly Random _rng = new Random(0xACED);

		// ── Tuning (mirrored from FluidConfig for hot-loop perf) ──────────────
		private readonly int   _solverIterations;
		private readonly float _gravity;
		private readonly float _epsilon;
		private readonly float _sCorrK;
		private readonly int   _sCorrN;
		private readonly float _xsphC;

		public FluidSimulation(
			int capacity,
			float worldLeft, float worldTop, float worldRight, float worldBottom)
		{
			Capacity      = capacity;
			_worldLeft    = worldLeft;
			_worldRight   = worldRight;
			_worldTop     = worldTop;
			_worldBottom  = worldBottom;

			Px      = new float[capacity];
			Py      = new float[capacity];
			Vx      = new float[capacity];
			Vy      = new float[capacity];
			PredX   = new float[capacity];
			PredY   = new float[capacity];
			_lambda = new float[capacity];
			_dpx    = new float[capacity];
			_dpy    = new float[capacity];

			_lastHitSide = new int[capacity];

			_h  = FluidConfig.SmoothingRadius;
			_h2 = _h * _h;

			// Normalized kernels so values land in an h-independent scale:
			//   W_poly6(r)        = (h² − r²)³ / h⁶              → W(0) = 1
			//   |∇W_spiky(r)|     =  3·(h − r)²   / h³           → finite at r→0
			// Macklin's absolute normalization constants are dropped — ε, k, etc.
			// are tuned against this scale, not the paper's m/kg/s.
			float h6 = _h2 * _h2 * _h2;
			float h3 = _h * _h * _h;
			_poly6Coef = 1f / h6;
			_spikyCoef = 3f / h3;

			_wSelf = Poly6(0f);

			float dq = FluidConfig.SCorrDqRatio * _h;
			_wDq = Poly6(dq);

			_solverIterations = FluidConfig.SolverIterations;
			_gravity          = FluidConfig.Gravity;
			_epsilon          = FluidConfig.Epsilon;
			_sCorrK           = FluidConfig.SCorrK;
			_sCorrN           = FluidConfig.SCorrN;
			_xsphC            = FluidConfig.XsphC;

			// Spatial hash: dense grid with cell size = h
			_cellSize = (int)MathF.Ceiling(_h);
			_cols     = Math.Max(1, (int)MathF.Ceiling((worldRight - worldLeft) / _cellSize)) + 2; // ±1 halo
			_rows     = Math.Max(1, (int)MathF.Ceiling((worldBottom - worldTop) / _cellSize)) + 2;
			_cellStart = new int[_cols * _rows + 1];
			_cellIdx   = new int[capacity];
			_cellOf    = new int[capacity];

			RestDensity = CalibrateRestDensity();
		}

		// ──────────────────────────────────────────────────────────────────────
		// Lifecycle
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>Add a particle. Returns its index, or -1 if at capacity.</summary>
		public int Spawn(float x, float y, float vx, float vy)
		{
			if (Count >= Capacity)
			{
				return -1;
			}
			int i = Count++;
			Px[i]    = x;
			Py[i]    = y;
			Vx[i]    = vx;
			Vy[i]    = vy;
			PredX[i] = x;
			PredY[i] = y;
			return i;
		}

		/// <summary>Remove particle by swapping with the last alive — O(1).</summary>
		public void Despawn(int i)
		{
			int last = Count - 1;
			if (i < 0 || i > last)
			{
				return;
			}
			if (i != last)
			{
				Px[i]    = Px[last];
				Py[i]    = Py[last];
				Vx[i]    = Vx[last];
				Vy[i]    = Vy[last];
				PredX[i] = PredX[last];
				PredY[i] = PredY[last];
			}
			Count--;
		}

		// ──────────────────────────────────────────────────────────────────────
		// Per-frame step
		// ──────────────────────────────────────────────────────────────────────

		public void Step(float dt, FluidCollider colliders)
		{
			if (Count == 0)
			{
				return;
			}

			Predict(dt);
			BuildSpatialHash();

			for (int iter = 0; iter < _solverIterations; iter++)
			{
				ComputeLambda();
				ComputeAndApplyDeltaP(colliders);
			}

			UpdateVelocitiesAndPositions(dt, colliders);
			XsphViscosity();
			DespawnOffscreen();
		}

		// ── Predict ────────────────────────────────────────────────────────────

		private void Predict(float dt)
		{
			float vyAdd = _gravity * dt;
			// Velocity clamp: cap per-frame motion at h (one cell) to keep the
			// spatial-hash neighbor lookup correct and prevent tunneling through
			// thin platforms. NOT 0.4·h — that crushed terminal-velocity fall.
			float maxStep = _h;
			float maxStep2 = maxStep * maxStep;

			for (int i = 0; i < Count; i++)
			{
				Vy[i] += vyAdd;
				float dx = Vx[i] * dt;
				float dy = Vy[i] * dt;
				float mag2 = dx * dx + dy * dy;
				if (mag2 > maxStep2)
				{
					float scale = maxStep / MathF.Sqrt(mag2);
					dx *= scale;
					dy *= scale;
				}
				PredX[i] = Px[i] + dx;
				PredY[i] = Py[i] + dy;
			}
		}

		// ── Spatial hash (counting sort) ──────────────────────────────────────

		private int CellOf(float x, float y)
		{
			int cx = (int)((x - _worldLeft) / _cellSize) + 1; // +1 for halo
			int cy = (int)((y - _worldTop ) / _cellSize) + 1;
			if (cx < 0)        cx = 0;
			if (cx >= _cols)   cx = _cols - 1;
			if (cy < 0)        cy = 0;
			if (cy >= _rows)   cy = _rows - 1;
			return cy * _cols + cx;
		}

		private void BuildSpatialHash()
		{
			Array.Clear(_cellStart, 0, _cellStart.Length);

			// Pass 1: count
			for (int i = 0; i < Count; i++)
			{
				int c = CellOf(PredX[i], PredY[i]);
				_cellOf[i] = c;
				_cellStart[c + 1]++;
			}

			// Pass 2: prefix sum
			for (int c = 1; c < _cellStart.Length; c++)
			{
				_cellStart[c] += _cellStart[c - 1];
			}

			// Pass 3: scatter (we use a running offset)
			// Reuse _cellStart by temporarily incrementing — restore after.
			for (int i = 0; i < Count; i++)
			{
				int c = _cellOf[i];
				int slot = _cellStart[c]++;
				_cellIdx[slot] = i;
			}

			// Restore _cellStart: shift right by one
			for (int c = _cellStart.Length - 1; c > 0; c--)
			{
				_cellStart[c] = _cellStart[c - 1];
			}
			_cellStart[0] = 0;
		}

		// ── PBF: density + λ ──────────────────────────────────────────────────

		private void ComputeLambda()
		{
			float rho0 = RestDensity;
			float invRho0 = 1f / rho0;

			for (int i = 0; i < Count; i++)
			{
				float pix = PredX[i];
				float piy = PredY[i];
				float density = 0f;
				float sumGradX = 0f;
				float sumGradY = 0f;
				float sumGradSq = 0f;

				int ci = _cellOf[i];
				int cy = ci / _cols;
				int cx = ci - cy * _cols;

				for (int dy = -1; dy <= 1; dy++)
				{
					int ny = cy + dy;
					if (ny < 0 || ny >= _rows) continue;
					for (int dx = -1; dx <= 1; dx++)
					{
						int nx = cx + dx;
						if (nx < 0 || nx >= _cols) continue;
						int cell = ny * _cols + nx;
						int start = _cellStart[cell];
						int end   = _cellStart[cell + 1];
						for (int k = start; k < end; k++)
						{
							int j = _cellIdx[k];
							float rx = pix - PredX[j];
							float ry = piy - PredY[j];
							float r2 = rx * rx + ry * ry;
							if (r2 >= _h2)
							{
								continue;
							}
							// Density
							density += Poly6FromR2(r2);

							if (j == i || r2 == 0f)
							{
								continue;
							}
							float r = MathF.Sqrt(r2);
							// Spiky gradient (vector) for the constraint
							float gMag = SpikyGradMag(r) * invRho0;
							float gx = gMag * (rx / r);
							float gy = gMag * (ry / r);
							sumGradX += gx;
							sumGradY += gy;
							sumGradSq += gx * gx + gy * gy;
						}
					}
				}

				sumGradSq += sumGradX * sumGradX + sumGradY * sumGradY;
				// Clamp C ≥ 0: incompressibility is one-sided — don't pull particles
				// together at free surfaces (under-dense regions). Tensile-instability
				// correction (sCorr) handles surface cohesion instead.
				float c = density / rho0 - 1f;
				if (c < 0f) c = 0f;
				_lambda[i] = -c / (sumGradSq + _epsilon);
			}
		}

		// ── PBF: ΔP + collision ───────────────────────────────────────────────

		private void ComputeAndApplyDeltaP(FluidCollider colliders)
		{
			float rho0 = RestDensity;
			float invRho0 = 1f / rho0;
			float wDq = _wDq;

			for (int i = 0; i < Count; i++)
			{
				float pix = PredX[i];
				float piy = PredY[i];
				float dpx = 0f;
				float dpy = 0f;
				float li = _lambda[i];

				int ci = _cellOf[i];
				int cy = ci / _cols;
				int cx = ci - cy * _cols;

				for (int dy = -1; dy <= 1; dy++)
				{
					int ny = cy + dy;
					if (ny < 0 || ny >= _rows) continue;
					for (int dx = -1; dx <= 1; dx++)
					{
						int nx = cx + dx;
						if (nx < 0 || nx >= _cols) continue;
						int cell = ny * _cols + nx;
						int start = _cellStart[cell];
						int end   = _cellStart[cell + 1];
						for (int k = start; k < end; k++)
						{
							int j = _cellIdx[k];
							if (j == i) continue;
							float rx = pix - PredX[j];
							float ry = piy - PredY[j];
							float r2 = rx * rx + ry * ry;
							if (r2 >= _h2 || r2 == 0f)
							{
								continue;
							}
							float r = MathF.Sqrt(r2);

							// Tensile-instability correction (sCorr)
							float wRatio = Poly6FromR2(r2) / wDq;
							float sCorr = -_sCorrK * MathF.Pow(wRatio, _sCorrN);

							float coeff = (li + _lambda[j] + sCorr) * invRho0;
							float gMag = SpikyGradMag(r);
							dpx += coeff * gMag * (rx / r);
							dpy += coeff * gMag * (ry / r);
						}
					}
				}

				_dpx[i] = dpx;
				_dpy[i] = dpy;
			}

			// Apply ΔP (capped at h/4 per iteration to stay in the linear regime
			// of the constraint and keep neighbors valid through later iters),
			// then collide.
			float maxDp = 0.25f * _h;
			float maxDp2 = maxDp * maxDp;
			for (int i = 0; i < Count; i++)
			{
				float dx = _dpx[i];
				float dy = _dpy[i];
				float m2 = dx * dx + dy * dy;
				if (m2 > maxDp2)
				{
					float s = maxDp / MathF.Sqrt(m2);
					dx *= s;
					dy *= s;
				}
				PredX[i] += dx;
				PredY[i] += dy;
				colliders.Project(ref PredX[i], ref PredY[i], FluidConfig.ParticleRadius);
			}
		}

		// ── Finalize velocities + post-solve collision tag ────────────────────

		private void UpdateVelocitiesAndPositions(float dt, FluidCollider colliders)
		{
			float invDt = 1f / dt;
			float drag  = MathF.Max(0f, 1f - FluidConfig.LinearDrag * dt);
			for (int i = 0; i < Count; i++)
			{
				float vx = (PredX[i] - Px[i]) * invDt;
				float vy = (PredY[i] - Py[i]) * invDt;

				Px[i] = PredX[i];
				Py[i] = PredY[i];

				// Damp velocity components on whichever face the particle settled against
				int side = colliders.ContactSide(Px[i], Py[i], FluidConfig.ParticleRadius);
				if (side == 1 || side == 2) // top/bottom face — kill vertical, friction on tangent
				{
					vy *= -FluidConfig.PlatformRestitution;
					vx *= 1f - FluidConfig.WallTangentFriction * dt;
				}
				else if (side == 3 || side == 4) // left/right face
				{
					vx *= -FluidConfig.WallRestitution;
					vy *= 1f - FluidConfig.WallTangentFriction * dt;
				}

				// Global linear drag — converges resting fluid; cheap insurance
				// against energy creep from solver overshoot.
				vx *= drag;
				vy *= drag;

				Vx[i] = vx;
				Vy[i] = vy;
			}
		}

		// ── XSPH viscosity ────────────────────────────────────────────────────

		private void XsphViscosity()
		{
			if (_xsphC <= 0f)
			{
				return;
			}

			// Accumulate Δv into _dpx/_dpy (reused as scratch)
			for (int i = 0; i < Count; i++)
			{
				_dpx[i] = 0f;
				_dpy[i] = 0f;
			}

			for (int i = 0; i < Count; i++)
			{
				float pix = Px[i];
				float piy = Py[i];
				float vix = Vx[i];
				float viy = Vy[i];

				int ci = CellOf(pix, piy);
				int cy = ci / _cols;
				int cx = ci - cy * _cols;

				for (int dy = -1; dy <= 1; dy++)
				{
					int ny = cy + dy;
					if (ny < 0 || ny >= _rows) continue;
					for (int dx = -1; dx <= 1; dx++)
					{
						int nx = cx + dx;
						if (nx < 0 || nx >= _cols) continue;
						int cell = ny * _cols + nx;
						int start = _cellStart[cell];
						int end   = _cellStart[cell + 1];
						for (int k = start; k < end; k++)
						{
							int j = _cellIdx[k];
							if (j == i) continue;
							float rx = pix - Px[j];
							float ry = piy - Py[j];
							float r2 = rx * rx + ry * ry;
							if (r2 >= _h2)
							{
								continue;
							}
							float w = Poly6FromR2(r2);
							_dpx[i] += (Vx[j] - vix) * w;
							_dpy[i] += (Vy[j] - viy) * w;
						}
					}
				}
			}

			for (int i = 0; i < Count; i++)
			{
				Vx[i] += _xsphC * _dpx[i];
				Vy[i] += _xsphC * _dpy[i];
			}
		}

		// ── Despawn ───────────────────────────────────────────────────────────

		private void DespawnOffscreen()
		{
			float yCutoff = _worldBottom + FluidConfig.DespawnBelowMargin;
			for (int i = Count - 1; i >= 0; i--)
			{
				if (Py[i] > yCutoff)
				{
					Despawn(i);
				}
			}
		}

		// ──────────────────────────────────────────────────────────────────────
		// External effects (called by AcidSurface façade)
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>Add downward (or arbitrary) velocity impulse to particles within radius of worldPos.</summary>
		public void ApplyImpulseInRadius(Vector2 worldPos, float radius, Vector2 impulse)
		{
			float r2 = radius * radius;
			for (int i = 0; i < Count; i++)
			{
				float dx = Px[i] - worldPos.X;
				float dy = Py[i] - worldPos.Y;
				float d2 = dx * dx + dy * dy;
				if (d2 > r2)
				{
					continue;
				}
				float falloff = 1f - MathF.Sqrt(d2) / radius;
				Vx[i] += impulse.X * falloff;
				Vy[i] += impulse.Y * falloff;
			}
		}

		// ──────────────────────────────────────────────────────────────────────
		// Kernels
		// ──────────────────────────────────────────────────────────────────────

		private float Poly6(float r)
		{
			if (r >= _h) return 0f;
			float t = _h2 - r * r;
			return _poly6Coef * t * t * t;
		}

		private float Poly6FromR2(float r2)
		{
			if (r2 >= _h2) return 0f;
			float t = _h2 - r2;
			return _poly6Coef * t * t * t;
		}

		/// <summary>Magnitude of the Spiky gradient (sign + direction handled at call site).</summary>
		private float SpikyGradMag(float r)
		{
			if (r >= _h || r <= 0f) return 0f;
			float t = _h - r;
			// ∇W_spiky = -(45/πh⁶)·(h-r)²·r̂ — return signed scalar so multiplying by (dx/r) yields the gradient vector.
			return -_spikyCoef * t * t;
		}

		// ──────────────────────────────────────────────────────────────────────
		// Calibration: hex-pack at expected spacing, measure self-density
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Lay particles in a hex pattern at spacing 2·ParticleRadius and sum
		/// densities at the center. This becomes ρ₀ so that resting fluid has
		/// constraint C ≈ 0.
		/// </summary>
		private float CalibrateRestDensity()
		{
			float spacing = 2f * FluidConfig.ParticleRadius;
			float density = _wSelf; // self

			// First & second ring of hex packing (offsets in units of spacing)
			(float ox, float oy)[] ring1 = new (float, float)[]
			{
				( 1f, 0f), (-1f, 0f),
				( 0.5f,  0.8660254f), (-0.5f,  0.8660254f),
				( 0.5f, -0.8660254f), (-0.5f, -0.8660254f),
			};
			(float ox, float oy)[] ring2 = new (float, float)[]
			{
				( 2f, 0f), (-2f, 0f),
				( 1f,  1.7320508f), (-1f,  1.7320508f),
				( 1f, -1.7320508f), (-1f, -1.7320508f),
				( 1.5f,  0.8660254f), (-1.5f,  0.8660254f),
				( 1.5f, -0.8660254f), (-1.5f, -0.8660254f),
				( 0f,  1.7320508f), ( 0f, -1.7320508f),
			};

			foreach (var (ox, oy) in ring1)
			{
				float r = spacing * MathF.Sqrt(ox * ox + oy * oy);
				density += Poly6(r);
			}
			foreach (var (ox, oy) in ring2)
			{
				float r = spacing * MathF.Sqrt(ox * ox + oy * oy);
				density += Poly6(r);
			}

			// Guard against zero density (would NaN λ)
			return MathF.Max(density, 1f);
		}

		// ──────────────────────────────────────────────────────────────────────
		// Test helpers
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>Total accumulated particle area (px²) — used for volumetric level estimate.</summary>
		public float ParticleVolume => Count * MathHelper.Pi * FluidConfig.ParticleRadius * FluidConfig.ParticleRadius;
	}
}
