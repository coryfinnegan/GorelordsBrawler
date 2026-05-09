using Nez;
using GorelordsBrawler.Input;

namespace GorelordsBrawler.Components.Abilities
{
	public class CrouchAbility : Component, IUpdatable
	{
		private readonly InputProfile _input;
		private PhysicsBody _body;

		public bool IsCrouching { get; private set; }

		public CrouchAbility(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_body = Entity.GetComponent<PhysicsBody>();
		}

		public void Update()
		{
			IsCrouching = _body.Grounded && _input.MoveY.Value > 0;
		}
	}
}
