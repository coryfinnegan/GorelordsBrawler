using Microsoft.Xna.Framework;
using Nez.Persistence;

namespace GorelordsBrawler.Components.Stats
{
	public class AttackDefinition
	{
		public int Damage = 20;
		public float KnockbackForce = 300f;
		public float KnockbackAngleX = 1f;
		public float KnockbackAngleY = -0.5f;

		[JsonExclude]
		public Vector2 KnockbackAngle => new Vector2(KnockbackAngleX, KnockbackAngleY);

		public float HitboxWidth = 40f;
		public float HitboxHeight = 30f;
		public float HitboxOffsetX = 30f;
		public float HitboxOffsetY = 0f;
		public float HitstunDuration = 0.3f;
		public float Cooldown = 0.5f;
		public float HitboxDuration = 0.15f;

		/// <summary>
		/// First animation frame on which the hitbox becomes active (-1 = use HitboxDuration timer instead).
		/// </summary>
		public int ActiveStartFrame = -1;

		/// <summary>
		/// Last animation frame on which the hitbox is active (-1 = use HitboxDuration timer instead).
		/// </summary>
		public int ActiveEndFrame = -1;

		/// <summary>
		/// Animation suffix used to build the animation key (e.g. "Jab" -> "{Prefix}_Jab").
		/// If null or empty, falls back to the legacy LeftHand/RightHand attack animations.
		/// </summary>
		public string AnimationSuffix;

		/// <summary>
		/// How much ground movement speed is preserved during this attack.
		/// 0 = frozen, 0.3 = slow drift, 1.0 = full speed (used for aerials).
		/// </summary>
		public float MovementMultiplier = 0.3f;
	}
}
