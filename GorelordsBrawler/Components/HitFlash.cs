using Microsoft.Xna.Framework;
using Nez;
using Nez.Sprites;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Briefly tints the character's sprite red when hit. Uses unscaled time so
	/// the flash is visible during the hitstop freeze.
	/// SpriteAnimator Color is multiplicative — Color.White = no tint (normal),
	/// bright red = hit flash.
	/// </summary>
	public class HitFlash : Component, IUpdatable
	{
		private static readonly Color _flashColor = new Color(255, 60, 60);

		private SpriteAnimator _animator;
		private float _timer;

		public override void OnAddedToEntity()
		{
			_animator = Entity.GetComponent<SpriteAnimator>();
		}

		public void Trigger()
		{
			_timer = GameConstants.Combat.HitFlashDuration;
			if (_animator != null)
			{
				_animator.Color = _flashColor;
			}
		}

		public void Update()
		{
			if (_timer <= 0)
			{
				return;
			}

			_timer -= Time.UnscaledDeltaTime;
			if (_timer <= 0 && _animator != null)
			{
				_animator.Color = Color.White;
			}
		}
	}
}
