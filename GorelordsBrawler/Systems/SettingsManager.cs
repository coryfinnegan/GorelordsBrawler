using Nez;
using Nez.Persistence.Binary;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	public static class SettingsManager
	{
		private static FileDataStore _fileStore;
		private static KeyValueDataStore _prefs;

		private const string KeyWidth = "screen_width";
		private const string KeyHeight = "screen_height";
		private const string KeyFullscreen = "fullscreen";
		private const string KeyBorderless = "borderless";
		private const string KeyVSync = "vsync";

		public static int ResolutionWidth => _prefs.GetInt(KeyWidth, GameConstants.Screen.DesignWidth);
		public static int ResolutionHeight => _prefs.GetInt(KeyHeight, GameConstants.Screen.DesignHeight);
		public static bool IsFullscreen => _prefs.GetBool(KeyFullscreen, false);
		public static bool IsBorderless => _prefs.GetBool(KeyBorderless, true);
		public static bool VSync => _prefs.GetBool(KeyVSync, true);

		public static void Initialize()
		{
			_fileStore = new FileDataStore();
			_prefs = KeyValueDataStore.Default;
			_prefs.Load(_fileStore);
		}

		public static void SetResolution(int width, int height)
		{
			_prefs.Set(KeyWidth, width);
			_prefs.Set(KeyHeight, height);
		}

		public static void SetFullscreen(bool fullscreen)
		{
			_prefs.Set(KeyFullscreen, fullscreen);
		}

		public static void SetBorderless(bool borderless)
		{
			_prefs.Set(KeyBorderless, borderless);
		}

		public static void SetVSync(bool vsync)
		{
			_prefs.Set(KeyVSync, vsync);
		}

		public static void Apply()
		{
			Screen.IsFullscreen = IsFullscreen;
			Screen.HardwareModeSwitch = !IsBorderless;
			Screen.SynchronizeWithVerticalRetrace = VSync;
			Screen.SetSize(ResolutionWidth, ResolutionHeight);
		}

		public static void Save()
		{
			if (_prefs.IsDirty)
				_prefs.Flush(_fileStore);
		}
	}
}
