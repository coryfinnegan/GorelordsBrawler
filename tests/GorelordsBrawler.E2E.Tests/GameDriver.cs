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
/// The debug/automation flags are injected as environment variables on the
/// spawned game process (see StartAsync) — never by writing appsettings.json in
/// the build output, which would leak debug mode (and silently kill the keyboard)
/// into the next manual run. The game's AppSettings overlays these env vars on
/// top of its config file.
///
/// Guard: the E2E_TESTS environment variable must be set to "1" to enable E2E
/// tests, so they don't run in standard CI unless explicitly opted in (IsEnabled).
/// </summary>
public sealed class GameDriver : IDisposable, IAsyncDisposable
{
	public const string EnableEnvVar   = "E2E_TESTS";
	public const string ServerUrl      = "http://localhost:7777";
	// A COLD first launch (JIT + first content decode, on a machine busy with the test
	// runner itself) can exceed 15s; 15s was enough to poison a whole run — the launcher
	// gave up while its game was still booting. Healthy launches return the moment the
	// game is ready, so the extra headroom costs nothing.
	public const int    StartupTimeout = 30_000;  // ms to wait for server to come up

	public static bool IsEnabled =>
		string.Equals(Environment.GetEnvironmentVariable(EnableEnvVar), "1", StringComparison.Ordinal);

	private readonly Process    _process;
	private readonly HttpClient _http;

	private GameDriver(Process proc)
	{
		_process = proc;
		// 30s: comfortably above the server's own 10s /step ceiling (which returns 408 on a hung
		// game), so a large step batch never trips a premature client-side timeout — but a truly
		// stuck game still surfaces as a 408 → EnsureSuccessStatusCode throw, not a silent hang.
		_http    = new HttpClient { BaseAddress = new Uri(ServerUrl), Timeout = TimeSpan.FromSeconds(30) };
	}

	/// <summary>Locate and launch the game with E2E appsettings then wait for the debug server.</summary>
	public static async Task<GameDriver> StartAsync()
	{
		// The port must be OURS before we launch: a stale game left by a previous run
		// (or a launch this sweep failed to prevent) serves /state with time >= 5s, and
		// the freshness gate below then correctly rejects everything — a whole-suite
		// cascade. Ask any squatter to quit rather than timing out test after test.
		await EvictPortSquatterAsync();

		var (fileName, arguments, workingDir) = FindGameLauncher();

		var psi = new ProcessStartInfo(fileName)
		{
			Arguments        = arguments,
			UseShellExecute  = false,
			WorkingDirectory = workingDir,
		};

		// Inject the automation config as environment variables scoped to THIS
		// child process — deliberately NOT by writing appsettings.json in the
		// build output. That file is the player's own config (defaults to the
		// keyboard); an earlier design overwrote it here, and because
		// File.WriteAllText stamps it newer than the source, MSBuild's
		// PreserveNewest copy then skipped restoring it — so DebugAutomation:true
		// leaked into the next manual `dotnet run` and silently swapped both
		// players off the keyboard onto scripted HTTP input. Env vars die with
		// the process, so nothing persists. DebugAutomation is the load-bearing
		// flag: without it both players get keyboard devices and POST /input is
		// ignored. These names mirror AppSettings.Env* (a cross-process contract,
		// like the HTTP routes below).
		psi.Environment["GLB_DEBUG_SERVER"]       = "1";
		psi.Environment["GLB_DEBUG_DIRECT_ARENA"] = "1";
		psi.Environment["GLB_DEBUG_FAST_ACID"]    = "1";
		psi.Environment["GLB_DEBUG_AUTOMATION"]   = "1";

		var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start game process.");
		var driver = new GameDriver(proc);

		try
		{
			await driver.WaitForReadyAsync();
		}
		catch
		{
			// Never leak the spawned game on a failed launch. The one leak that slipped
			// through here kept serving /state on :7777 and every later launch in the
			// suite timed out against it (18 straight failures) — the process must die
			// with the exception.
			driver.Dispose();
			throw;
		}
		return driver;
	}

