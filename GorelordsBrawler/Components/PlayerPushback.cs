using System;
using Nez;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Applies a soft repulsion force between overlapping player characters.
	/// Players can still pass through each other, but overlapping bodies
	/// nudge apart so hitboxes don't overshoot at close range.
	/// </summary>
	public class PlayerPushback : Component, IUpdatable
	{
		private PhysicsBody _body;
		private BoxCollider _collider;

		public override void OnAddedToEntity()
		{
			_body = Entity.GetComponent<PhysicsBody>();
			_collider = Entity.GetComponent<BoxCollider>();
		}

		public void Update()
		{
			if (_collider == null || _body == null)
			{
				return;
			}

			// Find all overlapping Player-layer colliders
			var neighbors = Physics.BoxcastBroadphaseExcludingSelf(
				_collider, _collider.CollidesWithLayers | PhysicsLayers.Player);

			foreach (var other in neighbors)
			{
				if ((other.PhysicsLayer & PhysicsLayers.Player) == 0)
				{
					continue;
				}

				if (other.Entity == Entity)
				{
					continue;
				}

				var otherBody = other.Entity.GetComponent<PhysicsBody>();
				if (otherBody == null)
				{
					continue;
				}

				// Check actual overlap
				if (!_collider.Overlaps(other))
				{
					continue;
				}

				float myX = Entity.Transform.Position.X;
				float otherX = other.Entity.Transform.Position.X;
				float diff = myX - otherX;

				// If exactly on top of each other, push based on facing direction
				if (Math.Abs(diff) < 0.1f)
				{
					diff = _body.FacingDirection;
				}

				float pushDir = Math.Sign(diff);
				float pushForce = GameConstants.Combat.PlayerPushbackForce * Time.DeltaTime;

				_body.Velocity.X += pushDir * pushForce;
				otherBody.Velocity.X -= pushDir * pushForce;
			}
		}
	}
}
