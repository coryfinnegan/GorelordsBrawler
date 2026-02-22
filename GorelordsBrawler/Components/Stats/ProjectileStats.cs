using Microsoft.Xna.Framework;
using Nez;
using Nez.Persistence;

namespace GorelordsBrawler.Components.Stats
{
	public class ProjectileStats : Component
	{
		[Inspectable] [Range(0, 800)]
		public float Speed = 400f;

		[Inspectable] [Range(0, 50)]
		public float Width = 12f;

		[Inspectable] [Range(0, 50)]
		public float Height = 6f;

		[Inspectable] [Range(0, 100)]
		public int Damage = 15;

		[Inspectable] [Range(0, 1000)]
		public float KnockbackForce = 200f;

		[Inspectable] [Range(-1, 1)]
		public float KnockbackAngleX = 1f;

		[Inspectable] [Range(-1, 1)]
		public float KnockbackAngleY = -0.2f;

		[JsonExclude]
		public Vector2 KnockbackAngle => new Vector2(KnockbackAngleX, KnockbackAngleY);

		[Inspectable] [Range(0, 5)]
		public float MaxLifetime = 1.5f;

		[Inspectable] [Range(0, 2)]
		public float Cooldown = 0.7f;
	}
}
