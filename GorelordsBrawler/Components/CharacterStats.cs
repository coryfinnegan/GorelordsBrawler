using Microsoft.Xna.Framework;
using Nez;
using Nez.Persistence;

namespace GorelordsBrawler.Components
{
	public class CharacterStats : Component
	{
		// Identity
		[NotInspectable]
		public string name;

		[NotInspectable]
		public string description;

		// Movement
		[Inspectable] [Range(0, 500)]
		public float moveSpeed = 100f;

		[Inspectable] [Range(0, 600)]
		public float jumpSpeed = 250f;

		[Inspectable] [Range(0, 2000)]
		public float gravity = 900f;

		// Melee
		[Inspectable] [Range(0, 2)]
		public float attackCooldown = 0.5f;

		[Inspectable] [Range(0, 1)]
		public float hitboxDuration = 0.15f;

		[Inspectable] [Range(0, 100)]
		public float hitboxWidth = 40f;

		[Inspectable] [Range(0, 100)]
		public float hitboxHeight = 30f;

		[Inspectable] [Range(0, 100)]
		public float hitboxOffsetX = 30f;

		// Combat
		[Inspectable] [Range(0, 500)]
		public int maxHp = 100;

		[Inspectable] [Range(0, 100)]
		public int meleeDamage = 20;

		[Inspectable] [Range(0, 1000)]
		public float meleeKnockbackForce = 300f;

		[Inspectable] [Range(-1, 1)]
		public float meleeKnockbackAngleX = 1f;

		[Inspectable] [Range(-1, 1)]
		public float meleeKnockbackAngleY = -0.5f;

		[JsonExclude]
		public Vector2 MeleeKnockbackAngle => new Vector2(meleeKnockbackAngleX, meleeKnockbackAngleY);

		// Body
		[Inspectable] [Range(8, 128)]
		public float bodyWidth = 32f;

		[Inspectable] [Range(8, 128)]
		public float bodyHeight = 48f;

		// Color
		[Inspectable] [Range(0, 255)]
		public int colorR = 50;

		[Inspectable] [Range(0, 255)]
		public int colorG = 120;

		[Inspectable] [Range(0, 255)]
		public int colorB = 50;

		[JsonExclude]
		public Color BodyColor => new Color(colorR, colorG, colorB);
	}
}
