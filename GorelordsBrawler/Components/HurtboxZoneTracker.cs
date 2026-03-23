using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;
using Nez.Sprites;
using GorelordsBrawler.Components.Stats;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Per-frame hurtbox zone tracker. Creates one BoxCollider per zone and
	/// repositions them each frame based on HurtboxZoneData + current animation.
	/// Runs after LocomotionAnimator (UpdateOrder=200).
	/// </summary>
	public class HurtboxZoneTracker : Component, IUpdatable
	{
		private HurtboxZoneData _zoneData;
		private SpriteAnimator _animator;
		private SpriteData _spriteData;
		private CharacterStats _stats;
		private PhysicsBody _body;

		// Zone name → collider
		private Dictionary<string, BoxCollider> _zoneColliders = new();

		// Zone name → enabled state (for future limb removal)
		private Dictionary<string, bool> _zoneEnabled = new();

		// Zone name → last known LocalOffset (hold position during null frames)
		private Dictionary<string, Vector2> _lastOffsets = new();

		public override void OnAddedToEntity()
		{
			UpdateOrder = 200;
			_zoneData = Entity.GetComponent<HurtboxZoneData>();
			_animator = Entity.GetComponent<SpriteAnimator>();
			_spriteData = Entity.GetComponent<SpriteData>();
			_stats = Entity.GetComponent<CharacterStats>();
			_body = Entity.GetComponent<PhysicsBody>();

			// Create one trigger BoxCollider per zone
			foreach (var zoneName in _zoneData.GetZoneNames())
			{
				var pixelSize = _zoneData.GetZoneSize(zoneName);
				float scale = _spriteData?.SpriteScale ?? 1f;
				float worldW = pixelSize.X * scale;
				float worldH = pixelSize.Y * scale;

				var collider = Entity.AddComponent(new BoxCollider(worldW, worldH));
				collider.PhysicsLayer = PhysicsLayers.Hurtbox;
				collider.CollidesWithLayers = PhysicsLayers.Hitbox;
				collider.IsTrigger = true;
				collider.ShouldColliderScaleAndRotateWithTransform = false;

				_zoneColliders[zoneName] = collider;
				_zoneEnabled[zoneName] = true;
			}
		}

		public void Update()
		{
			if (_animator == null || _zoneData == null)
			{
				return;
			}

			var animName = _animator.CurrentAnimationName;
			int frame = _animator.CurrentFrame;
			float scale = _spriteData?.SpriteScale ?? 1f;
			float bodyHalfH = _stats?.BodyHeight / 2f ?? 0f;
			int facing = _body?.FacingDirection ?? 1;

			foreach (var kvp in _zoneColliders)
			{
				var zoneName = kvp.Key;
				var collider = kvp.Value;

				// Respect limb removal
				if (!_zoneEnabled[zoneName])
				{
					collider.Enabled = false;
					continue;
				}

				var center = _zoneData.GetZoneCenter(animName, zoneName, frame);
				if (center.HasValue)
				{
					// Sprite origin is bottom-center (0.5, 1.0):
					//   frame bottom-center in pixels = (FrameWidth/2, FrameHeight)
					//   zone offset from bottom-center = (cx - W/2, cy - H)
					//   in world units: multiply by scale
					//   plus entity-to-sprite-bottom offset = BodyHeight/2
					// LocalOffset is relative to entity center (not world position).
					//
					// FaceLeft correction: the baker generates FaceLeft sprites by
					// rotating the character 180° around its armature origin (OriginX
					// in pixel space). When facing=-1 we must mirror zone positions
					// around OriginX, not around FrameWidth/2.  The correction term
					// accounts for the difference between these two pivot points.
					float originX = _zoneData.EffectiveOriginX;
					float rawDx = (center.Value.X - _zoneData.FrameWidth / 2f) * scale;
					float pivotCorrection = (originX - _zoneData.FrameWidth / 2f) * scale;
					float dx = rawDx * facing + (1 - facing) * pivotCorrection;
					float dy = bodyHalfH + (center.Value.Y - _zoneData.FrameHeight) * scale;

					var offset = new Vector2(dx + _zoneData.OffsetX, dy + _zoneData.OffsetY);
					collider.LocalOffset = offset;
					collider.Enabled = true;
					_lastOffsets[zoneName] = offset;
				}
				else if (_lastOffsets.TryGetValue(zoneName, out var lastOffset))
				{
					// Null frame — hold last known position
					collider.LocalOffset = lastOffset;
					collider.Enabled = true;
				}
				else
				{
					// No data at all yet for this zone in this animation
					collider.Enabled = false;
				}
			}
		}

		/// <summary>Disable a zone collider (for future limb removal).</summary>
		public void DisableZone(string zoneName)
		{
			if (_zoneEnabled.ContainsKey(zoneName))
			{
				_zoneEnabled[zoneName] = false;
				if (_zoneColliders.TryGetValue(zoneName, out var collider))
				{
					collider.Enabled = false;
				}
			}
		}

		/// <summary>Re-enable a zone collider.</summary>
		public void EnableZone(string zoneName)
		{
			if (_zoneEnabled.ContainsKey(zoneName))
			{
				_zoneEnabled[zoneName] = true;
			}
		}

		/// <summary>Look up which zone a collider belongs to (for hit callbacks).</summary>
		public string GetZoneName(Collider collider)
		{
			foreach (var kvp in _zoneColliders)
			{
				if (kvp.Value == collider)
				{
					return kvp.Key;
				}
			}
			return null;
		}

		/// <summary>Get the collider for a specific zone.</summary>
		public BoxCollider GetZoneCollider(string zoneName)
		{
			_zoneColliders.TryGetValue(zoneName, out var collider);
			return collider;
		}
	}
}
