using Nez;

namespace GorelordsBrawler.Input
{
	public class InputProfile
	{
		public VirtualIntegerAxis MoveX;
		public VirtualButton Jump;
		public VirtualButton Attack;

		public void Deregister()
		{
			MoveX?.Deregister();
			Jump?.Deregister();
			Attack?.Deregister();
		}
	}
}
