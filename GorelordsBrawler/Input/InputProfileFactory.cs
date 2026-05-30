using System;
using Microsoft.Xna.Framework.Input;
using Nez;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.Input
{
	public static class InputProfileFactory
	{
		public static InputProfile CreateFromDevice(InputDeviceType device)
		{
#if DEBUG
			// Automation devices: route to a scripted profile whose VirtualInput nodes read
			// from ScriptedInputRegistry. Handled before the switch so the scripted factory
			// (also DEBUG-only) doesn't need to appear in a release-compiled switch arm.
			if (device == InputDeviceType.Scripted0)
			{
				return CreateScripted(0);
			}
			if (device == InputDeviceType.Scripted1)
			{
				return CreateScripted(1);
			}
#endif
			return device switch
			{
				InputDeviceType.KeyboardWASD => CreateKeyboardWASD(),
				InputDeviceType.KeyboardArrows => CreateKeyboardArrows(),
				InputDeviceType.Gamepad0 => CreateGamepad(0),
				InputDeviceType.Gamepad1 => CreateGamepad(1),
				InputDeviceType.Gamepad2 => CreateGamepad(2),
				InputDeviceType.Gamepad3 => CreateGamepad(3),
				_ => throw new ArgumentException($"Unknown device type: {device}")
			};
		}

#if DEBUG
		/// <summary>
		/// Build a scripted input profile for E2E automation. Every node reads from the
		/// shared <see cref="Scripted.ScriptedInputRegistry"/> entry for this player, which the
		/// debug server writes via <c>POST /input</c>. Because these are ordinary VirtualInput
		/// nodes, gameplay components see them exactly as keyboard/gamepad input.
		/// </summary>
		public static InputProfile CreateScripted(int playerIndex)
		{
			var s = Scripted.ScriptedInputRegistry.ForPlayer(playerIndex);
			return new InputProfile
			{
				MoveX   = new VirtualIntegerAxis(new Scripted.ScriptedAxisNode(() => s.MoveX)),
				MoveY   = new VirtualIntegerAxis(new Scripted.ScriptedAxisNode(() => s.MoveY)),
				Jump    = new VirtualButton(GameConstants.Input.JumpBufferTime,
					new Scripted.ScriptedButtonNode(() => s.Jump)),
				Attack  = new VirtualButton(new Scripted.ScriptedButtonNode(() => s.Attack)),
				Special = new VirtualButton(new Scripted.ScriptedButtonNode(() => s.Special)),
			};
		}
#endif

		public static InputProfile CreateKeyboardWASD()
		{
			return new InputProfile
			{
				MoveX = new VirtualIntegerAxis()
					.AddKeyboardKeys(VirtualInput.OverlapBehavior.TakeNewer, Keys.A, Keys.D),
				MoveY = new VirtualIntegerAxis()
					.AddKeyboardKeys(VirtualInput.OverlapBehavior.TakeNewer, Keys.W, Keys.S),
				Jump = new VirtualButton(GameConstants.Input.JumpBufferTime)
					.AddKeyboardKey(Keys.W),
				Attack = new VirtualButton()
					.AddKeyboardKey(Keys.F),
				Special = new VirtualButton()
					.AddKeyboardKey(Keys.G),
			};
		}

		public static InputProfile CreateKeyboardArrows()
		{
			return new InputProfile
			{
				MoveX = new VirtualIntegerAxis()
					.AddKeyboardKeys(VirtualInput.OverlapBehavior.TakeNewer, Keys.Left, Keys.Right),
				MoveY = new VirtualIntegerAxis()
					.AddKeyboardKeys(VirtualInput.OverlapBehavior.TakeNewer, Keys.Up, Keys.Down),
				Jump = new VirtualButton(GameConstants.Input.JumpBufferTime)
					.AddKeyboardKey(Keys.Up),
				Attack = new VirtualButton()
					.AddKeyboardKey(Keys.RightControl),
				Special = new VirtualButton()
					.AddKeyboardKey(Keys.RightShift),
			};
		}

		public static InputProfile CreateGamepad(int gamepadIndex)
		{
			return new InputProfile
			{
				MoveX = new VirtualIntegerAxis()
					.AddGamePadLeftStickX(gamepadIndex)
					.AddGamePadDPadLeftRight(gamepadIndex),
				MoveY = new VirtualIntegerAxis()
					.AddGamePadLeftStickY(gamepadIndex)
					.AddGamePadDPadUpDown(gamepadIndex),
				Jump = new VirtualButton(GameConstants.Input.JumpBufferTime)
					.AddGamePadButton(gamepadIndex, Buttons.A),
				Attack = new VirtualButton()
					.AddGamePadButton(gamepadIndex, Buttons.X),
				Special = new VirtualButton()
					.AddGamePadButton(gamepadIndex, Buttons.Y),
			};
		}
	}
}
