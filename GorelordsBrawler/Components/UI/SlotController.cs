using Nez;
using GorelordsBrawler.Input;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.Components.UI
{
	public class SlotController : Component, IUpdatable
	{
		public int SlotIndex { get; }
		public InputProfile Input { get; private set; }
		public InputDeviceType Device { get; private set; }
		public int CharacterIndex { get; private set; }
		public bool IsReady { get; private set; }
		public bool IsJoined => Input != null;

		private readonly string[] _characters;
		private bool _skipFrame;

		public SlotController(int slotIndex, string[] characters)
		{
			SlotIndex = slotIndex;
			_characters = characters;
		}

		public void Join(InputDeviceType device, InputProfile input)
		{
			Device = device;
			Input = input;
			CharacterIndex = 0;
			IsReady = false;
			_skipFrame = true;
		}

		public void Unjoin()
		{
			if (Input != null)
			{
				Input.Deregister();
				Input = null;
			}
			IsReady = false;
		}

		public void Update()
		{
			if (!IsJoined)
			{
				return;
			}

			if (_skipFrame)
			{
				_skipFrame = false;
				return;
			}

			if (IsReady)
			{
				if (Input.Attack.IsPressed)
				{
					IsReady = false;
				}
			}
			else
			{
				// Cycle character using VirtualIntegerAxis built-in edge detection
				var dir = Input.MoveX.DirectionJustPushed;
				if (dir != 0)
				{
					CharacterIndex += dir;
					if (CharacterIndex < 0)
					{
						CharacterIndex = _characters.Length - 1;
					}
					if (CharacterIndex >= _characters.Length)
					{
						CharacterIndex = 0;
					}
				}

				// Ready up on Attack
				if (Input.Attack.IsPressed)
				{
					IsReady = true;
				}

				// Unjoin on Jump
				if (Input.Jump.IsPressed)
				{
					Unjoin();
				}
			}
		}
	}
}
