using Microsoft.Xna.Framework;
using Nez;
using Nez.BitmapFonts;
using Nez.UI;

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
            table.Add(new Label("GORELORDS", SludgebornFont, Color.Red, 4, 4));
            table.Row();
            table.Add(new Label("MUTANT DEATH MATCH FIGURES", GorlordsFont, Color.Yellow, 0.75f, 0.75f));
            table.Row();
            var buttonStyle = new TextButtonStyle(new PrimitiveDrawable(Color.Transparent, 10f), new PrimitiveDrawable(Color.Transparent, 10f), new PrimitiveDrawable(Color.Transparent, 10f), GorlordsFont)
            {
                DownFontColor = Color.DarkGreen,
                OverFontColor = Color.Green
            };
            var playButton = new TextButton("PLAY", buttonStyle);
            playButton.OnClicked += x => GorelordsBrawlerGame.LoadScene("ArenaScene");

            table.Add(playButton);

        }
    }
}
