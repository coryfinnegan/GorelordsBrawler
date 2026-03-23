using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.Components
{
	public class Hurtbox : Component, ITriggerListener
	{
		private Health _health;
		private PhysicsBody _body;
		private Hitstun _hitstun;
		private HurtboxZoneTracker _zoneTracker;
		private CombatEffectsManager _effectsManager;

		public override void OnAddedToEntity()
		{
			_health = Entity.GetComponent<Health>();
			_body = Entity.GetComponent<PhysicsBody>();
			_hitstun = Entity.GetComponent<Hitstun>();
			_zoneTracker = Entity.GetComponent<HurtboxZoneTracker>();
		}

		public void OnTriggerEnter(Collider other, Collider local)
		{
			if ((other.PhysicsLayer & PhysicsLayers.Hitbox) == 0) return;

			if (_health.IsDead) return;

			var attackData = other.Entity.GetComponent<AttackData>();
			if (attackData == null) return;

			if (attackData.OwnerEntity == Entity) return;

			// Identify which zone was hit (for future damage multipliers / limb removal)
			string hitZone = _zoneTracker?.GetZoneName(local);

			_health.TakeDamage(attackData.Damage);

			// Scale knockback by damage already taken — near-death characters go flying
			var damageRatio = 1f - (float)_health.CurrentHp / _health.MaxHp;
			var knockbackScale = 1f + damageRatio * GameConstants.Combat.KnockbackScaling;

			var knockback = attackData.KnockbackAngle;
			knockback.X *= attackData.FacingDirection;
			if (knockback != Vector2.Zero)
			{
				knockback.Normalize();
			}
			_body.Velocity = knockback * attackData.KnockbackForce * knockbackScale;
			_body.Grounded = false;

			_hitstun?.Trigger(attackData.HitstunDuration);

			if (_effectsManager == null)
			{
				_effectsManager = Entity.Scene.GetSceneComponent<CombatEffectsManager>();
			}
			_effectsManager?.TriggerHit(Entity, attackData.KnockbackForce * knockbackScale);
		}

		public void OnTriggerExit(Collider other, Collider local) { }
	}
}
