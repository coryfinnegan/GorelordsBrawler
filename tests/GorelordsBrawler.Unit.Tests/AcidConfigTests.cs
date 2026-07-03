using GorelordsBrawler.Components.Hazards.Fluid;
using GorelordsBrawler.Constants;
using Xunit;

namespace GorelordsBrawler.Unit.Tests;

/// <summary>
/// Pins the Phase-C design promises encoded in AcidConfig: the escalation
/// curves actually escalate (with floors/caps), the CONTEST-THEN-CONSUME rise
/// schedule laps a tier class before the loop that submerges it, the particle
/// budget can never be silently blown by retuning, the dual inlets pour clear
/// of tiers and spawns, and every regular loop leaves a dry respawn.
/// </summary>
public class AcidConfigTests
{
	// ── Escalation curves ────────────────────────────────────────────────────

	[Fact]
	public void RiseCeilings_FollowContestThenConsume()
	{
		// Smaller y = higher fill. Monotonic per loop, saturating at the min.
		float prev = AcidConfig.RiseCeilingFor(0);
		for (int loop = 1; loop <= 10; loop++)
		{
			float cur = AcidConfig.RiseCeilingFor(loop);
			Assert.True(cur <= prev, $"rise ceiling went DOWN the vessel at loop {loop}: {prev} -> {cur}");
			prev = cur;
		}
		Assert.Equal(AcidConfig.RiseCeilingMinY, AcidConfig.RiseCeilingFor(100), precision: 3);

		// Loop 0: pressure without destruction — banks awash, every tier dry.
		// The C.2 diving boards are the lowest structure (480–512) and must
		// stay STANDING-dry through loop 0 (waves may nibble; the board's top
		// face keeps its feet).
		Assert.True(AcidConfig.RiseCeilingFor(0) < AcidConfig.LipY,
			"loop 0 must wash over the banks (below the lip in Y)");
		Assert.True(AcidConfig.RiseCeilingFor(0) > AcidConfig.ShallowTierBottomY,
			"loop 0's standing surface must stay under the diving boards — the first loop teaches, it doesn't take");

		// Loop 1's rise DROWNS the boards outright on its way to the low tiers
		// — the match's first destruction beat, before the low-tier contest.
		Assert.True(AcidConfig.RiseCeilingFor(1) < AcidConfig.ShallowTierTopY,
			"loop 1 must fully submerge the diving boards en route to the low-tier lap");

		// Loop 1 CONTESTS the low tiers: waterline across the slab body, top
		// face dry — players fight on ground that is dissolving under them.
		Assert.InRange(AcidConfig.RiseCeilingFor(1),
			AcidConfig.LowTierTopY + 8f, AcidConfig.LowTierBottomY - 8f);

		// Loop 2 CONSUMES them: past the tops (with margin for the ±10%
		// density-vs-depth spread), but never touching the mid tiers' bottoms —
		// the standing surface can't afford the mid tops (budget), so the mids
		// belong to surge crests and the storm.
		Assert.True(AcidConfig.RiseCeilingFor(2) < AcidConfig.LowTierTopY - 8f,
			"loop 2 must submerge the LOW tier tops (with density margin)");
		Assert.True(AcidConfig.RiseCeilingMinY > AcidConfig.MidTierBottomY,
			"no regular loop's STANDING surface may reach the MID tiers — they're crest/storm territory");
	}

	[Fact]
	public void StormSubmergesTheMids_AndCrestSplashReachesTheTops()
	{
		// The terminal storm's kill mechanism, as measured (2026-07-02): only
		// genuinely SUBMERGED faces erode (crest tips are mist the density
		// gate rightly ignores; a surface bobbing AT a tier's underside resets
		// the dwell streaks), so the storm's closed-loop fill must hold the
		// surface ABOVE the mid tops — the mids then die by the same shell
		// path as the lows. The TOP tiers survive as the final refuge — but
		// crest SPLASH (WetThreshold-1 wetness → damage, not erosion) must
		// still reach them, or camping the last perch is free.
		Assert.True(AcidConfig.StormCeilingY < AcidConfig.MidTierTopY - 8f,
			"the storm must hold its surface clear ABOVE the mid tops (with bob margin) to consume them");
		Assert.True(AcidConfig.StormCeilingY > AcidConfig.TopTierTopY + 32f,
			"the storm must NOT drown the top tiers — they are the endgame's knife-edge refuge");
		float crestTop = AcidConfig.StormCeilingY - AcidConfig.CrestHeightFor(AcidConfig.StormSurgeStrength);
		Assert.True(crestTop <= AcidConfig.TopTierTopY,
			$"storm crest splash tops out at y={crestTop:F0} — short of the TOP tiers (y={AcidConfig.TopTierTopY}); camping the last perch would be free");
		// And no regular loop's standing surface may enter the mid band —
		// that entrance is the storm's announcement.
		Assert.True(AcidConfig.RiseCeilingMinY > AcidConfig.MidTierBottomY,
			"regular loops must stop short of the mid tiers — the storm owns them");
	}

