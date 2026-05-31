using System;
using System.Threading.Tasks;
using GorelordsBrawler.E2E.Tests.Pages;
using Shouldly;
using Xunit;

namespace GorelordsBrawler.E2E.Tests;

/// <summary>
/// E2E tests that drive REAL gameplay through the scripted input device + deterministic
/// frame-stepping. They operate through the <see cref="ArenaPage"/> Page Object (and the
/// <see cref="PlayerObject"/>s it hands out) rather than the raw HTTP transport, assert with
/// Shouldly, and follow an explicit ARRANGE / ACT / ASSERT shape.
///
/// Oracles are acid-independent on purpose: a melee connection is proven by <c>MeleeHitsTaken</c>
/// (never moved by acid), input suppression by <c>Facing</c> (written only by the normal-movement
/// branch), the hit-freeze by <c>HitstopActive</c>. HP deltas are never used to detect a hit,
/// because fast-acid drains both players every frame.
///
/// Enable with E2E_TESTS=1 (a real window is required; see the proposal doc).
/// </summary>
[Trait("Category", "E2E")]
public class GameplayAutomationE2ETests
{
	// ── Movement ──────────────────────────────────────────────────────────────--

	[SkippableFact]
	public async Task ScriptedInput_MovesPlayer_LeftAndRight()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		var player = arena.Player(0);
		int startX = (await player.StateAsync()).X;

		// ACT
		await player.HoldRightAsync();
		await arena.StepAsync(15);
		var movedRight = await player.StateAsync();
		await player.StopAsync();

		await player.HoldLeftAsync();
		await arena.StepAsync(15);
		var movingLeft = await player.StateAsync();
		await player.StopAsync();

