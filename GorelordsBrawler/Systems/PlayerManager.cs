using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Data;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Systems
{
	public class PlayerManager : SceneComponent
	{
		public const int MaxPlayers = 4;

		private readonly PlayerSlot[] _slots = new PlayerSlot[MaxPlayers];

		public Entity AddPlayer(int slotIndex, InputProfile input, string characterType, Vector2 spawnPosition)
		{
			if (_slots[slotIndex] != null)
				RemovePlayer(slotIndex);

			var player = CharacterFactory.Create(Scene, characterType, input, spawnPosition);
			player.Name = $"player{slotIndex + 1}";

			_slots[slotIndex] = new PlayerSlot
			{
				SlotIndex = slotIndex,
				Input = input,
				PlayerEntity = player,
				CharacterType = characterType,
			};

			return player;
		}

		public void RemovePlayer(int slotIndex)
		{
			var slot = _slots[slotIndex];
			if (slot == null)
				return;

			slot.Input.Deregister();
			slot.PlayerEntity.Destroy();
			_slots[slotIndex] = null;
		}

		public List<Entity> GetActivePlayers()
		{
			var players = new List<Entity>();
			for (int i = 0; i < MaxPlayers; i++)
			{
				if (_slots[i] != null)
					players.Add(_slots[i].PlayerEntity);
			}
			return players;
		}

		/// <summary>
		/// Active slots (entity + its InputProfile together). Use this when a
		/// scene-level system needs to attach an input-driven component to each
		/// player after creation — e.g. SwimAbility, whose acid dependency only
		/// exists at scene scope so it can't be wired in CharacterFactory.
		/// </summary>
		public List<PlayerSlot> GetActiveSlots()
		{
			var slots = new List<PlayerSlot>();
			for (int i = 0; i < MaxPlayers; i++)
			{
				if (_slots[i] != null)
					slots.Add(_slots[i]);
			}
			return slots;
		}

		public override void OnRemovedFromScene()
		{
			for (int i = 0; i < MaxPlayers; i++)
				RemovePlayer(i);
		}
	}
}
