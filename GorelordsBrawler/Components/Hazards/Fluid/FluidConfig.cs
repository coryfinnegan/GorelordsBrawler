namespace GorelordsBrawler.Components.Hazards.Fluid
{
	/// <summary>
	/// All tunable constants for the particle-based acid simulation.
	/// Kept separate from GameConstants because these are domain-specific to
	/// the PBF (Position-Based Fluids) solver and renderer.
	///
	/// Reference: Macklin & Müller, "Position Based Fluids", ACM TOG 2013.
	/// </summary>
	public static class FluidConfig
	{
		// ── Particle physics ──────────────────────────────────────────────────
		public const float ParticleRadius     = 4f;    // px (physics radius)
		public const float SmoothingRadius    = 12f;   // h, ≈ 3 · ParticleRadius
		public const float RestDensity        = 1000f; // ρ₀ — overridden by hex-pack calibration at init
		public const float ParticleMass       = 1f;
		public const float Gravity            = 1200f; // px/s²
		public const int   SolverIterations   = 3;     // paper
		public const int   MaxParticles       = 5000;

		// ── PBF tuning ────────────────────────────────────────────────────────
		// NOTE: these are calibrated for the NORMALIZED kernels used by
		// FluidSimulation (W_poly6(0) = 1, no h⁹ etc. blow-up). Paper defaults
		// (ε=600, k=0.0001) assume the absolute kernels and water in SI units
		// and do NOT apply here.
		public const float Epsilon            = 0.05f;   // λ-denominator regularizer
		public const float SCorrK             = 0.001f;  // tensile-instability strength
		public const float SCorrDqRatio       = 0.2f;    // Δq = 0.2 · h
		public const int   SCorrN             = 4;       // exponent

		// ── Viscosity / vorticity ─────────────────────────────────────────────
		public const float XsphC              = 0.1f;   // XSPH viscosity (strong for thick acid feel)
		public const float LinearDrag         = 0.5f;   // per-frame drag (1 - drag·dt) on velocity
		public const bool  UseVorticity       = false;  // optional polish
		public const float VorticityEpsilon   = 0.0f;

		// ── Collision damping ─────────────────────────────────────────────────
		public const float WallRestitution    = 0.0f;
		public const float WallTangentFriction = 0.4f;
		public const float PlatformRestitution = 0.05f;

		// ── Inlet ─────────────────────────────────────────────────────────────
		public const float InletYOffset       = -40f;   // above map top
		public const float InletJitterX       = 6f;     // ±px lateral randomization
		public const float InletJitterY       = 2f;     // ±px vertical randomization
		public const float InletDownVelocity  = 250f;   // px/s initial downward velocity
		public const float InletJitterVx      = 30f;    // ±px/s horizontal velocity randomization

		// ── Surface / damage queries ──────────────────────────────────────────
		public const int   GridCellSize       = 16;     // px per occupancy cell
		public const int   WetThreshold       = 1;      // particles needed to mark a cell wet
		public const float DamageBoundsPadY   = 4f;

		// ── Rendering ─────────────────────────────────────────────────────────
		public const float DiscSpriteRadius   = 8f;     // half-extent of disc texture in px (≈ 2× physics)
		public const bool  UseMetaballPass    = false;  // optional polish flag

		// ── Compatibility ─────────────────────────────────────────────────────
		/// <summary>
		/// Replaces the old `AcidSurface.CellSize` constant used by DynamicPlatform
		/// to pad surface-level queries when computing float equilibrium.
		/// </summary>
		public const float SurfacePadding     = 8f;

		// ── Particle lifecycle ────────────────────────────────────────────────
		/// <summary>Despawn particles whose Y exceeds mapHeight + this value.</summary>
		public const float DespawnBelowMargin = 100f;

		/// <summary>
		/// Once CurrentLevel passes (above) this Y the inlet stops spawning. Used to
		/// cap the working-set particle count when the acid mechanic is effectively
		/// complete (top platform submerged).
		/// </summary>
		public const float InletStopMargin    = 32f;    // px above topPlatformY
	}
}
