using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.BitmapFonts;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	public class AnnouncementOverlay : RenderableComponent, IUpdatable
	{
		public override float Width => 0;
		public override float Height => 0;

		private string _text;
		private Color _color;
		private float _alpha;
		private float _timer;
		private BitmapFont _font;
		private AnnouncementPhase _phase;

		private enum AnnouncementPhase
		{
			Idle,
			FadeIn,
			Hold,
			FadeOut
		}

		public AnnouncementOverlay()
		{
			RenderLayer = GameConstants.Rendering.ScreenSpaceRenderLayer;
			_phase = AnnouncementPhase.Idle;
		}

		public override void OnAddedToEntity()
		{
			_font = Entity.Scene.Content.LoadBitmapFont(Nez.Content.Fonts.GoreFont);
		}

		public void ShowAnnouncement(string text, Color color)
		{
			_text = text;
			_color = color;
			_alpha = 0f;
			_timer = 0f;
			_phase = AnnouncementPhase.FadeIn;
		}

		public void Update()
		{
			if (_phase == AnnouncementPhase.Idle) return;

			_timer += Time.UnscaledDeltaTime;

			switch (_phase)
			{
				case AnnouncementPhase.FadeIn:
					_alpha = MathHelper.Clamp(_timer / GameConstants.Match.AnnouncementFadeInDuration, 0f, 1f);
					if (_timer >= GameConstants.Match.AnnouncementFadeInDuration)
					{
						_alpha = 1f;
						_timer = 0f;
						_phase = AnnouncementPhase.Hold;
					}
					break;

				case AnnouncementPhase.Hold:
					_alpha = 1f;
					if (_timer >= GameConstants.Match.AnnouncementDisplayDuration)
					{
						_timer = 0f;
						_phase = AnnouncementPhase.FadeOut;
					}
					break;

				case AnnouncementPhase.FadeOut:
					_alpha = 1f - MathHelper.Clamp(_timer / GameConstants.Match.AnnouncementFadeOutDuration, 0f, 1f);
					if (_timer >= GameConstants.Match.AnnouncementFadeOutDuration)
					{
						_alpha = 0f;
						_phase = AnnouncementPhase.Idle;
					}
					break;
			}
		}

		public override void Render(Batcher batcher, Camera camera)
		{
			if (_phase == AnnouncementPhase.Idle || _font == null) return;

			var scale = GameConstants.Match.AnnouncementScale;
			var size = _font.MeasureString(_text) * scale;
			var screenCenter = new Vector2(
				Nez.Screen.Width / 2f,
				Nez.Screen.Height / 2f);
			var position = screenCenter - size / 2f;

			batcher.DrawString(_font, _text, position, _color * _alpha, 0f,
				Vector2.Zero, new Vector2(scale), SpriteEffects.None, 0f);
		}
	}
}
