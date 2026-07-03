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

		// Pour plumbing. Inlet positions/velocities live in AcidConfig.Inlets
		// (dual ceiling-corner streams); the TOTAL flow (particles/sec) is split
		// evenly across them so adding inlets never adds volume. The rate is
		// re-targeted per loop by AcidPhaseManager (AcidConfig.InletFlowFor) so
		// every rise lands in ~30 s despite the caps growing loop over loop.
		private float _particlesPerSec = AcidConfig.InletFlowFor(0);
		private float _inletAccum;            // fractional carry for spawn rate
		private float _smoothedLevelY;        // exponentially smoothed CurrentLevel

		// Phase-C state driven by AcidPhaseManager.
		private float _fillCeilingY;
		private int   _particleCap;       // formula estimate for the ceiling (oracle + budget maths)
		private int   _safetyCap;         // hard pour stop — the closed loop's overrun guard
		private float _surgeBurstTimer;   // pour burst window after TriggerSurge
		private float _drainAccum;        // fractional carry for drain rate
		private float _drainRatePerSec;   // derived at BeginDrain for a fixed-duration recession

		/// <summary>While true, the drain sluice removes particles each frame (pour should be off).</summary>
		public bool Draining;

		// Time since the last drain ended — lets surface-lag-sensitive checks
		// (the hovering-log oracle) grace the catch-up window after a drain,
		// when logs legitimately hang a few px above a tide that moved faster
		// than buoyant descent.
		private float _sinceDrainEnd = 999f;

		/// <summary>
		/// True during a drain and for the catch-up window after it (measured:
		/// under debug-fast's 4× tides, stranded hulls take up to ~3.5 s for
		/// the next rise to reach; real-speed lag never exceeds ~16 px).
		/// </summary>
		public bool DrainSettling => Draining || _sinceDrainEnd < 4f;

		/// <summary>Total surges fired this match — automation oracle.</summary>
		public int SurgeCount { get; private set; }

		// ── Telegraph channel (Phase D) ───────────────────────────────────────
		// One tell state that three cues read each frame: the bubble emitter
		// (boil harder), the liquid post-processor (meniscus pulse quickens and
		// brightens), and the camera (building rumble). The phase machine arms
		// it ahead of every surge, storm crest, and rise.
		private float _tellTimer;
		private float _tellDuration;

		/// <summary>Arm the telegraph: cues ramp over the next <paramref name="seconds"/>.</summary>
		public void BeginTell(float seconds)
		{
			_tellDuration = Math.Max(seconds, 0.01f);
			_tellTimer    = _tellDuration;
		}

		/// <summary>True while a telegraph is running — automation oracle.</summary>
		public bool TellActive => _tellTimer > 0f;

		/// <summary>0 → 1 as the telegraphed beat approaches (0 when idle).</summary>
		public float TellProgress =>
			TellActive ? 1f - (_tellTimer / _tellDuration) : 0f;

		/// <summary>Formula estimate of the pour target (geometry × measured density) — oracle + budget maths.</summary>
		public int ParticleCap => _particleCap;

		/// <summary>
		/// True once the pool stands at its target: the fill is CLOSED-LOOP on
		/// the MEASURED standing surface, because the settled density varies
		/// ~±10% with pool depth and any fixed count target chronically under-
		/// or over-shoots deep ceilings (the storm missed its mid-tier mark by
		/// 30–50 px at every constant we tried). The count-based safety cap
		/// still backstops a broken surface probe.
		/// </summary>
		public bool AtFillCap => _sim != null
			&& (_sim.Count >= _safetyCap
				|| (_sim.Count > 0 && GetStandingSurfaceY() <= _fillCeilingY));

		private readonly System.Random _rng = new System.Random(0xACED);

		// Pre-fill request (deferred until OnAddedToEntity, when _sim exists).
		private bool  _preFillRequested;
		private float _pfLeft, _pfRight, _pfTop, _pfBottom;

		private static readonly Color _acidColor = new Color((byte)60, (byte)180, (byte)40, (byte)200);

		public AcidSurface(int mapWidth, int mapHeight, TmxMap map = null)
		{
			_mapWidth  = mapWidth;
			_mapHeight = mapHeight;

			_smoothedLevelY = mapHeight;
			CurrentLevel    = mapHeight;
		}

		/// <summary>
		/// Re-target the pour rate (particles/sec, split across the inlets).
		/// Driven per loop by AcidPhaseManager from AcidConfig.InletFlowFor —
		/// the flow escalates with the caps so every rise lands in ~30 s.
		/// </summary>
		public void SetInletFlow(float particlesPerSec)
		{
			_particlesPerSec = particlesPerSec;
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

			// Geometry-derived pour cap (the Phase-A fix, now ceiling-parametric):
			// AcidPhaseManager re-targets the ceiling per phase/loop; until it
			// does, default to the Phase-A basin rest target.
			SetFillCeiling(GameConstants.Hazards.BasinFillCeilingY);

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

		/// <summary>Stop the inlets (used by the Drain phase; Draining handles removal).</summary>
		public void StopPour() => IsRising = false;

		/// <summary>
		/// Start a fixed-DURATION drain toward <paramref name="ceilingY"/>: the
		/// sluice rate is derived from the live surplus so the recession always
		/// takes ~<paramref name="durationSeconds"/> (already time-scaled by the
		/// caller) regardless of how much the loop poured — a fixed rate made
		/// loop 0's "relief beat" a 2.7 s blink while loop 2's would have taken
		/// nearly a minute.
		/// </summary>
		public void BeginDrain(float ceilingY, float durationSeconds)
		{
			int target       = AcidConfig.ParticleCapForCeiling(ceilingY);
			int surplus      = Math.Max(0, ParticleCount - target);
			_drainRatePerSec = surplus / Math.Max(0.1f, durationSeconds);
			Draining         = true;
		}

		/// <summary>
		/// Re-target how high the pour fills the vessel. The pour then runs
		/// closed-loop on the MEASURED standing surface (see AtFillCap /
		/// SpawnInlet); the geometry-derived count (AcidConfig.
		/// ParticleCapForCeiling) becomes the estimate the oracle reports and,
		/// ×1.25 (clamped to 90% of MaxParticles), the hard safety stop.
		/// Driven per phase/loop by AcidPhaseManager.
		/// </summary>
		public void SetFillCeiling(float ceilingY)
		{
			_fillCeilingY = ceilingY;
			_particleCap  = AcidConfig.ParticleCapForCeiling(ceilingY);
			_safetyCap    = Math.Min(
				(int)(_particleCap * 1.25f),
				(int)(FluidConfig.MaxParticles * 0.9f));
		}

		/// <summary>
		/// Throw a surge: an upward impulse swept across the LIVE WET SPAN at
		/// the surface (the wave) plus a brief pour burst at the valves (the
		/// volume spike). Strength comes from the per-loop escalation curve.
		///
		/// The sweep follows the pool, not the basin: once the level is above
		/// the lip the pool spans the whole arena, and a basin-only sweep
		/// (the original) erupted crests over the tier-free CENTER — the storm
		/// could never actually break the mid/top refuges sitting over the
		/// banks. Main-thread only — ApplyImpulseInRadius is the serial sim API.
		/// </summary>
		public void TriggerSurge(float strength)
		{
			if (_sim == null)
			{
				return;
			}

			SurgeCount++;
			_surgeBurstTimer = AcidConfig.SurgeBurstSeconds;

			float left  = GameConstants.Hazards.BasinLeftX  + 40f;
			float right = GameConstants.Hazards.BasinRightX - 40f;
			if (_grid != null && _grid.HasWetCells)
			{
				var wet = _grid.GetWetBounds();
				left  = Math.Max(wet.X, GameConstants.Arena.InnerLeft) + 40f;
				right = Math.Min(wet.X + wet.Width, GameConstants.Arena.InnerRight) - 40f;
				if (right <= left)
				{
					left  = GameConstants.Hazards.BasinLeftX  + 40f;
					right = GameConstants.Hazards.BasinRightX - 40f;
				}
			}
			// Impulses belong in the POOL: the per-column topmost-wet query can
			// return stray spray far above the body (the corner geysers, storm
			// mist), and an impulse placed there detonates mist instead of
			// launching a dense crest tongue. Clamp every point to at or below
			// the measured standing surface.
			float standing = GetStandingSurfaceY();
			int n = AcidConfig.SurgeImpulsePoints;
			for (int k = 0; k < n; k++)
			{
				float x = MathHelper.Lerp(left, right, n > 1 ? (float)k / (n - 1) : 0.5f);
				float surfaceY = Math.Max(GetSurfaceLevelAtX(x), standing - 24f);
				_sim.ApplyImpulseInRadius(
					new Vector2(x, surfaceY),
					AcidConfig.SurgeImpulseRadius,
					new Vector2(0f, -strength));
			}
		}

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
			// Lay particles on a square grid at the MEASURED rest density
			// (EffectiveParticleArea) so the pool actually stands at the
			// requested fill height. The old 2r spacing packed ~½ as dense as
			// the solver's true equilibrium, so the "resting pool" immediately
			// slumped to half its intended depth. The tiny remaining settle
			// reads as the pool "finding its level."
			float spacing = MathF.Sqrt(FluidConfig.EffectiveParticleArea);
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

			// Pour from the inlets only while rising (and never while draining).
			// Pre-filled acid (the Calm phase) has particles but isn't "rising"
			// yet — it must still simulate so it settles, stays contained, and
			// damages anyone knocked into it. Only the pour is gated.
			if (IsRising && !Draining)
			{
				SpawnInlet(dt);
			}

			// Drain sluice (Phase C): remove particles at the basin-floor sluice
			// at the rate BeginDrain derived for a fixed-duration recession; the
			// pool above feeds the hole under its own gravity/pressure so the
			// level visibly recedes. (DebugFastAcid compression rides in via the
			// caller's time-scaled duration.)
			if (Draining && _sim.Count > 0)
			{
				_drainAccum += _drainRatePerSec * dt;
				int toRemove = (int)_drainAccum;
				if (toRemove > 0)
				{
					_drainAccum -= toRemove;
					var sluice = AcidConfig.DrainSluice;
					_sim.DespawnInRect(sluice.X, sluice.Y, sluice.Width, sluice.Height, toRemove);
				}
			}

			if (_surgeBurstTimer > 0f)
			{
				_surgeBurstTimer -= dt;
			}

			if (_tellTimer > 0f)
			{
				_tellTimer -= dt;
			}

			if (Draining)
			{
				_sinceDrainEnd = 0f;
			}
			else if (_sinceDrainEnd < 999f)
			{
				_sinceDrainEnd += dt;
			}

			// Dry and not pouring — nothing to simulate, skip the per-frame work
			// (broadphase rebuild + step) entirely.
			if (_sim.Count == 0)
			{
				return;
			}

			// Refresh dynamic collider list — picks up newly spawned platforms.
			// Query area = wet bounds expanded by 2·h, or the basin region if dry
			// (where the first poured/pre-filled particles will land).
			var queryArea = _grid.HasWetCells
				? Expand(_grid.GetWetBounds(), 2f * FluidConfig.SmoothingRadius)
				: new RectangleF(
					GameConstants.Hazards.BasinLeftX - 64f,
					AcidConfig.LipY - 96f,
					(GameConstants.Hazards.BasinRightX - GameConstants.Hazards.BasinLeftX) + 128f,
					(GameConstants.Hazards.BasinFloorY - AcidConfig.LipY) + 160f);
			_colliders.RebuildFromPhysics(queryArea);

			_sim.Step(dt, _colliders);

			// Post-step hard containment (the "leaking out of the pit corners"
			// fix). The per-iteration AABB projection occasionally loses a
			// particle at concave seams (bank wall ∧ floor) under surge
			// pressure — a known SPH/PBF failure mode whose accepted game-grade
			// remedy is artificial repositioning at the boundary (cf.
			// DualSPHysics guidance). Push anything inside the three static
			// solids back to the nearest legal spot. O(n), serial, cheap.
			ClampIntoVessel();

			_grid.RebuildFrom(_sim);

			UpdateCurrentLevel();
		}

		private void ClampIntoVessel()
		{
			float r        = FluidConfig.ParticleRadius;
			float lipY     = AcidConfig.LipY;
			float floorY   = GameConstants.Hazards.BasinFloorY;
			float basinL   = GameConstants.Hazards.BasinLeftX;
			float basinR   = GameConstants.Hazards.BasinRightX;

			for (int i = 0; i < _sim.Count; i++)
			{
				float x = _sim.Px[i];
				float y = _sim.Py[i];

				if (y > lipY + r)
				{
					// At bank depth: only the basin channel is legal. Push
					// anything inside a bank body horizontally back into it.
					if (x < basinL + r)
					{
						_sim.Px[i] = basinL + r;
						if (_sim.Vx[i] < 0f) _sim.Vx[i] = 0f;
					}
					else if (x > basinR - r)
					{
						_sim.Px[i] = basinR - r;
						if (_sim.Vx[i] > 0f) _sim.Vx[i] = 0f;
					}
				}

				// Never below the basin floor.
				if (_sim.Py[i] > floorY - r && _sim.Px[i] > basinL && _sim.Px[i] < basinR)
				{
					_sim.Py[i] = floorY - r;
					if (_sim.Vy[i] > 0f) _sim.Vy[i] = 0f;
				}
			}
		}

		private void SpawnInlet(float dt)
		{
			// CLOSED-LOOP pour: stop when the MEASURED standing surface reaches
			// the fill ceiling (resuming if it recedes — the pool actively holds
			// its level through splash losses), with the count-based safety cap
			// as the overrun guard. A pure count target chronically missed deep
			// ceilings because settled density varies with depth.
			if (_sim.Count >= _safetyCap
				|| (_sim.Count > 0 && GetStandingSurfaceY() <= _fillCeilingY))
			{
				return;
			}

			// Direct particles/sec (AcidConfig.InletFlowFor via SetInletFlow) —
			// the old px²→particles conversion used π·r², a THIRD density
			// assumption disagreeing with both the cap formula and the real
			// settled pool. DebugFastAcid keeps its ×4 pour here.
			float spawnRate = _particlesPerSec *
				(AppSettings.DebugFastAcid ? GameConstants.Hazards.AcidDebugRiseMultiplier : 1f);
			if (_surgeBurstTimer > 0f)
			{
				// Surge: the valves visibly gush for a beat on top of the wave.
				spawnRate *= AcidConfig.SurgeBurstFlowMult;
			}
			_inletAccum += spawnRate * dt;

			int toSpawn = (int)_inletAccum;
			_inletAccum -= toSpawn;

			// Split the TOTAL flow evenly across the inlets — dual valves change
			// where the acid arrives, never how much arrives.
			var inlets = AcidConfig.Inlets;
			for (int i = 0; i < toSpawn; i++)
			{
				var inlet = inlets[i % inlets.Length];
				float jx  = ((float)_rng.NextDouble() * 2f - 1f) * FluidConfig.InletJitterX;
				float jy  = ((float)_rng.NextDouble() * 2f - 1f) * FluidConfig.InletJitterY;
				float jvx = ((float)_rng.NextDouble() * 2f - 1f) * FluidConfig.InletJitterVx;
				_sim.Spawn(
					inlet.x + jx,
					inlet.y + jy,
					inlet.vx + jvx,
					inlet.vy);
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

		/// <summary>True if acid occupies the cell at this world point (contact test).</summary>
		public bool IsAcidAt(float worldX, float worldY)
		{
			return _grid?.IsWetAt(worldX, worldY) ?? false;
		}

		/// <summary>
		/// True if a BODY of acid (not stray spray) occupies the cell at this
		/// world point — the erosion contact test. See
		/// <see cref="FluidConfig.BodyWetMinCount"/> for the density story.
		/// </summary>
		public bool IsAcidBodyAt(float worldX, float worldY)
		{
			return _grid?.IsDenselyWetAt(worldX, worldY, FluidConfig.BodyWetMinCount) ?? false;
		}

		/// <summary>
		/// Topmost cell holding a BODY of acid at or below the ceiling — the
		/// waterline query for surface-anchored PHYSICS (log buoyancy, landing
		/// checks). The threshold-1 <see cref="GetLocalSurfaceLevelAtX"/>
		/// returns the first stray droplet, and a float spring coupled to that
		/// reading ratchets its log into the air: an end sample inside a corner
		/// stream's column read "waterline y≈48" and carried a log to the
		/// ceiling (the "logs floating above the acid" bug).
		/// </summary>
		public float GetBodySurfaceLevelAtX(float worldX, float ceilingWorldY)
		{
			return _grid?.GetDenseSurfaceYAtBelow(worldX, ceilingWorldY, FluidConfig.BodyWetMinCount)
				?? _mapHeight;
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

		// Probe columns for the standing-surface measurement: strictly between
		// the C.2 diving boards (spans 448-544 / 736-832) — a column passing
		// through a board reads the splash PUDDLE sitting on it as "surface"
		// and permanently burns one of the percentile guard's two outlier
		// slots (measured: loop 0 stalled at its safety cap one quantum short
		// of the ceiling). Keep these inside x 544-736 exclusive if the layout
		// moves again. Scratch buffer avoids a per-call allocation (this runs
		// in the pour gate every frame).
		private static readonly float[] _probeColumns = { 560f, 600f, 640f, 680f, 720f };
		private readonly float[] _probeScratch = new float[5];

		/// <summary>
		/// The MEASURED standing surface: five probe columns over the basin,
		/// 75th-percentile toward the floor (2nd-LARGEST reading) — so up to
		/// two splash-contaminated columns (whose topmost-wet reads far above
		/// the bulk) can't fake a high surface. A 3-column median fired the
		/// closed-loop fill's "target reached" on wave transients mid-pour and
		/// truncated whole Rise phases. This is the oracle tests assert fill
		/// ceilings against — unlike <see cref="CurrentLevel"/>, the legacy
		/// full-width volumetric ESTIMATE, geometry-blind for a basin pool.
		/// </summary>
		public float GetStandingSurfaceY()
		{
			if (_grid == null)
			{
				return _mapHeight;
			}
			for (int i = 0; i < _probeColumns.Length; i++)
			{
				_probeScratch[i] = _grid.GetSurfaceYAt(_probeColumns[i]);
			}
			Array.Sort(_probeScratch);
			return _probeScratch[3];   // 2nd-largest of five
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
