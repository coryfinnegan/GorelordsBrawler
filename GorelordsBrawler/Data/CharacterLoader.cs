using Nez;
using Nez.Persistence;
using GorelordsBrawler.Components;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Data
{
	public static class CharacterLoader
	{
		public static CharacterStats Load(Scene scene, string characterType)
		{
			var path = GameConstants.ContentPaths.CharactersFolder + characterType + GameConstants.ContentPaths.JsonExtension;
			var jsonString = scene.Content.LoadJson(path);
			return Json.FromJson<CharacterStats>(jsonString);
		}
	}
}
