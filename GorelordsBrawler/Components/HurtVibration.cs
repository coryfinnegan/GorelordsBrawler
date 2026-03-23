using System;
using Microsoft.Xna.Framework;
using Nez;
using Nez.Sprites;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Rapid X-axis oscillation on the character's sprite during hitstun.
	/// Classic 2D fighter technique — sells the "shock" of getting hit.
	/// Captures the sprite's base LocalOffset when hitstun starts and restores
	/// it when hitstun ends.
	/// </summary>
	public class HurtVibration : Component, IUpdatable
	{
		private SpriteAnimator _animator;
		private Hitstun _hitstun;
		private Vector2 _baseOffset;
		private bool _wasActive;

		public override void OnAddedToEntity()
		{
			_animator = Entity.GetComponent<SpriteAnimator>();
			_hitstun = Entity.GetComponent<Hitstun>();
		}

		public void Update()
		{
			if (_animator == null || _hitstun == null)
			{
				return;
			}

			if (_hitstun.IsActive)
			{
				if (!_wasActive)
				{
					_baseOffset = _animator.LocalOffset;
				}

				float offset = MathF.Sin(Time.TotalTime * GameConstants.Combat.HurtVibrationFrequency * MathF.PI * 2)
					* GameConstants.Combat.HurtVibrationAmplitude;
				_animator.LocalOffset = _baseOffset + new Vector2(offset, 0);
				_wasActive = true;
			}
			else if (_wasActive)
			{
				_animator.LocalOffset = _baseOffset;
				_wasActive = false;
			}
		}
	}
}
