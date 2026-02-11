using Nez;
using Microsoft.Xna.Framework;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Scenes
{
	public class BaseScene : Scene
	{
		public UICanvas Canvas => CreateEntity(GameConstants.EntityNames.UI).AddComponent(new UICanvas());

		public BaseScene()
		{
			ClearColor = Color.Black;
			AddRenderer(new ScreenSpaceRenderer(
				GameConstants.Rendering.ScreenSpaceRendererOrder,
				GameConstants.Rendering.ScreenSpaceRenderLayer));
		}
	}
}
