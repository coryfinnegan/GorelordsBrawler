using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.BitmapFonts;
using Nez.UI;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.Scenes
{
	public class SettingsScene : BaseScene
	{
		public BitmapFont SludgebornFont => Content.LoadBitmapFont(Nez.Content.Fonts.Sludgeborn);
		public BitmapFont GoreFont => Content.LoadBitmapFont(Nez.Content.Fonts.GoreFont);

		private SelectBox<string> _resolutionSelect;
		private CheckBox _fullscreenCheckbox;
		private CheckBox _borderlessCheckbox;
		private CheckBox _vsyncCheckbox;

		private List<(int width, int height)> _resolutions;

		public SettingsScene()
		{
			BuildResolutionList();

			var table = Canvas.Stage.AddElement(new Table());
			table.SetFillParent(true);

			// Title
			table.Add(new Label(GameConstants.UI.SettingsTitleText, SludgebornFont, Color.Red,
				GameConstants.UI.TitleScale, GameConstants.UI.TitleScale));
			table.Row().SetPadTop(GameConstants.UI.SettingsRowPadding);

			// Resolution row
			AddSettingRow(table, GameConstants.UI.ResolutionLabel, CreateResolutionSelect());
			// Fullscreen row
			AddSettingRow(table, GameConstants.UI.FullscreenLabel, CreateFullscreenCheckbox());
			// Borderless row
			AddSettingRow(table, GameConstants.UI.BorderlessLabel, CreateBorderlessCheckbox());
			// VSync row
			AddSettingRow(table, GameConstants.UI.VSyncLabel, CreateVSyncCheckbox());

			// Button row
			table.Row().SetPadTop(GameConstants.UI.SettingsRowPadding * 2);
			var buttonTable = new Table();

			var buttonStyle = CreateButtonStyle();

			var applyButton = new TextButton(GameConstants.UI.ApplyButtonText, buttonStyle);
			applyButton.OnClicked += OnApplyClicked;
			buttonTable.Add(applyButton).SetPadRight(GameConstants.UI.ButtonPadding);

			var backButton = new TextButton(GameConstants.UI.BackButtonText, buttonStyle);
			backButton.OnClicked += OnBackClicked;
			buttonTable.Add(backButton);

			table.Add(buttonTable).SetColspan(2);
		}

		private void BuildResolutionList()
		{
			_resolutions = [];
			var seen = new HashSet<string>();

			// Always include design resolution
			_resolutions.Add((GameConstants.Screen.DesignWidth, GameConstants.Screen.DesignHeight));
			seen.Add(FormatResolution(GameConstants.Screen.DesignWidth, GameConstants.Screen.DesignHeight));

			var monitorWidth = Screen.MonitorWidth;
			var monitorHeight = Screen.MonitorHeight;

			foreach (var mode in GraphicsAdapter.DefaultAdapter.SupportedDisplayModes)
			{
				if (mode.Width > monitorWidth || mode.Height > monitorHeight)
					continue;
				if (mode.Width < GameConstants.Screen.DesignWidth)
					continue;

				var key = FormatResolution(mode.Width, mode.Height);
				if (seen.Add(key))
					_resolutions.Add((mode.Width, mode.Height));
			}

			_resolutions.Sort((a, b) =>
			{
				var cmp = a.width.CompareTo(b.width);
				return cmp != 0 ? cmp : a.height.CompareTo(b.height);
			});
		}

		private static string FormatResolution(int width, int height)
		{
			return string.Format(GameConstants.UI.ResolutionFormat, width, height);
		}

		private SelectBox<string> CreateResolutionSelect()
		{
			var listStyle = new ListBoxStyle
			{
				FontColorHovered = Color.White,
				FontColorSelected = Color.White,
				FontColorUnselected = Color.LightGray,
				Selection = new PrimitiveDrawable(Color.DarkGreen, 5, 5),
				HoverSelection = new PrimitiveDrawable(Color.Green * 0.5f, 5, 5),
				Background = new PrimitiveDrawable(new Color(30, 30, 30))
			};
			listStyle.Font = GoreFont;

			var scrollStyle = new ScrollPaneStyle
			{
				VScroll = new PrimitiveDrawable(6, 0, new Color(60, 60, 60)),
				VScrollKnob = new PrimitiveDrawable(6, 50, Color.Gray)
			};

			var selectStyle = new SelectBoxStyle
			{
				Font = GoreFont,
				FontColor = Color.White,
				Background = new PrimitiveDrawable(new Color(50, 50, 50), 6, 2),
				BackgroundOver = new PrimitiveDrawable(new Color(70, 70, 70), 6, 2),
				BackgroundOpen = new PrimitiveDrawable(new Color(40, 40, 40), 6, 2),
				ListStyle = listStyle,
				ScrollStyle = scrollStyle
			};

			_resolutionSelect = new SelectBox<string>(selectStyle);

			var items = new List<string>();
			foreach (var res in _resolutions)
				items.Add(FormatResolution(res.width, res.height));
			_resolutionSelect.SetItems(items);

			var currentRes = FormatResolution(SettingsManager.ResolutionWidth, SettingsManager.ResolutionHeight);
			_resolutionSelect.SetSelected(currentRes);

			return _resolutionSelect;
		}

		private CheckBox CreateFullscreenCheckbox()
		{
			_fullscreenCheckbox = new CheckBox(string.Empty, CreateCheckboxStyle());
			_fullscreenCheckbox.IsChecked = SettingsManager.IsFullscreen;
			_fullscreenCheckbox.OnChanged += isChecked =>
			{
				_borderlessCheckbox.SetDisabled(!isChecked);
			};
			return _fullscreenCheckbox;
		}

		private CheckBox CreateBorderlessCheckbox()
		{
			_borderlessCheckbox = new CheckBox(string.Empty, CreateCheckboxStyle());
			_borderlessCheckbox.IsChecked = SettingsManager.IsBorderless;
			_borderlessCheckbox.SetDisabled(!SettingsManager.IsFullscreen);
			return _borderlessCheckbox;
		}

		private CheckBox CreateVSyncCheckbox()
		{
			_vsyncCheckbox = new CheckBox(string.Empty, CreateCheckboxStyle());
			_vsyncCheckbox.IsChecked = SettingsManager.VSync;
			return _vsyncCheckbox;
		}

		private CheckBoxStyle CreateCheckboxStyle()
		{
			return new CheckBoxStyle
			{
				CheckboxOn = new PrimitiveDrawable(20, Color.Green),
				CheckboxOff = new PrimitiveDrawable(20, new Color(80, 80, 80)),
				CheckboxOver = new PrimitiveDrawable(20, Color.DarkGreen),
				Font = GoreFont,
				FontColor = Color.White,
				OverFontColor = Color.Green,
				DisabledFontColor = Color.Gray
			};
		}

		private TextButtonStyle CreateButtonStyle()
		{
			return new TextButtonStyle(
				new PrimitiveDrawable(Color.Transparent, GameConstants.UI.ButtonPadding),
				new PrimitiveDrawable(Color.Transparent, GameConstants.UI.ButtonPadding),
				new PrimitiveDrawable(Color.Transparent, GameConstants.UI.ButtonPadding),
				GoreFont)
			{
				DownFontColor = Color.DarkGreen,
				OverFontColor = Color.Green
			};
		}

		private void AddSettingRow(Table table, string labelText, Element widget)
		{
			table.Row().SetPadTop(GameConstants.UI.SettingsRowPadding);
			table.Add(new Label(labelText, GoreFont, Color.Yellow, GameConstants.UI.SettingsLabelScale))
				.SetPadRight(GameConstants.UI.ButtonPadding)
				.Right();
			table.Add(widget).SetMinWidth(GameConstants.UI.SettingsWidgetMinWidth).Left();
		}

		private void OnApplyClicked(Button button)
		{
			var selectedIndex = _resolutionSelect.GetSelectedIndex();
			var res = _resolutions[selectedIndex];
			SettingsManager.SetResolution(res.width, res.height);
			SettingsManager.SetFullscreen(_fullscreenCheckbox.IsChecked);
			SettingsManager.SetBorderless(_borderlessCheckbox.IsChecked);
			SettingsManager.SetVSync(_vsyncCheckbox.IsChecked);
			SettingsManager.Apply();
			SettingsManager.Save();
		}

		private void OnBackClicked(Button button)
		{
			GorelordsBrawlerGame.LoadScene(GameConstants.SceneNames.MainMenuScene);
		}
	}
}
