using Nez;

namespace GorelordsBrawler.Input
{
	public class InputProfile
	{
		public VirtualIntegerAxis MoveX;
		public VirtualIntegerAxis MoveY;
		public VirtualButton Jump;
		public VirtualButton Attack;
		public VirtualButton Special;

		public void Deregister()
		{
			MoveX?.Deregister();
			MoveY?.Deregister();
			Jump?.Deregister();
			Attack?.Deregister();
			Special?.Deregister();
		}
	}
}
