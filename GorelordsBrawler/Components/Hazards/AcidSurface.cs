using System;
using Microsoft.Xna.Framework;
using Nez;
using Nez.Tiled;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards
{
	/// <summary>
	/// Rising acid hazard with a 1D shallow-water height-field surface.
	///
	/// The base level (_sourceY) is derived entirely from accumulated liquid volume
	/// poured in by CascadeRenderer — the pool fills UP because the stream poured
	/// into it, not from an independent timer.
	///
	/// Wave simulation runs on top of the base level:
	///   v[x] += k * (h[x-1] + h[x+1] - 2*h[x])   (Laplacian spring)
	///   v[x] *= damping
	///   h[x] += v[x]
	///   surfaceY[x] = _sourceY + h[x]
	/// </summary>
	public class AcidSurface : Component, IUpdatable
	{
		/// <summary>World Y of the base acid level (smooth, used by camera / phase-manager).</summary>
		public float CurrentLevel { get; private set; }

		public bool IsRising { get; private set; }

		public readonly int Cols;
		public readonly int Rows;
		public const    int CellSize = 8;

		// ── Wave simulation ───────────────────────────────────────────────────
		private const float WaveK       = 0.35f;
		private const float WaveDamping = 0.994f;
		private const float WaveMax     = 24f;

		private const float RestoreK    = 0.0001f;

		private const float BubbleProbability = 0.10f;
		private const float BubbleStrength    = 3f;
		private readonly System.Random _rng = new System.Random();

		private readonly float[] _surfaceY;
		private readonly float[] _waveHeight;
		private readonly float[] _waveVelocity;

		private readonly int   _mapWidth;
		private readonly int   _mapHeight;

		// Pool base level derived from accumulated liquid (not time)
		private float _accumulatedVolume;  // pixel-area of liquid poured in
		private float _sourceY;           // world Y; decreases as volume grows

		private WaterRenderer _renderer;
		private static readonly Color _acidColor = new Color(60, 180, 40, 200);

		public AcidSurface(int mapWidth, int mapHeight, TmxMap map = null)
		{
			_mapWidth  = mapWidth;
			_mapHeight = mapHeight;

			Cols = mapWidth  / CellSize;
			Rows = mapHeight / CellSize;

			_surfaceY     = new float[Cols];
			_waveHeight   = new float[Cols];
			_waveVelocity = new float[Cols];

			_sourceY = mapHeight + CellSize;

			for (int c = 0; c < Cols; c++) _surfaceY[c] = mapHeight;
			CurrentLevel = mapHeight;
		}

		public override void OnAddedToEntity()
		{
			_renderer = Entity.AddComponent(new WaterRenderer(_mapWidth, _mapHeight, _acidColor));
			_renderer.SetFluidState(_surfaceY, Cols, CellSize);
		}

		public void Activate() => IsRising = true;

		/// <summary>
		/// Called every frame by CascadeRenderer to pour liquid into the pool.
		/// pixelArea is volume in px² (width × height); the base level rises by
		/// pixelArea / mapWidth pixels.  This is the ONLY driver of the pool level.
		/// </summary>
		public void AddVolume(float pixelArea)
		{
			if (!IsRising || pixelArea <= 0f) return;
			_accumulatedVolume += pixelArea;
		}

		// ── Per-frame update ──────────────────────────────────────────────────

		public void Update()
		{
			float dt = Math.Min(Time.DeltaTime, GameConstants.Physics.MaxDeltaTime);
			StepWaves();
			ComputeSurfaces();
		}

		// ── 1D wave equation ──────────────────────────────────────────────────

		private void StepWaves()
		{
			if ((float)_rng.NextDouble() < BubbleProbability)
			{
				int   col = _rng.Next(1, Cols - 1);
				float vel = ((float)_rng.NextDouble() * 2f - 1f) * BubbleStrength;
				_waveVelocity[col] += vel;
			}

			for (int x = 1; x < Cols - 1; x++)
				_waveVelocity[x] += WaveK * (_waveHeight[x - 1] + _waveHeight[x + 1] - 2f * _waveHeight[x]);

			for (int x = 0; x < Cols; x++)
				_waveVelocity[x] -= RestoreK * _waveHeight[x];

			for (int x = 0; x < Cols; x++)
			{
				_waveVelocity[x] *= WaveDamping;
				_waveHeight[x]   += _waveVelocity[x];
				_waveHeight[x]    = Math.Clamp(_waveHeight[x], -WaveMax, WaveMax);
			}

			_waveHeight[0]          = _waveHeight[1];
			_waveHeight[Cols - 1]   = _waveHeight[Cols - 2];
			_waveVelocity[0]        = _waveVelocity[1];
			_waveVelocity[Cols - 1] = _waveVelocity[Cols - 2];
		}

		// ── Surface tracking ──────────────────────────────────────────────────

		private void ComputeSurfaces()
		{
			// Pool level comes entirely from accumulated volume; no independent rise.
			_sourceY = _mapHeight - _accumulatedVolume / _mapWidth;
			_sourceY = Math.Max(0f, _sourceY);

			if (_sourceY >= _mapHeight)
			{
				for (int x = 0; x < Cols; x++) _surfaceY[x] = _mapHeight;
				CurrentLevel = _mapHeight;
				return;
			}

			for (int x = 0; x < Cols; x++)
				_surfaceY[x] = Math.Clamp(_sourceY + _waveHeight[x], 0, _mapHeight);

			CurrentLevel = _sourceY;
		}

		// ── Public API ────────────────────────────────────────────────────────

		/// <summary>
		/// Sustained per-frame injection of downward velocity at a world position.
		/// Call every frame with dt; models a continuous pour landing on the surface.
		/// </summary>
		public void PourAt(float worldX, float width, float ratePerSec, float dt)
		{
			int colCenter = Math.Clamp((int)(worldX / CellSize), 1, Cols - 2);
			int colRadius = Math.Max(1, (int)(width  / CellSize / 2));
			float inject  = ratePerSec * dt;

			for (int cx = colCenter - colRadius; cx <= colCenter + colRadius; cx++)
			{
				if (cx < 1 || cx >= Cols - 1) continue;
				float falloff = 1f - (float)Math.Abs(cx - colCenter) / (colRadius + 1f);
				float headroom = Math.Max(0f, 1f - Math.Abs(_waveHeight[cx]) / WaveMax);
				_waveVelocity[cx] += inject * falloff * headroom;
			}
		}

		/// <summary>
		/// Inject downward wave velocity at worldX to create a visible splash.
		/// </summary>
		public void Disturb(float worldX, float width, float speedPxPerSec)
		{
			int colCenter = Math.Clamp((int)(worldX / CellSize), 1, Cols - 2);
			int colRadius = Math.Max(1, (int)(width  / CellSize / 2));
			float str     = Math.Min(Math.Abs(speedPxPerSec) * 0.12f, 60f);

			for (int cx = colCenter - colRadius; cx <= colCenter + colRadius; cx++)
			{
				if (cx < 1 || cx >= Cols - 1) continue;
				float falloff = 1f - (float)Math.Abs(cx - colCenter) / (colRadius + 1f);
				_waveVelocity[cx] += str * falloff;
			}
		}

		/// <summary>World Y of the acid surface at the given world X.</summary>
		public float GetSurfaceLevelAtX(float worldX)
		{
			int col = Math.Clamp((int)(worldX / CellSize), 0, Cols - 1);
			return _surfaceY[col];
		}

		/// <summary>Minimum (highest on-screen) surface across a world-X range.</summary>
		public float GetSurfaceLevelInRange(float leftX, float rightX)
		{
			int colL = Math.Clamp((int)(leftX  / CellSize), 0, Cols - 1);
			int colR = Math.Clamp((int)(rightX / CellSize), 0, Cols - 1);
			float min = _mapHeight;
			for (int x = colL; x <= colR; x++)
				if (_surfaceY[x] < min) min = _surfaceY[x];
			return min;
		}

		/// <summary>Damage rectangle covering the acid body (used by ContactHazard).</summary>
		public RectangleF GetDamageBounds()
		{
			if (_sourceY >= _mapHeight) return default;
			return new RectangleF(
				GameConstants.Arena.InnerLeft,
				_sourceY - 16f,
				GameConstants.Arena.InnerRight - GameConstants.Arena.InnerLeft,
				_mapHeight - _sourceY + 16f);
		}

		// ── No-ops for API compatibility ──────────────────────────────────────
		public void RemoveSolidRect(float worldX0, float worldY0, float worldX1, float worldY1) { }
		public void SetTileSolid(int col, int row, bool solid) { }
		public void SetDynSolid(int col, int row, bool solid) { }
		public bool IsSolid(int col, int row) => false;
		public void FillToLevel(int leftCol, int rightCol, float targetSurfaceY) { }
	}
}
