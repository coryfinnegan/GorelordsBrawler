using System;
using System.Threading.Tasks;
using Xunit;

namespace GorelordsBrawler.E2E.Tests;

/// <summary>
/// E2E tests that drive REAL gameplay through the scripted input device + deterministic
/// frame-stepping (see docs/e2e-gameplay-automation-hardening-proposal.md). Scripted input flows
/// through the same VirtualButton / VirtualIntegerAxis pipeline as the keyboard, so these exercise
/// the actual input → ability → physics/combat path — not a mock.
///
/// Enable with E2E_TESTS=1. <see cref="GameDriver.StartAsync"/> launches the game with
/// DebugAutomation=true (both players on the scripted device) and honours /run + /step.
///
/// DESIGN NOTE — trustworthy oracles. Every "did the event happen?" assertion keys off a state
/// change that ONLY that event can produce, never an inference that the environment can fake:
///   • a melee connection → <c>meleeHitsTaken</c> increments (acid damage-over-time can't touch it),
///   • a hit freeze       → <c>hitstopActive</c> (the exact TimeScale=0 / dt=0 path that NaN'd acid),
///   • input suppression  → <c>facing</c> (written only by WalkAbility's normal movement branch),
///   • death              → <c>dead</c>.
/// HP deltas are deliberately avoided as hit-detection because fast-acid drains BOTH players every
/// frame — an HP-based oracle can go green with the punch missing entirely.
/// </summary>
[Trait("Category", "E2E")]
public class GameplayAutomationE2ETests
{
	// Enough fixed-dt frames for a freshly-spawned player to fall and settle on the ground.
	private const int SettleFrames = 30;
	// Horizontal gap (px) at which a jab reliably connects — verified against the live build.
	private const int MeleeGap = 36;

	// ── Movement ────────────────────────────────────────────────────────────────

	[SkippableFact]
	public async Task ScriptedInput_MovesPlayer_LeftAndRight()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		await game.RunAsync("stepped");
		await game.StepAsync(SettleFrames);

		int startX = (await game.GetStateAsync()).Players[0].X;

		await game.SetInputAsync(0, moveX: 1);
		await game.StepAsync(15);
		var right = await game.GetStateAsync();
		await game.SetInputAsync(0, moveX: 0);

		Assert.True(right.Players[0].X > startX && right.Players[0].Vx > 0,
			$"Right input should move P0 right: x {startX}->{right.Players[0].X}, vx {right.Players[0].Vx}.");

		await game.SetInputAsync(0, moveX: -1);
		await game.StepAsync(15);
		var left = await game.GetStateAsync();
		await game.SetInputAsync(0, moveX: 0);

