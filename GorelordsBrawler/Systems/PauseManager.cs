using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Nez;
using Nez.BitmapFonts;
using Nez.UI;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	public class PauseManager : SceneComponent
	{
		public bool IsPaused { get; private set; }

		private VirtualButton _pauseInput;
		private Entity _pauseEntity;
		private BitmapFont _font;

		public override void OnEnabled()
		{
			_pauseInput = new VirtualButton();
			_pauseInput.AddKeyboardKey(Keys.Escape);
			for (int i = 0; i < GameConstants.Input.MaxGamePads; i++)
				_pauseInput.AddGamePadButton(i, Buttons.Start);
		}

		public override void OnDisabled()
		{
			_pauseInput.Deregister();
			if (IsPaused)
				Resume();
		}

		public override void Update()
		{
			if (_pauseInput.IsPressed)
			{
				if (IsPaused)
					Resume();
				else
					Pause();
			}
		}

		private void Pause()
		{
			IsPaused = true;
			Time.TimeScale = 0f;
			BuildPauseMenu();
		}

		private void Resume()
		{
			IsPaused = false;
			Time.TimeScale = 1f;
			DestroyPauseUI();
		}

		private void DestroyPauseUI()
		{
			if (_pauseEntity != null)
			{
				_pauseEntity.Destroy();
				_pauseEntity = null;
			}
		}

		private BitmapFont GetFont()
		{
			_font ??= Scene.Content.LoadBitmapFont(Nez.Content.Fonts.GoreFont);
			return _font;
		}

		private TextButtonStyle CreateButtonStyle()
		{
			return new TextButtonStyle(
				new PrimitiveDrawable(Color.Transparent, GameConstants.UI.ButtonPadding),
				new PrimitiveDrawable(Color.Transparent, GameConstants.UI.ButtonPadding),
				new PrimitiveDrawable(Color.Transparent, GameConstants.UI.ButtonPadding),
				GetFont())
			{
				DownFontColor = Color.DarkGreen,
				OverFontColor = Color.Green
			};
		}

		private UICanvas CreateCanvas()
		{
			_pauseEntity = Scene.CreateEntity(GameConstants.EntityNames.PauseMenu);
			var canvas = _pauseEntity.AddComponent(new UICanvas());
			canvas.RenderLayer = GameConstants.Rendering.ScreenSpaceRenderLayer;
			return canvas;
		}

		private void BuildPauseMenu()
		{
			DestroyPauseUI();

			var canvas = CreateCanvas();

			// Fullscreen overlay for dimming
			var overlay = canvas.Stage.AddElement(new Table());
			overlay.SetFillParent(true);
			overlay.SetBackground(new PrimitiveDrawable(GameConstants.PauseMenu.OverlayColor));

			// Centered content table
			var root = canvas.Stage.AddElement(new Table());
			root.SetFillParent(true);

			// Title
			root.Add(new Label(GameConstants.UI.PausedTitleText, GetFont(), Color.Red,
				GameConstants.UI.TitleScale, GameConstants.UI.TitleScale));
			root.Row();

			var buttonStyle = CreateButtonStyle();

			var resumeButton = new TextButton(GameConstants.UI.ResumeButtonText, buttonStyle);
			resumeButton.OnClicked += _ => Resume();
			root.Add(resumeButton);
			root.Row();

			var controlsButton = new TextButton(GameConstants.UI.ControlsButtonText, buttonStyle);
			controlsButton.OnClicked += _ => ShowControls();
			root.Add(controlsButton);
			root.Row();

			var mainMenuButton = new TextButton(GameConstants.UI.MainMenuButtonText, buttonStyle);
			mainMenuButton.OnClicked += _ =>
			{
				Resume();
				GorelordsBrawlerGame.LoadScene(GameConstants.SceneNames.MainMenuScene);
			};
			root.Add(mainMenuButton);
			root.Row();

			var quitButton = new TextButton(GameConstants.UI.QuitButtonText, buttonStyle);
			quitButton.OnClicked += _ => Core.Exit();
			root.Add(quitButton);
		}

		private void ShowControls()
		{
			DestroyPauseUI();

			var canvas = CreateCanvas();

			// Fullscreen overlay for dimming
			var overlay = canvas.Stage.AddElement(new Table());
			overlay.SetFillParent(true);
			overlay.SetBackground(new PrimitiveDrawable(GameConstants.PauseMenu.OverlayColor));

			// Centered content table
			var root = canvas.Stage.AddElement(new Table());
			root.SetFillParent(true);

			var font = GetFont();
			var scale = GameConstants.PauseMenu.ControlsLabelScale;

			// Title
			root.Add(new Label(GameConstants.UI.ControlsTitleText, font, Color.Red,
				GameConstants.UI.TitleScale, GameConstants.UI.TitleScale)).SetColspan(2);
			root.Row().SetPadTop(GameConstants.PauseMenu.ControlsSectionPadding);

			// Player 1
			AddControlsSection(root, font, scale, GameConstants.UI.Player1Header,
				GameConstants.UI.Player1MoveKeys, GameConstants.UI.Player1JumpKey, GameConstants.UI.Player1AttackKey);

			// Player 2
			root.Row().SetPadTop(GameConstants.PauseMenu.ControlsSectionPadding);
			AddControlsSection(root, font, scale, GameConstants.UI.Player2Header,
				GameConstants.UI.Player2MoveKeys, GameConstants.UI.Player2JumpKey, GameConstants.UI.Player2AttackKey);

			// Gamepad
			root.Row().SetPadTop(GameConstants.PauseMenu.ControlsSectionPadding);
			AddControlsSection(root, font, scale, GameConstants.UI.GamepadHeader,
				GameConstants.UI.GamepadMoveInput, GameConstants.UI.GamepadJumpInput, GameConstants.UI.GamepadAttackInput);

			// Back button
			root.Row().SetPadTop(GameConstants.PauseMenu.ControlsSectionPadding);
			var backButton = new TextButton(GameConstants.UI.BackButtonText, CreateButtonStyle());
			backButton.OnClicked += _ => BuildPauseMenu();
			root.Add(backButton).SetColspan(2);
		}

		private void AddControlsSection(Table table, BitmapFont font, float scale,
			string header, string moveInput, string jumpInput, string attackInput)
		{
			table.Add(new Label(header, font, Color.Yellow, scale)).SetColspan(2).Left();

			table.Row().SetPadTop(GameConstants.PauseMenu.ControlsRowPadding);
			table.Add(new Label(GameConstants.UI.MoveLabel, font, Color.White, scale))
				.SetPadRight(GameConstants.PauseMenu.ControlsColumnPadding).Right();
			table.Add(new Label(moveInput, font, Color.LightGray, scale)).Left();

			table.Row().SetPadTop(GameConstants.PauseMenu.ControlsRowPadding);
			table.Add(new Label(GameConstants.UI.JumpLabel, font, Color.White, scale))
				.SetPadRight(GameConstants.PauseMenu.ControlsColumnPadding).Right();
			table.Add(new Label(jumpInput, font, Color.LightGray, scale)).Left();

			table.Row().SetPadTop(GameConstants.PauseMenu.ControlsRowPadding);
			table.Add(new Label(GameConstants.UI.AttackLabel, font, Color.White, scale))
				.SetPadRight(GameConstants.PauseMenu.ControlsColumnPadding).Right();
			table.Add(new Label(attackInput, font, Color.LightGray, scale)).Left();
		}
	}
}
