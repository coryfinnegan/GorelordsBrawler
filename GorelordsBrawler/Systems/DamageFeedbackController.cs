using System;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Components.PostProcessors;

namespace GorelordsBrawler.Systems
{
	/// <summary>
	/// Phase 4 of the acid-deadly-polish-plan: orchestrates three screen-space
	/// damage-feedback effects from one entity so they can all be live-tuned
	/// from the same inspector panel.
	///
	/// Triggers:
	///   • Camera shake — additive trauma into the existing
	///     <see cref="BrawlerCamera.AddShake"/> model on every acid damage tick.
	///     The 4 Hz tick rate vs 6 trauma/sec decay means each tick reads as a
	///     small individual "thunk" rather than a sustained hum — desired.
	///   • Chromatic-aberration pulse — spikes <c>CaStrength</c> on each tick,
	///     decays exponentially between ticks. Pulses stack mildly so wading in
	///     deeper acid feels visibly more chaotic.
	///   • HP-driven vignette — recomputed every frame from
	///     <c>min(alive players' HP%)</c>. Below
	///     <see cref="VignetteEngagesBelowHpRatio"/> the intensity ramps 0→1 and
	///     the colour lerps from "calm tunnel" (
	///     <see cref="VignetteColorAtEngage"/>) to "panicked danger" (
	///     <see cref="VignetteColorAtZeroHp"/>).
	///
	/// Acid-only by design (subscribes to <see cref="ContactHazard.OnDamageApplied"/>,
	/// not <see cref="Health.OnDamaged"/>). Melee already has full feedback via
	/// <see cref="CombatEffectsManager"/>; layering more on top would be the
	/// "Christmas tree" failure mode flagged in the parent plan.
	///
	/// Hosted as a <see cref="Component"/> on its own entity so the inspector
	/// shows the tunables — same convention as
	/// <see cref="AcidSizzleManager"/>/<see cref="AcidBubbleEmitter"/>.
	/// </summary>
	public class DamageFeedbackController : Component, IUpdatable
	{
		// ── Shake (per acid damage tick) ──────────────────────────────────
		[Inspectable, Range(0f, 1f)]
		public float AcidTickShakeIntensity = 0.30f;

		// ── Vignette ──────────────────────────────────────────────────────
		[Inspectable, Range(0.3f, 1.5f)]
		public float VignetteRadius = 0.85f;

		[Inspectable, Range(0.01f, 1.0f)]
		public float VignetteSoftness = 0.40f;

		[Inspectable, Range(0f, 1f)]
		public float VignetteMaxIntensity = 0.85f;

		[Inspectable, Range(0f, 1f)]
		public float VignetteEngagesBelowHpRatio = 0.60f;

		[Inspectable]
		public Color VignetteColorAtEngage = Color.Black;

		// Dark red: signals DANGER (Titanfall 2 / HK Fury convention) rather
		// than just dimming, which is what pure black would read as.
		[Inspectable]
		public Color VignetteColorAtZeroHp = new Color(120, 0, 0);

		// ── Chromatic aberration (per acid damage tick) ───────────────────
		// Peak normalised-UV offset on each tick spike. Very small intentionally
		// — research signal is that CA + shake combined causes readability /
		// eye-strain issues in fast 2D games (see proposal Sources §3).
		[Inspectable, Range(0f, 0.02f)]
		public float CaPulsePeak = 0.004f;

		// Exponential decay rate per unscaled second. ~8 → half-life 87 ms,
		// nearly fully decays before the next ~250 ms acid tick — pulses
		// stack only mildly. Raise to make each tick read as more discrete;
		// lower to make wading feel like one continuous shimmer.
		[Inspectable, Range(1f, 30f)]
		public float CaDecayRate = 8.0f;

		// ── Refs / state ──────────────────────────────────────────────────
		private readonly DamageFeedbackPostProcessor _post;
		private readonly PlayerManager _playerManager;
		private readonly ContactHazard _hazard;
		private readonly BrawlerCamera _camera;

