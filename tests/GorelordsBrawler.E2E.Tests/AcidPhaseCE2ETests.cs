using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GorelordsBrawler.E2E.Tests.Pages;
using Shouldly;
using Xunit;

namespace GorelordsBrawler.E2E.Tests;

/// <summary>
/// E2E coverage for Acid Arena Phase C: the looping, intensifying phase machine
/// (Calm → Rise → Scramble → Surge → Drain → loop → FinalFlood), driven in
/// deterministic stepped mode against the phase oracles.
///
/// The driver always launches with DebugFastAcid, so AcidConfig.TimeScale()
/// compresses every duration ×4 and the pour runs ×4 — a full loop is a few
/// thousand fixed-dt frames, which stepped batches cover in seconds.
///
/// Determinism matters most for the ESCALATION test: in stepped mode the same
/// script yields the same frames, so "loop 1's surges come measurably sooner
/// than loop 0's" is an exact assertion, not a statistical one.
/// </summary>
[Trait("Category", "E2E")]
public class AcidPhaseCE2ETests
{
	/// <summary>Step in batches until the phase oracle reads <paramref name="phase"/>.</summary>
	private static Task<GameStateSnapshot> StepUntilPhaseAsync(
		ArenaPage arena, string phase, int maxFrames, int batch = 10)
		=> arena.StepUntilAsync(s => s.AcidPhase == phase, maxFrames, batch);

	// ── The loop, in order, with both valves pouring ─────────────────────────

	[SkippableFact]
	public async Task PhaseMachine_RunsTheFullLoop_InOrder_AndEscalatesTheNextRise()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		var calm = await arena.StateAsync();

		// ACT / ASSERT — walk the machine through one full cycle. Each
		// transition is its own wait so a hang pinpoints the stuck phase.
		calm.AcidPhase.ShouldBe("Calm");
		calm.AcidLoop.ShouldBe(0);

		var rise = await StepUntilPhaseAsync(arena, "Rise", maxFrames: 400);
		int countAtRiseStart = rise.AcidParticleCount;

		// Both valves pour: the pool grows well past the pre-fill while rising.
		var scramble = await StepUntilPhaseAsync(arena, "Scramble", maxFrames: 1500);
		scramble.AcidParticleCount.ShouldBeGreaterThan(countAtRiseStart + 300,
			"the Rise phase should add substantial volume (both inlets pouring)");
		scramble.AcidFillCap.ShouldBeGreaterThan(0);
		// The fill is CLOSED-LOOP on the measured surface: Rise hands off when
		// the pool STANDS at the loop-0 ceiling (528), and the count lands
		// near — not exactly at — the geometric estimate.
		scramble.AcidSurfaceY.ShouldBeLessThanOrEqualTo(536,
			"Rise hands off to Scramble when the measured surface reaches the fill ceiling");
		scramble.AcidParticleCount.ShouldBeGreaterThan((int)(scramble.AcidFillCap * 0.7f),
			"the standing pool's count should be in the neighborhood of the geometric estimate");

		var surge = await StepUntilPhaseAsync(arena, "Surge", maxFrames: 1200);
		int surgesBefore = surge.AcidSurgeCount;

		// Phase D: entering Surge opens with a TELEGRAPH — bubbles boil, the
		// meniscus agitates, the camera rumbles — and the wave lands one tell
		// lead later, never on the entry frame. Deterministic in stepped mode.
		surge.AcidTellActive.ShouldBeTrue(
			"Surge entry must arm the telegraph; the first wave lands one tell-lead later");
		var firstWave = await arena.StepUntilAsync(
			s => s.AcidSurgeCount > surgesBefore, maxFrames: 60, batch: 2);
		firstWave.AcidSurgeCount.ShouldBe(surgesBefore + 1,
			"the telegraphed wave must land within its lead window");

		var drain = await StepUntilPhaseAsync(arena, "Drain", maxFrames: 1500);
		drain.AcidSurgeCount.ShouldBeGreaterThan(surgesBefore,
			"the Surge phase must actually fire surges before handing off");
		drain.AcidDraining.ShouldBeTrue();

		int countAtDrainStart = drain.AcidParticleCount;
		var riseAgain = await StepUntilPhaseAsync(arena, "Rise", maxFrames: 1500);

