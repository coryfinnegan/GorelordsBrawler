using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Camera that frames all tracked entities by zooming in/out like a party brawler.
	/// Centers between all players and adjusts zoom so everyone stays on screen.
	/// </summary>
	public class BrawlerCamera : Component, IUpdatable
	{
		private readonly List<Entity> _targets = [];
		private Camera _camera;
		private CameraShake _shake;
		// The intended (un-shaken) camera position. Shake modulates around this
		// each frame so it never accumulates into the base — the static view
		// returns to exactly this point once trauma settles.
		private Vector2 _basePosition;
		private bool _hasMapBounds;
		private float _mapWidth;
		private float _mapHeight;
		private AcidSurface _acid;

		public void AddShake(float intensity)
		{
			_shake.AddTrauma(intensity);
		}

        [Inspectable]
        /// <summary>
        /// How quickly the camera moves to the target position (0 = never, 1 = instant).
        /// </summary>
        public float FollowLerp = 0.15f;

		[Inspectable]
		/// <summary>
		/// Extra padding around the players in world units.
		/// </summary>
		public float Padding = 80f;

        [Inspectable]
        /// <summary>
        /// Minimum zoom (zoomed out limit). Lower = more zoomed out.
        /// </summary>
        public float MinZoom = 0.5f;

        [Inspectable]
        /// <summary>
        /// Maximum zoom (zoomed in limit).
        /// </summary>
        public float MaxZoom = 1.0f;

        [Inspectable]
        /// <summary>
        /// The design resolution width used for zoom calculation.
        /// </summary>
        public float DesignWidth = 800f;

        [Inspectable]
        /// <summary>
        /// The design resolution height used for zoom calculation.
        /// </summary>
        public float DesignHeight = 600f;

        [Inspectable]
        /// <summary>
        /// When true the camera locks to map center at a zoom that fits the whole
        /// map and skips all player-tracking + acid-following motion. Shake is
        /// still applied. Set this before targets/acid are wired in so the
        /// one-shot positioning runs in the first Update tick.
        /// </summary>
        public bool Static = false;

        private bool _staticPlaced;

		public void AddTarget(Entity target)
		{
			_targets.Add(target);
		}

		/// <summary>
		/// When set, the camera is constrained so the acid surface is always visible
		/// in the upper portion of the view — prevents the camera from showing only
		/// the interior of the acid when players are submerged.
		/// </summary>
		public void SetAcidSurface(AcidSurface acid)
		{
			_acid = acid;
		}

		/// <summary>
		/// Set map bounds so the camera doesn't scroll past edges.
		/// </summary>
		public void SetMapBounds(float width, float height)
		{
			_hasMapBounds = true;
			_mapWidth = width;
			_mapHeight = height;
		}

		public override void OnAddedToEntity()
		{
			_camera = Entity.Scene.Camera;
			// Seed the base with the camera's starting position so dynamic mode's
			// first-frame Lerp behaves exactly as before (lerps from here, not origin).
			_basePosition = _camera.Position;
		}

		public void Update()
		{
			if (_camera == null)
				return;

			if (Static)
			{
				ApplyStaticPlacement();
			}
			else if (_targets.Count > 0)
			{
				ApplyDynamicTracking();
			}

			// Telegraph rumble (Phase D): a low shake that BUILDS while the
			// acid winds up a wave/rise — trauma fed per-frame, scaled by tell
			// progress, so it swells toward the beat then decays naturally
			// through the shake system. Subtle by design (juice guidance:
			// amplitude creep reads as a bug, not a feature).
			if (_acid != null && _acid.TellActive)
			{
				_shake.AddTrauma(
					Constants.AcidConfig.TellTraumaPerSec * _acid.TellProgress * Time.DeltaTime);
			}

			ApplyShake();
		}

		private void ApplyStaticPlacement()
		{
			// Place the camera once (centered on map, zoomed to fit) and never
			// touch it again — keeps the play area locked while shake still
			// modulates around the fixed position each frame.
			if (_staticPlaced || !_hasMapBounds)
				return;

			float fitZoomX = DesignWidth  / _mapWidth;
			float fitZoomY = DesignHeight / _mapHeight;
			_camera.RawZoom  = MathHelper.Min(fitZoomX, fitZoomY);
			_basePosition    = new Vector2(_mapWidth * 0.5f, _mapHeight * 0.5f);
			_staticPlaced    = true;
		}

		private void ApplyDynamicTracking()
		{
			// Calculate bounding box of all targets
			var min = _targets[0].Transform.Position;
			var max = min;

			for (int i = 1; i < _targets.Count; i++)
			{
				var pos = _targets[i].Transform.Position;
				if (pos.X < min.X) min.X = pos.X;
				if (pos.Y < min.Y) min.Y = pos.Y;
				if (pos.X > max.X) max.X = pos.X;
				if (pos.Y > max.Y) max.Y = pos.Y;
			}

			// Center point between all players
			var center = (min + max) * 0.5f;

			// Calculate required view size with padding
			var requiredWidth  = (max.X - min.X) + Padding * 2;
			var requiredHeight = (max.Y - min.Y) + Padding * 2;

			// Determine zoom based on which axis needs more room
			var zoomX = DesignWidth  / requiredWidth;
			var zoomY = DesignHeight / requiredHeight;
			var targetZoom = MathHelper.Min(zoomX, zoomY);
			targetZoom = MathHelper.Clamp(targetZoom, MinZoom, MaxZoom);

			// Smoothly interpolate the base (un-shaken) position and zoom. Operating
			// on _basePosition rather than _camera.Position keeps last frame's shake
			// offset out of the smoothing — ApplyShake re-adds it after.
			_basePosition   = Vector2.Lerp(_basePosition, center, FollowLerp);
			_camera.RawZoom = MathHelper.Lerp(_camera.RawZoom, targetZoom, FollowLerp);

			// Clamp camera to map bounds so it doesn't scroll past edges
			if (_hasMapBounds)
			{
				var halfViewW = (DesignWidth  / _camera.RawZoom) * 0.5f;
				var halfViewH = (DesignHeight / _camera.RawZoom) * 0.5f;
				_basePosition = new Vector2(
					MathHelper.Clamp(_basePosition.X, halfViewW, _mapWidth  - halfViewW),
					MathHelper.Clamp(_basePosition.Y, halfViewH, _mapHeight - halfViewH));
			}

			// Acid surface constraint: keep the rising surface in the upper 60% of the view
			// so players always see where the acid is, even when submerged below it.
			if (_acid != null && _acid.IsRising)
			{
				var halfViewH = (DesignHeight / _camera.RawZoom) * 0.5f;
				// Minimum camera Y ensures acidLevel appears no lower than 60% down the view.
				float minCamY = _acid.CurrentLevel - halfViewH * 0.8f;
				if (_basePosition.Y < minCamY)
				{
					_basePosition = new Vector2(_basePosition.X, minCamY);
				}
			}
		}

		private void ApplyShake()
		{
			// Always rebuild Position from the fixed base + this frame's offset (set, not +=).
			// When trauma is spent the offset is exactly zero, so a static view settles back to
			// precisely map center with no residual drift.
			//
			// Decay runs on UNSCALED time: a hit triggers shake and hitstop (TimeScale=0) on the
			// same frame, so scaled DeltaTime would freeze trauma for the whole freeze and the
			// camera would resume at full-strength shake when the world un-pauses. Unscaled time
			// lets the shake keep relaxing in real time through the freeze, matching
			// CombatEffectsManager/HitFlash which also tick on unscaled time during hitstop.
			var offset = _shake.Advance(
				Time.UnscaledDeltaTime,
				GameConstants.Combat.MaxShakeOffset,
				GameConstants.Combat.ShakeDecay,
				Nez.Random.NextFloat);
			_camera.Position = _basePosition + offset;
		}
	}
}
