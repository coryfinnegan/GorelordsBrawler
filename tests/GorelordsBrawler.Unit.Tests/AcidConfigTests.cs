using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
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
	public void ADryRespawnRung_ExistsThroughEveryRegularLoop()
	{
		// Footing above the banks is DYNAMIC now (the respawn picker's higher
		// rungs are the living platforms), so the dry-respawn guarantee is a
		// chain: every loop's ceiling must leave a SPAWNABLE lattice row whose
		// stand point (feet on the platform top) also clears the ceiling by
		// the respawn margin — then the picker can always land a death on dry
		// footing. (The terminal storm additionally has the picker's fallback:
		// spawn ABOVE the live surface — ArenaScene.PickSafeSpawn.)
		Assert.True(AcidConfig.PlatformSpawnClearance >= AcidConfig.RespawnClearancePx,
			"a platform viable to SPAWN must also be dry enough to RESPAWN onto — " +
			"the spawn clearance may not undercut the respawn clearance");
		for (int loop = 0; loop <= 10; loop++)
		{
			float ceiling = AcidConfig.RiseCeilingFor(loop);
			Assert.Contains(AcidConfig.PlatformSlotTopY,
				y => y + AcidConfig.RespawnClearancePx <= ceiling);
		}
		Assert.Contains(AcidConfig.PlatformSlotTopY,
			y => y + AcidConfig.RespawnClearancePx <= AcidConfig.StormCeilingY);
	}

	// ── The footing cycle (docs/platform-respawn-proposal.md) ────────────────

	[Fact]
	public void PlatformLattice_StaysOnArena_ClearOfInletsAndTheProbeLane()
	{
		float half = AcidConfig.PlatformW * 0.5f;
		foreach (var x in AcidConfig.PlatformSlotX)
		{
			Assert.True(x - half >= GameConstants.Arena.InnerLeft + 16f
				&& x + half <= GameConstants.Arena.InnerRight - 16f,
				$"slot {x} hangs a slab off the arena");
			// Corner inlet streams fall at x 72 / 1208 — a slab under one
			// would catch the pour mid-air.
			Assert.False(x - half <= 72f && x + half >= 72f,
				$"slot {x} blocks the left inlet stream");
			Assert.False(x - half <= 1208f && x + half >= 1208f,
				$"slot {x} blocks the right inlet stream");
			// The standing-surface probe lane (x 544-736): a slab breaching
			// the surface there puts splash puddles under the probe columns
			// and corrupts the closed-loop fill — the rockfall era hit exactly
			// this with center cairns (fill over-poured, islands drowned).
			Assert.False(x - half < 736f && x + half > 544f,
				$"slot {x} intrudes on the standing-surface probe lane (544-736)");
		}
	}

	[Fact]
	public void GhostTiming_IsInsideTheUserBand()
	{
		// User requirement: "flash for maybe 2-5 seconds".
		Assert.InRange(AcidConfig.GhostSeconds, 2f, 5f);
	}

	// ── The footing director (pacing rework, 2026-07-24) ─────────────────────

	[Fact]
	public void SpawnBand_TracksTheCeiling_PerLoop()
	{
		// The sliding band is the pacing promise: footing spawns just above
		// the acid — low rows while the pool is low, climbing with the loops,
		// top row only in the storm. Pin the exact viable rows per phase so a
		// retune of ceilings/band/lattice that breaks the climb fails here.
		static float[] ViableRows(float ceiling) => AcidConfig.PlatformSlotTopY
			.Where(y => AcidConfig.PlatformRowViable(
				y, ceiling, AcidConfig.PlatformSpawnClearance, bandRelaxed: false))
			.OrderBy(y => y)
			.ToArray();

		Assert.Equal(new[] { 352f, 416f }, ViableRows(AcidConfig.RiseCeilingFor(0)));
		Assert.Equal(new[] { 256f, 352f }, ViableRows(AcidConfig.RiseCeilingFor(1)));
		Assert.Equal(new[] { 160f, 256f }, ViableRows(AcidConfig.RiseCeilingFor(2)));
		Assert.Equal(new[] { 160f },       ViableRows(AcidConfig.StormCeilingY));

		// And the relaxed form must never orphan a phase (the cycle cannot
		// stall even if the band empties a cramped set).
		foreach (float ceiling in new[] {
			AcidConfig.RiseCeilingFor(0), AcidConfig.RiseCeilingFor(1),
			AcidConfig.RiseCeilingFor(2), AcidConfig.StormCeilingY })
		{
			Assert.Contains(AcidConfig.PlatformSlotTopY, y =>
				AcidConfig.PlatformRowViable(y, ceiling, 16f, bandRelaxed: true));
		}
	}

	[Fact]
	public void OpeningVolley_IsSymmetric_OnLattice_InBand_OffTheProbeLane()
	{
		// The match's first footing must be FAIR (mirrored about the arena
		// center, neither player advantaged), on the spawn lattice, inside the
		// loop-0 band (it hugs the opening danger zone), clear of the probe
		// lane, and clear of the starting pair by the stack rule.
		float half = AcidConfig.PlatformW * 0.5f;
		float arenaCenter = (GameConstants.Arena.InnerLeft + GameConstants.Arena.InnerRight) * 0.5f;

		Assert.Equal(2, AcidConfig.PlatformOpeningCenters.Length);
		var l = AcidConfig.PlatformOpeningCenters[0];
		var r = AcidConfig.PlatformOpeningCenters[1];
		Assert.Equal(arenaCenter - l.X, r.X - arenaCenter, precision: 3);
		Assert.Equal(l.Y, r.Y, precision: 3);

		foreach (var c in AcidConfig.PlatformOpeningCenters)
		{
			Assert.Contains(AcidConfig.PlatformSlotX, x => x == c.X);
			float topY = c.Y - AcidConfig.PlatformH * 0.5f;
			Assert.Contains(AcidConfig.PlatformSlotTopY, y => y == topY);
			Assert.True(AcidConfig.PlatformRowViable(
				topY, AcidConfig.RiseCeilingFor(0), AcidConfig.PlatformSpawnClearance, bandRelaxed: false),
				$"opening slab at top y={topY} is outside the loop-0 spawn band");
			Assert.False(c.X - half < 736f && c.X + half > 544f,
				$"opening slab at x={c.X} intrudes on the standing-surface probe lane (544-736)");

			// Stack rule vs the starting pair (map v3: centers 224/1056, y 432):
			// same-column spawns must keep the full stack clearance.
			foreach (var pair in new[] { new Vector2(224f, 432f), new Vector2(1056f, 432f) })
			{
				bool overlaps = Math.Abs(c.X - pair.X) < AcidConfig.PlatformW + 32f
					&& Math.Abs(c.Y - pair.Y) < AcidConfig.PlatformH + AcidConfig.PlatformStackClearance;
				Assert.False(overlaps,
					$"opening slab at ({c.X},{c.Y}) stacks on the starting pair at ({pair.X},{pair.Y})");
			}
		}
	}

	[Fact]
	public void FootingTargets_AreAchievable_AndTheStormShrinksThem()
	{
		// Regular target must be reachable the moment the match opens: the
		// starting pair plus the opening volley IS the target — the director
		// tops up only after deaths, so an unreachable target would leave it
		// grinding the relaxation stages forever.
		Assert.Equal(AcidConfig.PlatformTargetAlive,
			2 + AcidConfig.PlatformOpeningCenters.Length);

		// The storm's cramped-chase call: fewer than regular play, more than
		// the old two-island stalemate, and never beyond the top row's
		// physical capacity (its columns are the only storm-viable slots).
		Assert.True(AcidConfig.PlatformTargetStorm < AcidConfig.PlatformTargetAlive);
		Assert.InRange(AcidConfig.PlatformTargetStorm, 2, AcidConfig.PlatformSlotX.Length);

		Assert.Equal(AcidConfig.PlatformTargetAlive, AcidConfig.PlatformTargetFor(storm: false));
		Assert.Equal(AcidConfig.PlatformTargetStorm, AcidConfig.PlatformTargetFor(storm: true));
	}

	[Fact]
	public void OpeningFooting_ArrivesWithinTheCalmPhase_Fast()
	{
		// The pacing complaint this rework answers: full footing must exist
		// within seconds of the first frame, not after the acid's first
		// consume beat. Opening ghosts flash at t=0 and any director top-up
		// cascades on the stagger, so worst-case full footing lands at
		// GhostSeconds + target·stagger — pin it under 10 s of the ~30 s calm.
		Assert.InRange(AcidConfig.PlatformTopUpStaggerSeconds, 0.25f, 2f);
		float worstCase = AcidConfig.GhostSeconds
			+ AcidConfig.PlatformTargetAlive * AcidConfig.PlatformTopUpStaggerSeconds;
		Assert.True(worstCase < 10f,
			$"full opening footing takes ~{worstCase:F1} s — the match should open ready to fight");
	}

	[Fact]
	public void PlatformRows_AreReachable_AndEveryPhaseKeepsASpawnableRow()
	{
		// Reachability: the lowest row within the known bank→platform hop
		// (128 px — the old lows), and no adjacent-row gap beyond it.
		var rows = AcidConfig.PlatformSlotTopY.OrderByDescending(y => y).ToArray();
		Assert.True(AcidConfig.LipY - rows[0] <= 128f,
			$"lowest row (top y={rows[0]}) is more than a hop above the bank lip");
		for (int i = 1; i < rows.Length; i++)
		{
			Assert.True(rows[i - 1] - rows[i] <= 128f,
				$"row gap {rows[i - 1]}→{rows[i]} exceeds the jump");
		}

		// The cycle can never stall: every regular loop AND the storm must
		// leave at least one row that clears the ceiling by the spawn margin.
		for (int loop = 0; loop <= 10; loop++)
		{
			float ceiling = AcidConfig.RiseCeilingFor(loop);
			Assert.Contains(AcidConfig.PlatformSlotTopY,
				y => y <= ceiling - AcidConfig.PlatformSpawnClearance);
		}
		Assert.Contains(AcidConfig.PlatformSlotTopY,
			y => y <= AcidConfig.StormCeilingY - AcidConfig.PlatformSpawnClearance);
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

}