	[Fact]
	public void SurgeInterval_ShrinksPerLoop_AndFloors()
	{
		float prev = AcidConfig.SurgeIntervalFor(0);
		for (int loop = 1; loop <= 10; loop++)
		{
			float cur = AcidConfig.SurgeIntervalFor(loop);
			Assert.True(cur <= prev, $"surge interval grew at loop {loop}: {prev} -> {cur}");
			prev = cur;
		}
		Assert.Equal(AcidConfig.SurgeIntervalMinSeconds, AcidConfig.SurgeIntervalFor(100), precision: 3);
	}

	[Fact]
	public void SurgeStrength_GrowsPerLoop_AndCaps()
	{
		float prev = AcidConfig.SurgeStrengthFor(0);
		for (int loop = 1; loop <= 10; loop++)
		{
			float cur = AcidConfig.SurgeStrengthFor(loop);
			Assert.True(cur >= prev, $"surge strength shrank at loop {loop}: {prev} -> {cur}");
			prev = cur;
		}
		Assert.Equal(AcidConfig.SurgeStrengthMax, AcidConfig.SurgeStrengthFor(100), precision: 3);
	}

	[Fact]
	public void RegularSurgeCrests_MissTheMidTiers_ThroughLoop3()
	{
		// Mid-tier harassment is the STORM's job. Within the loops a capped
		// match actually reaches (TimeCap diverts during loop 2's volley; 3 is
		// the safety margin), the crest envelope must stay under the mid
		// bottoms or the mids erode off-schedule. Beyond loop 3 the strength
		// cap MAY crest into them — acceptable escalation for an over-cap
		// match, so it is deliberately not pinned.
		for (int loop = 0; loop <= 3; loop++)
		{
			float surface  = AcidConfig.RiseCeilingFor(loop);
			float crestTop = surface - AcidConfig.CrestHeightFor(AcidConfig.SurgeStrengthFor(loop));
			Assert.True(crestTop > AcidConfig.MidTierBottomY,
				$"loop {loop}'s surge crests at y={crestTop:F0}, into the MID tiers (bottoms y={AcidConfig.MidTierBottomY}) — they'd erode before the storm");
		}
	}

	[Fact]
	public void InletFlow_EscalatesPerLoop_SoRisesStayOnTempo()
	{
		// Later loops pour a much larger volume; a flat flow would stretch the
		// loop-2 rise past a minute of dead air (measured 69 s pre-fix).
		float prev = AcidConfig.InletFlowFor(0);
		Assert.True(prev > 0f);
		for (int loop = 1; loop <= 10; loop++)
		{
			float cur = AcidConfig.InletFlowFor(loop);
			Assert.True(cur >= prev, $"inlet flow shrank at loop {loop}: {prev} -> {cur}");
			prev = cur;
		}

		// Tempo bound: every loop's net pour volume (rise cap minus what the
		// drain left behind) must land in ~20–45 s at that loop's flow. This is
		// the pacing contract — it fails if someone retunes ceilings, drain, or
		// flow without keeping the rise on tempo.
		int drainCap = AcidConfig.ParticleCapForCeiling(AcidConfig.DrainCeilingY);
		for (int loop = 0; loop <= 3; loop++)
		{
			int cap   = AcidConfig.ParticleCapForCeiling(AcidConfig.RiseCeilingFor(loop));
			int start = loop == 0
				? AcidConfig.ParticleCapForCeiling(GameConstants.Hazards.BasinRestTopY)
				: drainCap;
			float riseSeconds = (cap - start) / AcidConfig.InletFlowFor(loop);
			Assert.InRange(riseSeconds, 20f, 45f);
		}
	}

	// ── Particle budget (the invariant that guards the frame rate) ───────────

	[Fact]
	public void StormFlood_FitsTheParticleBudget_WithMargin()
	{
		// The single most important number in the file: if someone retunes the
		// storm ceiling (or shrinks MaxParticles) so the terminal fill can't
		// fit, this fails BEFORE the game does. The fill is closed-loop on the
		// measured surface, so the REAL count runs up to ~10% past the formula
		// estimate at storm depth (density-vs-depth spread) — that expected
		// real fill must sit inside the pour's own 90%-of-budget safety stop.
		int stormCap = AcidConfig.ParticleCapForCeiling(AcidConfig.StormCeilingY);
		Assert.True(stormCap * 1.10f <= FluidConfig.MaxParticles * 0.9f,
			$"the storm's expected real fill (~{stormCap * 1.10f:F0}) exceeds the pour safety stop " +
			$"({FluidConfig.MaxParticles * 0.9f:F0} = 90% of MaxParticles={FluidConfig.MaxParticles})");
	}

	[Fact]
	public void ParticleCap_GrowsAsTheCeilingRises()
	{
		// Monotonic: a higher fill (smaller y) always needs at least as many
		// particles — and the piecewise seam at the lip must not jump backwards.
		int prev = AcidConfig.ParticleCapForCeiling(GameConstants.Hazards.BasinFloorY);
		for (float y = GameConstants.Hazards.BasinFloorY - 8f; y >= AcidConfig.StormCeilingY; y -= 8f)
		{
			int cur = AcidConfig.ParticleCapForCeiling(y);
			Assert.True(cur >= prev, $"cap shrank as ceiling rose past y={y}: {prev} -> {cur}");
			prev = cur;
		}
	}

