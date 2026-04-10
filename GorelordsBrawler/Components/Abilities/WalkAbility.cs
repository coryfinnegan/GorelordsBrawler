using Microsoft.Xna.Framework;
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
		private LocomotionAnimator _locomotion;
		private Hitstun _hitstun;
		private CombatController _combat;

		public WalkAbility(InputProfile input)
		{
			_input = input;
		}

		public override void OnAddedToEntity()
		{
			_body = Entity.GetComponent<PhysicsBody>();
			_movement = Entity.GetComponent<MovementStats>();
			_locomotion = Entity.GetComponent<LocomotionAnimator>();
			_hitstun = Entity.GetComponent<Hitstun>();
		}

		public void Update()
		{
			// Lazy resolve: CombatController may be added after WalkAbility
			if (_combat == null)
			{
				_combat = Entity.GetComponent<CombatController>();
			}

			// During hitstun: preserve knockback velocity, apply ground friction to decelerate
			if (_hitstun != null && _hitstun.IsActive)
			{
				if (_body.Grounded)
				{
					_body.Velocity.X = MathHelper.Lerp(_body.Velocity.X, 0f, 10f * Time.DeltaTime);
				}
				return;
			}

			// During attacks: aerials allow full air control, ground attacks use MovementMultiplier
			if (_locomotion != null && _locomotion.IsPlayingAttack && _combat != null && _combat.CurrentAttack != null)
			{
				if (_body.Grounded)
				{
					float mult = _combat.CurrentAttack.MovementMultiplier;
					if (mult <= 0f)
					{
						_body.Velocity.X = 0;
						return;
					}
					// Apply reduced movement during ground attacks
					var moveDir = _input.MoveX.Value;
					if (moveDir != 0)
					{
						float targetVelocity = moveDir * _movement.MoveSpeed * mult;
						_body.Velocity.X = Mathf.Approach(
							_body.Velocity.X, targetVelocity, _movement.GroundAcceleration * Time.DeltaTime);
					}
					else
					{
						_body.Velocity.X = Mathf.Approach(
							_body.Velocity.X, 0f, _movement.GroundFriction * Time.DeltaTime);
					}
					return;
				}
				// Airborne attacks: fall through to normal air movement (full control)
			}
			else if (_locomotion != null && _locomotion.IsPlayingAttack)
			{
				// Fallback for when combat controller is not present
				_body.Velocity.X = 0;
				return;
			}

			var dir = _input.MoveX.Value;

			float accel, friction;
			if (_body.Grounded)
			{
				accel = _movement.GroundAcceleration;
				friction = _movement.GroundFriction;
			}
			else
			{
				accel = _movement.AirAcceleration;
				friction = _movement.AirFriction;
			}

			if (dir != 0)
			{
				float targetVelocity = dir * _movement.MoveSpeed;
				_body.Velocity.X = Mathf.Approach(
					_body.Velocity.X, targetVelocity, accel * Time.DeltaTime);
				_body.FacingDirection = dir;
			}
			else
			{
				_body.Velocity.X = Mathf.Approach(
					_body.Velocity.X, 0f, friction * Time.DeltaTime);
			}
		}
	}
}
