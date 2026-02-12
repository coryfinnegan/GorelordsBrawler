using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;
using Nez.BitmapFonts;
using Nez.UI;
using GorelordsBrawler.Components;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Systems.Rules;

namespace GorelordsBrawler.Systems
{
	public class MatchManager : SceneComponent
	{
		public bool CanPause => _state == MatchState.Active;

		private enum MatchState
		{
			Countdown,
			Active,
			Victory
		}

		private readonly IMatchRuleset _ruleset;
		private MatchState _state;
		private float _countdownTimer;
		private AnnouncementOverlay _announcement;
		private List<Entity> _players;
		private Entity _winner;
		private Entity _victoryEntity;
		private BitmapFont _font;

		public MatchManager(IMatchRuleset ruleset)
		{
			_ruleset = ruleset;
		}

		public override void OnEnabled()
		{
			var playerManager = Scene.GetSceneComponent<PlayerManager>();
			_players = playerManager.GetActivePlayers();

			// Create announcement entity
			var announcementEntity = Scene.CreateEntity(GameConstants.EntityNames.Announcement);
			_announcement = announcementEntity.AddComponent(new AnnouncementOverlay());

			// Initialize ruleset
			var announcements = new AnnouncementAccess((text, color) => ShowAnnouncement(text, color));
			_ruleset.Initialize(_players, OnMatchWon, announcements);

			// Start countdown
			_state = MatchState.Countdown;
			_countdownTimer = GameConstants.Match.CountdownDuration;

			// Disable combat during countdown
			foreach (var player in _players)
			{
				var respawn = player.GetComponent<RespawnHandler>();
				respawn.SetCombatComponentsEnabled(false);
			}

			ShowAnnouncement(GameConstants.Match.FightText, GameConstants.Match.AnnouncementColor);
		}

		public override void Update()
		{
			if (_state == MatchState.Countdown)
			{
				_countdownTimer -= Time.DeltaTime;
				if (_countdownTimer <= 0)
				{
					_state = MatchState.Active;
					foreach (var player in _players)
					{
						var respawn = player.GetComponent<RespawnHandler>();
						respawn.SetCombatComponentsEnabled(true);
					}
				}
			}
			else if (_state == MatchState.Active)
			{
				_ruleset.Update();
			}
		}

		/// <summary>
		/// Called by RespawnHandler when a player dies.
		/// Returns true if the player should respawn, false if they stay dead.
		/// </summary>
		public bool NotifyPlayerDeath(Entity player)
		{
			if (_state == MatchState.Victory) return false;
			return _ruleset.OnPlayerDeath(player);
		}

		public void ShowAnnouncement(string text, Color color)
		{
			_announcement.ShowAnnouncement(text, color);
		}

		private void OnMatchWon(Entity winner)
		{
			if (_state == MatchState.Victory) return;

			_state = MatchState.Victory;
			_winner = winner;
			ShowAnnouncement(GameConstants.Match.GameText, GameConstants.Match.AnnouncementColor);

			// Disable all combat
			foreach (var p in _players)
			{
				var respawn = p.GetComponent<RespawnHandler>();
				respawn.SetCombatComponentsEnabled(false);
			}

			Core.Schedule(GameConstants.Match.VictoryDelay, _ => BuildVictoryScreen());
		}

		private BitmapFont GetFont()
		{
			_font ??= Scene.Content.LoadBitmapFont(Nez.Content.Fonts.GoreFont);
			return _font;
		}

		private void BuildVictoryScreen()
		{
			if (_victoryEntity != null)
			{
				_victoryEntity.Destroy();
				_victoryEntity = null;
			}

			_victoryEntity = Scene.CreateEntity(GameConstants.EntityNames.VictoryScreen);
			var canvas = _victoryEntity.AddComponent(new UICanvas());
			canvas.RenderLayer = GameConstants.Rendering.ScreenSpaceRenderLayer;

			// Fullscreen overlay
			var overlay = canvas.Stage.AddElement(new Table());
			overlay.SetFillParent(true);
			overlay.SetBackground(new PrimitiveDrawable(GameConstants.PauseMenu.OverlayColor));

			// Main layout table
			var root = canvas.Stage.AddElement(new Table());
			root.SetFillParent(true);

			var font = GetFont();

			// Split-screen player columns
			var columnsTable = new Table();
			for (int i = 0; i < _players.Count; i++)
			{
				var player = _players[i];
				var isWinner = player == _winner;
				var resultText = _ruleset.GetVictoryText(player, _winner);
				var resultColor = isWinner ? GameConstants.Match.VictorColor : GameConstants.Match.DefeatColor;

				var column = new Table();

				// Player label
				var playerLabel = string.Format(GameConstants.UI.PlayerLabelFormat, i + 1);
				var playerColor = player.GetComponent<PrototypeSpriteRenderer>().Color;
				column.Add(new Label(playerLabel, font, playerColor,
					GameConstants.UI.TitleScale, GameConstants.UI.TitleScale));
				column.Row();

				// Result text (VICTOR / DEFEAT)
				column.Add(new Label(resultText, font, resultColor,
					GameConstants.Match.ResultScale, GameConstants.Match.ResultScale))
					.SetPadTop(GameConstants.UI.ButtonPadding);
				column.Row();

				columnsTable.Add(column).Expand(true, false).Fill(true, false);
			}

			root.Add(columnsTable).Expand().Fill();
			root.Row();

			// Buttons below
			var buttonStyle = new TextButtonStyle(
				new PrimitiveDrawable(Color.Transparent, GameConstants.UI.ButtonPadding),
				new PrimitiveDrawable(Color.Transparent, GameConstants.UI.ButtonPadding),
				new PrimitiveDrawable(Color.Transparent, GameConstants.UI.ButtonPadding),
				font)
			{
				DownFontColor = Color.DarkGreen,
				OverFontColor = Color.Green
			};

			var rematchButton = new TextButton(GameConstants.Match.RematchText, buttonStyle);
			rematchButton.OnClicked += _ =>
			{
				Time.TimeScale = 1f;
				GorelordsBrawlerGame.TransitionToScene<Scenes.CharacterSelectScene>();
			};
			root.Add(rematchButton).SetPadBottom(GameConstants.UI.ButtonPadding);
			root.Row();

			var mainMenuButton = new TextButton(GameConstants.UI.MainMenuButtonText, buttonStyle);
			mainMenuButton.OnClicked += _ =>
			{
				Time.TimeScale = 1f;
				GorelordsBrawlerGame.TransitionToScene<Scenes.MainMenuScene>();
			};
			root.Add(mainMenuButton).SetPadBottom(GameConstants.UI.ButtonPadding);
		}
	}
}
