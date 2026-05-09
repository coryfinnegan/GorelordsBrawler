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
		private LedgeHangAbility _ledge;

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
			// Lazy resolve: components may be added after WalkAbility
			if (_combat == null)
			{
				_combat = Entity.GetComponent<CombatController>();
			}
			if (_ledge == null)
			{
				_ledge = Entity.GetComponent<LedgeHangAbility>();
			}

			// Suppress movement while on a ledge
			if (_ledge != null && _ledge.IsOnLedge)
			{
				return;
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

			// During attacks: restrict horizontal movement
			bool inAttackAnim = _locomotion != null && _locomotion.IsPlayingAttack && _combat != null && _combat.CurrentAttack != null;
			bool inAttackCooldown = _combat != null && _combat.IsAttacking && !_body.Grounded;
			if (inAttackAnim || inAttackCooldown)
			{
				if (_body.Grounded && inAttackAnim)
				{
					// Aerial attacks that land on ground: kill momentum, don't allow sliding
					if (_combat.IsAerialAttack)
					{
						_body.Velocity.X = Mathf.Approach(
							_body.Velocity.X, 0f, _movement.GroundFriction * Time.DeltaTime);
						return;
					}

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
				// Airborne during attack animation: allow air control if attack permits
				if (inAttackAnim && _combat.CurrentAttack != null && _combat.CurrentAttack.MovementMultiplier >= 1.0f)
				{
					// Fall through to normal movement code below
				}
				else
				{
					// Airborne attacks: kill horizontal momentum through entire cooldown
					_body.Velocity.X = Mathf.Approach(
						_body.Velocity.X, 0f, _movement.AirFriction * 3f * Time.DeltaTime);
					return;
				}
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
