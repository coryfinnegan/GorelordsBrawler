using Nez;

namespace GorelordsBrawler.Components.Stats
{
	public class AttackMoveSet : Component
	{
		// ── Ground normals ────────────────────────────────────────────────
		public AttackDefinition Jab;
		public AttackDefinition SideTilt;
		public AttackDefinition UpTilt;
		public AttackDefinition DownTilt;

		// ── Aerial (single aerial attack for all airborne states) ────────
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
