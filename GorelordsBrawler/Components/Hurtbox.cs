using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	public class Hurtbox : Component, ITriggerListener
	{
		private Health _health;
		private PhysicsBody _body;

		public override void OnAddedToEntity()
		{
			_health = Entity.GetComponent<Health>();
			_body = Entity.GetComponent<PhysicsBody>();
		}

		public void OnTriggerEnter(Collider other, Collider local)
		{
			if ((other.PhysicsLayer & PhysicsLayers.Hitbox) == 0) return;

			if (_health.IsDead) return;

			var attackData = other.Entity.GetComponent<AttackData>();
			if (attackData == null) return;

			if (attackData.OwnerEntity == Entity) return;

			_health.TakeDamage(attackData.Damage);

			var knockback = attackData.KnockbackAngle;
			knockback.X *= attackData.FacingDirection;
			if (knockback != Vector2.Zero)
				knockback.Normalize();
			_body.Velocity = knockback * attackData.KnockbackForce;
			_body.Grounded = false;
		}

		public void OnTriggerExit(Collider other, Collider local) { }
	}
}
