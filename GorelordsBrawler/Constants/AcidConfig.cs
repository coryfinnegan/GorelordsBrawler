using System;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components.Hazards.Fluid;

namespace GorelordsBrawler.Constants
{
	/// <summary>
	/// All tuning + geometry for the acid match timeline (Phase C of
	/// docs/acid-arena-design-proposal.md): the Calm → Rise → Scramble → Surge →
	/// Drain loop, its per-loop escalation, the dual inlets, and the terminal
	/// storm-flood.
	///
	/// Every particles↔height conversion goes through
	/// <see cref="FluidConfig.EffectiveParticleArea"/> — the MEASURED standing
	/// density of the settled pool. The original hex-pack assumption made every
	/// ceiling land ~half its intended height above the lip (the acid never
	/// reached a platform in a real-speed match; 2026-07-01 timeline capture).
	///
	/// The rise schedule follows CONTEST-THEN-CONSUME: a tier class first gets a
	/// loop where the acid LAPS it (waterline across the slab body — contact
	/// erosion chews the wet part and self-limits, so players fight on ground
	/// that is visibly dissolving under them), and only the NEXT loop submerges
	/// it (shell erosion, gone in ~3 s). One ceiling can do both jobs at once:
	/// rising past a lapped tier finishes it en route to lapping the next. The
	/// escalation curves stay PURE functions of the loop counter so they are
	/// unit-testable (AcidConfigTests).
	/// </summary>
	public static class AcidConfig
	{
		// ── Sump geometry (world px, mirrors arena1.tmx) ─────────────────────
		/// <summary>Top surface of the banks — the basin lip (row 17 × 32).</summary>
		public const float LipY = 544f;

		// Tier surfaces the schedule is written against (tools/gen_sump_map.py):
		// SHALLOW diving boards span y 480–512 (overhanging the pit — the early
		// risk/reward perch), LOW tiers 416–448, MID 288–320, TOP 160–192.
		// Change the map and these TOGETHER.
		public const float ShallowTierTopY    = 480f;
		public const float ShallowTierBottomY = 512f;
		public const float LowTierTopY    = 416f;
		public const float LowTierBottomY = 448f;
		public const float MidTierTopY    = 288f;
		public const float MidTierBottomY = 320f;
		public const float TopTierTopY    = 160f;

		// ── Dual inlets (top corners of the MAP — functional-test decision) ──
		/// <summary>
		/// Spawn point + initial velocity per inlet: the very top corners of the
		/// arena, just inside the outer walls. The streams DEAD-DROP down the
		/// corner columns the map generator keeps clear (cols 2/37), land on the
		/// banks, sheet across them, and cascade over the lip into the pit.
		/// vx is 0 on purpose: an earlier inward drift (vx=±30) walked the
		/// falling stream onto the LOW tier span (x≥128) and the spawn bodies —
		/// wetting a tier the schedule hadn't contested yet and dropping acid on
		/// respawning heads.
		/// </summary>
		public static readonly (float x, float y, float vx, float vy)[] Inlets =
		{
			(72f,   48f, 0f, 120f),   // top-left corner, straight down
			(1208f, 48f, 0f, 120f),   // top-right corner, straight down
		};

		// ── Escalation curves (pure functions of the loop counter) ───────────
		// Contest-then-consume rise targets:
		//   loop 0 → 528: banks awash ankle-deep; every tier dry. Pressure, no
		//                 destruction — the arena teaches the loop.
		//   loop 1 → 432: waterline across the LOW tier bodies (416–448) — they
		//                 erode up to the waterline and SURVIVE as chewed slabs
		//                 players still fight on.
		//   loop 2+ → 392: past the LOW tier tops — the remnants shell-erode and
		//                 fall; the surface holds ~70 px under the MID tiers,
		//                 whose bottoms only the growing surge crests can reach.
		// MID/TOP tiers are deliberately out of standing-surface reach: getting
		// a standing surface to the MID tops costs ~14.5k particles — over the
		// 15k solver budget — so the high tiers belong to the STORM's crests
		// (below), not to the pour. Margins (16 px below a lapped tier's top,
		// 24 px past a consumed tier's top) absorb the ±10% density-vs-depth
		// spread documented on EffectiveParticleArea.
		public static readonly float[] RiseCeilings = { 528f, 432f, 392f };
		public const float RiseCeilingMinY = 392f;

		public static float RiseCeilingFor(int loop) =>
			loop < RiseCeilings.Length ? RiseCeilings[loop] : RiseCeilingMinY;

