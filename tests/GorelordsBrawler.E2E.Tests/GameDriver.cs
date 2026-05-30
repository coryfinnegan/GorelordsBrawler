using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GorelordsBrawler.E2E.Tests;

/// <summary>
/// Manages a game process for E2E tests.
///
/// Prerequisites — create appsettings.e2e.json next to the game exe:
///   { "DebugServer": true, "DebugDirectArena": true, "DebugFastAcid": true }
///
/// Guard: call GameDriver.IsAvailable before starting tests.
/// The E2E_TESTS environment variable must be set to "1" to enable E2E tests.
/// This prevents them from running in standard CI unless explicitly opted in.
/// </summary>
public sealed class GameDriver : IDisposable, IAsyncDisposable
{
	public const string EnableEnvVar   = "E2E_TESTS";
	public const string ServerUrl      = "http://localhost:7777";
	public const int    StartupTimeout = 15_000;  // ms to wait for server to come up

	public static bool IsEnabled =>
		string.Equals(Environment.GetEnvironmentVariable(EnableEnvVar), "1", StringComparison.Ordinal);

	private readonly Process    _process;
	private readonly HttpClient _http;

	private GameDriver(Process proc)
	{
		_process = proc;
		_http    = new HttpClient { BaseAddress = new Uri(ServerUrl), Timeout = TimeSpan.FromSeconds(5) };
	}

	/// <summary>Locate and launch the game with E2E appsettings then wait for the debug server.</summary>
	public static async Task<GameDriver> StartAsync()
	{
		string exe = FindGameExe();

		// Write appsettings.json that enables debug server + direct arena + fast acid +
		// automation (both players use the scripted input device, driven over HTTP).
		string settingsPath = Path.Combine(Path.GetDirectoryName(exe)!, "appsettings.json");
		File.WriteAllText(settingsPath, """
			{
			  "DebugServer":      true,
			  "DebugDirectArena": true,
			  "DebugFastAcid":    true,
			  "DebugAutomation":  true
			}
			""");

		var psi = new ProcessStartInfo(exe)
		{
			UseShellExecute  = false,
			WorkingDirectory = Path.GetDirectoryName(exe)!,
		};

		var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start game process.");
		var driver = new GameDriver(proc);

		await driver.WaitForServerAsync();
		return driver;
	}

	/// <summary>Poll /state until the server responds, up to StartupTimeout ms.</summary>
	private async Task WaitForServerAsync()
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(StartupTimeout);
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				using var resp = await _http.GetAsync("/state");
				if (resp.IsSuccessStatusCode) return;
			}
			catch { /* not up yet */ }
			await Task.Delay(200);
		}
		throw new TimeoutException($"Game debug server did not respond within {StartupTimeout}ms.");
	}

	/// <summary>Query the current game state from /state.</summary>
	public async Task<GameStateSnapshot> GetStateAsync()
	{
		var json = await _http.GetStringAsync("/state");
		return JsonSerializer.Deserialize<GameStateSnapshot>(json)
		    ?? throw new InvalidOperationException("Empty state response.");
	}

	/// <summary>Poll state until predicate is true or timeout elapses.</summary>
	public async Task<GameStateSnapshot> WaitForAsync(Func<GameStateSnapshot, bool> condition, int timeoutMs = 60_000)
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		while (DateTime.UtcNow < deadline)
		{
			var state = await GetStateAsync();
			if (condition(state)) return state;
			await Task.Delay(200);
		}
		throw new TimeoutException($"Condition not met within {timeoutMs}ms.");
	}

	// ── Automation write-channel ───────────────────────────────────────────────

	/// <summary>Switch the game's run mode: "free" (real-time) or "stepped" (frame-by-frame).</summary>
	public Task RunAsync(string mode) => PostAsync("/run", new { mode });

	/// <summary>
	/// Set scripted input for a player. Null arguments leave that field unchanged, so you can
	/// toggle a button without disturbing a held movement direction. moveX/moveY are -1/0/1.
	/// </summary>
	public Task SetInputAsync(int player, int? moveX = null, int? moveY = null,
		bool? jump = null, bool? attack = null, bool? special = null)
		=> PostAsync("/input", new { player, moveX, moveY, jump, attack, special });

	/// <summary>Advance exactly <paramref name="frames"/> fixed-dt frames; returns once they've run.</summary>
	public Task StepAsync(int frames = 1) => PostAsync("/step", new { frames });

	/// <summary>Place a player at a world position with zero velocity (scenario setup helper).</summary>
	public Task TeleportAsync(int player, float x, float y) => PostAsync("/teleport", new { player, x, y });

	/// <summary>
	/// Step in batches (default 5 frames) until the predicate holds or maxFrames is reached.
	/// Deterministic alternative to WaitForAsync's wall-clock polling — use in stepped mode.
	/// </summary>
	public async Task<GameStateSnapshot> StepUntilAsync(
		Func<GameStateSnapshot, bool> condition, int maxFrames = 600, int batch = 5)
	{
		var state = await GetStateAsync();
		if (condition(state)) return state;

		int stepped = 0;
		while (stepped < maxFrames)
		{
			await StepAsync(batch);
			stepped += batch;
			state = await GetStateAsync();
			if (condition(state)) return state;
		}
		throw new TimeoutException($"Condition not met within {maxFrames} stepped frames.");
	}

	private async Task PostAsync(string path, object body)
	{
		var json    = JsonSerializer.Serialize(body);
		using var c = new StringContent(json, Encoding.UTF8, "application/json");
		using var r = await _http.PostAsync(path, c);
		r.EnsureSuccessStatusCode();
	}

	public void Dispose()
	{
		try { if (!_process.HasExited) _process.Kill(); } catch { /* swallow */ }
		_process.Dispose();
		_http.Dispose();
	}

	public ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}

	private static string FindGameExe()
	{
		// Walk up from the test bin directory to find the game exe
		string dir = AppContext.BaseDirectory;
		for (int i = 0; i < 8; i++)
		{
			var candidate = Path.Combine(dir, "GorelordsBrawler", "bin", "Debug", "net8.0",
				"GorelordsBrawler.exe");
			if (File.Exists(candidate)) return candidate;

			dir = Path.GetDirectoryName(dir) ?? dir;
		}

		// Fall back to PATH
		string? onPath = FindOnPath("GorelordsBrawler");
		if (onPath != null) return onPath;

		throw new FileNotFoundException(
			"Could not locate GorelordsBrawler.exe. Build the game project first, or set PATH.");
	}

	private static string? FindOnPath(string name)
	{
		foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
		{
			var full = Path.Combine(dir, name + ".exe");
			if (File.Exists(full)) return full;
		}
		return null;
	}
}
