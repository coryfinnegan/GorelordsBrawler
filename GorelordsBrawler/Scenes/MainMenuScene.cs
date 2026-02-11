using Microsoft.Xna.Framework;
using Nez;
using Nez.BitmapFonts;
using Nez.UI;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Scenes
{
	public class MainMenuScene : BaseScene
	{
		public BitmapFont GorlordsFont => Content.LoadBitmapFont(Nez.Content.Fonts.GoreFont);
		public BitmapFont SludgebornFont => Content.LoadBitmapFont(Nez.Content.Fonts.Sludgeborn);

		public MainMenuScene()
		{
			var table = Canvas.Stage.AddElement(new Table());
			table.SetFillParent(true);
			table.Add(new Label(GameConstants.UI.TitleText, SludgebornFont, Color.Red,
				GameConstants.UI.TitleScale, GameConstants.UI.TitleScale));
			table.Row();
			table.Add(new Label(GameConstants.UI.SubtitleText, GorlordsFont, Color.Yellow,
				GameConstants.UI.SubtitleScale, GameConstants.UI.SubtitleScale));
			table.Row();
			var buttonStyle = new TextButtonStyle(
				new PrimitiveDrawable(Color.Transparent, GameConstants.UI.ButtonPadding),
				new PrimitiveDrawable(Color.Transparent, GameConstants.UI.ButtonPadding),
				new PrimitiveDrawable(Color.Transparent, GameConstants.UI.ButtonPadding),
				GorlordsFont)
			{
				DownFontColor = Color.DarkGreen,
				OverFontColor = Color.Green
			};
			var playButton = new TextButton(GameConstants.UI.PlayButtonText, buttonStyle);
			playButton.OnClicked += x => GorelordsBrawlerGame.LoadScene(GameConstants.SceneNames.ArenaScene);

			table.Add(playButton);
			table.Row();

			var settingsButton = new TextButton(GameConstants.UI.SettingsButtonText, buttonStyle);
			settingsButton.OnClicked += x => GorelordsBrawlerGame.LoadScene(GameConstants.SceneNames.SettingsScene);
			table.Add(settingsButton);
		}
	}
}
