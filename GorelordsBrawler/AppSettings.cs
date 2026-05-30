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

		public static void Load()
		{
			try
			{
				var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
				if (!File.Exists(path))
					return;

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
			catch (Exception e)
			{
				Debug.Warn("appsettings.json load failed: {0}", e.Message);
			}
		}
	}
}
