using Nez;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Components.Abilities
{
	public class WalkAbility : Component, IUpdatable
	{
		private readonly InputProfile _input;
		private PhysicsBody _body;
		private MovementStats _movement;

		public WalkAbility(InputProfile input)
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
			var moveDir = _input.MoveX.Value;
			_body.Velocity.X = moveDir * _movement.moveSpeed;
			if (moveDir != 0)
				_body.FacingDirection = moveDir;
		}
	}
}
