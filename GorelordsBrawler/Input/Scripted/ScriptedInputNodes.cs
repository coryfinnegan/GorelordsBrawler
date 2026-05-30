#if DEBUG
using System;
using Nez;

namespace GorelordsBrawler.Input.Scripted
{
	/// <summary>
	/// A <see cref="VirtualButton.Node"/> whose down-state is driven programmatically.
	///
	/// <see cref="VirtualButton.Update"/> calls <c>node.Update()</c> and THEN reads
	/// <c>IsPressed</c>/<c>IsReleased</c>, so we must compute the rising/falling edge inside
	/// <see cref="Update"/> and latch booleans for the getters to return. This mirrors how
	/// Nez's <c>KeyboardKey</c> node reports edges from <c>Input</c>'s per-frame scan — which
	/// is exactly why a consumer like <c>CombatController</c> can't tell scripted input from a
	/// real key: buffering, edge detection and the attack-buffer window all behave identically.
	/// </summary>
	public sealed class ScriptedButtonNode : VirtualButton.Node
	{
		private readonly Func<bool> _read;
		private bool _down, _prevDown, _pressed, _released;

		public ScriptedButtonNode(Func<bool> read) => _read = read;

		public override void Update()
		{
			_down     = _read();
			_pressed  = _down && !_prevDown;
			_released = !_down && _prevDown;
			_prevDown = _down;
		}

		public override bool IsDown     => _down;
		public override bool IsPressed  => _pressed;
		public override bool IsReleased => _released;
	}

	/// <summary>
	/// A <see cref="VirtualAxis.Node"/> whose value is driven programmatically.
	///
	/// The <see cref="VirtualAxis.Node"/> base already derives <c>DirectionJustPushed</c> /
	/// <c>JustPushed</c> from <c>Value</c> across frames (via its own <c>Update</c> that tracks
	/// <c>_previousValue</c>), so we only supply the current scripted value.
	/// </summary>
	public sealed class ScriptedAxisNode : VirtualAxis.Node
	{
		private readonly Func<int> _read;

		public ScriptedAxisNode(Func<int> read) => _read = read;

		public override float Value => _read();
	}
}
#endif