		// Inlet flow (particles/sec), escalating so every loop's rise lands in
		// ~30 s despite the caps growing loop over loop (the volume from lip to
		// a lapped LOW tier is ~3× the loop-0 pour). The gush itself escalating
		// is part of the "crazier and crazier" read. DebugFastAcid multiplies by
		// GameConstants.Hazards.AcidDebugRiseMultiplier (applied in AcidSurface).
		public const float InletParticlesPerSecBase = 60f;
		public static readonly float[] InletFlowMults = { 1f, 2.8f, 3.7f };

		public static float InletFlowFor(int loop) =>
			InletParticlesPerSecBase *
			InletFlowMults[Math.Min(loop, InletFlowMults.Length - 1)];

		// Surge cadence: sooner every loop, floored so input can still react.
		public const float SurgeIntervalBaseSeconds = 12f;
		public const float SurgeIntervalDecay       = 0.8f;
		public const float SurgeIntervalMinSeconds  = 5f;

		public static float SurgeIntervalFor(int loop) =>
			MathF.Max(SurgeIntervalMinSeconds,
				SurgeIntervalBaseSeconds * MathF.Pow(SurgeIntervalDecay, loop));

		// Surge violence: upward impulse speed (px/s), growing per loop, capped
		// so late-game waves stay readable rather than teleporting the pool.
		// Lowered after functional testing ("the acid is going insane") — the
		// wave should HEAVE, not explode.
		public const float SurgeStrengthBase   = 200f;
		public const float SurgeStrengthGrowth = 1.25f;
		public const float SurgeStrengthMax    = 500f;

		public static float SurgeStrengthFor(int loop) =>
			MathF.Min(SurgeStrengthMax,
				SurgeStrengthBase * MathF.Pow(SurgeStrengthGrowth, loop));

		/// <summary>
		/// Ballistic crest height above the standing surface for a surge of the
		/// given impulse speed: v²/2g. An upper bound (drag shaves it), used to
		/// reason about which tiers a surge can splash — AcidConfigTests pins
		/// the storm's crests against the TOP tier height with it.
		/// </summary>
		public static float CrestHeightFor(float strength) =>
			strength * strength / (2f * FluidConfig.Gravity);

		// ── Phase timing / shape ──────────────────────────────────────────────
		public const float ScrambleDurationSeconds = 12f;
		public const int   SurgesPerCycle          = 3;
		public const float DrainCeilingY           = 600f;  // recede target (pool mostly back in the basin)

		/// <summary>
		/// The closed-loop fill's "target reached" must HOLD this long before
		/// Rise hands off — a single frame's reading can be a wave transient
		/// even through the percentile probe, and a premature handoff truncates
		/// the whole rising beat (measured: a loop-1 "rise" of 1.3 s).
		/// </summary>
		public const float FillHoldSeconds = 0.75f;

		// ── Telegraphing (Phase D) ────────────────────────────────────────────
		// Every dynamic beat announces itself so deaths feel earned, not
		// ambushed (Brinstar shakes before its acid rises). One tell channel on
		// AcidSurface (BeginTell/TellProgress) drives three synchronized cues:
		// bubbles boil harder, the meniscus pulse quickens and brightens, and
		// the camera builds a low rumble — then the wave. Kept SUBTLE per the
		// juice guidance (trauma-based shake, brief windows): the tell is a
		// warning, not its own light show.
		public const float SurgeTellSeconds = 0.9f;   // lead before every surge/storm crest
		public const float RiseTellSeconds  = 1.2f;   // rumble before each rise's valves open
		public const float TellBubbleMult   = 4f;     // bubble spawn multiplier at full tell
		public const float TellTraumaPerSec = 0.6f;   // camera trauma/sec at full tell progress
		public const float TellPulseSpeed   = 5.9f;   // Hz — prime-ish ratio vs the 2.5 idle pulse
		public const float TellPulseBoost   = 2f;     // meniscus pulse strength multiplier at full tell

		/// <summary>
		/// Drain is a BEAT, not a blink: the sluice rate is derived at drain
		/// entry so the recession always takes about this long regardless of how
		/// much the loop poured (a fixed rate made loop 0's drain ~2.7 s — over
		/// before the relief registered; 2026-07-01 capture).
		/// </summary>
		public const float DrainDurationSeconds = 9f;