	[Fact]
	public void DrainTarget_IsBelowEveryRiseTarget()
	{
		// Drain must actually relieve: its cap must sit below the smallest rise
		// cap, or the Drain phase could complete instantly (no visible recede).
		int drainCap = AcidConfig.ParticleCapForCeiling(AcidConfig.DrainCeilingY);
		int loop0Cap = AcidConfig.ParticleCapForCeiling(AcidConfig.RiseCeilingFor(0));
		Assert.True(drainCap < loop0Cap,
			$"drain target ({drainCap}) must be below the loop-0 rise cap ({loop0Cap})");
	}

	// ── Inlets (top corners of the map — functional-test decision) ───────────

	[Fact]
	public void Inlets_DeadDropTheCornerColumns_ClearOfTiersAndSpawns()
	{
		// The streams pour from the very top corners, inside the outer walls,
		// straight down the columns the map generator keeps clear (cols 2/37).
		// vx must be ~0: an inward drift walks the falling stream onto the LOW
		// tier span (x>=128) — wetting a tier the schedule hasn't contested —
		// and across the spawn bodies at x=96/1184.
		Assert.Equal(2, AcidConfig.Inlets.Length);
		foreach (var inlet in AcidConfig.Inlets)
		{
			Assert.True(inlet.y < 64f, $"inlet at y={inlet.y} is not at the top of the map");
			Assert.InRange(inlet.x, GameConstants.Arena.InnerLeft + 8f, GameConstants.Arena.InnerRight - 8f);
			bool nearACorner = inlet.x < 128f || inlet.x > 1152f;
			Assert.True(nearACorner, $"inlet at x={inlet.x} is not in a clear corner column (tier spans start at 128)");
			Assert.Equal(0f, inlet.vx, precision: 3);
			Assert.True(inlet.vy > 0f, "inlet streams must pour downward");
		}
	}

	// ── Flood-safe respawn ────────────────────────────────────────────────────

	[Fact]
	public void AtLeastOneRespawnCandidate_StaysDry_ThroughEveryRegularLoop()
	{
		// Through every NON-terminal rise (the deepest being RiseCeilingMinY) a
		// dry respawn must exist, or a mid-match death becomes a respawn-melt
		// loop. (The terminal storm has its own guarantee: the picker's
		// fallback spawns ABOVE the live surface — ArenaScene.PickSafeSpawn.)
		bool anyDry = false;
		foreach (var p in AcidConfig.RespawnPoints)
		{
			float feetY = p.Y + 24f;   // FutureAxe body height 48 — feet below center
			if (feetY + AcidConfig.RespawnClearancePx <= AcidConfig.RiseCeilingMinY)
			{
				anyDry = true;
			}
		}
		Assert.True(anyDry, "no respawn candidate survives the deepest regular rise — mid-match deaths would respawn into acid");
	}

	[Fact]
	public void LogSpawnSlots_SitOnTheirSides_OffTheInletColumns()
	{
		// Drop anchors must be left/right of the basin mouth respectively (one
		// per side, never clumping center) and clear of the corner stream
		// columns so a spawning log doesn't ride down inside a pour.
		foreach (var x in AcidConfig.LogSpawnXLeft)
		{
			Assert.True(x < GameConstants.Hazards.BasinLeftX, $"left slot {x} is not left of the basin");
			Assert.True(x > 160f, $"left slot {x} sits in the corner stream column");
		}
		foreach (var x in AcidConfig.LogSpawnXRight)
		{
			Assert.True(x > GameConstants.Hazards.BasinRightX, $"right slot {x} is not right of the basin");
			Assert.True(x < 1120f, $"right slot {x} sits in the corner stream column");
		}
	}

	[Fact]
	public void RespawnCandidates_AreOrderedLowToHigh()
	{
		// The picker takes the FIRST candidate that is dry, so the array must be
		// ordered lowest (banks, near the action) to highest (top tiers).
		for (int i = 1; i < AcidConfig.RespawnPoints.Length; i++)
		{
			Assert.True(AcidConfig.RespawnPoints[i].Y <= AcidConfig.RespawnPoints[i - 1].Y,
				$"respawn candidate {i} is lower than candidate {i - 1} — the low-to-high pick order breaks");
		}
	}

	// ── Log population ────────────────────────────────────────────────────────

	[Fact]
	public void ScramblePlatformTarget_GrowsWithTheLoop_AndCaps()
	{
		// Subtraction → transformation: the debris field must GROW as the
		// static tiers die, and stay bounded (each log costs colliders and a
		// fluid-interaction footprint).
		int prev = AcidConfig.ScramblePlatformTargetFor(0);
		Assert.True(prev >= 2, "the first drops must give both players a target");
		for (int loop = 1; loop <= 10; loop++)
		{
			int cur = AcidConfig.ScramblePlatformTargetFor(loop);
			Assert.True(cur >= prev, $"log target shrank at loop {loop}");
			Assert.True(cur <= 4, "log target must stay bounded");
			prev = cur;
		}
	}
}
