using Microsoft.Xna.Framework;

namespace GorelordsBrawler.Constants
{
	public static class GameConstants
	{
		public static class EntityNames
		{
			public static readonly string UI = nameof(UI);
			public const string Camera = "camera";
			public const string MeleeHitbox = "melee-hitbox";
			public const string Ground = "ground";
			public const string PlatformMid = "platform-mid";
			public const string PlatformLeft = "platform-left";
			public const string PlatformRight = "platform-right";
			public const string PauseMenu = "pause-menu";
		}

		public static class ContentPaths
		{
			public const string CharactersFolder = "Content/Characters/";
			public const string JsonExtension = ".json";
		}

		public static class Characters
		{
			public const string Trollborg = "Trollborg";
		}

		public static class SceneNames
		{
			public const string ArenaScene = "ArenaScene";
			public const string MainMenuScene = "MainMenuScene";
			public const string SettingsScene = "SettingsScene";
		}

		public static class Physics
		{
			public const float GroundNormalThreshold = -0.5f;
			public const float CeilingNormalThreshold = 0.5f;
			public const int PhysicsBodyUpdateOrder = 100;
		}

		public static class Rendering
		{
			public const int ScreenSpaceRenderLayer = 999;
			public const int ScreenSpaceRendererOrder = 100;
			public const int DefaultRenderLayer = 0;
			public const int HitboxRenderLayer = -1;
			public const int HealthBarRenderLayer = -2;
			public const float HitboxColorAlpha = 0.6f;
		}

		public static class Input
		{
			public const float JumpBufferTime = 0.1f;
			public const int MaxGamePads = 4;
		}

		public static class Screen
		{
			public const int DesignWidth = 800;
			public const int DesignHeight = 600;
		}

		public static class UI
		{
			public const string TitleText = "GORELORDS";
			public const string SubtitleText = "MUTANT DEATH MATCH FIGURES";
			public const string PlayButtonText = "PLAY";
			public const string SettingsButtonText = "SETTINGS";
			public const string SettingsTitleText = "SETTINGS";
			public const string ApplyButtonText = "APPLY";
			public const string BackButtonText = "BACK";
			public const string ResolutionLabel = "Resolution";
			public const string FullscreenLabel = "Fullscreen";
			public const string BorderlessLabel = "Borderless";
			public const string VSyncLabel = "VSync";
			public const string ResolutionFormat = "{0}x{1}";
			public const float TitleScale = 4f;
			public const float SubtitleScale = 0.75f;
			public const float ButtonPadding = 10f;
			public const float SettingsLabelScale = 0.6f;
			public const float SettingsRowPadding = 8f;
			public const float SettingsWidgetMinWidth = 200f;

			// Pause menu
			public const string PausedTitleText = "PAUSED";
			public const string ResumeButtonText = "RESUME";
			public const string ControlsButtonText = "CONTROLS";
			public const string MainMenuButtonText = "MAIN MENU";
			public const string QuitButtonText = "QUIT";
			public const string ControlsTitleText = "CONTROLS";

			// Controls reference
			public const string Player1Header = "Player 1 (WASD)";
			public const string Player2Header = "Player 2 (Arrows)";
			public const string GamepadHeader = "Gamepad";
			public const string MoveLabel = "Move";
			public const string JumpLabel = "Jump";
			public const string AttackLabel = "Attack";
			public const string Player1MoveKeys = "A / D";
			public const string Player1JumpKey = "W";
			public const string Player1AttackKey = "F";
			public const string Player2MoveKeys = "Left / Right";
			public const string Player2JumpKey = "Up";
			public const string Player2AttackKey = "Right Ctrl";
			public const string GamepadMoveInput = "L-Stick / D-Pad";
			public const string GamepadJumpInput = "A Button";
			public const string GamepadAttackInput = "X Button";
		}

		public static class Combat
		{
			public const float RespawnDelay = 2f;
			public const float HealthBarWidth = 40f;
			public const float HealthBarHeight = 4f;
			public const float HealthBarOffsetY = 35f;
			public static readonly Color HealthBarBackgroundColor = new Color(40, 40, 40);
			public static readonly Color HealthBarHighColor = Color.Green;
			public static readonly Color HealthBarLowColor = Color.Red;
		}

		public static class PauseMenu
		{
			public static readonly Color OverlayColor = new Color(0, 0, 0, 150);
			public const float ControlsLabelScale = 0.5f;
			public const float ControlsRowPadding = 4f;
			public const float ControlsColumnPadding = 15f;
			public const float ControlsSectionPadding = 12f;
		}

		public static class Arena
		{
			// Player spawn positions
			public static readonly Vector2 Player1Spawn = new Vector2(300, 500);
			public static readonly Vector2 Player2Spawn = new Vector2(500, 500);

			// Platform colors
			public static readonly Color GroundColor = new Color(80, 80, 80);
			public static readonly Color PlatformColor = new Color(100, 100, 100);

			// Ground
			public static readonly Vector2 GroundPosition = new Vector2(400, 568);
			public const float GroundWidth = 800f;
			public const float GroundHeight = 32f;

			// Mid platform
			public static readonly Vector2 PlatformMidPosition = new Vector2(400, 420);
			public const float PlatformMidWidth = 200f;
			public const float PlatformMidHeight = 16f;

			// Left platform
			public static readonly Vector2 PlatformLeftPosition = new Vector2(150, 300);
			public const float PlatformLeftWidth = 120f;
			public const float PlatformLeftHeight = 16f;

			// Right platform
			public static readonly Vector2 PlatformRightPosition = new Vector2(650, 300);
			public const float PlatformRightWidth = 120f;
			public const float PlatformRightHeight = 16f;
		}
	}
}
