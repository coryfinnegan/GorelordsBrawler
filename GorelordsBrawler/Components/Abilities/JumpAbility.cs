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
		private Hitstun _hitstun;

		public JumpAbility(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_body = Entity.GetComponent<PhysicsBody>();
			_movement = Entity.GetComponent<MovementStats>();
			_hitstun = Entity.GetComponent<Hitstun>();
		}

		public void Update()
		{
			if (_hitstun != null && _hitstun.IsActive)
			{
				return;
			}

			// Can jump if grounded OR within coyote time window
			var canJump = _body.Grounded || _body.TimeSinceGrounded <= _movement.CoyoteTime;

			if (canJump && _input.Jump.IsPressed)
			{
				_body.Velocity.Y = -_movement.JumpSpeed;
				_body.Grounded = false;
				_body.TimeSinceGrounded = _movement.CoyoteTime + 1f; // exhaust coyote time
				_body.JumpHeld = true;
				_input.Jump.ConsumeBuffer();
			}

			// Release jump early = short hop (PhysicsBody applies ShortHopMultiplier)
			if (_body.JumpHeld && !_input.Jump.IsDown)
			{
				_body.JumpHeld = false;
			}

			// Clear jump held at apex (start of descent)
			if (_body.JumpHeld && _body.Velocity.Y >= 0)
			{
				_body.JumpHeld = false;
			}
		}
	}
}
