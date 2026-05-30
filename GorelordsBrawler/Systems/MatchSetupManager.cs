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

		// Automation devices (DEBUG-only): input is driven over HTTP by the E2E harness,
		// not by physical hardware. Scripted0/Scripted1 map to ScriptedInputRegistry
		// player indices 0/1. Defined in all builds so the enum stays stable; release
		// builds never select them (CreateFromDevice handles them under #if DEBUG).
		Scripted0,
		Scripted1,
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
