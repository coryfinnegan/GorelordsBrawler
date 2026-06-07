using System;
using Microsoft.Xna.Framework;
using Nez;
using Nez.Tiled;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Components.Hazards.Fluid;

namespace GorelordsBrawler.Components.Hazards
{
	/// <summary>
	/// Rising acid hazard backed by a real Position-Based Fluid (PBF) particle
	/// simulation. AcidSurface is a thin façade that:
	///
	///   - Owns the particle sim, the collision-snapshot helper, and the
	///     occupancy grid used for damage / surface queries.
	///   - Spawns particles at an inlet above the top platform every frame
	///     after Activate() — the pool fills BECAUSE the inlet is pouring.
	///   - Adds the FluidRenderer sibling component for drawing.
	///
	/// Public API is preserved verbatim from the legacy 1D-wave implementation
	/// so DynamicPlatform, BrawlerCamera, AcidPhaseManager, and ContactHazard
	/// require no changes.
	/// </summary>
	public class AcidSurface : Component, IUpdatable
	{
		// ── Public API (unchanged) ────────────────────────────────────────────
		public bool  IsRising     { get; private set; }
		public float CurrentLevel { get; private set; }

		/// <summary>Live particle count — exposed for E2E assertions (e.g. "acid didn't vanish").</summary>
		public int ParticleCount => _sim?.Count ?? 0;

		/// <summary>
		/// True if every live particle position is finite. The hitstop→NaN failure mode
		/// (TimeScale=0 → dt=0 → 1/dt) collapses particles to NaN, which is otherwise invisible
		/// in a screenshot until the pool drains. E2E tests assert this directly.
		/// </summary>
		public bool AllParticlesFinite()
		{
			if (_sim == null)
			{
				return true;
			}
			for (int i = 0; i < _sim.Count; i++)
			{
				if (!float.IsFinite(_sim.Px[i]) || !float.IsFinite(_sim.Py[i]))
				{
					return false;
				}
			}
			return true;
		}

		// ── Internal state ────────────────────────────────────────────────────
		private readonly int _mapWidth;
		private readonly int _mapHeight;

		private FluidSimulation     _sim;
		private FluidCollider       _colliders;
		private FluidOccupancyGrid  _grid;
		private FluidRenderer       _renderer;

		// Inlet geometry (derived in ctor from GameConstants.Hazards.Platforms)
		private readonly float _inletX;
		private readonly float _topPlatformY;
		private readonly float _flowRatePerSec;       // pixel-area / sec
		private float          _inletAccum;          // fractional carry for spawn rate
		private float          _smoothedLevelY;       // exponentially smoothed CurrentLevel

		private readonly System.Random _rng = new System.Random(0xACED);

		// Pre-fill request (deferred until OnAddedToEntity, when _sim exists).
		private bool  _preFillRequested;
		private float _pfLeft, _pfRight, _pfTop, _pfBottom;

		// Inlet fill cap: max live particles the inlet will pour up to, derived
		// from basin geometry in OnAddedToEntity. Bounds the working set so the
		// basin fills to ~the lip and stops, instead of running to MaxParticles.
		private int _inletParticleCap;

		private static readonly Color _acidColor = new Color((byte)60, (byte)180, (byte)40, (byte)200);

		public AcidSurface(int mapWidth, int mapHeight, TmxMap map = null)
		{
			_mapWidth  = mapWidth;
			_mapHeight = mapHeight;

			// Find top platform (min cy) — inlet goes above its center.
			float minCy = float.MaxValue;
			(float cx, float cy, float w) top = (0.5f, 0.5f, 0.25f);
			foreach (var p in GameConstants.Hazards.Platforms)
			{
				if (p.cy < minCy)
				{
					minCy = p.cy;
					top = p;
				}
			}

			_inletX        = top.cx * mapWidth;
			_topPlatformY  = top.cy * mapHeight - GameConstants.Hazards.PlatformHeight * 0.5f;

			// Flow rate (px²/sec) to match the legacy renderer's nominal rise speed.
			// With AcidRiseSpeed=0.0025 and the 1280×800 map (× DebugFastAcid×4):
			//   risePxSec = 0.0025 * 800 * speedMult = 2 (or 8) px/s
			//   flowRate  = risePxSec * mapWidth     = 2560 (or 10240) px²/s
			// Translated to particles/sec by dividing by π·r² area per particle.
			float speedMult  = AppSettings.DebugFastAcid
				? GameConstants.Hazards.AcidDebugRiseMultiplier : 1f;
			float risePxSec  = GameConstants.Hazards.AcidRiseSpeed * mapHeight * speedMult;
			_flowRatePerSec  = risePxSec * mapWidth;

			_smoothedLevelY = mapHeight;
			CurrentLevel    = mapHeight;
		}

