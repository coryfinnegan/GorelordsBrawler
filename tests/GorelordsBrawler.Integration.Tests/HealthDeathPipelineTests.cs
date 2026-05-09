using GorelordsBrawler.Components;
using GorelordsBrawler.Constants;
using Xunit;

namespace GorelordsBrawler.Integration.Tests;

/// <summary>
/// Integration tests for the Health → StockTracker → elimination pipeline.
/// Wires components via events, the same contract the game uses in production.
/// No Entity/Scene context required — components are wired manually.
/// </summary>
public class HealthDeathPipelineTests
{
	private static (Health health, StockTracker stocks) MakePlayer(int maxHp = 100, int stocks = 3)
	{
		var health = new Health { MaxHp = maxHp };
		health.CurrentHp = maxHp;

		var tracker = new StockTracker();
		tracker.Reset(stocks);

		// Wire death → lose stock (mirrors RespawnHandler behavior)
		health.OnDeath += () => tracker.LoseStock();

		return (health, tracker);
	}

	[Fact]
	public void Death_TriggersStockLoss()
	{
		var (health, stocks) = MakePlayer(maxHp: 100, stocks: 3);
		health.TakeDamage(100);
		Assert.Equal(2, stocks.RemainingStocks);
	}

	[Fact]
	public void ThreeDeaths_EliminatesPlayer()
	{
		var (health, stocks) = MakePlayer(maxHp: 50, stocks: 3);

		for (int i = 0; i < 3; i++)
		{
			health.Reset();
			health.TakeDamage(999);
		}

		Assert.True(stocks.IsEliminated);
		Assert.Equal(0, stocks.RemainingStocks);
	}

	[Fact]
	public void TwoDeaths_DoesNotEliminate_WithThreeStocks()
	{
		var (health, stocks) = MakePlayer(maxHp: 100, stocks: 3);

		health.TakeDamage(100); health.Reset();
		health.TakeDamage(100);

		Assert.False(stocks.IsEliminated);
		Assert.Equal(1, stocks.RemainingStocks);
	}

	[Fact]
	public void Respawn_AfterDeath_AllowsDamageAgain()
	{
		var (health, stocks) = MakePlayer();
		health.TakeDamage(100);
		Assert.True(health.IsDead);

		health.Reset();
		Assert.False(health.IsDead);

		health.TakeDamage(30);
		Assert.Equal(70, health.CurrentHp);
	}

	[Fact]
	public void DamageWhileDead_DoesNotTriggerExtraStockLoss()
	{
		var (health, stocks) = MakePlayer(stocks: 3);
		health.TakeDamage(100);           // death → 2 stocks
		health.TakeDamage(50);            // already dead — ignored
		Assert.Equal(2, stocks.RemainingStocks);
	}

	[Fact]
	public void TwoPlayers_DeathsAreIndependent()
	{
		var (h1, s1) = MakePlayer(stocks: 3);
		var (h2, s2) = MakePlayer(stocks: 3);

		h1.TakeDamage(100);

		Assert.Equal(2, s1.RemainingStocks);
		Assert.Equal(3, s2.RemainingStocks);
	}

	[Fact]
	public void OnEliminated_FiredExactlyOnce_AfterThreeDeaths()
	{
		var (health, stocks) = MakePlayer(maxHp: 10, stocks: 3);
		int count = 0;
		stocks.OnEliminated += () => count++;

		for (int i = 0; i < 3; i++)
		{
			health.Reset();
			health.TakeDamage(999);
		}

		Assert.Equal(1, count);
	}

	[Fact]
	public void OnStockLost_Reports_CorrectRemainingCount_InSequence()
	{
		var (health, stocks) = MakePlayer(maxHp: 50, stocks: 3);
		var counts = new System.Collections.Generic.List<int>();
		stocks.OnStockLost += r => counts.Add(r);

		health.TakeDamage(999); health.Reset();
		health.TakeDamage(999); health.Reset();
		health.TakeDamage(999);

		Assert.Equal(new[] { 2, 1, 0 }, counts);
	}

	[Fact]
	public void FourPlayerMatch_LastPlayerStanding_NotEliminated()
	{
		var players = new (Health h, StockTracker s)[4];
		for (int i = 0; i < 4; i++)
			players[i] = MakePlayer(maxHp: 30, stocks: 1);

		// Eliminate 3 players
		for (int i = 0; i < 3; i++)
			players[i].h.TakeDamage(999);

		int eliminated = 0, alive = 0;
		foreach (var (_, s) in players)
		{
			if (s.IsEliminated) eliminated++;
			else alive++;
		}

		Assert.Equal(3, eliminated);
		Assert.Equal(1, alive);
	}

	[Fact]
	public void HealAfterDamage_DoesNotReverseStockLoss()
	{
		var (health, stocks) = MakePlayer(stocks: 3);
		health.TakeDamage(100);
		int stocksAfterDeath = stocks.RemainingStocks;

		health.Reset();
		health.Heal(999);  // fully healed

		// Stocks don't go back
		Assert.Equal(stocksAfterDeath, stocks.RemainingStocks);
	}
}
