using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace GorelordsBrawler.E2E.Tests;

/// <summary>
/// E2E tests for the acid hazard lifecycle.
///
/// Enable by setting E2E_TESTS=1 and running with DebugFastAcid + DebugDirectArena.
/// With DebugFastAcid=true, the acid start delay collapses to ~3s and rise speed is
/// multiplied by AcidDebugRiseMultiplier (4×), making the full lifecycle observable in ~30s.
/// </summary>
[Trait("Category", "E2E")]
public class AcidHazardE2ETests
{
	[SkippableFact]
	public async Task Acid_IsInactive_AtGameStart()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		var state = await game.GetStateAsync();

		Assert.False(state.AcidActive, "Acid should be inactive at game start (before delay).");
	}

	[SkippableFact]
	public async Task Acid_BecomesActive_AfterStartDelay()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();

		// With DebugFastAcid, start delay ≈ 3s. Wait up to 15s for acid to activate.
		var state = await game.WaitForAsync(s => s.AcidActive, timeoutMs: 15_000);

		Assert.True(state.AcidActive, "Acid should have activated.");
		Assert.True(state.AcidSpeed > 0f, "Acid speed should be positive while rising.");
	}

	[SkippableFact]
	public async Task AcidLevel_DecreasesWhileRising()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		await game.WaitForAsync(s => s.AcidActive, timeoutMs: 15_000);

		var s1 = await game.GetStateAsync();
		await Task.Delay(2_000);  // let acid rise for 2s
		var s2 = await game.GetStateAsync();

		Assert.True(s2.AcidLevel < s1.AcidLevel,
			$"Acid level should decrease (rise) over time: {s1.AcidLevel} → {s2.AcidLevel}.");
	}

	[SkippableFact]
	public async Task Players_TakeDamage_WhenStandingInAcid()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		await game.WaitForAsync(s => s.AcidActive, timeoutMs: 15_000);

		// Wait until acid rises enough that at least one player's Y is below acid level.
		// Players standing in acid will lose HP.
		var stateWithDamage = await game.WaitForAsync(
			s => s.Players.Any(p => p.Hp < p.MaxHp),
			timeoutMs: 45_000);

		Assert.True(stateWithDamage.Players.Any(p => p.Hp < p.MaxHp),
			"At least one player should have taken damage from the acid.");
	}
}