		public override void OnAddedToEntity()
		{
			// Allow particles to spawn above the screen (negative Y) and fall a
			// bit below the floor before despawn — gives the despawn margin room.
			_sim = new FluidSimulation(
				FluidConfig.MaxParticles,
				0,                                              // left
				FluidConfig.InletYOffset - 4f,                  // top (above-screen spawn)
				_mapWidth,                                      // right
				_mapHeight + FluidConfig.DespawnBelowMargin);   // bottom

			_colliders = new FluidCollider(
				GameConstants.Arena.InnerLeft,
				GameConstants.Arena.InnerRight,
				0f,
				_mapHeight);

			_grid = new FluidOccupancyGrid(_mapWidth, _mapHeight, FluidConfig.GridCellSize);

			// Geometry-derived inlet cap: how many particles fill the basin from
			// its floor up to the fill ceiling. PBF settles to ~hex packing, so
			// each particle's footprint is (2r)²·(√3/2). This REPLACES the old
			// volumetric inlet-stop (CurrentLevel < topPlatformY + margin), which
			// assumed the acid spreads across the full map width — wrong by ~3×
			// for the narrow basin, so it never tripped and the pool ran to the
			// 5000 hard cap, flooding the arena and tanking FPS.
			float fillArea    = (GameConstants.Hazards.BasinRightX - GameConstants.Hazards.BasinLeftX)
			                  * (GameConstants.Hazards.BasinFloorY  - GameConstants.Hazards.BasinFillCeilingY);
			float perParticle = 4f * FluidConfig.ParticleRadius * FluidConfig.ParticleRadius * 0.8660254f;
			_inletParticleCap = (int)(fillArea / perParticle);

			_renderer = Entity.AddComponent(new FluidRenderer(_sim, _mapWidth, _mapHeight, _acidColor));

			// Deferred pre-fill: the resting basin pool must be spawned AFTER _sim
			// exists. PreFill() (called from ArenaScene during scene construction)
			// only records the region; this is where it actually runs.
			if (_preFillRequested)
			{
				ExecutePreFill();
			}
		}

		public void Activate() => IsRising = true;

		/// <summary>
		/// Request a resting block of acid filling the world-space rectangle
		/// [left,right] × [top,bottom], spawned at scene start so the basin holds
		/// a pool from t=0 (the "Calm" phase) — long before the rise begins.
		/// Deferred to <see cref="OnAddedToEntity"/> because components are
		/// constructed before that hook fires, so <c>_sim</c> doesn't exist yet
		/// when ArenaScene calls this.
		/// </summary>
		public void PreFill(float left, float right, float top, float bottom)
		{
			_preFillRequested = true;
			_pfLeft   = left;
			_pfRight  = right;
			_pfTop    = top;
			_pfBottom = bottom;
		}

		private void ExecutePreFill()
		{
			// Lay particles on a grid at rest spacing (2·radius). PBF relaxes them
			// toward hex packing over the first few Steps; the tiny initial settle
			// reads as the pool "finding its level."
			float spacing = 2f * FluidConfig.ParticleRadius;
			for (float y = _pfBottom - spacing; y > _pfTop; y -= spacing)
			{
				for (float x = _pfLeft + spacing; x < _pfRight - spacing; x += spacing)
				{
					if (_sim.Spawn(x, y, 0f, 0f) < 0)
					{
						return;   // at capacity — stop early
					}
				}
			}

			// Make surface/damage-bounds queries valid on frame 0, before the
			// first Update() Step rebuilds the grid.
			_grid.RebuildFrom(_sim);
			UpdateCurrentLevel();
		}

		// ──────────────────────────────────────────────────────────────────────
		// Per-frame
		// ──────────────────────────────────────────────────────────────────────

		public void Update()
		{
			if (_sim == null)
			{
				return;
			}

			float dt = Math.Min(Time.DeltaTime, GameConstants.Physics.MaxDeltaTime);

			// Pour from the inlet only while rising. Pre-filled acid (the Calm
			// phase) has particles but isn't "rising" yet — it must still simulate
			// so it settles, stays contained by the basin, and damages anyone
			// knocked into it. Only the inlet pour is gated on IsRising.
			if (IsRising)
			{
				SpawnInlet(dt);
			}

			// Dry and not pouring — nothing to simulate, skip the per-frame work
			// (broadphase rebuild + step) entirely.
			if (_sim.Count == 0)
			{
				return;
			}

			// Refresh dynamic collider list — picks up newly spawned platforms.
			// Query area = wet bounds expanded by 2·h, or the full map if dry.
			var queryArea = _grid.HasWetCells
				? Expand(_grid.GetWetBounds(), 2f * FluidConfig.SmoothingRadius)
				: new RectangleF(0, _topPlatformY - 32f, _mapWidth, _mapHeight - _topPlatformY + 96f);
			_colliders.RebuildFromPhysics(queryArea);

			_sim.Step(dt, _colliders);
			_grid.RebuildFrom(_sim);

			UpdateCurrentLevel();
		}

