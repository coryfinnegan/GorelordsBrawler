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
		private HitParticleManager _particleManager;

		/// <summary>
		/// True while a hit freeze is in effect (i.e. <c>Time.TimeScale == 0</c>). The E2E harness
		/// reads this to confirm a melee hit actually drove the game into the <c>dt=0</c> window —
		/// the exact path that NaN'd the acid sim (PR #11) — so the acid-survives-a-hit regression
		/// can prove it exercised the failure mode rather than passing trivially.
		/// </summary>
		public bool IsHitstopActive => _hitstopTimer > 0f;

		public void TriggerHit(Entity defender, float scaledKnockbackForce,
			Vector2 hitPosition, Vector2 knockbackDirection)
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

			// Blood splatter + impact flash particles
			if (_particleManager == null)
			{
				_particleManager = Scene.GetSceneComponent<HitParticleManager>();
			}
			_particleManager?.SpawnHitEffect(hitPosition, knockbackDirection, intensity);
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
