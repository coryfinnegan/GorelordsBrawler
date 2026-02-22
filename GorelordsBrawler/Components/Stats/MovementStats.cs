using Nez;

namespace GorelordsBrawler.Components.Stats
{
	public class MovementStats : Component
	{
		[Inspectable] [Range(0, 500)]
		public float MoveSpeed = 100f;

		[Inspectable] [Range(0, 600)]
		public float JumpSpeed = 250f;

		[Inspectable] [Range(0, 2000)]
		public float Gravity = 900f;
	}
}
