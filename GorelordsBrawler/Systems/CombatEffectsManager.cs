using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	/// <summary>
	/// Orchestrates hit impact effects: hitstop (TimeScale freeze), camera shake,
	/// and hit flash on the defender. Uses unscaled time for its own countdown so
	/// it ticks correctly while TimeScale = 0.
	/// </summary>
	public class CombatEffectsManager : SceneComponent
	{
		private float _hitstopTimer;
		private BrawlerCamera _brawlerCam;

		public void TriggerHit(Entity defender, float scaledKnockbackForce)
		{
			// Hitstop — freeze everything for a few frames
			Time.TimeScale = 0f;
			_hitstopTimer = GameConstants.Combat.HitstopDuration;

			// Camera shake — intensity proportional to final knockback force
			if (_brawlerCam == null)
			{
				_brawlerCam = Scene.FindComponentOfType<BrawlerCamera>();
			}
			var intensity = MathHelper.Clamp(scaledKnockbackForce / 600f, 0f, 1f);
			_brawlerCam?.AddShake(intensity);

			// Hit flash on the defender
			defender?.GetComponent<HitFlash>()?.Trigger();
		}

		public override void Update()
		{
			if (_hitstopTimer <= 0)
			{
				return;
			}

			_hitstopTimer -= Time.UnscaledDeltaTime;
			if (_hitstopTimer <= 0)
			{
				Time.TimeScale = 1f;
			}
		}
	}
}
