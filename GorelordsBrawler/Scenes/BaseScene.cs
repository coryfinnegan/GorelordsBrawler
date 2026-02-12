using Nez;
using Microsoft.Xna.Framework;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Scenes
{
	public class BaseScene : Scene
	{
        public UICanvas Canvas { get; private set; }

        public BaseScene()
		{
			ClearColor = Color.Black;
			AddRenderer(new ScreenSpaceRenderer(
				GameConstants.Rendering.ScreenSpaceRendererOrder,
				GameConstants.Rendering.ScreenSpaceRenderLayer));
		}

        public override void Initialize()
        {
            base.Initialize();

            var uiEntity = CreateEntity(GameConstants.EntityNames.UI);
            Canvas = uiEntity.AddComponent(new UICanvas());
        }
    }
}
