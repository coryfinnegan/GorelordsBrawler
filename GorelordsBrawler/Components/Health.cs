using System;
using Nez;

namespace GorelordsBrawler.Components
{
	public class Health : Component
	{
		[Inspectable] [Range(0, 500)]
		public int MaxHp;

		[Inspectable]
		public int CurrentHp;

		public bool IsDead => CurrentHp <= 0;

		public event Action<int> OnDamaged;
		public event Action OnDeath;

		public void TakeDamage(int amount)
		{
			if (IsDead) return;
			CurrentHp = Math.Max(0, CurrentHp - amount);
			OnDamaged?.Invoke(amount);
			if (IsDead)
				OnDeath?.Invoke();
		}

		public void Heal(int amount)
		{
			CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
		}

		public void Reset()
		{
			CurrentHp = MaxHp;
		}
	}
}
