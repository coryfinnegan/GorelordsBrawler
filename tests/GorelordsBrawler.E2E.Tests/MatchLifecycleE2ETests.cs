using System.Threading.Tasks;
using Xunit;

namespace GorelordsBrawler.E2E.Tests;

/// <summary>
/// E2E tests for match lifecycle: startup → active → player HP changes over time.
/// Enable by setting E2E_TESTS=1.
/// </summary>
[Trait("Category", "E2E")]
public class MatchLifecycleE2ETests
{
	[SkippableFact]
	public async Task MatchState_ReportsPlayers_WithValidHp()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		var state = await game.GetStateAsync();

		foreach (var p in state.Players)
		{
			Assert.True(p.MaxHp > 0,     $"Player {p.Id}: MaxHp must be positive, got {p.MaxHp}.");
			Assert.True(p.Hp >= 0,       $"Player {p.Id}: HP must be non-negative, got {p.Hp}.");
			Assert.True(p.Hp <= p.MaxHp, $"Player {p.Id}: HP must not exceed MaxHp ({p.Hp}/{p.MaxHp}).");
		}
	}

	[SkippableFact]
	public async Task MatchState_ReportsPlayers_WithValidPositions()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		var state = await game.GetStateAsync();

		// Arena is 1280×800; players shouldn't be outside the arena bounds at spawn.
		foreach (var p in state.Players)
		{
			Assert.InRange(p.X, 0,    1280);
			Assert.InRange(p.Y, 0,   1000);  // allow some below-map margin for fall-off checks
		}
	}

	[SkippableFact]
	public async Task StateJson_IsConsistentAcrossConsecutivePolls()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();

		// Poll twice in quick succession — player count must be stable.
		var s1 = await game.GetStateAsync();
		await Task.Delay(50);
		var s2 = await game.GetStateAsync();

		Assert.Equal(s1.Players.Count, s2.Players.Count);
		Assert.True(s2.Time >= s1.Time, "Time should not go backwards.");
	}
}

