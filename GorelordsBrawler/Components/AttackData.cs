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
	}
}
