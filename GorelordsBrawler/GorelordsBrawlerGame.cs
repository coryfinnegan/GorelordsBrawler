using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nez;

namespace GorelordsBrawler
{
    public class GorelordsBrawlerGame : Core
    {
        public static GorelordsBrawlerGame GameReference { get; private set; }
        private Scene.SceneResolutionPolicy _sceneResolutionPolicy;
        private GameTime _gameTime;


        public GorelordsBrawlerGame()
        {
            _sceneResolutionPolicy = Scene.SceneResolutionPolicy.BestFit;
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


            Scene.SetDefaultDesignResolution(800, 600, Scene.SceneResolutionPolicy.BestFit);
            Screen.SetSize(800, 600);
            Scene = new Scenes.MainMenuScene();
        }
    }
}
