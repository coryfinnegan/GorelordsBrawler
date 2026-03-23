using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Per-frame hurtbox zone positions loaded from .hurtboxes.json sidecar files.
	/// Each zone (Head, Body, Legs) has per-animation per-frame center positions
	/// in rendered pixels, plus zone dimensions in pixels.
	/// </summary>
	public class HurtboxZoneData : Component
	{
		public int FrameWidth;
		public int FrameHeight;

		/// <summary>
		/// Armature origin projected pixel X — the pivot point for the 180° FaceLeft
		/// rotation in Blender.  Defaults to FrameWidth/2 (frame center) when the
		/// sidecar doesn't include it, which is correct only when the camera is
		/// perfectly centered on the armature origin.
		/// </summary>
		public float OriginX = -1f;

		/// <summary>Returns OriginX if set, otherwise falls back to FrameWidth/2.</summary>
		public float EffectiveOriginX => OriginX >= 0f ? OriginX : FrameWidth / 2f;

		/// <summary>Zone name → pixel dimensions (width, height) from sidecar "zones" block.</summary>
		public Dictionary<string, Vector2> ZoneSizes = new();

		/// <summary>Animation name → zone name → per-frame center positions in rendered pixels.</summary>
		public Dictionary<string, Dictionary<string, Vector2?[]>> Animations = new();

		public Vector2? GetZoneCenter(string animName, string zone, int frame)
		{
			// Face-left animations reuse face-right zone data (same as WeaponSocketData).
			const string faceLeftSuffix = "FaceLeft";
			if (animName.EndsWith(faceLeftSuffix))
			{
				var baseName = animName[..^faceLeftSuffix.Length];
				if (Animations.TryGetValue(baseName, out var baseZones)
					&& baseZones.TryGetValue(zone, out var baseFrames)
					&& frame < baseFrames.Length)
				{
					return baseFrames[frame];
				}
				return null;
			}

			if (Animations.TryGetValue(animName, out var zones)
				&& zones.TryGetValue(zone, out var frames)
				&& frame < frames.Length)
			{
				return frames[frame];
			}

			return null;
		}

		public Vector2 GetZoneSize(string zone)
		{
			if (ZoneSizes.TryGetValue(zone, out var size))
			{
				return size;
			}
			return new Vector2(24, 24); // sensible fallback
		}

		/// <summary>Returns all zone names that have data.</summary>
		public IEnumerable<string> GetZoneNames()
		{
			return ZoneSizes.Keys;
		}
	}
}