		// ASSERT
		movedRight.X.ShouldBeGreaterThan(startX, "right input should move the player right");
		movedRight.Vx.ShouldBeGreaterThan(0);
		movingLeft.Vx.ShouldBeLessThan(0, "left input should drive the player left");
	}

	// ── Jump ──────────────────────────────────────────────────────────────────--

	[SkippableFact]
	public async Task ScriptedInput_Jump_LeavesGroundThenLands()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		var player = arena.Player(0);

		// ACT
		await player.PressJumpAsync();
		await arena.StepAsync(2);
		await player.ReleaseJumpAsync();
		var airborne = await arena.StepUntilAsync(s => s.Players[0].Vy < 0, maxFrames: 10, batch: 1);
		var landed   = await arena.StepUntilAsync(s => s.Players[0].Grounded, maxFrames: 300, batch: 5);

		// ASSERT
		airborne.Players[0].Vy.ShouldBeLessThan(0, "a jump should produce upward velocity (negative Y)");
		landed.Players[0].Grounded.ShouldBeTrue("the player should land again after jumping");
	}

	// ── Melee connects (player-vs-player core) ───────────────────────────────────

	[SkippableFact]
	public async Task MeleeHit_Connects_DealsDamage_AndKnocksBack()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();                          // act before acid activates — no DoT contamination
		await arena.StageMeleeConnectAsync(attacker: 0, target: 1);
		var before = await arena.Player(1).StateAsync();

		// ACT
		var atHit = await arena.ThrowAttackUntilHitAsync(attacker: 0, target: 1);

		// ASSERT
		var target = atHit.Players[1];
		target.MeleeHitsTaken.ShouldBe(before.MeleeHitsTaken + 1);          // exactly one connection
		target.Hitstun.ShouldBeTrue("the target should be in hitstun after the hit");
		target.Hp.ShouldBeLessThan(before.Hp, "the target should take melee damage");
		target.Vx.ShouldBeGreaterThan(100, "the target should be knocked back (a player with no input can't gain this vx otherwise)");
	}

	// ── Acid survives a melee hit (THE regression that slipped through) ───────────

	[SkippableFact]
	public async Task MeleeHit_WhileAcidPresent_DoesNotBreakTheFluid()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		var before = await arena.StepUntilAsync(
			s => s.AcidActive && s.AcidParticleCount > 150 && s.AcidFinite, maxFrames: 2000, batch: 10);
		await arena.StageMeleeConnectAsync(attacker: 0, target: 1);
		int hitsBefore = (await arena.Player(1).StateAsync()).MeleeHitsTaken;

		// ACT — throw one attack, then step the hitbox + hitstop window one frame at a time,
		// latching the evidence (a coarse step could skip the ~4-frame freeze).
		await arena.Player(0).PressAttackAsync();
		await arena.StepAsync(1);
		await arena.Player(0).ReleaseAttackAsync();

		bool sawHitstop = false;
		bool stayedFinite = true;
		int minCount = int.MaxValue;
		GameStateSnapshot after = before;
		for (int i = 0; i < 40; i++)
		{
			await arena.StepAsync(1);
			after = await arena.StateAsync();
			sawHitstop  |= after.HitstopActive;
			stayedFinite &= after.AcidFinite;
			minCount = Math.Min(minCount, after.AcidParticleCount);
		}

		// ASSERT — guard (we really exercised the failure path), then the regression itself.
		after.Players[1].MeleeHitsTaken.ShouldBeGreaterThan(hitsBefore, "a melee hit must connect to exercise the regression");
		sawHitstop.ShouldBeTrue("hitstop (TimeScale=0 / dt=0) must fire — the exact path that NaN'd the acid");
		stayedFinite.ShouldBeTrue("acid went non-finite (NaN) after a melee hit");
		after.AcidFinite.ShouldBeTrue();
		minCount.ShouldBeGreaterThanOrEqualTo(before.AcidParticleCount / 2, "the acid pool collapsed after a melee hit");
	}

	// ── Knockback survives hitstun (knockback-cancel regression) ─────────────────

	[SkippableFact]
	public async Task Knockback_SurvivesHitstun_IsNotCanceled()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		await arena.StageMeleeConnectAsync(attacker: 0, target: 1);

		// ACT — land a hit, then give the target NO input and let a few frames pass.
		var atHit = await arena.ThrowAttackUntilHitAsync(attacker: 0, target: 1);
		int knockback = atHit.Players[1].Vx;
		await arena.StepAsync(3);
		var later = await arena.Player(1).StateAsync();

		// ASSERT — knockback must persist; the bug snapped Velocity.X to ~0 the next frame.
		atHit.Players[1].Hitstun.ShouldBeTrue();
		knockback.ShouldBeGreaterThan(100, "expected a knockback velocity to preserve");
		later.Vx.ShouldBeGreaterThanOrEqualTo(knockback / 2, "knockback was canceled instead of preserved through hitstun");
		later.Vx.ShouldBeGreaterThan(80);
	}

	// ── Hitstun locks out input (knockback-independent, via facing) ──────────────

	[SkippableFact]
	public async Task Hitstun_SuppressesInput_UntilItEnds()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE — face the target right (a known facing), then stage the connect (teleport keeps facing).
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		var target = arena.Player(1);
		await target.HoldRightAsync();
		await arena.StepAsync(2);
		await target.StopAsync();
		await arena.StageMeleeConnectAsync(attacker: 0, target: 1);

		// ACT — hit the target, then command LEFT for one stunned frame, then again after hitstun ends.
		var atHit = await arena.ThrowAttackUntilHitAsync(attacker: 0, target: 1);

		await target.HoldLeftAsync();
		await arena.StepAsync(1);
		var during = await target.StateAsync();

		await arena.StepUntilAsync(s => !s.Players[1].Hitstun, maxFrames: 60, batch: 1);
		await arena.StepAsync(3);
		var afterHitstun = await target.StateAsync();
		await target.StopAsync();

		// ASSERT
		atHit.Players[1].Facing.ShouldBe(1, "target should still face right at the moment of impact");
		during.Hitstun.ShouldBeTrue("expected to sample while still in hitstun");
		during.Facing.ShouldBe(1, "input is suppressed during hitstun: facing must not flip");
		afterHitstun.Facing.ShouldBe(-1, "input is restored after hitstun: the held-left input flips facing");
	}

	// ── Death → respawn ──────────────────────────────────────────────────────────

	[SkippableFact]
	public async Task Player_Dies_AndRespawnsAtFullHp()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		await arena.SettleAsync();
		int maxHp = (await arena.Player(0).StateAsync()).MaxHp;

		// ACT — kill via the setup helper (acid-independent), then run out the respawn delay.
		await arena.DamageAsync(player: 0, amount: 9999);
		var dead = await arena.StepUntilAsync(s => s.Players[0].Dead, maxFrames: 10, batch: 1);
		var respawned = await arena.StepUntilAsync(s => !s.Players[0].Dead, maxFrames: 300, batch: 1);

		// ASSERT
		dead.Players[0].Hp.ShouldBe(0);
		respawned.Players[0].Dead.ShouldBeFalse("the player should respawn after the delay");
		respawned.Players[0].Hp.ShouldBe(maxHp, "respawn should restore full HP");
	}

	// ── Acid damage-over-time (rising-hazard contract) ───────────────────────────

	[SkippableFact]
	public async Task Acid_DamagesPlayer_OnlyAfterItRisesToThem()
	{
		Skip.IfNot(ArenaPage.IsEnabled, $"Set {ArenaPage.EnableEnvVar}=1 to run E2E tests.");

		// ARRANGE — at activation the surface is below the floor (acidLevel > player.Y), so the
		// player is clear and undamaged. (Larger Y is lower on screen.)
		await using var arena = await ArenaPage.LaunchAsync();
		await arena.EnterSteppedModeAsync();
		var onset = await arena.StepUntilAsync(s => s.AcidActive, maxFrames: 1200, batch: 5);

		// ACT — let the surface rise until it submerges the player, then keep stepping for damage.
		var submerged = await arena.StepUntilAsync(s => s.AcidLevel < s.Players[0].Y, maxFrames: 3000, batch: 10);
		int hpWhenSubmerged = submerged.Players[0].Hp;
		var later = await arena.StepUntilAsync(s => s.Players[0].Hp < hpWhenSubmerged, maxFrames: 240, batch: 2);

		// ASSERT
		onset.AcidLevel.ShouldBeGreaterThan(onset.Players[0].Y, "acid should activate below the player (clear)");
		onset.Players[0].Hp.ShouldBe(onset.Players[0].MaxHp, "a player clear of the acid takes no damage");
		later.Players[0].Hp.ShouldBeLessThan(hpWhenSubmerged, "a submerged player should take acid damage over time");
	}
}
