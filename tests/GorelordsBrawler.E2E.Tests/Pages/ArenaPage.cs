using System;
using System.Threading.Tasks;

namespace GorelordsBrawler.E2E.Tests.Pages;

/// <summary>
/// Page Object Model for the Arena screen — the single "page" these gameplay E2E tests operate on.
///
/// It wraps the low-level <see cref="GameDriver"/> (the process + HTTP transport, our "WebDriver"
/// analogue) and re-expresses the arena in domain language: enter stepped mode, settle on the
/// ground, drive a player, stage a melee connect, read state. Tests talk ONLY to this page (and the
/// <see cref="PlayerObject"/>s it hands out), never to the raw driver — so a test reads as intent,
/// and any change to the transport (endpoints, readiness, launch) is absorbed here in one place.
///
/// Owns the game-process lifetime: <c>await using var arena = await ArenaPage.LaunchAsync();</c>.
/// </summary>
public sealed class ArenaPage : IAsyncDisposable
{
	private readonly GameDriver _driver;

	private ArenaPage(GameDriver driver) => _driver = driver;

	/// <summary>The E2E gate (E2E_TESTS=1), surfaced here so tests need not reference the driver.</summary>
	public static bool IsEnabled => GameDriver.IsEnabled;
	public static string EnableEnvVar => GameDriver.EnableEnvVar;

	/// <summary>
	/// Launch the game and wait until the arena is live and both players are grounded (the driver's
	/// readiness wait). The returned page is in free-run mode — call
	/// <see cref="EnterSteppedModeAsync"/> to take deterministic control.
	/// </summary>
	public static async Task<ArenaPage> LaunchAsync() => new ArenaPage(await GameDriver.StartAsync());

	// ── Time / run mode ─────────────────────────────────────────────────────────

	/// <summary>Switch to deterministic frame-stepping; nothing advances until <see cref="StepAsync"/>.</summary>
	public Task EnterSteppedModeAsync() => _driver.RunAsync("stepped");

	/// <summary>Advance exactly <paramref name="frames"/> fixed-dt frames.</summary>
	public Task StepAsync(int frames = 1) => _driver.StepAsync(frames);

	/// <summary>Step (in batches) until <paramref name="until"/> holds; returns that snapshot.</summary>
	public Task<GameStateSnapshot> StepUntilAsync(
		Func<GameStateSnapshot, bool> until, int maxFrames = 600, int batch = 5)
		=> _driver.StepUntilAsync(until, maxFrames, batch);

	/// <summary>Step until both players are standing on the ground.</summary>
	public Task<GameStateSnapshot> SettleAsync(int maxFrames = 120)
		=> _driver.StepUntilAsync(
			s => s.Players.Count >= 2 && s.Players.TrueForAll(p => p.Grounded), maxFrames, batch: 5);

	/// <summary>A fresh full-arena snapshot (both players + acid), coherent within one frame.</summary>
	public Task<GameStateSnapshot> StateAsync() => _driver.GetStateAsync();

	// ── Players ───────────────────────────────────────────────────────────────--
	public PlayerObject Player(int index) => new PlayerObject(_driver, index);

	// ── Setup helpers (bypass input to stage a scenario) ─────────────────────────
	public Task TeleportAsync(int player, float x, float y) => _driver.TeleportAsync(player, x, y);
	public Task DamageAsync(int player, int amount) => _driver.DamageAsync(player, amount);

	// ── Compound domain actions ──────────────────────────────────────────────────

	/// <summary>
	/// Stage a guaranteed melee connect: turn the attacker to face right (facing is input-driven),
	/// then place the target a fixed gap to the attacker's right with both at zero velocity. Teleport
	/// preserves facing, so the attacker stays aimed at the target.
	/// </summary>
	public async Task StageMeleeConnectAsync(int attacker, int target, int gap = 36)
	{
		var atk = Player(attacker);
		await atk.HoldRightAsync();
		await StepAsync(2);
		await atk.StopAsync();
		await StepAsync(1);

		var state = await StateAsync();
		var a = state.Players[attacker];
		await TeleportAsync(target, a.X + gap, a.Y);
		await TeleportAsync(attacker, a.X, a.Y);
		await StepAsync(1);
	}

	/// <summary>
	/// Throw one attack from <paramref name="attacker"/> and step one frame at a time until
	/// <paramref name="target"/> registers a new melee hit. Returns the snapshot at the connecting
	/// frame (so hitstun/knockback are observed at their peak). Throws on no connection.
	/// </summary>
	public async Task<GameStateSnapshot> ThrowAttackUntilHitAsync(int attacker, int target, int maxFrames = 30)
	{
		int hitsBefore = (await StateAsync()).Players[target].MeleeHitsTaken;

		var atk = Player(attacker);
		await atk.PressAttackAsync();
		await StepAsync(1);
		await atk.ReleaseAttackAsync();

		return await StepUntilAsync(
			s => s.Players[target].MeleeHitsTaken > hitsBefore, maxFrames, batch: 1);
	}

	public ValueTask DisposeAsync() => _driver.DisposeAsync();
}
