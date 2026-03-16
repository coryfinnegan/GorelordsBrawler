using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;
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
		private float _shakeTrauma;

		public void AddShake(float intensity)
		{
			_shakeTrauma = Math.Min(_shakeTrauma + intensity, 1f);
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

		public void AddTarget(Entity target)
		{
			_targets.Add(target);
		}

		public override void OnAddedToEntity()
		{
			_camera = Entity.Scene.Camera;
		}

		public void Update()
		{
			if (_targets.Count == 0 || _camera == null)
				return;

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
			var requiredWidth = (max.X - min.X) + Padding * 2;
			var requiredHeight = (max.Y - min.Y) + Padding * 2;

			// Determine zoom based on which axis needs more room
			var zoomX = DesignWidth / requiredWidth;
			var zoomY = DesignHeight / requiredHeight;
			var targetZoom = MathHelper.Min(zoomX, zoomY);
			targetZoom = MathHelper.Clamp(targetZoom, MinZoom, MaxZoom);

			// Smoothly interpolate camera position and zoom
			_camera.Position = Vector2.Lerp(_camera.Position, center, FollowLerp);
			_camera.RawZoom = MathHelper.Lerp(_camera.RawZoom, targetZoom, FollowLerp);

			// Apply trauma-based screen shake on top of the lerped position
			if (_shakeTrauma > 0f)
			{
				var shakeMag = _shakeTrauma * _shakeTrauma * GameConstants.Combat.MaxShakeOffset;
				_camera.Position += new Vector2(
					(Nez.Random.NextFloat() * 2f - 1f) * shakeMag,
					(Nez.Random.NextFloat() * 2f - 1f) * shakeMag);
				_shakeTrauma -= GameConstants.Combat.ShakeDecay * Time.DeltaTime;
				if (_shakeTrauma < 0f)
				{
					_shakeTrauma = 0f;
				}
			}
		}
	}
}
