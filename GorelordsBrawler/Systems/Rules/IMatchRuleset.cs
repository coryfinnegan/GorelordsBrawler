using System;
using System.Collections.Generic;
using Nez;
using Nez.BitmapFonts;
using Nez.UI;

namespace GorelordsBrawler.Systems.Rules
{
	public interface IMatchRuleset
	{
		/// <summary>
		/// Attach mode-specific components, subscribe to events.
		/// Call onMatchWon(winner) when a winner is determined.
		/// </summary>
		void Initialize(List<Entity> players, Action<Entity> onMatchWon, AnnouncementAccess announcements);

		/// <summary>
		/// Populate the HUD table with mode-specific widgets.
		/// </summary>
		void BuildHUD(Table root, BitmapFont font, List<Entity> players);

		/// <summary>
		/// Called every frame during Active state.
		/// </summary>
		void Update();

		/// <summary>
		/// Called when a player dies. Returns true = respawn, false = stay dead.
		/// </summary>
		bool OnPlayerDeath(Entity player);

		/// <summary>
		/// Victory text for the given player (winner or loser).
		/// </summary>
		string GetVictoryText(Entity player, Entity winner);

		/// <summary>
		/// Cleanup.
		/// </summary>
		void Shutdown();
	}

	/// <summary>
	/// Provides ruleset access to the announcement system without exposing MatchManager internals.
	/// </summary>
	public class AnnouncementAccess
	{
		private readonly Action<string, Microsoft.Xna.Framework.Color> _showAnnouncement;

		public AnnouncementAccess(Action<string, Microsoft.Xna.Framework.Color> showAnnouncement)
		{
			_showAnnouncement = showAnnouncement;
		}

		public void Show(string text, Microsoft.Xna.Framework.Color color) => _showAnnouncement(text, color);
	}
}