		// Terminal STORM-flood (diverts from Surge at the time cap): the pour
		// SUBMERGES the MID tiers outright — the closed-loop fill holds the
		// real surface at 272, 16 px over their tops, so they die by the same
		// proven shell-erosion path as the lows (marginal-contact schemes all
		// stalled: crest tips are mist the density gate rightly ignores, and a
		// surface AT the tier bottoms bobs the dwell streaks apart — measured
		// 2026-07-02, tiers survived 65 s of crests twice). The TOP tiers
		// survive as the FINAL refuge: crests still REACH them (v²/2g ≈ 204 px
		// at 700) as splash, which damages campers (WetThreshold=1) without
		// dissolving the ground — the endgame is a knife-edge fight over the
		// last dry perches above a lethal sea, with Phase-B depth lethality
		// ruling the water. A full-map fill would need ~25k particles; this
		// stops at ~14.2k, inside the (raised, re-benched) 17k solver budget.
		public const float StormCeilingY             = 272f;
		public const float StormSurgeStrength        = 700f;
		public const float StormCrestIntervalSeconds = 5f;
		public const float StormFlowMult             = 1.5f;

		/// <summary>
		/// Total match seconds before the loop diverts to the storm (checked at
		/// each surge volley's end, so the flood arrives on a beat). 200 s lands
		/// at loop 2's volley (~t≈230): Calm 30 + three rises at ~30 s each +
		/// scrambles + volleys + two ~9 s drains ⇒ the storm opens ~3:50 and the
		/// match resolves in ~4:15–4:45.
		/// </summary>
		public const float TimeCapSeconds = 200f;

		// Drain sluice: particles are despawned inside this rect at the basin
		// floor center; gravity/pressure feed the hole, so the level visibly
		// recedes without any fake suction forces.
		public static readonly RectangleF DrainSluice = new RectangleF(580f, 704f, 120f, 40f);

		// Surge delivery: N upward impulse points swept across the basin span +
		// a brief pour burst so the wave visibly adds volume at the valves.
		// Burst toned down with the surge strength — the valves should gush,
		// not detonate.
		public const int   SurgeImpulsePoints   = 5;
		public const float SurgeImpulseRadius   = 60f;
		public const float SurgeBurstSeconds    = 0.6f;
		public const float SurgeBurstFlowMult   = 1.5f;

		// Scramble platforms: how many drop-in logs the spawner keeps alive —
		// GROWING with the loop, so as static footing dissolves the arena
		// transforms into a bobbing debris field instead of emptying out
		// (subtraction → transformation; cf. Brinstar returning its platforms).
		// Drops keep the SPORADIC stagger (functional-test decision: irregular,
		// not metronomic), one per SIDE at the dissolved tiers' former X spots.
		public const float PlatformDropStaggerMin = 2.5f;
		public const float PlatformDropStaggerMax = 7f;

		public static int ScramblePlatformTargetFor(int loop) =>
			Math.Min(2 + loop, 4);

		/// <summary>
		/// Drop-log X anchors: on the former LOW/MID tier spans, per side (C.2).
		/// The mid anchors sit on the OUTER third of the old mid spans — the
		/// centers (416/864) are directly under the new top tiers' edges, and a
		/// falling log clips through anything in its column (DynamicPlatform
		/// only lands on other logs and the acid).
		/// </summary>
		public static readonly float[] LogSpawnXLeft  = { 224f, 376f };
		public static readonly float[] LogSpawnXRight = { 904f, 1056f };
		public const float LogSpawnXJitter = 12f;

		// Swiss-cheese erosion (ErodibleSurface): platforms are a grid of solid
		// cells the acid eats by CONTACT — every pass, perimeter cells whose
		// world position overlaps the acid metaball are removed, so holes carve
		// inward from exactly the faces the acid laps (Worms / Cortex Command
		// destructible-2D-terrain, contact-driven). 4 px cells (user call: finer
		// pitting looks better). Rendering + collision greedy-merge the
		// survivors, so an intact slab is ONE collider.
		//
		// Contact requires DWELL: an exposed face erodes only after its outside
		// sample has been wet for this many CONSECUTIVE passes. One droplet
		// grazing a face is not immersion — without this, the corner streams'
		// impact froth (a flickering spray tower at each bank) chewed through
		// the low AND mid tiers mid-rise while the standing surface was still
		// 100+ px below them (2026-07-02 probe screenshots). Standing laps,
		// submersion, and storm-crest washes hold faces wet continuously, so
		// they pass the filter a couple of passes late; flicker resets to 0.
		public const int ErosionDwellPasses = 3;

