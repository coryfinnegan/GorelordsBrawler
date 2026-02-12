using Microsoft.Xna.Framework;
using Nez;
using Nez.Persistence;

namespace GorelordsBrawler.Components
{
	public class CharacterStats : Component
	{
		[NotInspectable]
		public string name;

		[NotInspectable]
		public string description;

		[Inspectable] [Range(0, 500)]
		public int maxHp = 100;

		[Inspectable] [Range(8, 128)]
		public float bodyWidth = 32f;

		[Inspectable] [Range(8, 128)]
		public float bodyHeight = 48f;

		[Inspectable] [Range(0, 255)]
		public int colorR = 128;

		[Inspectable] [Range(0, 255)]
		public int colorG = 128;

		[Inspectable] [Range(0, 255)]
		public int colorB = 128;

		[JsonExclude]
		public Color BodyColor => new Color(colorR, colorG, colorB);
	}
}
