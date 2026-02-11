using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	public class RespawnHandler : Component, IUpdatable
	{
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
			_waitingToRespawn = true;
			_respawnTimer = GameConstants.Combat.RespawnDelay;
			SetCombatComponentsEnabled(false);
		}

		public void Update()
		{
			if (!_waitingToRespawn) return;

			_respawnTimer -= Time.DeltaTime;
			if (_respawnTimer <= 0)
			{
				_health.Reset();
				Entity.Transform.Position = _spawnPosition;
				Entity.GetComponent<PhysicsBody>().Velocity = Vector2.Zero;
				SetCombatComponentsEnabled(true);
				_waitingToRespawn = false;
			}
		}

		private void SetCombatComponentsEnabled(bool enabled)
		{
			// Disable individual components, NOT Entity.SetEnabled(),
			// so RespawnHandler and Health stay active.

			Entity.GetComponent<PhysicsBody>()?.SetEnabled(enabled);
			Entity.GetComponent<Hurtbox>()?.SetEnabled(enabled);
			Entity.GetComponent<HealthBar>()?.SetEnabled(enabled);
			Entity.GetComponent<PrototypeSpriteRenderer>()?.SetEnabled(enabled);

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
