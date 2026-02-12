using System;
using GorelordsBrawler.Constants;
using Nez;

namespace GorelordsBrawler.Components
{
	public class StockTracker : Component
	{
		[Inspectable]
		public int RemainingStocks { get; private set; }

		public bool IsEliminated => RemainingStocks <= 0;

		public event Action<int> OnStockLost;
		public event Action OnEliminated;

		public StockTracker()
		{
			RemainingStocks = GameConstants.Combat.DefaultStockCount;
		}

		/// <summary>
		/// Removes one stock. Returns true if stocks remain, false if eliminated.
		/// </summary>
		public bool LoseStock()
		{
			if (IsEliminated) return false;

			RemainingStocks--;
			OnStockLost?.Invoke(RemainingStocks);

			if (IsEliminated)
			{
				OnEliminated?.Invoke();
				return false;
			}

			return true;
		}

		public void Reset(int count)
		{
			RemainingStocks = count;
		}
	}
}
