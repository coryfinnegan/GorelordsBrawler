using System.Threading.Tasks;
using Xunit;

namespace GorelordsBrawler.E2E.Tests;

/// <summary>
/// E2E tests that verify the game process starts, initializes, and exposes state.
///
/// Enable by setting the E2E_TESTS=1 environment variable and building the game first.
/// These tests will be skipped in standard CI to avoid requiring a running game process.
/// </summary>
[Trait("Category", "E2E")]
public class GameStartupTests
{
	[SkippableFact]
	public async Task GameProcess_StartsAndServesState()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		var state = await game.GetStateAsync();

		Assert.NotNull(state);
		Assert.True(state.Time >= 0f, "Game time should be non-negative.");
	}

	[SkippableFact]
	public async Task GameProcess_HasTwoPlayers_InDebugDirectArena()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		var state = await game.GetStateAsync();

		Assert.True(state.Players.Count >= 2,
			$"DebugDirectArena mode should spawn at least 2 players; got {state.Players.Count}.");
	}

	[SkippableFact]
	public async Task Players_StartWithFullHp()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		var state = await game.GetStateAsync();

		foreach (var p in state.Players)
		{
			Assert.True(p.Hp == p.MaxHp,
				$"Player {p.Id} should start at full HP ({p.MaxHp}), got {p.Hp}.");
		}
	}

	[SkippableFact]
	public async Task GameTime_IsAdvancing()
	{
		Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");

		await using var game = await GameDriver.StartAsync();
		float t1 = (await game.GetStateAsync()).Time;
		await Task.Delay(500);
		float t2 = (await game.GetStateAsync()).Time;

		Assert.True(t2 > t1, $"Game time should advance: {t1:F2} → {t2:F2}");
	}

}
