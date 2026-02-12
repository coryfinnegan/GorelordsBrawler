using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.BitmapFonts;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Data;
using GorelordsBrawler.Systems;
using System.Collections.Generic;

namespace GorelordsBrawler.Components.UI
{
	public class SlotRenderer : RenderableComponent
	{
		public override float Width => GameConstants.CharacterSelect.SlotWidth;
		public override float Height => GameConstants.CharacterSelect.SlotHeight;

		private readonly BitmapFont _font;
		private readonly string[] _characters;
		private readonly Dictionary<string, CharacterData> _characterDataCache = new();

		public SlotRenderer(BitmapFont font, string[] characters)
		{
			_font = font;
			_characters = characters;
		}

		public override void Render(Batcher batcher, Camera camera)
		{
			var slot = Entity.GetComponent<SlotController>();
			var pos = Entity.Transform.Position;

			if (!slot.IsJoined)
			{
				DrawJoinPrompts(batcher, slot, pos);
				return;
			}

			var scale = GameConstants.CharacterSelect.NameScale;
			var y = pos.Y;

			// Player label
			var playerText = string.Format(GameConstants.UI.PlayerLabelFormat, slot.SlotIndex + 1);
			var playerSize = _font.MeasureString(playerText) * GameConstants.CharacterSelect.ReadyScale;
			batcher.DrawString(_font, playerText,
				new Vector2(pos.X - playerSize.X / 2, y),
				Color.White, 0f, Vector2.Zero, new Vector2(GameConstants.CharacterSelect.ReadyScale),
				SpriteEffects.None, 0f);
			y += playerSize.Y + GameConstants.CharacterSelect.PanelPadding;

			// Character color preview
			var charData = GetCharacterData(slot);
			var previewColor = new Color(charData.colorR, charData.colorG, charData.colorB);
			var previewW = charData.bodyWidth * GameConstants.CharacterSelect.PreviewScale;
			var previewH = charData.bodyHeight * GameConstants.CharacterSelect.PreviewScale;
			batcher.DrawRect(pos.X - previewW / 2, y, previewW, previewH, previewColor);
			y += previewH + GameConstants.CharacterSelect.PanelPadding;

			// Character name with arrows (hide arrows when ready)
			var charName = charData.name ?? _characters[slot.CharacterIndex];
			var displayName = slot.IsReady
				? charName
				: GameConstants.CharacterSelect.LeftArrow + charName + GameConstants.CharacterSelect.RightArrow;
			var nameSize = _font.MeasureString(displayName) * scale;
			batcher.DrawString(_font, displayName,
				new Vector2(pos.X - nameSize.X / 2, y),
				Color.White, 0f, Vector2.Zero, new Vector2(scale),
				SpriteEffects.None, 0f);
			y += nameSize.Y + GameConstants.CharacterSelect.PanelPadding;

			// Ready indicator or action prompts
			if (slot.IsReady)
			{
				var readySize = _font.MeasureString(GameConstants.CharacterSelect.ReadyText)
					* GameConstants.CharacterSelect.ReadyScale;
				batcher.DrawString(_font, GameConstants.CharacterSelect.ReadyText,
					new Vector2(pos.X - readySize.X / 2, y),
					Color.Green, 0f, Vector2.Zero, new Vector2(GameConstants.CharacterSelect.ReadyScale),
					SpriteEffects.None, 0f);
				y += readySize.Y + GameConstants.CharacterSelect.PanelPadding;

				DrawCenteredText(batcher, GameConstants.CharacterSelect.UnreadyPrompt, pos.X, y, scale, Color.DarkGray);
			}
			else
			{
				DrawCenteredText(batcher, GameConstants.CharacterSelect.ReadyPrompt, pos.X, y, scale, Color.DarkGray);
				y += _font.LineHeight * scale + 4;
				DrawCenteredText(batcher, GameConstants.CharacterSelect.LeavePrompt, pos.X, y, scale, Color.DarkGray);
			}
		}

		private void DrawJoinPrompts(Batcher batcher, SlotController slot, Vector2 pos)
		{
			var scale = GameConstants.CharacterSelect.NameScale;
			var lineHeight = _font.LineHeight * scale + 4;
			var y = pos.Y;

			if (!IsDeviceJoined(InputDeviceType.KeyboardWASD))
			{
				DrawCenteredText(batcher, GameConstants.CharacterSelect.JoinPromptWASD, pos.X, y, scale, Color.DarkGray);
				y += lineHeight;
			}
			if (!IsDeviceJoined(InputDeviceType.KeyboardArrows))
			{
				DrawCenteredText(batcher, GameConstants.CharacterSelect.JoinPromptArrows, pos.X, y, scale, Color.DarkGray);
				y += lineHeight;
			}
			for (int i = 0; i < GameConstants.Input.MaxGamePads; i++)
			{
				var device = InputDeviceType.Gamepad0 + i;
				if (!IsDeviceJoined(device) && Nez.Input.GamePads[i].IsConnected())
				{
					DrawCenteredText(batcher, GameConstants.CharacterSelect.JoinPromptGamepad, pos.X, y, scale, Color.DarkGray);
					y += lineHeight;
				}
			}
		}

		private bool IsDeviceJoined(InputDeviceType device)
		{
			var scene = Entity.Scene;
			for (int i = 0; i < PlayerManager.MaxPlayers; i++)
			{
				var slotEntity = scene.FindEntity($"slot-{i}");
				if (slotEntity != null)
				{
					var controller = slotEntity.GetComponent<SlotController>();
					if (controller != null && controller.IsJoined && controller.Device == device)
					{
						return true;
					}
				}
			}
			return false;
		}

		private void DrawCenteredText(Batcher batcher, string text, float centerX, float y, float scale, Color color)
		{
			var size = _font.MeasureString(text) * scale;
			batcher.DrawString(_font, text,
				new Vector2(centerX - size.X / 2, y),
				color, 0f, Vector2.Zero, new Vector2(scale),
				SpriteEffects.None, 0f);
		}

		private CharacterData GetCharacterData(SlotController slot)
		{
			var characterType = _characters[slot.CharacterIndex];
			if (!_characterDataCache.TryGetValue(characterType, out var data))
			{
				data = CharacterLoader.Load(Entity.Scene, characterType);
				_characterDataCache[characterType] = data;
			}
			return data;
		}
	}
}