	/// <summary>
	/// If something already answers on the port, it is a leftover automation game (the
	/// harness serializes classes, so no legitimate sibling exists) — POST /quit and wait
	/// for the port to fall silent. Throws with a clear message if the squatter won't die,
	/// e.g. a manually-started game with the debug server on: better one honest failure
	/// than a suite of opaque timeouts.
	/// </summary>
	private static async Task EvictPortSquatterAsync()
	{
		using var http = new HttpClient
		{
			BaseAddress = new Uri(ServerUrl),
			Timeout     = TimeSpan.FromSeconds(2),
		};

		try
		{
			await http.GetStringAsync("/state");
		}
		catch
		{
			return; // Nothing listening — the normal case.
		}

		try
		{
			using var empty = new StringContent("{}", Encoding.UTF8, "application/json");
			using var r     = await http.PostAsync("/quit", empty);
		}
		catch { /* it may die mid-response; the poll below is the arbiter */ }

		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				await http.GetStringAsync("/state");
			}
			catch
			{
				return; // Port is silent — evicted.
			}
			await Task.Delay(100);
		}
		throw new InvalidOperationException(
			$"A game is already serving {ServerUrl} and did not honor POST /quit. " +
			"Close the running GorelordsBrawler instance and re-run the E2E suite.");
	}

	/// <summary>
	/// Wait until the game is actually INTERACTIVE, not merely until the HTTP server answers.
	///
	/// The debug server starts inside Initialize() — BEFORE <c>Scene = new ArenaScene()</c> — and
	/// Nez promotes/begins the scene on the next Update. So "the server responds" can be true while
	/// /state still reports zero players. If a test connects in that window and switches to stepped
	/// mode before the scene has begun, the scene initializes under stepping and scripted input is
	/// never processed — the cold-start race that made earlier runs flaky. Waiting for live, grounded
	/// players guarantees every test starts from the same warm, settled state a human would see.
	/// </summary>
	private async Task WaitForReadyAsync()
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(StartupTimeout);
		while (DateTime.UtcNow < deadline)
		{
			if (_process.HasExited)
			{
				throw new InvalidOperationException(
					$"Game process exited during startup (exit code {_process.ExitCode}) — " +
					"a boot crash, not a slow start.");
			}
			try
			{
				var state = await GetStateAsync();
				// time < 5 s proves the /state answering on :7777 is OUR fresh
				// boot: a LINGERING previous test's game also serves grounded
				// players instantly, and a driver that accepts it steers a
				// battle-worn match (players pre-damaged, phase mid-storm) —
				// the "hp wasn't full at onset" suite-order flake.
				if (state.Time < 5f
					&& state.Players.Count >= 2
					&& state.Players.TrueForAll(p => p.Grounded))
				{
					return;
				}
			}
			catch { /* server not up yet, or state not yet serializable */ }
			await Task.Delay(100);
		}
		throw new TimeoutException(
			$"Game did not reach a live, grounded, FRESH state (time < 5s) within {StartupTimeout}ms.");
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

	/// <summary>Apply raw damage to a player (scenario setup helper — e.g. stage a death).</summary>
	public Task DamageAsync(int player, int amount) => PostAsync("/damage", new { player, amount });

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
		try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { /* swallow */ }
		_process.Dispose();
		_http.Dispose();
	}

	public ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}

	/// <summary>
	/// Locate the built game and decide how to launch it.
	///
	/// A <c>dotnet build GorelordsBrawler.slnx</c> emits NO standalone apphost: the test projects
	/// reference the game with <c>UseAppHost=false</c> (so a test build never clobbers a running
	/// game exe), and that is the only build of the game the solution performs. So the normal path
	/// is to launch via <c>dotnet GorelordsBrawler.dll</c>. If a standalone exe IS present (e.g.
	/// from a <c>dotnet publish</c>), we prefer it.
	/// </summary>
	private static (string fileName, string arguments, string workingDir) FindGameLauncher()
	{
		string dll = FindGameArtifact("GorelordsBrawler.dll")
			?? throw new FileNotFoundException(
				"Could not locate GorelordsBrawler.dll. Build the solution first: " +
				"dotnet build GorelordsBrawler.slnx");

		string workingDir = Path.GetDirectoryName(dll)!;
		string exe        = Path.ChangeExtension(dll, ".exe");

		// Both content and appsettings.json resolve from AppContext.BaseDirectory (the artifact
		// dir), so WorkingDirectory only needs to point there for either launch form.
		return File.Exists(exe)
			? (exe, "", workingDir)
			: ("dotnet", $"\"{dll}\"", workingDir);
	}

	/// <summary>Walk up from the test bin dir into the game's build output to find an artifact.</summary>
	private static string? FindGameArtifact(string fileName)
	{
		string dir = AppContext.BaseDirectory;
		for (int i = 0; i < 8; i++)
		{
			var candidate = Path.Combine(dir, "GorelordsBrawler", "bin", "Debug", "net8.0", fileName);
			if (File.Exists(candidate)) return candidate;

			dir = Path.GetDirectoryName(dir) ?? dir;
		}
		return null;
	}
}
