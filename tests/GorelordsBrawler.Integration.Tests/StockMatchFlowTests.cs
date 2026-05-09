using GorelordsBrawler.Components;
using GorelordsBrawler.Constants;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GorelordsBrawler.Integration.Tests;

/// <summary>
/// Integration tests for stock-based match flow across multiple players.
/// Uses manually-wired Health + StockTracker components.
/// Exercises the same elimination logic that StockRuleset drives in production.
/// </summary>
public class StockMatchFlowTests
{
	private record Player(Health Health, StockTracker Stocks);

	private static List<Player> CreateMatch(int playerCount, int maxHp = 100, int stocks = 3)
	{
		var players = new List<Player>(playerCount);
		for (int i = 0; i < playerCount; i++)
		{
			var h = new Health { MaxHp = maxHp };
			h.CurrentHp = maxHp;
			var s = new StockTracker();
			s.Reset(stocks);
			h.OnDeath += () => s.LoseStock();
			players.Add(new Player(h, s));
		}
		return players;
	}

	private static int AliveCount(List<Player> players) =>
		players.Count(p => !p.Stocks.IsEliminated);

	private static void Kill(Player p)
	{
		p.Health.TakeDamage(9999);
		p.Health.Reset();
	}

	// ── 2-player ──────────────────────────────────────────────────────────────

	[Fact]
	public void TwoPlayer_WinnerIsLastAlive()
	{
		var match = CreateMatch(2, stocks: 1);
		Kill(match[0]);
		Assert.True(match[0].Stocks.IsEliminated);
		Assert.False(match[1].Stocks.IsEliminated);
		Assert.Equal(1, AliveCount(match));
	}

	[Fact]
	public void TwoPlayer_SimultaneousElimination_CanHappenInEdgeCase()
	{
		var match = CreateMatch(2, stocks: 1);
		Kill(match[0]);
		Kill(match[1]);
		Assert.Equal(0, AliveCount(match));
	}

	// ── 3-player ──────────────────────────────────────────────────────────────

	[Fact]
	public void ThreePlayer_TwoEliminatedLeaveOneAlive()
	{
		var match = CreateMatch(3, stocks: 2);
		for (int i = 0; i < 2; i++) Kill(match[0]);  // eliminate P1
		for (int i = 0; i < 2; i++) Kill(match[1]);  // eliminate P2

		Assert.True(match[0].Stocks.IsEliminated);
		Assert.True(match[1].Stocks.IsEliminated);
		Assert.False(match[2].Stocks.IsEliminated);
		Assert.Equal(1, AliveCount(match));
	}

	// ── 4-player ──────────────────────────────────────────────────────────────

	[Fact]
	public void FourPlayer_ProgressiveElimination_SequenceIsCorrect()
	{
		var match = CreateMatch(4, stocks: 1);

		Assert.Equal(4, AliveCount(match));
		Kill(match[3]); Assert.Equal(3, AliveCount(match));
		Kill(match[2]); Assert.Equal(2, AliveCount(match));
		Kill(match[1]); Assert.Equal(1, AliveCount(match));

		var winner = match.Single(p => !p.Stocks.IsEliminated);
		Assert.Same(match[0].Stocks, winner.Stocks);
	}

	// ── Stock counting ────────────────────────────────────────────────────────

	[Fact]
	public void StocksAreConsumedBeforeElimination()
	{
		var match  = CreateMatch(2, stocks: 3);
		var player = match[0];

		Kill(player); Assert.Equal(2, player.Stocks.RemainingStocks);
		Kill(player); Assert.Equal(1, player.Stocks.RemainingStocks);
		Assert.False(player.Stocks.IsEliminated);
		Kill(player); Assert.True(player.Stocks.IsEliminated);
	}

	[Fact]
	public void DefaultStockCount_IsThree_MatchesGameConstants()
	{
		var match = CreateMatch(1);
		Assert.Equal(GameConstants.Combat.DefaultStockCount, match[0].Stocks.RemainingStocks);
	}

	// ── Events ────────────────────────────────────────────────────────────────

	[Fact]
	public void AllEliminationEvents_Fire_DuringMatch()
	{
		var match   = CreateMatch(3, stocks: 1);
		int events  = 0;
		foreach (var p in match)
			p.Stocks.OnEliminated += () => events++;

		foreach (var p in match) Kill(p);

		Assert.Equal(3, events);
	}

	[Fact]
	public void OnStockLost_FiresEachTime_WithCorrectCount()
	{
		var p    = CreateMatch(1, stocks: 3)[0];
		var seen = new List<int>();
		p.Stocks.OnStockLost += r => seen.Add(r);

		Kill(p); Kill(p); Kill(p);

		Assert.Equal(new[] { 2, 1, 0 }, seen);
	}
}
