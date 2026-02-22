using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	public class PhysicsBody : Component, IUpdatable
	{
		public Vector2 Velocity;
		public bool Grounded;
		public int FacingDirection = 1;

		private Mover _mover;
		private MovementStats _movement;

		public override void OnAddedToEntity()
		{
			_mover = Entity.GetComponent<Mover>();
			_movement = Entity.GetComponent<MovementStats>();
			UpdateOrder = GameConstants.Physics.PhysicsBodyUpdateOrder;
		}

		public void Update()
		{
			// Gravity is universal
			Velocity.Y += _movement.Gravity * Time.DeltaTime;

			// Move with collision
			var motion = Velocity * Time.DeltaTime;
			var collided = _mover.Move(motion, out var collisionResult);

			if (collided)
			{
				if (collisionResult.Normal.Y < GameConstants.Physics.GroundNormalThreshold)
				{
					Grounded = true;
					Velocity.Y = 0;
				}

				if (collisionResult.Normal.Y > GameConstants.Physics.CeilingNormalThreshold)
				{
					Velocity.Y = 0;
				}
			}
			else
			{
				Grounded = false;
			}
		}
	}
}
