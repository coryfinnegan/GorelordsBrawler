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
			// During hitstun: preserve knockback velocity, apply ground friction to decelerate
			if (_hitstun != null && _hitstun.IsActive)
			{
				if (_body.Grounded)
				{
					_body.Velocity.X = MathHelper.Lerp(_body.Velocity.X, 0f, 10f * Time.DeltaTime);
				}
				return;
			}

			if (_locomotion != null && _locomotion.IsPlayingAttack)
			{
				_body.Velocity.X = 0;
				return;
			}

			var moveDir = _input.MoveX.Value;
			_body.Velocity.X = moveDir * _movement.MoveSpeed;
			if (moveDir != 0)
			{
				_body.FacingDirection = moveDir;
			}
		}
	}
}
