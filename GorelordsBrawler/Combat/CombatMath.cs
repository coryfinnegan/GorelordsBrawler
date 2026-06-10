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

		/// <summary>
		/// Acid damage multiplier as a function of submersion depth (px below the
		/// local surface). At depth ≤ 0 the body is at/above the surface → 1× (the
		/// base chip rate). It ramps linearly to <paramref name="deepMult"/> at
		/// <paramref name="fullSubmergeDepth"/> and saturates there. Linear (not
		/// curved) so the "act now" threshold reads predictably to the player:
		/// every extra inch of depth is a proportional extra bite.
		/// </summary>
		/// <param name="depthPx">Feet-below-local-surface in pixels (0 when dry/at surface).</param>
		/// <param name="deepMult">Multiplier once fully submerged (≥ 1).</param>
		/// <param name="fullSubmergeDepth">Depth (px) at which the multiplier saturates (&gt; 0).</param>
		public static float AcidDpsMultiplier(float depthPx, float deepMult, float fullSubmergeDepth)
		{
			if (depthPx <= 0f || fullSubmergeDepth <= 0f)
			{
				return 1f;
			}
			float t = depthPx / fullSubmergeDepth;
			if (t > 1f)
			{
				t = 1f;
			}
			return 1f + (deepMult - 1f) * t;
		}
	}
}