		// ASSERT — the loop closed with escalation: counter up, level receded,
		// and the new rise targets MORE volume than loop 0 did.
		riseAgain.AcidLoop.ShouldBe(1, "completing Drain must increment the loop counter");
		riseAgain.AcidParticleCount.ShouldBeLessThan(countAtDrainStart,
			"the Drain phase should visibly remove volume before the next rise");
		riseAgain.AcidFillCap.ShouldBeGreaterThan(scramble.AcidFillCap,
			"loop 1's rise ceiling must be higher (bigger cap) than loop 0's — the escalation");
	}

	// ── Escalation: surges measurably accelerate loop-over-loop ──────────────

	[SkippableFact]
	public async Task Surges_ComeFaster_OnTheNextLoop()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();

		// ACT — record game-time gaps between consecutive surges in each loop's
		// Surge phase. state.time advances by fixed dt in stepped mode, so the
		// gaps are exact.
		var loop0Gaps = await MeasureSurgeGapsAsync(arena, expectedLoop: 0);
		var loop1Gaps = await MeasureSurgeGapsAsync(arena, expectedLoop: 1);

		// ASSERT — the cadence tightened (AcidConfig.SurgeIntervalDecay).
		loop0Gaps.Count.ShouldBeGreaterThanOrEqualTo(1);
		loop1Gaps.Count.ShouldBeGreaterThanOrEqualTo(1);
		float avg0 = Avg(loop0Gaps);
		float avg1 = Avg(loop1Gaps);
		avg1.ShouldBeLessThan(avg0,
			$"loop 1 surges should come faster than loop 0's (escalation): {avg1:F2}s vs {avg0:F2}s");
	}

	private static async Task<List<float>> MeasureSurgeGapsAsync(ArenaPage arena, int expectedLoop)
	{
		// Enter this loop's Surge phase…
		var surge = await arena.StepUntilAsync(
			s => s.AcidPhase == "Surge" && s.AcidLoop == expectedLoop, maxFrames: 6000, batch: 10);

		// …then sample surge timestamps frame-batch by frame-batch until the
		// phase ends. Batch 2 keeps timestamp error ≤ 2 frames per event.
		//
		// ORDER MATTERS: the cycle's last surge fires on the SAME tick that the
		// machine hands off to Drain, so the state where the count increments is
		// also the state where the phase has already changed. Process the surge
		// evidence FIRST, then check the phase for exit — a while(phase) loop
		// head silently drops the final surge and under-counts every cycle.
		var gaps = new List<float>();
		float lastSurgeTime = -1f;
		int   lastCount     = surge.AcidSurgeCount;
		var   s             = surge;
		while (true)
		{
			if (s.AcidSurgeCount > lastCount)
			{
				if (lastSurgeTime >= 0f)
				{
					gaps.Add(s.Time - lastSurgeTime);
				}
				lastSurgeTime = s.Time;
				lastCount     = s.AcidSurgeCount;
			}
			if (s.AcidPhase != "Surge")
			{
				break;
			}
			await arena.StepAsync(2);
			s = await arena.StateAsync();
		}
		return gaps;
	}

	private static float Avg(List<float> xs)
	{
		float sum = 0f;
		foreach (var x in xs)
		{
			sum += x;
		}
		return sum / xs.Count;
	}

	// ── The footing cycle, mid-loop respawn safety, and the storm ────────────

	[SkippableFact]
	public async Task FootingCycle_GhostsLeadSpawns_RespawnsStayDry_AndTheStormChasesUp()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE — the test's subject is the PLATFORM CYCLE, so the players
		// are HOVER-PARKED: re-teleported to mid-air over the pit every batch
		// (teleport zeroes velocity), touching nothing and taking no damage.
		// Any staging that leaves them standing in the arena burns stocks —
		// two contested phases of chip is a whole stock, and an ELIMINATED
		// player never respawns, which poisons the ACT-3 respawn beat.
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();

		async Task HoverParkAsync()
		{
			await arena.TeleportAsync(player: 0, 608f, 120f);
			await arena.TeleportAsync(player: 1, 672f, 120f);
		}
		async Task<GameStateSnapshot> HoverUntilAsync(
			Func<GameStateSnapshot, bool> condition, int maxIters, string what)
		{
			for (int i = 0; i < maxIters; i++)
			{
				await HoverParkAsync();
				await arena.StepAsync(10);
				var s = await arena.StateAsync();
				if (condition(s))
				{
					return s;
				}
			}
			throw new TimeoutException($"{what} not reached within {maxIters * 10} hovered frames.");
		}

		await HoverParkAsync();
		await arena.StepAsync(5);
		var start = await arena.StateAsync();
		start.PlatformsAlive.ShouldBe(2, "map v3 starts with exactly the platform pair");

		// ACT 1 — loop 1 CONTESTS the pair (ceiling 432 laps the 416 tops;
		// waterline erosion self-limits) but must not consume it.
		var loop1Drain = await HoverUntilAsync(
			s => s.AcidPhase == "Drain" && s.AcidLoop == 1, 600, "loop-1 drain");
		loop1Drain.PlatformsAlive.ShouldBe(2,
			"loop 1 contests the starting pair but must not consume it");

		// ACT 2 — loop 2 eats the pair. The FIRST death must raise a ghost
		// telegraph immediately, and the eventual platform must materialize
		// EXACTLY on its ghost, above the live surface.
		await HoverUntilAsync(s => s.PlatformsAlive < 2, 600, "the pair's first death");
		var ghost = await arena.StepUntilAsync(s => s.GhostActive, maxFrames: 30, batch: 1);
		int ghostX = ghost.GhostX;
		int ghostY = ghost.GhostY;
		var spawned = await arena.StepUntilAsync(s => s.LastSpawnX > -1, maxFrames: 600, batch: 5);
		spawned.LastSpawnX.ShouldBe(ghostX, "the platform must materialize on its ghost's column");
		spawned.LastSpawnY.ShouldBe(ghostY, "the platform must materialize at its ghost's height");
		spawned.LastSpawnY.ShouldBeLessThan(spawned.AcidSurfaceY,
			"a fresh platform must spawn above the live surface, never inside it");
		spawned.PlatformsAlive.ShouldBeLessThanOrEqualTo(2,
			"population is conserved — one death, one replacement");

		// CHASE: both players onto the new footing (lastSpawn is the slab
		// CENTER; stand point is 40 px above it — half slab + half body).
		await arena.TeleportAsync(player: 0, spawned.LastSpawnX - 32f, spawned.LastSpawnY - 40f);
		await arena.TeleportAsync(player: 1, spawned.LastSpawnX + 32f, spawned.LastSpawnY - 40f);
		await arena.StepAsync(10);

		// ACT 3 — mid-match respawn safety: the picker's rungs above the banks
		// are the LIVING platforms now. Kill P0 mid-climb; he must come back
		// at full HP on dry footing, not inside the acid.
		await arena.DamageAsync(player: 0, amount: 9999);
		await arena.StepUntilAsync(s => s.Players[0].Dead, maxFrames: 10, batch: 1);
		var respawned = await arena.StepUntilAsync(s => !s.Players[0].Dead, maxFrames: 300, batch: 2);
		await arena.StepAsync(30);
		var settled = await arena.StateAsync();
		settled.Players[0].Submerged.ShouldBeFalse(
			$"a mid-match respawn must land on dry footing, not in the acid (y={settled.Players[0].Y})");
		respawned.Players[0].Hp.ShouldBe(respawned.Players[0].MaxHp);

		// Re-park P0 beside P1 on the newest platform before the storm.
		var s2 = await arena.StateAsync();
		if (s2.LastSpawnX > -1)
		{
			await arena.TeleportAsync(player: 0, s2.LastSpawnX - 32f, s2.LastSpawnY - 40f);
		}

		// ACT 4 — the STORM: the fill holds the terminal ceiling, and the
		// cycle's replacements have climbed with the flood — by the time the
		// sea stands at its ceiling, the surviving footing sits in the upper
		// band, dry above the flood (the "last perches" endgame; whether the
		// steady state arrives just before or during the storm is a seed
		// detail — the invariant is footing above the terminal sea). Modest
		// step batches: the debug server's /step has a 10 s wall-clock
		// ceiling (408 on overrun) and storm-scale Debug frames are expensive.
		var storm = await StepUntilPhaseAsync(arena, "FinalFlood", maxFrames: 6000, batch: 10);
		// Wait for an IN-BAND reading, not merely "high enough": a storm crest
		// transiting the probe lane can read far above the ceiling for a
		// moment (208 on the new open map), and a <=288 predicate latches that
		// transient as "filled". The standing level is what the closed loop
		// holds; a reading inside the band is by definition the standing one.
		var filled = await arena.StepUntilAsync(
			s => s.AcidSurfaceY >= 248 && s.AcidSurfaceY <= 288, maxFrames: 4000, batch: 10);

		// ASSERT
		storm.AcidPhase.ShouldBe("FinalFlood");
		filled.AcidFillCap.ShouldBeGreaterThan(12000,
			"the storm's fill estimate should target the terminal ceiling (~13k)");
		filled.AcidSurfaceY.ShouldBeInRange(248, 288,
			"the storm's closed-loop fill must HOLD the measured surface at the ceiling (272 ± bob)");
		filled.PlatformsAlive.ShouldBe(2,
			"population is conserved: the pair count survives into the storm");
		filled.LastSpawnY.ShouldBeLessThanOrEqualTo(224,
			"the cycle's replacements must have climbed into the upper band by the terminal flood");
	}

}
