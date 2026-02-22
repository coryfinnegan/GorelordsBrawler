using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.BitmapFonts;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.UI
{
	public class HeaderRenderer : RenderableComponent
	{
		public override float Width => GameConstants.Screen.DesignWidth;
		public override float Height => GameConstants.Screen.DesignHeight;

		public string StatusText { get; set; } = GameConstants.CharacterSelect.NeedPlayersText;
		public Color StatusColor { get; set; } = Color.Gray;

		private readonly BitmapFont _font;
		private readonly BitmapFont _titleFont;

		public HeaderRenderer(BitmapFont font, BitmapFont titleFont)
		{
			_font = font;
			_titleFont = titleFont;
		}

		public override void Render(Batcher batcher, Camera camera)
		{
			var screenWidth = GameConstants.Screen.DesignWidth;
			var screenHeight = GameConstants.Screen.DesignHeight;

			// Title — centered at top
			var titleScale = GameConstants.UI.TitleScale;
			var titleText = GameConstants.CharacterSelect.TitleText;
			var titleSize = _titleFont.MeasureString(titleText) * titleScale;
			batcher.DrawString(_titleFont, titleText,
				new Vector2(screenWidth / 2f - titleSize.X / 1.5f, GameConstants.CharacterSelect.PanelPadding),
				Color.Red, 0f, Vector2.Zero, new Vector2(titleScale),
				SpriteEffects.None, 0f);

			// Status — centered at bottom
			var statusScale = GameConstants.CharacterSelect.StatusScale;
			var statusSize = _font.MeasureString(StatusText) * statusScale;
			batcher.DrawString(_font, StatusText,
				new Vector2(screenWidth / 2f - statusSize.X / 2f,
					screenHeight - statusSize.Y - GameConstants.CharacterSelect.PanelPadding),
				StatusColor, 0f, Vector2.Zero, new Vector2(statusScale),
				SpriteEffects.None, 0f);
		}
	}
}
