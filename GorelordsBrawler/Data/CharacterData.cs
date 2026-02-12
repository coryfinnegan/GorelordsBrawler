using GorelordsBrawler.Components.Stats;

namespace GorelordsBrawler.Data
{
	public class CharacterData
	{
		public string name;
		public string description;
		public int maxHp = 100;
		public float bodyWidth = 32f;
		public float bodyHeight = 48f;
		public int colorR = 128;
		public int colorG = 128;
		public int colorB = 128;
		public MovementStats movement;
		public MeleeStats melee;
		public ProjectileStats projectile;
	}
}