		private float _caStrength;
		private bool  _subscribed;

		public DamageFeedbackController(
			DamageFeedbackPostProcessor post,
			PlayerManager playerManager,
			ContactHazard hazard,
			BrawlerCamera camera)
		{
			_post          = post;
			_playerManager = playerManager;
			_hazard        = hazard;
			_camera        = camera;
		}

		public override void OnAddedToEntity()
		{
			if (_hazard != null)
			{
				_hazard.OnDamageApplied += OnDamageApplied;
				_subscribed = true;
			}
		}

		public override void OnRemovedFromEntity()
		{
			if (_subscribed && _hazard != null)
			{
				_hazard.OnDamageApplied -= OnDamageApplied;
				_subscribed = false;
			}
		}

		public void Update()
		{
			// CA decays on UNSCALED time so the pulse keeps relaxing during any
			// future hitstop freeze. Phase 4 itself only fires off acid (no
			// hitstop) but if CA gets generalised later this is the correct
			// time base — same reason HitFlash uses UnscaledDeltaTime.
			_caStrength *= MathF.Exp(-CaDecayRate * Time.UnscaledDeltaTime);

			// Worst-alive HP drives both vignette intensity and colour lerp.
			// Smash Bros precedent: shared-screen feedback fires on "important
			// moments either player cares about", not per-player slot.
			float worstHp  = ComputeWorstAliveHpRatio();
			float engageAt = VignetteEngagesBelowHpRatio;

			float intensity = 0f;
			float colorT    = 0f;
			if (worstHp < engageAt && engageAt > 0f)
			{
				// Linear ramp: at engage threshold (e.g. 60% HP) we're at 0;
				// at 0% HP we're at 1. Same value drives BOTH intensity (so
				// the vignette closes in as HP drops) AND the colour lerp (so
				// the vignette goes black → dark red as we approach death).
				float t = 1f - (worstHp / engageAt);
				intensity = MathHelper.Clamp(t * VignetteMaxIntensity, 0f, 1f);
				colorT    = MathHelper.Clamp(t, 0f, 1f);
			}

			_post.CaStrength       = _caStrength;
			_post.VignetteRadius   = VignetteRadius;
			_post.VignetteSoftness = VignetteSoftness;
			_post.VignetteIntensity = intensity;
			_post.VignetteColor    = Color.Lerp(VignetteColorAtEngage, VignetteColorAtZeroHp, colorT);
		}

		private float ComputeWorstAliveHpRatio()
		{
			float worst      = 1f;
			bool  foundAlive = false;

			var players = _playerManager.GetActivePlayers();
			for (int i = 0; i < players.Count; i++)
			{
				var health = players[i].GetComponent<Health>();
				if (health == null || health.IsDead || health.MaxHp <= 0)
				{
					continue;
				}

				float ratio = (float)health.CurrentHp / health.MaxHp;
				if (ratio < worst)
				{
					worst = ratio;
				}
				foundAlive = true;
			}

			// All players dead (mid-respawn window): hold full-health so the
			// vignette doesn't flash to max during the brief gap before the
			// respawn handler ticks. Vignette eases off on respawn instead of
			// snapping shut for a frame.
			return foundAlive ? worst : 1f;
		}

		private void OnDamageApplied(Entity player)
		{
			// Additive into the trauma model (BrawlerCamera clamps to 1.0).
			// AcidTickShakeIntensity ~0.30 + ~6 trauma/sec decay + 4 Hz tick
			// rate → each tick reads as an individual "thunk" rather than
			// sustained hum.
			_camera?.AddShake(AcidTickShakeIntensity);

			// CA pulse — Max() (not assignment) so simultaneous ticks on
			// different players don't reduce in-flight strength. Decay in
			// Update() will pull it back down.
			_caStrength = MathF.Max(_caStrength, CaPulsePeak);
		}
	}
}
