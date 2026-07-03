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
		// 18000: sized for the terminal storm-flood, which SUBMERGES the mid
		// tiers — the closed-loop fill holds the real surface at y=272, which
		// takes ~15.3k particles at the real storm-depth density incl. airborne
		// spray (measured 2026-07-02: a 15.3k safety stop was hit exactly and
		// starved the far mid tier). 0.9 × 18k = 16.2k pour headroom + solver
		// margin above that. Parallel solver cost is ~linear in N — measured
		// (Release, i9-13900K) 10k = 12% of a 60 fps frame; the FluidBenchmark
		// table includes a 17000 row; a real-speed Debug session held 12.2k at
		// 60 fps with no sag (2026-07-01 capture) and the storm capture
		// re-verifies at true peak. AcidConfigTests pins the budget invariant
		// so retuning can't silently blow it.
		public const int   MaxParticles       = 18000;

		/// <summary>
		/// MEASURED standing area per particle (px²) of the settled pool — the
		/// single source of truth for every particles↔fill-height conversion
		/// (pour caps, inlet flow, pre-fill packing).
		///
		/// Why it is neither of the two "theoretical" values previously used:
		///   hex-pack at 2r  → 55.4 px²  (what ParticleCapForCeiling assumed)
		///   π·r²            → 50.3 px²  (what the pour conversion assumed)
		/// The solver CALIBRATES ρ₀ on a hex grid at spacing 2r, but the DYNAMIC
		/// equilibrium under gravity settles ~2× denser (3 constraint iterations
		/// leave residual hydrostatic compression; sCorr changes effective
		/// spacing). With the old 55.4 assumption every fill ceiling landed ~½
		/// its intended height above the lip — in a real-speed match the acid
		/// never reached a single platform (2026-07-01 timeline capture).
		///
		/// 31.0 = the arena-width, mid-depth settled value (headless measurement,
		/// FluidCalibrationTests). Density varies ±10% with pool depth (35.4 at
		/// 80 px deep → 28.3 at 220 px — residual hydrostatic compression), so
		/// AcidConfig's lap/delete ceilings carry 16–24 px margins to stay
		/// correct across the spread. Pinned by Settled_Pool_Stands_At_The_
		/// Calibrated_Density; re-measure with FLUID_CALIB=1 after solver
		/// retunes.
		/// </summary>
		public const float EffectiveParticleArea = 31.0f;

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
		// Spawn BELOW the arena's top wall row (collision tiles fill y=0..32),
		// otherwise the wall projection pushes every fresh particle back up
		// and they pile up off-screen at y≈-4 forever.
		public const float InletYOffset       = 50f;    // just below the ceiling wall
		public const float InletJitterX       = 6f;     // ±px lateral randomization
		public const float InletJitterY       = 2f;     // ±px vertical randomization
		public const float InletDownVelocity  = 250f;   // px/s initial downward velocity
		public const float InletJitterVx      = 30f;    // ±px/s horizontal velocity randomization

		// ── Surface / damage queries ──────────────────────────────────────────
		public const int   GridCellSize       = 16;     // px per occupancy cell
		public const int   WetThreshold       = 1;      // particles needed to mark a cell wet
		// Erosion contact needs a BODY of liquid, not spray: standing liquid at
		// rest spacing packs ~8 particles into a 16 px cell, a crest tongue
		// 4–8, airborne froth 1–3. Threshold 4 lets laps/submersion/crest
		// washes erode while the corner streams' impact geysers (which chewed
		// tiers 100+ px above the pool) read as mist. Damage keeps
		// WetThreshold=1 — getting splashed should hurt players, it just
		// shouldn't dissolve platforms.
		public const int   ErosionWetMinCount = 4;
		public const float DamageBoundsPadY   = 4f;

		// ── Rendering ─────────────────────────────────────────────────────────
		// SplatRadius: each particle is drawn as a soft-alpha disc of this
		// radius into the field RenderTexture in Pass 1 of the metaball
		// pipeline. Must be substantially larger than the physics radius so
		// neighbouring particles' fields overlap heavily — GameDev.net
		// "Fluid Rendering with Box2D" recommends ~3× physics radius as a
		// starting point.
		public const float SplatRadius        = 12f;
		public const float DiscSpriteRadius   = 8f;     // legacy / unused
		public const bool  UseMetaballPass    = false;  // legacy / unused

		// ── Liquid post-process shader tunables ───────────────────────────────
		// Read by LiquidPostProcessor and forwarded to the liquid.fx shader
		// every frame. Tune live by editing here + rebuilding.
		public const float LiquidThresholdMin = 0.40f;  // bottom of the smooth edge in field-alpha space
		public const float LiquidThresholdMax = 0.55f;  // top (fully solid above this)
		public const float LiquidEdgeBandWidth = 0.04f; // ½-width of the bright meniscus highlight

		// ── In-acid player presence (Phase 3 of acid-deadly-polish-plan) ──────
		// liquid.fx reduces the metaball bodyMask inside each player's rect by
		// this much, so the scene (player sprite) shows through the acid. The
		// shader also applies a subtle green tint inside player regions to read
		// as "stained by the acid" rather than fully clear glass.
		//   0.0 → no see-through (acid stays fully opaque over players)
		//   1.0 → fully see-through (acid completely transparent over players)
		// 0.85: was 0.9, dropped slightly so the metaball acid still has
		// presence ABOVE the submerged player (sells "they are UNDER the
		// liquid" rather than "they are RENDERED ON TOP of the liquid").
		// Player sprite is still very visible — see PlayerDesaturation +
		// PlayerCast + PlayerDarken below for the proper underwater filter
		// applied to the scene where the player is.
		public const float LiquidPlayerMaskStrength = 0.85f;

		// Underwater color filter applied to the scene IN PLAYER REGIONS,
		// modelled on real underwater photography (see Crest Ocean's
		// underwater rendering docs, Cyanilux's 2D water shader breakdown):
		//
		//   1. DESATURATION — water bleeds colors toward each other so
		//      submerged objects look more uniformly coloured than they
		//      really are. 0 = no desat, 1 = fully grey.
		//   2. CAST — multiplicative color tint toward the liquid's hue.
		//      For our green acid this pulls everything strongly green.
		//      A value of 1.0 means full multiplicative cast; 0 = none.
		//   3. DARKEN — light absorption underwater. Multiplicative
		//      brightness reduction. 1 = unchanged, 0 = fully black.
		//
		// These three replace the previous PlayerTintStrength single-knob,
		// which only did a weak additive green tint and didn't actually read
		// as "underwater" (review feedback: "looks like you are just
		// rendering the sprite on top of the acid").
		// Softened after functional testing: at 0.5/0.9/0.75 a submerged sprite
		// read as near-invisible green mush whenever field noise brushed it.
		// The deep-field gate in liquid.fx fixes WHERE the filter applies;
		// these fix HOW HARD it hits when it legitimately does.
		public const float LiquidPlayerDesaturation = 0.35f; // toward grey
		public const float LiquidPlayerCast         = 0.6f;  // green pull
		public const float LiquidPlayerDarken       = 0.85f; // 15% darker (depth absorption)

		// ── Surface "alive" pulse (Phase 1 of acid-deadly-polish-plan) ────────
		// Animates the brightness of the surface highlight in liquid.fx so the
		// acid reads as charged/corrosive instead of inert. Body geometry is
		// unaffected — only the highlight intensity breathes.
		public const float LiquidPulseSpeed    = 2.5f;   // radians/sec → ~0.4 Hz breathing
		public const float LiquidPulseStrength = 0.65f;  // 0..1: how much the highlight dims at trough

		// ── Surface bubbles (Phase 1 of acid-deadly-polish-plan) ──────────────
		// Single continuous-burst ParticleEmitter retargeted each spawn to a
		// random x along the current surface line. Reads as "this thing is
		// fizzing / corrosive". Spawn rate + lifespan were tuned in-game via
		// the AcidBubbleEmitter inspector sliders — see PR #5 review thread.
		public const float BubbleSpawnsPerSec  = 64f;
		public const int   BubbleMaxParticles  = 256;    // headroom for spawn-rate exploration
		public const float BubbleLifespan      = 0.9f;
		public const float BubbleLifespanVar   = 0.25f;
		public const float BubbleRiseSpeed     = 22f;    // px/sec upward
		public const float BubbleRiseSpeedVar  = 10f;
		public const float BubbleStartSize     = 4f;     // px radius
		public const float BubbleFinishSize    = 9f;

		// ── Compatibility ─────────────────────────────────────────────────────
		/// <summary>
		/// Replaces the old `AcidSurface.CellSize` constant used by DynamicPlatform
		/// to pad surface-level queries when computing float equilibrium.
		/// </summary>
		public const float SurfacePadding     = 8f;

		// ── Particle lifecycle ────────────────────────────────────────────────
		/// <summary>Despawn particles whose Y exceeds mapHeight + this value.</summary>
		public const float DespawnBelowMargin = 100f;

		// (InletStopMargin removed — the volumetric inlet-stop it served was
		// replaced by geometry-derived particle caps in Phase A, now ceiling-
		// parametric via AcidConfig.ParticleCapForCeiling.)
	}
}
