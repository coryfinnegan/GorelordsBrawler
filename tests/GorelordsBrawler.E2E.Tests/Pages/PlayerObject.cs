using System.Threading.Tasks;

namespace GorelordsBrawler.E2E.Tests.Pages;

/// <summary>
/// Page Object Model "element" for a single player in the arena. Exposes that player's INPUT in
/// domain language (hold a direction, jump, attack) and a query for its current state. Issued by
/// <see cref="ArenaPage.Player(int)"/>; tests never construct it directly and never touch the
/// underlying <see cref="GameDriver"/> transport.
///
/// All input verbs are momentary writes to the scripted-input device — they set state and return;
/// the simulation only advances when the test calls <see cref="ArenaPage.StepAsync"/>. So the
/// pattern is "set input → step → read", mirroring how a human holds a key across frames.
/// </summary>
public sealed class PlayerObject
{
	private readonly GameDriver _driver;

	public int Index { get; }

	internal PlayerObject(GameDriver driver, int index)
	{
		_driver = driver;
		Index = index;
	}

	// ── Movement ──────────────────────────────────────────────────────────────
	public Task HoldRightAsync() => _driver.SetInputAsync(Index, moveX: 1);
	public Task HoldLeftAsync()  => _driver.SetInputAsync(Index, moveX: -1);
	public Task StopAsync()      => _driver.SetInputAsync(Index, moveX: 0);

	// ── Jump (press one frame, release the next — the buffer does the rest) ─────
	public Task PressJumpAsync()   => _driver.SetInputAsync(Index, jump: true);
	public Task ReleaseJumpAsync() => _driver.SetInputAsync(Index, jump: false);

	// ── Attack ──────────────────────────────────────────────────────────────────
	public Task PressAttackAsync()   => _driver.SetInputAsync(Index, attack: true);
	public Task ReleaseAttackAsync() => _driver.SetInputAsync(Index, attack: false);

	/// <summary>This player's slice of a fresh /state snapshot.</summary>
	public async Task<PlayerSnapshot> StateAsync()
	{
		var state = await _driver.GetStateAsync();
		return state.Players[Index];
	}
}
