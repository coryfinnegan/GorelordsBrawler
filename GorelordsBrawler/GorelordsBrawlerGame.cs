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

#if DEBUG
		// Fixed timestep used while frame-stepping (RunMode.Stepped). Core.Update derives
		// Nez Time.DeltaTime from gameTime.ElapsedGameTime, so feeding a constant elapsed
		// makes each stepped frame advance a deterministic dt independent of wall-clock —
		// the determinism lever the E2E harness relies on. 1/60 s matches the normal cadence.
		private static readonly GameTime _fixedStepGameTime =
			new GameTime(System.TimeSpan.Zero, System.TimeSpan.FromSeconds(1.0 / 60.0));
#endif

		protected override void Update(GameTime gameTime)
		{
#if DEBUG
			if (AppSettings.DebugServer)
			{
				// Apply queued automation commands (scripted input, teleport, run-mode) on the
				// game thread before anything advances, so a stepped frame sees this turn's input.
				DevTools.DebugControl.DrainCommands();

				if (DevTools.DebugControl.Mode == DevTools.RunMode.Stepped)
				{
					// Frozen between steps. Gating base.Update is a TRUE pause (unlike
					// TimeScale = 0, which still ticks SceneComponents). Draw still runs below
					// via the normal loop, so /screenshot keeps working while paused.
					if (DevTools.DebugControl.HasPendingStep())
					{
						base.Update(_fixedStepGameTime);
						DevTools.DebugControl.CompleteStep();
					}
					return;
				}
			}
#endif
			base.Update(gameTime);
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
				// In automation mode both players use the scripted device (driven over HTTP);
				// otherwise the usual two-keyboard local setup. PauseOnFocusLost is disabled so
				// frame-stepping keeps advancing even when the test runner has window focus.
				bool automate = AppSettings.DebugAutomation;
				if (automate)
					PauseOnFocusLost = false;

				var p0Device = automate ? Systems.InputDeviceType.Scripted0 : Systems.InputDeviceType.KeyboardWASD;
				var p1Device = automate ? Systems.InputDeviceType.Scripted1 : Systems.InputDeviceType.KeyboardArrows;

				var setup = GetGlobalManager<Systems.MatchSetupManager>();
				setup.Selections.Add(new Systems.PlayerSelection
					{ SlotIndex = 0, Device = p0Device, CharacterType = Constants.GameConstants.Characters.FutureAxe });
				setup.Selections.Add(new Systems.PlayerSelection
					{ SlotIndex = 1, Device = p1Device, CharacterType = Constants.GameConstants.Characters.FutureAxe });
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
