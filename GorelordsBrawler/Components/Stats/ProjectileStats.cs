using Microsoft.Xna.Framework;
using Nez;
using Nez.Persistence;

namespace GorelordsBrawler.Components.Stats
{
	public class ProjectileStats : Component
	{
		[Inspectable] [Range(0, 800)]
		public float speed = 400f;

		[Inspectable] [Range(0, 50)]
		public float width = 12f;

		[Inspectable] [Range(0, 50)]
		public float height = 6f;

		[Inspectable] [Range(0, 100)]
		public int damage = 15;

		[Inspectable] [Range(0, 1000)]
		public float knockbackForce = 200f;

		[Inspectable] [Range(-1, 1)]
		public float knockbackAngleX = 1f;

		[Inspectable] [Range(-1, 1)]
		public float knockbackAngleY = -0.2f;

		[JsonExclude]
		public Vector2 KnockbackAngle => new Vector2(knockbackAngleX, knockbackAngleY);

		[Inspectable] [Range(0, 5)]
		public float maxLifetime = 1.5f;

		[Inspectable] [Range(0, 2)]
		public float cooldown = 0.7f;
	}
}
