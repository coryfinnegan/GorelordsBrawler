using System;
using System.IO;
using System.Text.Json;
using Nez;

namespace GorelordsBrawler
{
	public static class AppSettings
	{
		public static bool LedgeHangEnabled { get; private set; } = false;

		/// <summary>Start the HTTP debug server (port 7777). Debug builds only.</summary>
		public static bool DebugServer { get; private set; } = false;

		/// <summary>Collapse AcidStartDelay to 3 s for fast iteration testing.</summary>
		public static bool DebugFastAcid { get; private set; } = false;

		/// <summary>Skip menus and launch directly into ArenaScene with two keyboard players.</summary>
		public static bool DebugDirectArena { get; private set; } = false;

		/// <summary>
		/// E2E automation mode. When combined with DebugDirectArena, both players use the
		/// scripted input device (driven over HTTP) instead of the keyboard, and the loop
		/// disables pause-on-focus-loss so frame-stepping works while the window is in the
		/// background. Debug builds only.
		/// </summary>
		public static bool DebugAutomation { get; private set; } = false;

		// ── Environment-variable injection channel (tests + smoke harness) ──────
		// The harnesses set these on the GAME CHILD PROCESS to turn on debug
		// behaviour WITHOUT writing the shared appsettings.json in the build
		// output. That file is the player's own config (committed with
		// DebugAutomation:false, so a normal run keeps the keyboard); a harness
		// that overwrites it can leave DebugAutomation:true behind and silently
		// swap both players off the keyboard onto scripted HTTP input on the next
		// manual run — the bug this channel fixes. Env vars are scoped to the
		// spawned process: they vanish when it exits and never touch disk. The
		// names are a cross-process contract mirrored by GameDriver.cs
		// (psi.Environment[...]) and smoke_test.ps1 ($psi.Environment); change the
		// three together.
		public const string EnvLedgeHang        = "GLB_LEDGE_HANG_ENABLED";
		public const string EnvDebugServer      = "GLB_DEBUG_SERVER";
		public const string EnvDebugFastAcid    = "GLB_DEBUG_FAST_ACID";
		public const string EnvDebugDirectArena = "GLB_DEBUG_DIRECT_ARENA";
		public const string EnvDebugAutomation  = "GLB_DEBUG_AUTOMATION";

		public static void Load()
		{
			try
			{
				var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
				if (File.Exists(path))
				{
					using var doc = JsonDocument.Parse(File.ReadAllText(path));
					var root = doc.RootElement;

					if (root.TryGetProperty("LedgeHangEnabled", out var val))
						LedgeHangEnabled = val.GetBoolean();
					if (root.TryGetProperty("DebugServer", out var ds))
						DebugServer = ds.GetBoolean();
					if (root.TryGetProperty("DebugFastAcid", out var fa))
						DebugFastAcid = fa.GetBoolean();
					if (root.TryGetProperty("DebugDirectArena", out var da))
						DebugDirectArena = da.GetBoolean();
					if (root.TryGetProperty("DebugAutomation", out var au))
						DebugAutomation = au.GetBoolean();
				}
			}
			catch (Exception e)
			{
				Debug.Warn("appsettings.json load failed: {0}", e.Message);
			}

			// Env-var overlay — precedence over the file so a harness can flip a
			// flag on the child process only. A normal run has none set and keeps
			// the file's defaults (keyboard + the committed dev config).
			LedgeHangEnabled = EnvOverride(EnvLedgeHang,        LedgeHangEnabled);
			DebugServer      = EnvOverride(EnvDebugServer,      DebugServer);
			DebugFastAcid    = EnvOverride(EnvDebugFastAcid,    DebugFastAcid);
			DebugDirectArena = EnvOverride(EnvDebugDirectArena, DebugDirectArena);
			DebugAutomation  = EnvOverride(EnvDebugAutomation,  DebugAutomation);
		}

		/// <summary>Reads env var <paramref name="name"/> and applies <see cref="ParseBoolOverride"/>.</summary>
		private static bool EnvOverride(string name, bool current) =>
			ParseBoolOverride(Environment.GetEnvironmentVariable(name), current);

		/// <summary>
		/// The env-overlay precedence rule, kept pure so it can be unit-tested
		/// without mutating process-global state: a set, recognized boolean
		/// (1/0/true/false, case-insensitive) wins; anything else — unset, blank,
		/// or unrecognized — keeps <paramref name="current"/>. The unset case is
		/// exactly what guarantees a normal run (which injects none of these vars)
		/// keeps the file/default values, i.e. the keyboard default.
		/// </summary>
		public static bool ParseBoolOverride(string raw, bool current)
		{
			if (string.IsNullOrWhiteSpace(raw))
				return current;
			raw = raw.Trim();
			if (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase))
				return true;
			if (raw == "0" || raw.Equals("false", StringComparison.OrdinalIgnoreCase))
				return false;
			return current;
		}
	}
}
