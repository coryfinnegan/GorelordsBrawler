using Nez;

namespace GorelordsBrawler.Components.Stats
{
	public class MovementStats : Component
	{
		[Inspectable] [Range(0, 500)]
		public float MoveSpeed = 400f;

		[Inspectable] [Range(0, 2000)]
		public float JumpSpeed = 250f;

		[Inspectable] [Range(0, 3000)]
		public float Gravity = 900f;

		// Gravity multiplier when falling (Velocity.Y > 0). Higher = snappier descent.
		[Inspectable] [Range(1f, 5f)]
		public float FallMultiplier = 1.0f;

		// Gravity multiplier when rising but jump button released. Enables short hops.
		[Inspectable] [Range(1f, 10f)]
		public float ShortHopMultiplier = 1.0f;

		// Grace period (seconds) after walking off an edge where jump is still allowed.
		[Inspectable] [Range(0f, 0.3f)]
		public float CoyoteTime = 0f;

		// Acceleration/friction — controls how fast characters reach full speed and stop.
		[Inspectable] [Range(0, 3000)]
		public float GroundAcceleration = 800f;

		[Inspectable] [Range(0, 3000)]
		public float GroundFriction = 600f;

		[Inspectable] [Range(0, 3000)]
		public float AirAcceleration = 400f;

		[Inspectable] [Range(0, 3000)]
		public float AirFriction = 100f;

		// Fast fall gravity multiplier — applied when player taps down while airborne and falling.
		[Inspectable] [Range(1f, 5f)]
		public float FastFallMultiplier = 1.8f;
	}
}
