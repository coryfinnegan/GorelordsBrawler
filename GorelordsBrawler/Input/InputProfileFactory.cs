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

		public static InputProfile CreateKeyboardWASD()
		{
			return new InputProfile
			{
				MoveX = new VirtualIntegerAxis()
					.AddKeyboardKeys(VirtualInput.OverlapBehavior.TakeNewer, Keys.A, Keys.D),
				Jump = new VirtualButton(GameConstants.Input.JumpBufferTime)
					.AddKeyboardKey(Keys.W),
				Attack = new VirtualButton()
					.AddKeyboardKey(Keys.F),
			};
		}

		public static InputProfile CreateKeyboardArrows()
		{
			return new InputProfile
			{
				MoveX = new VirtualIntegerAxis()
					.AddKeyboardKeys(VirtualInput.OverlapBehavior.TakeNewer, Keys.Left, Keys.Right),
				Jump = new VirtualButton(GameConstants.Input.JumpBufferTime)
					.AddKeyboardKey(Keys.Up),
				Attack = new VirtualButton()
					.AddKeyboardKey(Keys.RightControl),
			};
		}

		public static InputProfile CreateGamepad(int gamepadIndex)
		{
			return new InputProfile
			{
				MoveX = new VirtualIntegerAxis()
					.AddGamePadLeftStickX(gamepadIndex)
					.AddGamePadDPadLeftRight(gamepadIndex),
				Jump = new VirtualButton(GameConstants.Input.JumpBufferTime)
					.AddGamePadButton(gamepadIndex, Buttons.A),
				Attack = new VirtualButton()
					.AddGamePadButton(gamepadIndex, Buttons.X),
			};
		}
	}
}
