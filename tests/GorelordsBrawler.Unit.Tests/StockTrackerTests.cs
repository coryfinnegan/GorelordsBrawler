using GorelordsBrawler.Components;
using GorelordsBrawler.Constants;
using Xunit;

namespace GorelordsBrawler.Unit.Tests;

/// <summary>
/// Unit tests for StockTracker component pure logic.
/// No Core.Instance or Entity context required.
/// </summary>
public class StockTrackerTests
{
	[Fact]
	public void DefaultStockCount_MatchesGameConstants()
	{
		var tracker = new StockTracker();
		Assert.Equal(GameConstants.Combat.DefaultStockCount, tracker.RemainingStocks);
	}

	[Fact]
	public void IsEliminated_FalseWhenStocksRemain()
	{
		var tracker = new StockTracker();
		Assert.False(tracker.IsEliminated);
	}

	[Fact]
	public void LoseStock_DecrementsRemainingStocks()
	{
		var tracker = new StockTracker();
		int before = tracker.RemainingStocks;
		tracker.LoseStock();
		Assert.Equal(before - 1, tracker.RemainingStocks);
	}

	[Fact]
	public void LoseStock_ReturnsTrueWhileStocksRemain()
	{
		var tracker = new StockTracker();
		tracker.Reset(3);
		Assert.True(tracker.LoseStock());  // 3→2
		Assert.True(tracker.LoseStock());  // 2→1
	}

	[Fact]
	public void LoseStock_ReturnsFalseOnLastStock()
	{
		var tracker = new StockTracker();
		tracker.Reset(1);
		bool result = tracker.LoseStock();
		Assert.False(result);
		Assert.True(tracker.IsEliminated);
	}

	[Fact]
	public void LoseStock_WhenAlreadyEliminated_ReturnsFalse()
	{
		var tracker = new StockTracker();
		tracker.Reset(1);
		tracker.LoseStock();   // eliminates
		bool result = tracker.LoseStock();  // already eliminated
		Assert.False(result);
		Assert.Equal(0, tracker.RemainingStocks);
	}

	[Fact]
	public void LoseStock_FiresOnStockLost_WithRemainingCount()
	{
		var tracker = new StockTracker();
		tracker.Reset(3);
		int remaining = -1;
		tracker.OnStockLost += r => remaining = r;
		tracker.LoseStock();
		Assert.Equal(2, remaining);
	}

	[Fact]
	public void LoseStock_OnFinalStock_FiresOnEliminated()
	{
		var tracker = new StockTracker();
		tracker.Reset(1);
		bool eliminated = false;
		tracker.OnEliminated += () => eliminated = true;
		tracker.LoseStock();
		Assert.True(eliminated);
	}

	[Fact]
	public void LoseStock_OnNonFinalStock_DoesNotFireOnEliminated()
	{
		var tracker = new StockTracker();
		tracker.Reset(2);
		bool eliminated = false;
		tracker.OnEliminated += () => eliminated = true;
		tracker.LoseStock();  // 2→1
		Assert.False(eliminated);
	}

	[Fact]
	public void Reset_SetsRemainingStocks()
	{
		var tracker = new StockTracker();
		tracker.Reset(1);
		tracker.LoseStock();
		Assert.True(tracker.IsEliminated);
		tracker.Reset(3);
		Assert.False(tracker.IsEliminated);
		Assert.Equal(3, tracker.RemainingStocks);
	}

	[Fact]
	public void FullDepletion_SequenceIsCorrect()
	{
		var tracker = new StockTracker();
		tracker.Reset(3);
		var lostCounts = new System.Collections.Generic.List<int>();
		tracker.OnStockLost += r => lostCounts.Add(r);

		tracker.LoseStock();
		tracker.LoseStock();
		tracker.LoseStock();

		Assert.Equal(new[] { 2, 1, 0 }, lostCounts);
		Assert.True(tracker.IsEliminated);
	}
}
