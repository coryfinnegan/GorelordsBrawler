using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.Components
{
	public class Hurtbox : Component, ITriggerListener
	{
		/// <summary>
		/// Monotonic count of distinct melee hits this hurtbox has registered. Incremented
		/// exactly once per connection (at the per-attack dedup point below) and ONLY on the
		/// melee path — contact hazards like acid call <see cref="Health.TakeDamage"/> directly
		/// and never touch this. The E2E harness reads it as a trustworthy "a hit landed" oracle
		/// that an HP delta (contaminated by acid damage-over-time) can't provide.
		/// </summary>
		public int HitsTaken { get; private set; }

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

			// Skip if this attack already hit us (persists for the attack's lifetime)
			if (!attackData.HitTargets.Add(Entity))
			{
				return;
			}

			// A new, distinct melee hit has connected — record it before any damage/knockback
			// so the count reflects "connections" even if HP is already at the floor.
			HitsTaken++;

			// Identify which zone was hit (for future damage multipliers / limb removal)
			string hitZone = _zoneTracker?.GetZoneName(local);

			_health.TakeDamage(attackData.Damage);

			// Scale knockback by damage already taken — near-death characters go flying
			var knockbackScale = GorelordsBrawler.Combat.CombatMath.KnockbackScale(
				_health.CurrentHp, _health.MaxHp, GameConstants.Combat.KnockbackScaling);

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
			var hitPosition = (other.AbsolutePosition + local.AbsolutePosition) / 2f;
			_effectsManager?.TriggerHit(Entity, attackData.KnockbackForce * knockbackScale,
				hitPosition, knockback);
		}

		public void OnTriggerExit(Collider other, Collider local) { }
	}
}
