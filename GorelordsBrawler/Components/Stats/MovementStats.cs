using Nez;

namespace GorelordsBrawler.Components.Stats
{
	public class MovementStats : Component
	{
		[Inspectable] [Range(0, 500)]
		public float moveSpeed = 100f;

		[Inspectable] [Range(0, 600)]
		public float jumpSpeed = 250f;

		[Inspectable] [Range(0, 2000)]
		public float gravity = 900f;
	}
}
