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
		private LocomotionAnimator _locomotion;
		private CombatController _combat;
		private LedgeHangAbility _ledge;
		private SubmersionFeel _submersion;

		public JumpAbility(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_body = Entity.GetComponent<PhysicsBody>();
			_movement = Entity.GetComponent<MovementStats>();
			_hitstun = Entity.GetComponent<Hitstun>();
			_locomotion = Entity.GetComponent<LocomotionAnimator>();
		}

		public void Update()
		{
			// Lazy resolve: components may be added after JumpAbility
			if (_combat == null)
			{
				_combat = Entity.GetComponent<CombatController>();
			}
			if (_ledge == null)
			{
				_ledge = Entity.GetComponent<LedgeHangAbility>();
			}
			if (_submersion == null)
			{
				// May be absent (non-acid scenes) — only resolved, never required.
				_submersion = Entity.GetComponent<SubmersionFeel>();
			}

			// Suppress jumping while on a ledge (LedgeHangAbility handles jump-release)
			if (_ledge != null && _ledge.IsOnLedge)
			{
				return;
			}

			// Suppress ALL jumping while submerged — the jump button belongs to
			// SwimAbility underwater (which performs the same full-strength jump,
			// but without consuming the aerial action and with the hitstun gate
			// the water demands). SubmersionFeel banks HasAerialAction every wet
			// frame, so a press in a momentarily-dry surface-bob frame falls
			// through to the double-jump branch below — a full-strength exit
			// either way, never a dead input.
			if (_submersion != null && _submersion.IsSubmerged)
			{
				return;
			}

			if (_hitstun != null && _hitstun.IsActive)
			{
				return;
			}

			// Block jumping during ground attacks (aerials are fine — already airborne)
			if (_locomotion != null && _locomotion.IsPlayingAttack
				&& _body.Grounded && _combat != null && _combat.CurrentAttack != null)
			{
				return;
			}

			// Can ground-jump if grounded OR within coyote time window
			var canGroundJump = _body.Grounded || _body.TimeSinceGrounded <= _movement.CoyoteTime;

			if (canGroundJump && _input.Jump.IsPressed)
			{
				_body.Velocity.Y = -_movement.JumpSpeed;
				_body.Grounded = false;
				_body.TimeSinceGrounded = _movement.CoyoteTime + 1f; // exhaust coyote time
				_body.JumpHeld = true;
				_body.HasAerialAction = true;
				_input.Jump.ConsumeBuffer();
			}
			else if (!canGroundJump && _input.Jump.IsPressed && _body.HasAerialAction)
			{
				// Double jump — consumes the aerial action
				_body.HasAerialAction = false;
				_body.Velocity.Y = -_movement.JumpSpeed;
				_body.JumpHeld = true;
				_body.FastFalling = false;
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

			// Fast fall: tap down while airborne and falling (or at apex)
			if (!_body.Grounded && !_body.FastFalling && _body.Velocity.Y >= 0
				&& _input.MoveY != null && _input.MoveY.Value > 0)
			{
				_body.FastFalling = true;
			}
		}
	}
}
