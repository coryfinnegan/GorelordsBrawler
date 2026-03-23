using Nez;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Tracks a stun window after being hit. Ability components (WalkAbility,
	/// JumpAbility, MeleeAttack) check IsActive and suppress input while stunned,
	/// which also preserves knockback velocity that would otherwise be zeroed by
	/// WalkAbility on the frame after impact.
	/// </summary>
	public class Hitstun : Component, IUpdatable
	{
		private float _timer;
		private float _duration;

		public bool IsActive => _timer > 0;

		/// <summary>The full duration of the current hitstun (for animation speed scaling).</summary>
		public float Duration => _duration;

		public void Trigger(float duration)
		{
			if (duration > _timer)
			{
				_timer = duration;
				_duration = duration;
			}
		}

		public void Update()
		{
			if (_timer > 0)
			{
				_timer -= Time.DeltaTime;
			}
		}
	}
}
