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
		public const float LiquidPlayerDesaturation = 0.5f;  // 50% toward grey
		public const float LiquidPlayerCast         = 0.9f;  // strong green pull
		public const float LiquidPlayerDarken       = 0.75f; // 25% darker (depth absorption)

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

		/// <summary>
		/// Once CurrentLevel passes (above) this Y the inlet stops spawning. Used to
		/// cap the working-set particle count when the acid mechanic is effectively
		/// complete (top platform submerged).
		/// </summary>
		public const float InletStopMargin    = 32f;    // px above topPlatformY
	}
}
