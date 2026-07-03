using System;
using Microsoft.Xna.Framework;
using Nez;
using Nez.Sprites;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.Components
{
	public class RespawnHandler : Component, IUpdatable
	{
		/// <summary>
		/// Optional flood-aware spawn picker (Phase C). When set, respawn asks it
		/// for a position at the moment of respawn — so a player who dies during
		/// a flood comes back on dry ground instead of inside the acid. Null =
		/// the fixed construction-time spawn (non-acid scenes unchanged).
		/// Installed as a closure by ArenaScene — the acid dependency only
		/// exists at scene level, same pattern as the depth-damage closure.
		/// </summary>
		public Func<Vector2> SafeSpawnProvider;

		private readonly Vector2 _spawnPosition;
		private Health _health;
		private float _respawnTimer;
		private bool _waitingToRespawn;

		public RespawnHandler(Vector2 spawnPosition)
		{
			_spawnPosition = spawnPosition;
		}

		public override void OnAddedToEntity()
		{
			_health = Entity.GetComponent<Health>();
			_health.OnDeath += OnDeath;
		}

		public override void OnRemovedFromEntity()
		{
			if (_health != null)
				_health.OnDeath -= OnDeath;
		}

		private void OnDeath()
		{
			Entity.GetComponent<Abilities.LedgeHangAbility>()?.ForceRelease();
			SetCombatComponentsEnabled(false);

			// Delegate to MatchManager if present; otherwise always respawn
			var matchManager = Entity.Scene.GetSceneComponent<MatchManager>();
			bool shouldRespawn = matchManager?.NotifyPlayerDeath(Entity) ?? true;

			if (!shouldRespawn)
			{
				// Eliminated — no respawn
				return;
			}

			_waitingToRespawn = true;
			_respawnTimer = GameConstants.Combat.RespawnDelay;
		}

		public void Update()
		{
			if (!_waitingToRespawn) return;

			_respawnTimer -= Time.DeltaTime;
			if (_respawnTimer <= 0)
			{
				_health.Reset();
				Entity.Transform.Position = SafeSpawnProvider?.Invoke() ?? _spawnPosition;
				Entity.GetComponent<PhysicsBody>().Velocity = Vector2.Zero;
				SetCombatComponentsEnabled(true);
				_waitingToRespawn = false;
			}
		}

		public void SetCombatComponentsEnabled(bool enabled)
		{
			// Disable individual components, NOT Entity.SetEnabled(),
			// so RespawnHandler and Health stay active.

			Entity.GetComponent<PhysicsBody>()?.SetEnabled(enabled);
			Entity.GetComponent<Hurtbox>()?.SetEnabled(enabled);
			Entity.GetComponent<HealthBar>()?.SetEnabled(enabled);
			Entity.GetComponent<SpriteRenderer>()?.SetEnabled(enabled);

			// Disable all abilities
			var updatables = Entity.GetComponents<IUpdatable>();
			foreach (var updatable in updatables)
			{
				if (updatable is Component c && c != this && c is not Health)
					c.SetEnabled(enabled);
			}

			// Disable all colliders
			var colliders = Entity.GetComponents<Collider>();
			foreach (var collider in colliders)
				collider.SetEnabled(enabled);
		}
	}
}
