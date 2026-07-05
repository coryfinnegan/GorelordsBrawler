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

	// ── Contest-then-consume, mid-loop respawn safety, and the storm ─────────

	[SkippableFact]
	public async Task TiersAreContestedThenConsumed_RespawnsStayDry_AndTheStormBreaksTheRefuges()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE — park BOTH players on the top-tier refuge for the duration.
		// The escalating rises drown the banks early, so idle bank-standers can
		// burn all three stocks before the storm — an ELIMINATED player never
		// respawns, which reads as a respawn bug but is a stocks flake. The top
		// tiers stay dry until the terminal storm's crests.
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.TeleportAsync(player: 0, 512f, 134f);
		await arena.TeleportAsync(player: 1, 768f, 134f);
		await arena.StepAsync(10);   // settle onto the tier
		int tiersAtStart = (await arena.StateAsync()).TiersRemaining;
		tiersAtStart.ShouldBe(8, "the C.2 Sump starts with eight dissolvable tiers (2 boards + 2 lows + 2 mids + 2 tops)");

		// ACT 1 — loop 1's rise DROWNS the diving boards outright (the first
		// destruction beat) and then LAPS the LOW tier bodies. Contact erosion
		// self-limits at the waterline, so the lows survive the whole contest
		// loop as fighting ground — STRICTLY: the rockfall doesn't start until
		// loop 2 precisely so nothing (acid or boulder wave) can break the
		// contest beat. At loop 1's drain: both boards gone, everything else
		// standing.
		var loop1Drain = await arena.StepUntilAsync(
			s => s.AcidPhase == "Drain" && s.AcidLoop == 1, maxFrames: 6000, batch: 10);
		loop1Drain.TiersRemaining.ShouldBe(6,
			"loop 1 takes the diving boards (fully submerged) but only CONTESTS the lows — 6 of 8 must stand at its drain");

		// ACT 2 — CONSUME: loop 2's rise passes the LOW tier tops; the chewed
		// remnants shell-erode and fall (functional-test decision: platforms
		// exist to be consumed — now on a schedule that laps before it takes).
		var lowsEaten = await arena.StepUntilAsync(
			s => s.TiersRemaining <= 4, maxFrames: 4000, batch: 10);

		// ACT 3 — mid-match respawn safety: kill P0 during the deepest regular
		// climb. The flood-aware picker must land them on a still-dry candidate.
		await arena.DamageAsync(player: 0, amount: 9999);
		await arena.StepUntilAsync(s => s.Players[0].Dead, maxFrames: 10, batch: 1);
		var respawned = await arena.StepUntilAsync(s => !s.Players[0].Dead, maxFrames: 300, batch: 2);
		await arena.StepAsync(30);
		var settled = await arena.StateAsync();
		settled.Players[0].Submerged.ShouldBeFalse(
			$"a mid-match respawn must land on dry refuge, not in the acid (y={settled.Players[0].Y})");

		// ACT 4 — the STORM: the time cap diverts the loop; the pour fills to
		// the budget-honest ceiling (standing surface just under the MID tiers)
		// and recurring crests break over the refuges. Modest step batches: the
		// debug server's /step has a 10 s wall-clock ceiling (408 on overrun)
		// and storm-scale Debug frames are expensive.
		var storm = await StepUntilPhaseAsync(arena, "FinalFlood", maxFrames: 4000, batch: 10);
		var filled = await arena.StepUntilAsync(
			s => s.AcidSurfaceY > 0 && s.AcidSurfaceY <= 288, maxFrames: 2500, batch: 10);

		// The rising sea must SUBMERGE the mid tiers — the "wait it out"
		// refuge dies by the same shell-erosion path as the lows.
		var midsBroken = await arena.StepUntilAsync(
			s => s.TiersRemaining <= 2, maxFrames: 4000, batch: 10);

		// ROCKFALL in its element: the storm rains boulders (3.5 s cadence);
		// with the pile bias and the seeded spawner rng, a cairn whose cap
		// breaches the sea — an ISLAND, the recovery route — must form within
		// a generous window.
		var island = await arena.StepUntilAsync(
			s => s.RockIslands >= 1, maxFrames: 6000, batch: 20);

		// ASSERT
		storm.AcidPhase.ShouldBe("FinalFlood");
		lowsEaten.TiersRemaining.ShouldBeLessThanOrEqualTo(4, "loop 2's rise must dissolve the LOW tier pair");
		respawned.Players[0].Hp.ShouldBe(respawned.Players[0].MaxHp);
		filled.AcidFillCap.ShouldBeGreaterThan(12000,
			"the storm's fill estimate should target the mid-submerging terminal ceiling (~13k)");
		filled.AcidSurfaceY.ShouldBeInRange(248, 288,
			"the storm's closed-loop fill must HOLD the measured surface at the ceiling (272 ± bob) — " +
			"above the mid tops, below the top tiers");
		midsBroken.TiersRemaining.ShouldBeLessThanOrEqualTo(2,
			"the storm sea must consume the MID tiers — otherwise two campers make the match unendable");
		island.RockIslands.ShouldBeGreaterThanOrEqualTo(1,
			"a cairn island must breach the storm sea — the recovery route has to exist when it matters most");
	}

	// ── The rockfall: gating, landing, and the impact hazard ─────────────────

	[SkippableFact]
	public async Task Rockfall_StartsAtLoopTwo_RocksRest_AndFallingRocksHurt()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE — park players on the top tiers, out of the early hazards.
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.TeleportAsync(player: 0, 512f, 134f);
		await arena.TeleportAsync(player: 1, 768f, 134f);
		await arena.StepAsync(10);

		// ACT/ASSERT 1 — no rocks until the arena starts LOSING footing: Calm,
		// loop 0, and the loop-1 CONTEST are rock-free (debris arrives because
		// the acid took the ground — and loop-1 pit towers' collapse waves
		// were killing the contested lows before this gate existed).
		var loop1Drain = await arena.StepUntilAsync(
			s => s.AcidPhase == "Drain" && s.AcidLoop == 1, maxFrames: 6000, batch: 10);
		loop1Drain.RocksAlive.ShouldBe(0, "no rocks may fall before loop 2 — loops 0-1 teach and contest");

		// ACT/ASSERT 2 — from loop 2 the facility sheds rock: a boulder spawns,
		// falls, and comes to REST (rocks cannot float by construction).
		var firstRock = await arena.StepUntilAsync(
			s => s.AcidLoop >= 2 && s.RocksAlive >= 1,
			maxFrames: 6000, batch: 10);
		firstRock.RocksAlive.ShouldBeGreaterThanOrEqualTo(1, "loop 2 must begin the rockfall");

		// ACT/ASSERT 3 — the impact hazard: catch a telegraphed drop mid-fall
		// and teleport P0 into its path below it. The boulder must hurt on the
		// way through (12 dmg + shove — the telegraph is what makes this fair).
		var falling = await arena.StepUntilAsync(
			s => s.RockFallingY > -1 && s.RockFallingY < 200, maxFrames: 6000, batch: 5);
		int hpBefore = (await arena.StateAsync()).Players[0].Hp;
		await arena.TeleportAsync(player: 0, falling.RockFallingX, 340f);
		var hurt = await arena.StepUntilAsync(
			s => s.Players[0].Hp < hpBefore, maxFrames: 240, batch: 5);
		hurt.Players[0].Hp.ShouldBeLessThan(hpBefore,
			"a falling boulder must damage the player it lands on");
	}
}
