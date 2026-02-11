using Nez;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Components.Abilities
{
	public class JumpAbility : Component, IUpdatable
	{
		private readonly InputProfile _input;
		private PhysicsBody _body;
		private CharacterStats _stats;

		public JumpAbility(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_body = Entity.GetComponent<PhysicsBody>();
			_stats = Entity.GetComponent<CharacterStats>();
		}

		public void Update()
		{
			if (_body.Grounded && _input.Jump.IsPressed)
			{
				_body.Velocity.Y = -_stats.jumpSpeed;
				_body.Grounded = false;
				_input.Jump.ConsumeBuffer();
			}
		}
	}
}
