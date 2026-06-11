using System;
using System.Collections.Generic;
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
		/// Optional per-entity damage multiplier (Phase B: acid depth scaling).
		/// Returns 1 for the base rate; &gt;1 deeper, etc. Null = flat
		/// <see cref="DamagePerSecond"/> for everyone. Evaluated every frame for
		/// each overlapping entity, so it can track a moving body's depth.
		/// </summary>
		public Func<Entity, float> DamageScaleForEntity;

		/// <summary>
		/// Fired once per overlapping entity each time damage is actually
		/// applied (i.e. after that entity's integer-damage buffer crosses 1).
		/// Used by per-hazard feedback systems (e.g. AcidSizzleManager) to drop a
		/// puff at the player and tint their sprite without coupling
		/// ContactHazard itself to any specific visual layer. Listeners
		/// must not block — fired synchronously inside Update().
		/// </summary>
		public event Action<Entity> OnDamageApplied;

		// Per-entity fractional damage accumulators. Was a single shared float,
		// but with depth scaling two players in the acid take damage at DIFFERENT
		// rates — a shared buffer would mis-truncate (one player's accrual leaking
		// into the other's integer crossing). Keyed by entity; pruned when an
		// entity stops overlapping so it can't grow unbounded across respawns.
		private readonly Dictionary<Entity, float> _damageBuffers = new();
		private static readonly Collider[] _overlapBuffer = new Collider[16];
		private static readonly List<Entity> _seenThisFrame = new(16);

		public void Update()
		{
			if (GetBounds == null) return;

			var bounds = GetBounds();
			if (bounds.Width <= 0 || bounds.Height <= 0)
			{
				if (_damageBuffers.Count > 0) _damageBuffers.Clear();
				return;
			}
			int count = Physics.OverlapRectangleAll(ref bounds, _overlapBuffer, PhysicsLayers.Player);
			if (count == 0)
			{
				if (_damageBuffers.Count > 0) _damageBuffers.Clear();
				return;
			}

			float dt = Time.DeltaTime;
			_seenThisFrame.Clear();

			for (int i = 0; i < count; i++)
			{
				var entity = _overlapBuffer[i].Entity;
				if (entity == null) continue;
				var health = entity.GetComponent<Health>();
				if (health == null || health.IsDead) continue;

				_seenThisFrame.Add(entity);

				float scale = DamageScaleForEntity?.Invoke(entity) ?? 1f;
				_damageBuffers.TryGetValue(entity, out float buffer);
				buffer += DamagePerSecond * scale * dt;

				int dmg = (int)buffer;
				_damageBuffers[entity] = buffer - dmg;
				if (dmg < 1) continue;

				health.TakeDamage(dmg);

				if (KnockbackForce > 0)
				{
					var body = entity.GetComponent<PhysicsBody>();
					if (body != null) body.Velocity += KnockbackDirection * KnockbackForce;
				}

				OnDamageApplied?.Invoke(entity);
			}

			PruneStaleBuffers();
		}

		// Drop accumulators for entities that are no longer overlapping so the
		// dictionary tracks only live contacts (and a re-entry starts fresh rather
		// than inheriting a stale fractional carry).
		private void PruneStaleBuffers()
		{
			if (_damageBuffers.Count == _seenThisFrame.Count) return;
			// Collect-then-remove to avoid mutating during enumeration.
			var stale = ListPool<Entity>.Obtain();
			foreach (var key in _damageBuffers.Keys)
			{
				if (!_seenThisFrame.Contains(key)) stale.Add(key);
			}
			for (int i = 0; i < stale.Count; i++)
			{
				_damageBuffers.Remove(stale[i]);
			}
			ListPool<Entity>.Free(stale);
		}
	}
}
