using Nez;
using Nez.BitmapFonts;
using Nez.UI;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Systems.Rules;

namespace GorelordsBrawler.Systems
{
	public class MatchHUD : SceneComponent
	{
		private readonly IMatchRuleset _ruleset;
		private Entity _hudEntity;

		public MatchHUD(IMatchRuleset ruleset)
		{
			_ruleset = ruleset;
		}

		public override void OnEnabled()
		{
			var font = Scene.Content.LoadBitmapFont(Nez.Content.Fonts.GoreFont);

			_hudEntity = Scene.CreateEntity(GameConstants.EntityNames.MatchHUD);
			var canvas = _hudEntity.AddComponent(new UICanvas());
			canvas.RenderLayer = GameConstants.Rendering.ScreenSpaceRenderLayer;

			var root = canvas.Stage.AddElement(new Table());
			root.SetFillParent(true);

			var playerManager = Scene.GetSceneComponent<PlayerManager>();
			var players = playerManager.GetActivePlayers();

			_ruleset.BuildHUD(root, font, players);
		}

		public override void OnRemovedFromScene()
		{
			if (_hudEntity != null)
			{
				_hudEntity.Destroy();
				_hudEntity = null;
			}
		}
	}
}
