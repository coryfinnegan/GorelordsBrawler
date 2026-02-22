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

		[Inspectable] [Range(0, 2)]
		public float Cooldown = 0.5f;

		[Inspectable] [Range(0, 1)]
		public float HitboxDuration = 0.15f;
	}
}
