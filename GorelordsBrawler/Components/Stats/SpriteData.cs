using Nez;

namespace GorelordsBrawler.Components.Stats
{
	public class SpriteData : Component
	{
		public string AtlasPath;

		// Multiplier applied to animation playback speed at full move speed.
		// Increase if the run animation looks too slow relative to movement,
		// decrease if it looks too fast. Default 1.0 plays at the atlas-defined fps.
		[Inspectable] [Range(0.1f, 10f)]
		public float RunAnimSpeed = 1.0f;
	}
}
