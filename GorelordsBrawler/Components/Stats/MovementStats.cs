using Nez;

namespace GorelordsBrawler.Components.Stats
{
	public class MovementStats : Component
	{
		[Inspectable] [Range(0, 500)]
		public float MoveSpeed = 100f;

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
	}
}
