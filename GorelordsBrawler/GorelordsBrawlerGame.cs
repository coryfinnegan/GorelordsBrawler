using Microsoft.Xna.Framework;
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
				Scene.SceneResolutionPolicy.BestFit);

			RegisterGlobalManager(new MatchSetupManager());

			SettingsManager.Initialize();
			SettingsManager.Apply();

			Scene = new Scenes.MainMenuScene();
		}

		public static void TransitionToScene<T>() where T : Scene, new()
		{
			StartSceneTransition(new FadeTransition(() => new T()));
		}
	}
}
