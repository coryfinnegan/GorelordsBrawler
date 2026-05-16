using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler
{
	public class GorelordsBrawlerGame : Core
	{
		public static GorelordsBrawlerGame GameReference { get; private set; }
		private GameTime _gameTime;

		public GorelordsBrawlerGame()
		{
			IsMouseVisible = true;
			GameReference = this;
		}

		protected override void Initialize()
		{
			Window.AllowUserResizing = true;
			_gameTime = new GameTime();
			base.Initialize();
			base.Update(_gameTime);
			base.Draw(_gameTime);

			ExitOnEscapeKeypress = false;
			Nez.Input.MaxSupportedGamePads = Constants.GameConstants.Input.MaxGamePads;
			Scene.SetDefaultDesignResolution(
				Constants.GameConstants.Screen.DesignWidth,
				Constants.GameConstants.Screen.DesignHeight,
				Scene.SceneResolutionPolicy.ShowAll);

			RegisterGlobalManager(new MatchSetupManager());

			AppSettings.Load();
			SettingsManager.Initialize();
			SettingsManager.Apply();

#if DEBUG
			DevTools.DebugCommands.RegisterKeybindings();
			if (AppSettings.DebugServer)
				DevTools.GameDebugServer.Start();
#endif

#if DEBUG
			if (AppSettings.DebugDirectArena)
			{
				var setup = GetGlobalManager<Systems.MatchSetupManager>();
				setup.Selections.Add(new Systems.PlayerSelection
					{ SlotIndex = 0, Device = Systems.InputDeviceType.KeyboardWASD,   CharacterType = Constants.GameConstants.Characters.FutureAxe });
				setup.Selections.Add(new Systems.PlayerSelection
					{ SlotIndex = 1, Device = Systems.InputDeviceType.KeyboardArrows, CharacterType = Constants.GameConstants.Characters.FutureAxe });
				Scene = new Scenes.ArenaScene();
				return;
			}
#endif
			Scene = new Scenes.MainMenuScene();
		}

		protected override void Draw(GameTime gameTime)
		{
			base.Draw(gameTime);
#if DEBUG
			if (AppSettings.DebugServer && DevTools.GameDebugServer.HasPendingScreenshot)
				CaptureScreenshot();
#endif
		}

#if DEBUG
		private void CaptureScreenshot()
		{
			var gd = GraphicsDevice;
			int w = gd.PresentationParameters.BackBufferWidth;
			int h = gd.PresentationParameters.BackBufferHeight;

			var colors = new Color[w * h];
			gd.GetBackBufferData(colors);

			using var tex = new Texture2D(gd, w, h);
			tex.SetData(colors);
			using var ms = new MemoryStream();
			// JPEG is ~5× faster to encode than PNG on the GPU thread and yields
			// 5–10× smaller files — keeps the smoke-test recording loop's HTTP
			// polling rate high enough for a smooth ~30 fps gameplay clip.
			tex.SaveAsJpeg(ms, w, h);
			DevTools.GameDebugServer.CompleteScreenshot(ms.ToArray());
		}
#endif

		public static void TransitionToScene<T>() where T : Scene, new()
		{
			StartSceneTransition(new FadeTransition(() => new T()));
		}
	}
}