		private void SpawnInlet(float dt)
		{
			// Stop pouring once the basin is filled to its target level. This is a
			// COUNT cap (not the old CurrentLevel test): CurrentLevel is a
			// volumetric estimate assuming full-map-width spreading, which is wrong
			// by ~3× for the narrow basin and never tripped — so the inlet used to
			// pour to MaxParticles (5000), overflow the arena, and tank FPS. The
			// count cap is derived from basin geometry in OnAddedToEntity.
			if (_sim.Count >= _inletParticleCap)
			{
				return;
			}

			float particleArea = MathHelper.Pi * FluidConfig.ParticleRadius * FluidConfig.ParticleRadius;
			float spawnRate    = _flowRatePerSec / particleArea;
			_inletAccum       += spawnRate * dt;

			int toSpawn = (int)_inletAccum;
			_inletAccum -= toSpawn;

			for (int i = 0; i < toSpawn; i++)
			{
				float jx = ((float)_rng.NextDouble() * 2f - 1f) * FluidConfig.InletJitterX;
				float jy = ((float)_rng.NextDouble() * 2f - 1f) * FluidConfig.InletJitterY;
				float jvx = ((float)_rng.NextDouble() * 2f - 1f) * FluidConfig.InletJitterVx;
				_sim.Spawn(
					_inletX + jx,
					FluidConfig.InletYOffset + jy,
					jvx,
					FluidConfig.InletDownVelocity);
			}
		}

		private void UpdateCurrentLevel()
		{
			// Volumetric estimate: the more particles, the higher the surface.
			// Monotonic by construction (Count only grows on net), which is what
			// AcidPhaseManager.spawnTriggerY relies on.
			float volume  = _sim.ParticleVolume;
			float targetY = _mapHeight - volume / _mapWidth;
			if (targetY < 0f) targetY = 0f;

			// Exponential smoothing (α=0.1) keeps camera and trigger checks stable.
			_smoothedLevelY = _smoothedLevelY * 0.9f + targetY * 0.1f;
			CurrentLevel    = _smoothedLevelY;
		}

		// ──────────────────────────────────────────────────────────────────────
		// External API (preserved)
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Legacy API. The old 1D-wave implementation derived its pool level from
		/// AddVolume() calls by CascadeRenderer. With particle physics the inlet
		/// itself adds volume, so this is now a documented no-op kept for ABI.
		/// </summary>
		public void AddVolume(float pixelArea)
		{
			// no-op
		}

		/// <summary>Apply an impulsive downward velocity to particles within radius — splash.</summary>
		public void Disturb(float worldX, float width, float speedPxPerSec)
		{
			if (_sim == null) return;
			float surfaceY = GetSurfaceLevelAtX(worldX);
			var pos    = new Vector2(worldX, surfaceY);
			float radius = Math.Max(width * 0.5f, FluidConfig.SmoothingRadius);
			var impulse = new Vector2(0f, Math.Abs(speedPxPerSec) * 0.5f);
			_sim.ApplyImpulseInRadius(pos, radius, impulse);
		}

		/// <summary>Legacy continuous-pour API — same model as Disturb, scaled by dt.</summary>
		public void PourAt(float worldX, float width, float ratePerSec, float dt)
		{
			if (_sim == null) return;
			float surfaceY = GetSurfaceLevelAtX(worldX);
			var pos    = new Vector2(worldX, surfaceY);
			float radius = Math.Max(width * 0.5f, FluidConfig.SmoothingRadius);
			var impulse = new Vector2(0f, ratePerSec * dt * 60f);
			_sim.ApplyImpulseInRadius(pos, radius, impulse);
		}

		public float GetSurfaceLevelAtX(float worldX)
		{
			return _grid?.GetSurfaceYAt(worldX) ?? _mapHeight;
		}

		/// <summary>
		/// Topmost wet cell at <paramref name="worldX"/> at or below
		/// <paramref name="ceilingWorldY"/>. Use this for "the acid surface
		/// near this point" queries (e.g. where a wading player meets the
		/// air-acid boundary) — splashes on higher platforms sharing the same
		/// column are ignored. Returns mapHeight if no qualifying wet cell.
		/// </summary>
		public float GetLocalSurfaceLevelAtX(float worldX, float ceilingWorldY)
		{
			return _grid?.GetSurfaceYAtBelow(worldX, ceilingWorldY) ?? _mapHeight;
		}

		public float GetSurfaceLevelInRange(float leftX, float rightX)
		{
			return _grid?.GetMinSurfaceYInRange(leftX, rightX) ?? _mapHeight;
		}

		public RectangleF GetDamageBounds()
		{
			if (_grid == null || !_grid.HasWetCells)
			{
				return default;
			}
			var r = _grid.GetWetBounds();
			// Clamp horizontally to the inner-arena bounds so off-arena overspray
			// (a falling stream that hasn't fully landed) doesn't damage players
			// outside the playable area.
			float left  = Math.Max(r.X, GameConstants.Arena.InnerLeft);
			float right = Math.Min(r.X + r.Width, GameConstants.Arena.InnerRight);
			if (right <= left) return default;
			float y = r.Y - FluidConfig.DamageBoundsPadY;
			return new RectangleF(left, y, right - left, _mapHeight - y);
		}

		// ── Helpers ───────────────────────────────────────────────────────────

		private static RectangleF Expand(RectangleF r, float pad)
		{
			return new RectangleF(r.X - pad, r.Y - pad, r.Width + 2f * pad, r.Height + 2f * pad);
		}
	}
}