		Assert.True(left.Players[0].Vx < 0,
			$"Left input should drive P0 left: vx {left.Players[0].Vx}.");
	}

	// ── Jump ────────────────────────────────────────────────────────────────────

	[SkippableFact]
	public async Task ScriptedInput_Jump_LeavesGroundThenLands()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		await game.RunAsync("stepped");
		await game.StepUntilAsync(s => s.Players[0].Grounded, maxFrames: 120);

		await game.SetInputAsync(0, jump: true);
		await game.StepAsync(2);
		await game.SetInputAsync(0, jump: false);

		// Up is negative Y (gravity increases Y). A jump must produce upward velocity.
		var airborne = await game.StepUntilAsync(s => s.Players[0].Vy < 0, maxFrames: 10, batch: 1);
		Assert.True(airborne.Players[0].Vy < 0,
			$"Jump should give P0 upward velocity (vy<0): vy {airborne.Players[0].Vy}.");

		var landed = await game.StepUntilAsync(s => s.Players[0].Grounded, maxFrames: 300, batch: 5);
		Assert.True(landed.Players[0].Grounded, "P0 should return to the ground after jumping.");
	}

	// ── Melee connects (player-vs-player core) ───────────────────────────────────

	[SkippableFact]
	public async Task MeleeHit_Connects_DealsDamage_AndKnocksBack()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		await game.RunAsync("stepped");
		await game.StepAsync(SettleFrames);   // act before acid activates — clean, no DoT contamination

		await StageMeleeConnectAsync(game, attacker: 0, target: 1);
		var before = await game.GetStateAsync();
		int hitsBefore = before.Players[1].MeleeHitsTaken;
		int hpBefore   = before.Players[1].Hp;

		var atHit = await ThrowAttackUntilHitAsync(game, attacker: 0, target: 1, hitsBefore);

		Assert.Equal(hitsBefore + 1, atHit.Players[1].MeleeHitsTaken);          // exactly one connection
		Assert.True(atHit.Players[1].Hitstun, "Target should be in hitstun after the hit.");
		Assert.True(atHit.Players[1].Hp < hpBefore,
			$"Target should lose HP from the hit: {hpBefore} -> {atHit.Players[1].Hp}.");
		// Attacker faced right and the target had no input of its own, so a positive vx spike is
		// unambiguously knockback (walk speed alone can't appear on a player issuing no input).
		Assert.True(atHit.Players[1].Vx > 100,
			$"Target should be knocked back (vx spike), got vx {atHit.Players[1].Vx}.");
	}

	// ── Acid survives a melee hit (THE regression that slipped through) ───────────

	/// <summary>
	/// Regression for "acid vanishes + FPS tanks when you hit a player." A melee hit fires
	/// CombatEffectsManager hitstop (Time.TimeScale = 0); the acid PBF sim read the scaled dt (→ 0)
	/// and did 1/dt, NaN-ing every particle — which then collapse into one spatial-hash cell (O(n²))
	/// for a permanent FPS cliff. Fixed by a dt &lt;= 0 early-return in FluidSimulation.Step.
	///
	/// The oracle here is deliberately acid-INDEPENDENT for the guard: we prove a real hit landed
	/// (<c>meleeHitsTaken</c>) AND that it actually drove the game into the dt=0 freeze
	/// (<c>hitstopActive</c>) before trusting the acid-finiteness assertions — otherwise the test
	/// could pass without ever exercising the failure mode.
	/// </summary>
	[SkippableFact]
	public async Task MeleeHit_WhileAcidPresent_DoesNotBreakTheFluid()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		await game.RunAsync("stepped");

		// Deterministically advance until a populated, finite acid pool exists.
		var before = await game.StepUntilAsync(
			s => s.AcidActive && s.AcidParticleCount > 150 && s.AcidFinite,
			maxFrames: 2000, batch: 10);
		Assert.True(before.AcidFinite && before.AcidParticleCount > 150,
			"Expected a populated, finite acid pool before the hit.");

		await StageMeleeConnectAsync(game, attacker: 0, target: 1);
		int hitsBefore = (await game.GetStateAsync()).Players[1].MeleeHitsTaken;

		// Throw one attack, then step the active-hitbox + hitstop window ONE frame at a time so a
		// coarse step can't skip over the (~4-frame) freeze. Latch the evidence as we go.
		await game.SetInputAsync(0, attack: true);
		await game.StepAsync(1);
		await game.SetInputAsync(0, attack: false);

		bool sawHitstop  = false;
		bool stayedFinite = true;
		int  minCount     = int.MaxValue;
		GameStateSnapshot after = before;
		for (int i = 0; i < 40; i++)
		{
			await game.StepAsync(1);
			after        = await game.GetStateAsync();
			sawHitstop  |= after.HitstopActive;
			stayedFinite &= after.AcidFinite;
			minCount     = Math.Min(minCount, after.AcidParticleCount);
		}

		// Guard: we must have actually exercised the failure path.
		Assert.True(after.Players[1].MeleeHitsTaken > hitsBefore,
			"No melee hit connected — the regression path was never exercised.");
		Assert.True(sawHitstop,
			"Hitstop (TimeScale=0 / dt=0) never fired — the regression path was never exercised.");

		// The regression assertions:
		Assert.True(stayedFinite && after.AcidFinite,
			"Acid went NaN after a melee hit — the hitstop dt=0 path broke the fluid sim.");
		Assert.True(minCount >= before.AcidParticleCount / 2,
			$"Acid pool collapsed after a melee hit: {before.AcidParticleCount} -> min {minCount}.");
	}

	// ── Knockback survives hitstun (knockback-cancel regression) ─────────────────

	/// <summary>
	/// Regression for the knockback-cancel bug: WalkAbility wrote Velocity.X every frame, zeroing
	/// the knockback on the frame after impact. The fix has WalkAbility return early while
	/// Hitstun.IsActive, preserving the launch velocity. Here we land a hit, send the target NO
	/// input, and confirm the knockback PERSISTS across the next frames instead of snapping to ~0.
	/// </summary>
	[SkippableFact]
	public async Task Knockback_SurvivesHitstun_IsNotCanceled()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		await game.RunAsync("stepped");
		await game.StepAsync(SettleFrames);

		await StageMeleeConnectAsync(game, attacker: 0, target: 1);
		int hitsBefore = (await game.GetStateAsync()).Players[1].MeleeHitsTaken;

		var atHit = await ThrowAttackUntilHitAsync(game, attacker: 0, target: 1, hitsBefore);
		Assert.True(atHit.Players[1].Hitstun, "Target should be in hitstun.");
		Assert.True(atHit.Players[1].Vx > 100, $"Expected knockback velocity, got {atHit.Players[1].Vx}.");
		int knockback = atHit.Players[1].Vx;

		// Target receives no input. The knockback must be preserved (the bug zeroed it next frame).
		await game.StepAsync(3);
		var later = await game.GetStateAsync();
		Assert.True(later.Players[1].Vx >= knockback / 2 && later.Players[1].Vx > 80,
			$"Knockback was canceled instead of preserved: {knockback} -> {later.Players[1].Vx} in 3 frames.");
	}

	// ── Hitstun locks out input (knockback-independent via facing) ───────────────

	/// <summary>
	/// While in hitstun, a player can't act. We prove it with <c>facing</c>, which WalkAbility writes
	/// ONLY in its normal movement branch (skipped during hitstun) — so it's immune to the knockback
	/// that confounds a velocity-based check. Face the target right, hit it, then command LEFT: the
	/// facing must stay right while stunned and only flip once hitstun ends.
	/// </summary>
	[SkippableFact]
	public async Task Hitstun_SuppressesInput_UntilItEnds()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		await game.RunAsync("stepped");
		await game.StepAsync(SettleFrames);

		// Give the target a known facing (right) BEFORE staging — teleport preserves facing.
		await game.SetInputAsync(1, moveX: 1);
		await game.StepAsync(2);
		await game.SetInputAsync(1, moveX: 0);
		await StageMeleeConnectAsync(game, attacker: 0, target: 1);

		var staged = await game.GetStateAsync();
		Assert.Equal(1, staged.Players[1].Facing);
		int hitsBefore = staged.Players[1].MeleeHitsTaken;

		var atHit = await ThrowAttackUntilHitAsync(game, attacker: 0, target: 1, hitsBefore);
		Assert.True(atHit.Players[1].Hitstun, "Target should be in hitstun right after the hit.");
		Assert.Equal(1, atHit.Players[1].Facing);

		// Command LEFT for a single frame WHILE stunned — input is suppressed, so facing can't flip.
		await game.SetInputAsync(1, moveX: -1);
		await game.StepAsync(1);
		var during = await game.GetStateAsync();
		Assert.True(during.Players[1].Hitstun, "Expected to still be in hitstun for the lockout check.");
		Assert.Equal(1, during.Players[1].Facing);   // input ignored: facing unchanged

		// Once hitstun ends, the SAME held input must take effect (facing flips to left).
		await game.StepUntilAsync(s => !s.Players[1].Hitstun, maxFrames: 60, batch: 1);
		await game.StepAsync(3);
		var after = await game.GetStateAsync();
		await game.SetInputAsync(1, moveX: 0);
		Assert.Equal(-1, after.Players[1].Facing);    // input restored after hitstun
	}

	// ── Death → respawn ──────────────────────────────────────────────────────────

	[SkippableFact]
	public async Task Player_Dies_AndRespawnsAtFullHp()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		await game.RunAsync("stepped");
		await game.StepAsync(SettleFrames);

		var start = await game.GetStateAsync();
		int maxHp = start.Players[0].MaxHp;
		Assert.False(start.Players[0].Dead, "P0 should start alive.");

		// Kill P0 outright via the setup helper — acid-independent, deterministic.
		await game.DamageAsync(0, 9999);
		var dead = await game.StepUntilAsync(s => s.Players[0].Dead, maxFrames: 10, batch: 1);
		Assert.True(dead.Players[0].Dead && dead.Players[0].Hp == 0, "P0 should be dead at 0 HP.");

		// RespawnDelay = 2s = 120 frames; allow generous headroom. batch:1 catches the exact
		// respawn frame before fast-acid can chip the restored HP.
		var respawned = await game.StepUntilAsync(s => !s.Players[0].Dead, maxFrames: 300, batch: 1);
		Assert.False(respawned.Players[0].Dead, "P0 should respawn after the delay.");
		Assert.Equal(maxHp, respawned.Players[0].Hp);   // HP restored to full
	}

	// ── Acid damage-over-time (hazard contract) ──────────────────────────────────

	/// <summary>
	/// Acid is a RISING hazard: both players share one floor, and the surface climbs until it
	/// submerges them. So the clean "clear vs submerged" contrast is across TIME, not across
	/// players — at onset the surface is below the floor (clear, no damage), and once it rises past
	/// the floor the player starts losing HP. acidLevel is the surface Y; larger Y is lower on
	/// screen, so "surface below the player" means acidLevel &gt; player.Y.
	/// </summary>
	[SkippableFact]
	public async Task Acid_DamagesPlayer_OnlyAfterItRisesToThem()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		await game.RunAsync("stepped");

		// At activation the surface is still below the floor — the player is clear and undamaged.
		var onset = await game.StepUntilAsync(s => s.AcidActive, maxFrames: 1200, batch: 5);
		Assert.True(onset.AcidLevel > onset.Players[0].Y,
			"Acid should activate below the players (surface beneath the floor).");
		Assert.Equal(onset.Players[0].MaxHp, onset.Players[0].Hp);   // clear ⇒ no damage yet

		// Let the surface rise until it reaches the player's floor.
		var submerged = await game.StepUntilAsync(
			s => s.AcidLevel < s.Players[0].Y, maxFrames: 3000, batch: 10);
		int hp0 = submerged.Players[0].Hp;

		// Now submerged: HP must drain over time (AcidDamagePerSec).
		var later = await game.StepUntilAsync(s => s.Players[0].Hp < hp0, maxFrames: 240, batch: 2);
		Assert.True(later.Players[0].Hp < hp0,
			$"Submerged player should take acid damage over time: {hp0} -> {later.Players[0].Hp}.");
	}

	// ── Helpers ──────────────────────────────────────────────────────────────────

	/// <summary>
	/// Stage a guaranteed melee connect: face the attacker right (FacingDirection is input-driven),
	/// then teleport the target a fixed gap to the attacker's right with both at zero velocity.
	/// Teleport preserves facing, so the attacker is still aimed at the target.
	/// </summary>
	private static async Task StageMeleeConnectAsync(GameDriver game, int attacker, int target, int gap = MeleeGap)
	{
		await game.SetInputAsync(attacker, moveX: 1);
		await game.StepAsync(2);
		await game.SetInputAsync(attacker, moveX: 0);
		await game.StepAsync(1);

		var s = await game.GetStateAsync();
		var a = s.Players[attacker];
		await game.TeleportAsync(target, a.X + gap, a.Y);
		await game.TeleportAsync(attacker, a.X, a.Y);
		await game.StepAsync(1);
	}

	/// <summary>
	/// Throw one attack from <paramref name="attacker"/> and step one frame at a time until
	/// <paramref name="target"/> registers a new melee hit. Returns the state at the connecting
	/// frame (so hitstun/knockback are observed at their peak). Throws on no connection.
	/// </summary>
	private static async Task<GameStateSnapshot> ThrowAttackUntilHitAsync(
		GameDriver game, int attacker, int target, int hitsBefore, int maxFrames = 30)
	{
		await game.SetInputAsync(attacker, attack: true);
		await game.StepAsync(1);
		await game.SetInputAsync(attacker, attack: false);
		return await game.StepUntilAsync(
			s => s.Players[target].MeleeHitsTaken > hitsBefore, maxFrames: maxFrames, batch: 1);
	}
}
