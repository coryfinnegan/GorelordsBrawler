using System.Threading.Tasks;
using Xunit;

namespace GorelordsBrawler.E2E.Tests;

/// <summary>
/// E2E tests that drive REAL gameplay through the scripted input device + deterministic
/// frame-stepping (see docs/e2e-gameplay-automation-proposal.md). Scripted input flows
/// through the same VirtualButton/VirtualIntegerAxis pipeline as the keyboard, so these
/// exercise the actual input → ability → physics/combat path.
///
/// Enable with E2E_TESTS=1. StartAsync launches the game with DebugAutomation=true, so both
/// players use the scripted device and the loop honours /run + /step.
/// </summary>
[Trait("Category", "E2E")]
public class GameplayAutomationE2ETests
{
	[SkippableFact]
	public async Task ScriptedInput_MovesPlayerRight()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		await game.RunAsync("stepped");

		// Let players fall to the ground and settle so we measure horizontal travel cleanly.
		await game.StepAsync(20);
		var before = await game.GetStateAsync();
		int startX = before.Players[0].X;

		// Hold right for a stretch of frames, then release.
		await game.SetInputAsync(0, moveX: 1);
		await game.StepAsync(15);
		await game.SetInputAsync(0, moveX: 0);

		var after = await game.GetStateAsync();
		Assert.True(after.Players[0].X > startX,
			$"Player 0 should move right under scripted input: {startX} → {after.Players[0].X}.");
	}

	/// <summary>
	/// Regression for the "acid vanishes + FPS tanks when you hit a player" bug.
	///
	/// Root cause: a melee hit fires CombatEffectsManager hitstop, which sets
	/// Time.TimeScale = 0 for a few frames. The acid PBF sim read the SCALED dt (→ 0) and did
	/// 1/dt, NaN-ing every particle; NaN positions then collapse into one spatial-hash cell
	/// (O(n²)) — the permanent FPS cliff. Fixed by a dt &lt;= 0 early-return in
	/// FluidSimulation.Step.
	///
	/// Because hitstop sets TimeScale GLOBALLY, the acid breaks no matter WHERE the hit
	/// happens — the players only need to connect a melee hit while acid particles exist. We
	/// stage that deterministically: accumulate acid, overlap the two players, face P0 at P1,
	/// throw one attack, and step through the hitstop window. Then assert the acid is still
	/// finite and didn't mass-despawn.
	/// </summary>
	[SkippableFact]
	public async Task MeleeHit_WhileAcidPresent_DoesNotBreakTheFluid()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();

		// Free-run until the acid activates (~3s with DebugFastAcid), then take control.
		await game.WaitForAsync(s => s.AcidActive, timeoutMs: 15_000);
		await game.RunAsync("stepped");

		// Accumulate a healthy pool of particles so "didn't vanish" is a meaningful check.
		var before = await game.StepUntilAsync(s => s.AcidParticleCount > 150, maxFrames: 1200);
		Assert.True(before.AcidFinite, "Acid should be finite before the hit.");
		Assert.True(before.AcidParticleCount > 150, "Expected a populated acid pool before the hit.");

		// Stage a guaranteed melee connect: drop P1 onto P0 (slightly to the right), turn P0
		// to face right, then throw a single attack.
		var p0 = before.Players[0];
		await game.TeleportAsync(player: 1, x: p0.X + 4, y: p0.Y);
		await game.SetInputAsync(0, moveX: 1);   // face right toward P1
		await game.StepAsync(2);
		await game.SetInputAsync(0, moveX: 0);

		int p1HpBefore = before.Players[1].Hp;

		await game.SetInputAsync(0, attack: true);
		await game.StepAsync(1);
		await game.SetInputAsync(0, attack: false);

		// Step through the active-hitbox frames + the ~4-frame hitstop (where TimeScale=0 fed
		// the acid sim dt=0) + recovery.
		await game.StepAsync(30);

		var after = await game.GetStateAsync();

		// Confirm a hit actually landed (damage path + hitstop ran) — otherwise the acid
		// invariants below would pass trivially without ever exercising the bug.
		Assert.True(after.Players[1].Hp < p1HpBefore,
			$"Expected P1 to take melee damage (hit must connect to trigger hitstop): {p1HpBefore} → {after.Players[1].Hp}.");

		// The actual regression assertions:
		Assert.True(after.AcidFinite,
			"Acid went NaN after a melee hit — the hitstop dt=0 path broke the fluid sim.");
		Assert.True(after.AcidParticleCount >= before.AcidParticleCount / 2,
			$"Acid pool vanished/respawned after a melee hit: {before.AcidParticleCount} → {after.AcidParticleCount}.");
	}
}
