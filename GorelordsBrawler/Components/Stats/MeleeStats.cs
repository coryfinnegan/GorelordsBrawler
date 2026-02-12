using Microsoft.Xna.Framework;
using Nez;
using Nez.Persistence;

namespace GorelordsBrawler.Components.Stats
{
	public class MeleeStats : Component
	{
		[Inspectable] [Range(0, 100)]
		public int damage = 20;

		[Inspectable] [Range(0, 1000)]
		public float knockbackForce = 300f;

		[Inspectable] [Range(-1, 1)]
		public float knockbackAngleX = 1f;

		[Inspectable] [Range(-1, 1)]
		public float knockbackAngleY = -0.5f;

		[JsonExclude]
		public Vector2 KnockbackAngle => new Vector2(knockbackAngleX, knockbackAngleY);

		[Inspectable] [Range(0, 100)]
		public float hitboxWidth = 40f;

		[Inspectable] [Range(0, 100)]
		public float hitboxHeight = 30f;

		[Inspectable] [Range(0, 100)]
		public float hitboxOffsetX = 30f;

		[Inspectable] [Range(0, 2)]
		public float cooldown = 0.5f;

		[Inspectable] [Range(0, 1)]
		public float hitboxDuration = 0.15f;
	}
}
