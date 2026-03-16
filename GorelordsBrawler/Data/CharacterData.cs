using GorelordsBrawler.Components.Stats;

namespace GorelordsBrawler.Data
{
	public class CharacterData
	{
		public string Name;
		public string Description;
		public int MaxHp = 100;
		public float BodyWidth = 32f;
		public float BodyHeight = 48f;
		public int ColorR = 128;
		public int ColorG = 128;
		public int ColorB = 128;
		public float HurtboxWidth  = 0f;
		public float HurtboxHeight = 0f;
		public MovementStats Movement;
		public MeleeStats Melee;
		public ProjectileStats Projectile;
		public SpriteData Sprite;
	}
}
