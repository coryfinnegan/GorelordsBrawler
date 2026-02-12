using Microsoft.Xna.Framework.Input;
using Nez;
using GorelordsBrawler.Components.UI;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Input;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.Scenes
{
	public class CharacterSelectScene : BaseScene
	{
		private readonly SlotController[] _slots = new SlotController[PlayerManager.MaxPlayers];
		private readonly HeaderRenderer _header;

		// Join listeners — VirtualButtons per device
		private readonly VirtualButton _joinWASD;
		private readonly VirtualButton _joinArrows;
		private readonly VirtualButton[] _joinGamepad;
		private readonly VirtualButton _escButton;

		private float _countdownTimer;
		private bool _countdownActive;
		private bool _transitioning;

		public CharacterSelectScene()
		{
			var font = Content.LoadBitmapFont(Nez.Content.Fonts.GoreFont);
			var titleFont = Content.LoadBitmapFont(Nez.Content.Fonts.Sludgeborn);

			// Join listeners (VirtualButtons — unified input model)
			_joinWASD = new VirtualButton();
			_joinWASD.AddKeyboardKey(Keys.W);
			_joinWASD.AddKeyboardKey(Keys.F);

			_joinArrows = new VirtualButton();
			_joinArrows.AddKeyboardKey(Keys.Up);
			_joinArrows.AddKeyboardKey(Keys.RightControl);

			_joinGamepad = new VirtualButton[GameConstants.Input.MaxGamePads];
			for (int i = 0; i < GameConstants.Input.MaxGamePads; i++)
			{
				_joinGamepad[i] = new VirtualButton();
				_joinGamepad[i].AddGamePadButton(i, Buttons.A);
				_joinGamepad[i].AddGamePadButton(i, Buttons.Start);
			}

			_escButton = new VirtualButton();
			_escButton.AddKeyboardKey(Keys.Escape);

			// Header entity — draws title and status
			var headerEntity = CreateEntity("header");
			_header = headerEntity.AddComponent(new HeaderRenderer(font, titleFont));

			// Slot entities — spaced horizontally across screen
			var characters = GameConstants.Characters.All;
			var slotSpacing = (float)GameConstants.Screen.DesignWidth / PlayerManager.MaxPlayers;
			var slotY = GameConstants.Screen.DesignHeight * 0.35f;

			for (int i = 0; i < PlayerManager.MaxPlayers; i++)
			{
				var x = slotSpacing * (i + 0.5f);
				var slotEntity = CreateEntity($"slot-{i}", new Microsoft.Xna.Framework.Vector2(x, slotY));
				_slots[i] = slotEntity.AddComponent(new SlotController(i, characters));
				slotEntity.AddComponent(new SlotRenderer(font, characters));
			}
		}

		public override void Update()
		{
			if (_transitioning)
			{
				return;
			}

			CheckJoin();
			base.Update();
			UpdateStatus();

			// Countdown
			if (_countdownActive)
			{
				_countdownTimer -= Time.DeltaTime;
				if (_countdownTimer <= 0)
				{
					StartMatch();
				}
			}

			// ESC to go back (only if no one is joined)
			if (_escButton.IsPressed && JoinedCount() == 0)
			{
				GorelordsBrawlerGame.TransitionToScene<MainMenuScene>();
				_transitioning = true;
			}
		}

		private void CheckJoin()
		{
			if (_joinWASD.IsPressed && !IsDeviceJoined(InputDeviceType.KeyboardWASD))
			{
				JoinDevice(InputDeviceType.KeyboardWASD);
			}

			if (_joinArrows.IsPressed && !IsDeviceJoined(InputDeviceType.KeyboardArrows))
			{
				JoinDevice(InputDeviceType.KeyboardArrows);
			}

			for (int i = 0; i < GameConstants.Input.MaxGamePads; i++)
			{
				var device = InputDeviceType.Gamepad0 + i;
				if (_joinGamepad[i].IsPressed && !IsDeviceJoined(device))
				{
					JoinDevice(device);
				}
			}
		}

		private void JoinDevice(InputDeviceType device)
		{
			var emptySlot = FindEmptySlot();
			if (emptySlot == null)
			{
				return;
			}

			var input = InputProfileFactory.CreateFromDevice(device);
			emptySlot.Join(device, input);
			CancelCountdown();
		}

		private SlotController FindEmptySlot()
		{
			for (int i = 0; i < PlayerManager.MaxPlayers; i++)
			{
				if (!_slots[i].IsJoined)
				{
					return _slots[i];
				}
			}
			return null;
		}

		private bool IsDeviceJoined(InputDeviceType device)
		{
			for (int i = 0; i < PlayerManager.MaxPlayers; i++)
			{
				if (_slots[i].IsJoined && _slots[i].Device == device)
				{
					return true;
				}
			}
			return false;
		}

		private int JoinedCount()
		{
			int count = 0;
			for (int i = 0; i < PlayerManager.MaxPlayers; i++)
			{
				if (_slots[i].IsJoined)
				{
					count++;
				}
			}
			return count;
		}

		private void UpdateStatus()
		{
			var joined = JoinedCount();

			if (joined < GameConstants.CharacterSelect.MinPlayers)
			{
				_header.StatusText = GameConstants.CharacterSelect.NeedPlayersText;
				_header.StatusColor = Microsoft.Xna.Framework.Color.Gray;
				CancelCountdown();
				return;
			}

			bool allReady = true;
			for (int i = 0; i < PlayerManager.MaxPlayers; i++)
			{
				if (_slots[i].IsJoined && !_slots[i].IsReady)
				{
					allReady = false;
					break;
				}
			}

			if (allReady)
			{
				_header.StatusText = GameConstants.CharacterSelect.AllReadyText;
				_header.StatusColor = Microsoft.Xna.Framework.Color.Green;
				if (!_countdownActive)
				{
					_countdownActive = true;
					_countdownTimer = GameConstants.CharacterSelect.CountdownDuration;
				}
			}
			else
			{
				_header.StatusText = GameConstants.CharacterSelect.NeedPlayersText;
				_header.StatusColor = Microsoft.Xna.Framework.Color.Gray;
				CancelCountdown();
			}
		}

		private void CancelCountdown()
		{
			_countdownActive = false;
		}

		private void StartMatch()
		{
			_transitioning = true;
			var setup = Core.GetGlobalManager<MatchSetupManager>();
			setup.Clear();

			for (int i = 0; i < PlayerManager.MaxPlayers; i++)
			{
				if (!_slots[i].IsJoined)
				{
					continue;
				}

				setup.Selections.Add(new PlayerSelection
				{
					SlotIndex = i,
					Device = _slots[i].Device,
					CharacterType = GameConstants.Characters.All[_slots[i].CharacterIndex],
				});
			}

			GorelordsBrawlerGame.TransitionToScene<ArenaScene>();
		}

		public override void Unload()
		{
			base.Unload();

			_joinWASD.Deregister();
			_joinArrows.Deregister();
			_escButton.Deregister();

			foreach (var vb in _joinGamepad)
			{
				vb.Deregister();
			}

			foreach (var slot in _slots)
			{
				if (slot.IsJoined)
				{
					slot.Unjoin();
				}
			}
		}
	}
}
