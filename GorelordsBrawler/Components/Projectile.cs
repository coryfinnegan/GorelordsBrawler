using Microsoft.Xna.Framework;
using Nez;

namespace GorelordsBrawler.Components
{
	public class Projectile : Component, IUpdatable
	{
		private readonly Vector2 _velocity;
		private readonly float _maxLifetime;
		private ProjectileMover _mover;

		public Projectile(Vector2 velocity, float maxLifetime)
		{
			_velocity = velocity;
			_maxLifetime = maxLifetime;
		}

		public override void OnAddedToEntity()
		{
			_mover = Entity.GetComponent<ProjectileMover>();

			Core.Schedule(_maxLifetime, timer =>
			{
				if (Entity != null && !Entity.IsDestroyed)
					Entity.Destroy();
			});
		}

		public void Update()
		{
			if (_mover.Move(_velocity * Time.DeltaTime))
				Entity.Destroy();
		}
	}
}
