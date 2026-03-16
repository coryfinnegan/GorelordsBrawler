using Nez;

namespace GorelordsBrawler.Components.Stats
{
	public class SpriteData : Component
	{
		// Character name prefix used to build animation keys (e.g. "FutureAxe").
		// Combined with AnimationKeyBuilder to produce "FutureAxe_Idle" etc.
		// Must match the character_prefix set in the Sprite Baker addon.
		public string AnimPrefix = "";

		public string AtlasPath;

		// Optional additional atlas files whose animations are merged into the same
		// SpriteAnimator (e.g. separate attack/hurt atlases with non-square frames).
		public string[] ExtraAtlasPaths;

		// Optional atlas used for the animated preview in the character select screen.
		// Expected to contain an animation named "select". Falls back to a color rectangle
		// if not set.
		public string SelectAtlasPath;

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

		// Attack animation playback speed multiplier.
		// Attack animations from Blender are often rendered at full quality and need to be
		// sped up to match the attack cooldown.  Formula: (frameCount / atlasFps) / desiredSeconds.
		// Example: 96 frames at 8fps to play in 1.5s → speed = (96/8) / 1.5 = 8.
		[Inspectable] [Range(0.1f, 30f)]
		public float AttackAnimSpeed = 1.0f;

		// Entity scale override used ONLY during attack animations.
		// Needed when attack atlas frames have different pixel dimensions than the main atlas
		// (e.g. main atlas uses 256px frames via targetSize, attack atlas uses 800px frames).
		// Formula: AttackSpriteScale = SpriteScale * (mainFrameHeight / attackFrameHeight)
		// Example: SpriteScale=0.8, main=256px, attack=800px → 0.8 * (256/800) = 0.256
		// Set to 0 to use the same SpriteScale as locomotion (correct when frame sizes match).
		[Inspectable] [Range(0f, 2f)]
		public float AttackSpriteScale = 0f;
	}
}
