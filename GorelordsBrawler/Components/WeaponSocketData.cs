using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;

namespace GorelordsBrawler.Components
{
	/// <summary>
	/// Per-frame weapon socket positions loaded from .sockets.json sidecar files.
	/// Indexed by animation name, then frame index. Null = no socket data for that frame.
	/// Coordinates are in rendered pixels relative to the frame's top-left corner.
	/// </summary>
	public class WeaponSocketData : Component
	{
		public int FrameWidth;
		public int FrameHeight;
		public Dictionary<string, Vector2?[]> Animations = new();

		public Vector2? GetSocket(string animName, int frame)
		{
			// Face-left animations ALWAYS reuse face-right socket data, even if a
			// face-left sidecar happens to exist on disk from an old bake.
			// Face-left sidecar positions are mirrored in screen space (character was
			// rotated 180° Z in Blender); ComputeHitboxCenter then multiplies the X
			// offset by FacingDirection=-1, which would double-flip the hitbox to the
			// wrong side.  Using face-right positions + FacingDirection=-1 is correct.
			const string faceLeftSuffix = "FaceLeft";
			if (animName.EndsWith(faceLeftSuffix))
			{
				var baseName = animName[..^faceLeftSuffix.Length];
				if (Animations.TryGetValue(baseName, out var baseFrames) && frame < baseFrames.Length)
					return baseFrames[frame];
				return null;
			}

			if (Animations.TryGetValue(animName, out var frames) && frame < frames.Length)
				return frames[frame];

			return null;
		}
	}
}
