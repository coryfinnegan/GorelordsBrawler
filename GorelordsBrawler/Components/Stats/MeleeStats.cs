using Microsoft.Xna.Framework;
using Nez;
using Nez.Persistence;

namespace GorelordsBrawler.Components.Stats
{
	public class MeleeStats : Component
	{
		[Inspectable] [Range(0, 100)]
		public int Damage = 20;

		[Inspectable] [Range(0, 1000)]
		public float KnockbackForce = 300f;

		[Inspectable] [Range(-1, 1)]
		public float KnockbackAngleX = 1f;

		[Inspectable] [Range(-1, 1)]
		public float KnockbackAngleY = -0.5f;

		[JsonExclude]
		public Vector2 KnockbackAngle => new Vector2(KnockbackAngleX, KnockbackAngleY);

		[Inspectable] [Range(0, 100)]
		public float HitboxWidth = 40f;

		[Inspectable] [Range(0, 100)]
		public float HitboxHeight = 30f;

		[Inspectable] [Range(0, 100)]
		public float HitboxOffsetX = 30f;

		[Inspectable] [Range(-50, 50)]
		public float HitboxOffsetY = 0f;

		[Inspectable] [Range(0, 2)]
		public float HitstunDuration = 0.3f;

		[Inspectable] [Range(0, 2)]
		public float Cooldown = 0.5f;

		[Inspectable] [Range(0, 1)]
		public float HitboxDuration = 0.15f;

		/// <summary>
		/// First animation frame on which the hitbox becomes active (-1 = use HitboxDuration timer instead).
		/// </summary>
		[Inspectable] [Range(-1, 200)]
		public int ActiveStartFrame = -1;

		/// <summary>
		/// Last animation frame on which the hitbox is active (-1 = use HitboxDuration timer instead).
		/// </summary>
		[Inspectable] [Range(-1, 200)]
		public int ActiveEndFrame = -1;
	}
}
