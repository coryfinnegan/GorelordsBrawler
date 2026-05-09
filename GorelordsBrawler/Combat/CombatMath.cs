namespace GorelordsBrawler.Combat
{
	/// <summary>Pure stateless combat formulas — no MonoGame or Nez dependencies.</summary>
	public static class CombatMath
	{
		/// <summary>
		/// Knockback multiplier that scales with damage taken.
		/// Returns 1.0 at full HP, 1+scalingFactor at 0 HP.
		/// </summary>
		public static float KnockbackScale(int currentHp, int maxHp, float scalingFactor) =>
			1f + (1f - (float)currentHp / maxHp) * scalingFactor;
	}
}
