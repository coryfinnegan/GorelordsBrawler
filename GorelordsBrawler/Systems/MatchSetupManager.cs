using System.Collections.Generic;
using Nez;

namespace GorelordsBrawler.Systems
{
	public enum InputDeviceType
	{
		KeyboardWASD,
		KeyboardArrows,
		Gamepad0,
		Gamepad1,
		Gamepad2,
		Gamepad3,
	}

	public class PlayerSelection
	{
		public int SlotIndex;
		public InputDeviceType Device;
		public string CharacterType;
	}

	public class MatchSetupManager : GlobalManager
	{
		public List<PlayerSelection> Selections { get; } = new();

		public void Clear() => Selections.Clear();
	}
}
