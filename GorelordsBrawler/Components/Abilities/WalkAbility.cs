using Nez;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Components.Abilities
{
	public class WalkAbility : Component, IUpdatable
	{
		private readonly InputProfile _input;
		private PhysicsBody _body;
		private CharacterStats _stats;

		public WalkAbility(InputProfile input)
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
			var moveDir = _input.MoveX.Value;
			_body.Velocity.X = moveDir * _stats.moveSpeed;
			if (moveDir != 0)
				_body.FacingDirection = moveDir;
		}
	}
}
