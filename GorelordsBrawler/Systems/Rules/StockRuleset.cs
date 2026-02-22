using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;
using Nez.BitmapFonts;
using Nez.UI;
using GorelordsBrawler.Components;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems.Rules
{
	public class StockRuleset : IMatchRuleset
	{
		private List<Entity> _players;
		private Action<Entity> _onMatchWon;
		private AnnouncementAccess _announcements;
		private readonly Dictionary<Entity, Label> _stockLabels = new();

		public void Initialize(List<Entity> players, Action<Entity> onMatchWon, AnnouncementAccess announcements)
		{
			_players = players;
			_onMatchWon = onMatchWon;
			_announcements = announcements;

			foreach (var player in _players)
			{
				player.AddComponent(new StockTracker());

				var stockTracker = player.GetComponent<StockTracker>();
				stockTracker.OnEliminated += () => OnPlayerEliminated(player);
			}
		}

		public bool OnPlayerDeath(Entity player)
		{
			var stockTracker = player.GetComponent<StockTracker>();
			if (stockTracker == null) return true;

			return stockTracker.LoseStock();
		}

		public void BuildHUD(Table root, BitmapFont font, List<Entity> players)
		{
			root.Top().Left().Pad(GameConstants.UI.StockHUDPadding);
			var scale = GameConstants.UI.StockHUDScale;

			for (int i = 0; i < players.Count; i++)
			{
				var player = players[i];
				var stockTracker = player.GetComponent<StockTracker>();

				// Player label
				var playerLabel = string.Format(GameConstants.UI.PlayerLabelFormat, i + 1);
				root.Add(new Label(playerLabel, font, player.GetComponent<CharacterStats>().BodyColor, scale))
					.SetPadRight(GameConstants.UI.StockSpacing);

				// Stock hearts label
				var stockText = BuildStockText(stockTracker.RemainingStocks);
				var stockLabel = new Label(stockText, font, Color.Red, scale);
				root.Add(stockLabel).SetPadRight(GameConstants.UI.StockHUDPadding);

				_stockLabels[player] = stockLabel;

				// Subscribe to stock changes
				var capturedPlayer = player;
				stockTracker.OnStockLost += remaining => OnStockChanged(capturedPlayer, remaining);
				stockTracker.OnEliminated += () => OnPlayerEliminatedHUD(capturedPlayer);
			}
		}

		public string GetVictoryText(Entity player, Entity winner)
		{
			return player == winner ? GameConstants.Match.VictorText : GameConstants.Match.DefeatText;
		}

		public void Update()
		{
			// Stock mode is event-driven, no per-frame work needed
		}

		public void Shutdown()
		{
			// Scene teardown handles component cleanup
		}

		private void OnPlayerEliminated(Entity player)
		{
			// Show K.O. announcement
			_announcements.Show(GameConstants.Match.KOText, GameConstants.Match.AnnouncementColor);

			// Count remaining alive players
			int aliveCount = 0;
			Entity lastAlive = null;
			foreach (var p in _players)
			{
				if (!p.GetComponent<StockTracker>().IsEliminated)
				{
					aliveCount++;
					lastAlive = p;
				}
			}

			if (aliveCount <= 1)
			{
				_onMatchWon?.Invoke(lastAlive);
			}
		}

		private void OnStockChanged(Entity player, int remaining)
		{
			if (_stockLabels.TryGetValue(player, out var label))
				label.SetText(BuildStockText(remaining));
		}

		private void OnPlayerEliminatedHUD(Entity player)
		{
			if (_stockLabels.TryGetValue(player, out var label))
			{
				label.SetText("X");
				label.GetStyle().FontColor = Color.Gray;
			}
		}

		private string BuildStockText(int stocks)
		{
			if (stocks <= 0) return "X";
			return new string(GameConstants.UI.StockIndicator[0], stocks);
		}
	}
}
