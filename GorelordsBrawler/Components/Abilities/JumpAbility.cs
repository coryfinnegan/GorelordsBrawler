using Nez;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Components.Abilities
{
	public class JumpAbility : Component, IUpdatable
	{
		private readonly InputProfile _input;
		private PhysicsBody _body;
		private MovementStats _movement;

		public JumpAbility(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_body = Entity.GetComponent<PhysicsBody>();
			_movement = Entity.GetComponent<MovementStats>();
		}

		public void Update()
		{
			if (_body.Grounded && _input.Jump.IsPressed)
			{
				_body.Velocity.Y = -_movement.jumpSpeed;
				_body.Grounded = false;
				_input.Jump.ConsumeBuffer();
			}
		}
	}
}
