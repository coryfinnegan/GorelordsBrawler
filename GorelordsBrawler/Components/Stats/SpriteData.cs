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

		// Multiplier applied to jump animation playback speed at full jump speed.
		// Speed is driven by |Velocity.Y| / JumpSpeed, so the animation naturally
		// slows to 0 at the apex and accelerates during launch and fall.
		[Inspectable] [Range(0.1f, 10f)]
		public float JumpAnimSpeed = 1.0f;

		// Visual scale applied to the entity transform for rendering.
		// Purely visual — does not affect physics collider dimensions (BodyWidth/BodyHeight).
		// Example: for a 512px-tall sprite frame to appear 48 world units tall, use 48/512 ≈ 0.094.
		[Inspectable] [Range(0.01f, 2f)]
		public float SpriteScale = 0.094f;
	}
}
