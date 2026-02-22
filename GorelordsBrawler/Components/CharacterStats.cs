using Microsoft.Xna.Framework;
using Nez;
using Nez.Persistence;

namespace GorelordsBrawler.Components
{
	public class CharacterStats : Component
	{
		[NotInspectable]
		public string Name;

		[NotInspectable]
		public string Description;

		[Inspectable] [Range(0, 500)]
		public int MaxHp = 100;

		[Inspectable] [Range(8, 128)]
		public float BodyWidth = 32f;

		[Inspectable] [Range(8, 128)]
		public float BodyHeight = 48f;

		[Inspectable] [Range(0, 255)]
		public int ColorR = 128;

		[Inspectable] [Range(0, 255)]
		public int ColorG = 128;

		[Inspectable] [Range(0, 255)]
		public int ColorB = 128;

		[JsonExclude]
		public Color BodyColor => new Color(ColorR, ColorG, ColorB);
	}
}
