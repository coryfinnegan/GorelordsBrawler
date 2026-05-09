using Nez;

namespace GorelordsBrawler.Components.Stats
{
	public class AttackMoveSet : Component
	{
		public AttackDefinition Jab;

		// ── Crouch (ground, while holding down) ───────────────────────────
		public AttackDefinition CrouchAttack;

		// ── Heavy (ground, Special button) ────────────────────────────────
		public AttackDefinition Heavy;

		// ── Aerial ────────────────────────────────────────────────────────
		public AttackDefinition NeutralAir;

		/// <summary>
		/// Creates an AttackMoveSet from a legacy MeleeStats component.
		/// Maps all MeleeStats values to the Jab slot so existing characters
		/// work unchanged through the new CombatController.
		/// </summary>
		public static AttackMoveSet FromMeleeStats(MeleeStats melee)
		{
			var moveSet = new AttackMoveSet
			{
				Jab = new AttackDefinition
				{
					Damage = melee.Damage,
					KnockbackForce = melee.KnockbackForce,
					KnockbackAngleX = melee.KnockbackAngleX,
					KnockbackAngleY = melee.KnockbackAngleY,
					HitboxWidth = melee.HitboxWidth,
					HitboxHeight = melee.HitboxHeight,
					HitboxOffsetX = melee.HitboxOffsetX,
					HitboxOffsetY = melee.HitboxOffsetY,
					HitstunDuration = melee.HitstunDuration,
					Cooldown = melee.Cooldown,
					HitboxDuration = melee.HitboxDuration,
					ActiveStartFrame = melee.ActiveStartFrame,
					ActiveEndFrame = melee.ActiveEndFrame,
					AnimationSuffix = null, // null = use legacy LeftHand/RightHand animations
					MovementMultiplier = 0f,
				}
			};
			return moveSet;
		}
	}
}
