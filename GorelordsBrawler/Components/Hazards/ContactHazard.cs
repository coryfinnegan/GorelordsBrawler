using System;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components.Hazards
{
	public class ContactHazard : Component, IUpdatable
	{
		public float DamagePerSecond;
		public float KnockbackForce;
		public Vector2 KnockbackDirection = new Vector2(0, -1);
		public Func<RectangleF> GetBounds;

		/// <summary>
		/// Fired once per overlapping entity each time damage is actually
		/// applied (i.e. after the integer-damage buffer crosses 1). Used by
		/// per-hazard feedback systems (e.g. AcidSizzleManager) to drop a
		/// puff at the player and tint their sprite without coupling
		/// ContactHazard itself to any specific visual layer. Listeners
		/// must not block — fired synchronously inside Update().
		/// </summary>
		public event Action<Entity> OnDamageApplied;

		private float _damageBuffer;
		private static readonly Collider[] _overlapBuffer = new Collider[16];

		public void Update()
		{
			if (GetBounds == null) return;

			var bounds = GetBounds();
			if (bounds.Width <= 0 || bounds.Height <= 0) return;
			int count = Physics.OverlapRectangleAll(ref bounds, _overlapBuffer, PhysicsLayers.Player);
			if (count == 0) return;

			_damageBuffer += DamagePerSecond * Time.DeltaTime;
			int dmg = (int)_damageBuffer;
			if (dmg < 1) return;
			_damageBuffer -= dmg;

			for (int i = 0; i < count; i++)
			{
				var entity = _overlapBuffer[i].Entity;
				var health = entity?.GetComponent<Health>();
				if (health == null || health.IsDead) continue;

				health.TakeDamage(dmg);

				if (KnockbackForce > 0)
				{
					var body = entity.GetComponent<PhysicsBody>();
					if (body != null) body.Velocity += KnockbackDirection * KnockbackForce;
				}

				OnDamageApplied?.Invoke(entity);
			}
		}
	}
}
