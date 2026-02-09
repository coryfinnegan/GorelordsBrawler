using Nez;
using Microsoft.Xna.Framework;
using Nez.BitmapFonts;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Scenes
{
    public class BaseScene : Scene
    {
        public const int ScreenSpaceRenderLayer = 999;
        public UICanvas Canvas => CreateEntity(GameConstants.EntityNames.UI).AddComponent(new UICanvas());
        //public BitmapFont GoreFont => Content.LoadBitmapFont(Nez.Content.Fonts.GhostbusterNew);

        public BaseScene()
        {
            ClearColor = Color.Black;
            AddRenderer(new ScreenSpaceRenderer(100, ScreenSpaceRenderLayer));

        }
    }
}
