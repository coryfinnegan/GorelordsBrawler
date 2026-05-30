#if DEBUG
using System.Collections.Concurrent;

namespace GorelordsBrawler.Input.Scripted
{
	/// <summary>
	/// Thread-safe per-player input state for the scripted (automation) input device.
	///
	/// Written on the game thread (via the <see cref="GorelordsBrawler.DevTools.DebugControl"/>
	/// command queue, which the HTTP debug-server thread feeds) and read by the scripted
	/// <see cref="ScriptedButtonNode"/>/<see cref="ScriptedAxisNode"/> during Nez's per-frame
	/// input update. The fields are simple scalars marked <c>volatile</c> so a read is always
	/// coherent even on the off chance a value is written from a non-game thread.
	///
	/// DEBUG-only: release builds have no automation surface at all.
	/// </summary>
	public sealed class ScriptedInputState
	{
		public volatile int  MoveX;   // -1 / 0 / 1
		public volatile int  MoveY;   // -1 / 0 / 1
		public volatile bool Jump;
		public volatile bool Attack;
		public volatile bool Special;

		public void Reset()
		{
			MoveX   = 0;
			MoveY   = 0;
			Jump    = false;
			Attack  = false;
			Special = false;
		}
	}

	/// <summary>
	/// Maps a player index to its <see cref="ScriptedInputState"/>. The scripted input device
	/// (built in <see cref="InputProfileFactory.CreateScripted"/>) and the debug server both
	/// resolve the same state through here, so a <c>POST /input</c> for player 0 lands on the
	/// VirtualInput nodes wired into player 0's profile.
	/// </summary>
	public static class ScriptedInputRegistry
	{
		private static readonly ConcurrentDictionary<int, ScriptedInputState> _states = new();

		public static ScriptedInputState ForPlayer(int playerIndex)
			=> _states.GetOrAdd(playerIndex, _ => new ScriptedInputState());

		public static void ClearAll()
		{
			foreach (var s in _states.Values)
			{
				s.Reset();
			}
		}
	}
}
#endif
