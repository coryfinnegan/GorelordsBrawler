using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;

namespace GorelordsBrawler.Components
{
	public class AttackData : Component
	{
		public Entity OwnerEntity;
		public int Damage;
		public float KnockbackForce;
		public Vector2 KnockbackAngle;
		public int FacingDirection;
		public float HitstunDuration;

		/// <summary>Tracks which entities this attack has already hit (prevents multi-hit).</summary>
		public HashSet<Entity> HitTargets = new();
	}
}