		// PassesPerSec = how fast the eaten front advances. With the dwell
		// filter each exposed shell needs DwellPasses before it falls, so the
		// rates are scaled up from the pre-dwell values to keep macro timings.
		// PER SURFACE, because the two platform kinds live in opposite regimes:
		//   TIERS sit still — a LAPPED tier erodes to the waterline and stops
		//   (self-limiting), a SUBMERGED one shell-erodes in
		//   ~(rows/2)·dwell/rate s. 3.0 → a submerged 32 px tier dies in ~4 s,
		//   the consume beat.
		//   LOGS float — buoyancy holds fresh hull at the waterline forever, so
		//   a log erodes CONTINUOUSLY while afloat (~8 rows bottom-up, dwell
		//   per row). 1.2 → ~20 s of life, long enough to outlive the surge
		//   volley it drops into (at tier-rate the late-game footing lived ~5 s).
		public const float ErosionCellSize         = 4f;
		public const float TierErosionPassesPerSec = 3.0f;
		public const float LogErosionPassesPerSec  = 1.2f;

		// The log spawner opens once the LOW tier pair is MOSTLY eaten (not
		// fully — full consumption of the last crumbs can lag well behind the
		// visible destruction, and the debris should start falling while the
		// chewing is still on screen).
		public const float TierMostlyEatenFraction = 0.5f;

		// ── Respawn safety (flood-aware) ──────────────────────────────────────
		/// <summary>
		/// Respawn candidates from lowest (banks — the normal case) to highest
		/// (top tiers — the storm refuge). The safe-spawn picker takes the
		/// LOWEST candidate whose column's acid surface clears the feet by
		/// <see cref="RespawnClearancePx"/>; if nothing qualifies (deep storm)
		/// it spawns ABOVE the live surface instead of into the acid.
		/// </summary>
		public static readonly Vector2[] RespawnPoints =
		{
			new Vector2(200f, 520f),    // left bank
			new Vector2(1080f, 520f),   // right bank
			new Vector2(416f, 260f),    // mid-left tier (C.2: pulled inward over the pit edge)
			new Vector2(864f, 260f),    // mid-right tier
			new Vector2(496f, 134f),    // top-left tier (last dry ground in the storm)
			new Vector2(784f, 134f),    // top-right tier
		};
		public const float RespawnClearancePx = 48f;

		// ── Debug-fast time scale ─────────────────────────────────────────────
		/// <summary>
		/// DebugFastAcid already multiplies the pour rate ×4 (AcidSurface); the
		/// phase TIMERS must compress by the same factor or a debug match would
		/// pour fast but loop slow. Multiply every duration/interval by this.
		/// NOTE for feel-testing: debug-fast compresses the WHOLE experience ×4
		/// (erosion included) — pacing verdicts need a real-speed session.
		/// </summary>
		public static float TimeScale() => AppSettings.DebugFastAcid ? 0.25f : 1f;

		/// <summary>
		/// Top of the solid ground at a column: the basin floor inside the pit,
		/// the bank surface (lip) everywhere else. Drop-logs use it so a log
		/// falling through a DRY column rests on the ground instead of
		/// tunnelling through the banks (DynamicPlatform never collides with
		/// static tiles). When the flood later rises past the ground, the same
		/// clamp lets the log lift off and float — buoyancy emerges on its own.
		/// </summary>
		public static float GroundYAt(float x) =>
			(x > GameConstants.Hazards.BasinLeftX && x < GameConstants.Hazards.BasinRightX)
				? GameConstants.Hazards.BasinFloorY
				: LipY;

		// ── Particle budget ───────────────────────────────────────────────────
		/// <summary>
		/// ESTIMATE of how many settled particles stand from the basin floor up
		/// to <paramref name="ceilingY"/>. Piecewise: below the lip only the
		/// basin channel holds liquid; above the lip the full inner arena width
		/// does. Converts area→particles via the MEASURED
		/// <see cref="FluidConfig.EffectiveParticleArea"/> (the hex-pack ideal
		/// it originally used over-estimated standing height ~2× — the acid
		/// never reached a platform in a real match). The pour itself is
		/// CLOSED-LOOP on the measured surface (density varies ±10% with
		/// depth); this estimate feeds the oracle, the budget invariants, and
		/// ×1.25 the pour's hard safety stop. (Tier volumes are ignored: ~330
		/// particles of error at storm height, inside those margins.)
		/// </summary>
		public static int ParticleCapForCeiling(float ceilingY)
		{
			float basinW = GameConstants.Hazards.BasinRightX - GameConstants.Hazards.BasinLeftX;
			float innerW = GameConstants.Arena.InnerRight - GameConstants.Arena.InnerLeft;
			float floorY = GameConstants.Hazards.BasinFloorY;

			float area;
			if (ceilingY >= LipY)
			{
				area = basinW * MathF.Max(0f, floorY - ceilingY);
			}
			else
			{
				area = basinW * (floorY - LipY) + innerW * (LipY - ceilingY);
			}

			return (int)(area / FluidConfig.EffectiveParticleArea);
		}
	}
}
