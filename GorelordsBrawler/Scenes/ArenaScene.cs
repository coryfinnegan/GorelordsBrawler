using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components;
using GorelordsBrawler.Constants;
using GorelordsBrawler.Input;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.Scenes
{
	public class ArenaScene : BaseScene
	{
		public ArenaScene()
		{
			AddRenderer(new RenderLayerRenderer(0, GameConstants.Rendering.DefaultRenderLayer));

			AddSceneComponent(new PauseManager());
			var playerManager = AddSceneComponent(new PlayerManager());

			var p1 = playerManager.AddPlayer(0,
				InputProfileFactory.CreateKeyboardWASD(),
				GameConstants.Characters.Trollborg, GameConstants.Arena.Player1Spawn);

			var p2 = playerManager.AddPlayer(1,
				InputProfileFactory.CreateKeyboardArrows(),
				GameConstants.Characters.Trollborg, GameConstants.Arena.Player2Spawn);

			CreatePlatforms();

			var cameraEntity = CreateEntity(GameConstants.EntityNames.Camera);
			var brawlerCam = cameraEntity.AddComponent(new BrawlerCamera());
			foreach (var player in playerManager.GetActivePlayers())
				brawlerCam.AddTarget(player);
		}

		private void CreatePlatforms()
		{
			CreatePlatform(GameConstants.EntityNames.Ground,
				GameConstants.Arena.GroundPosition,
				GameConstants.Arena.GroundWidth,
				GameConstants.Arena.GroundHeight,
				GameConstants.Arena.GroundColor);

			CreatePlatform(GameConstants.EntityNames.PlatformMid,
				GameConstants.Arena.PlatformMidPosition,
				GameConstants.Arena.PlatformMidWidth,
				GameConstants.Arena.PlatformMidHeight,
				GameConstants.Arena.PlatformColor);

			CreatePlatform(GameConstants.EntityNames.PlatformLeft,
				GameConstants.Arena.PlatformLeftPosition,
				GameConstants.Arena.PlatformLeftWidth,
				GameConstants.Arena.PlatformLeftHeight,
				GameConstants.Arena.PlatformColor);

			CreatePlatform(GameConstants.EntityNames.PlatformRight,
				GameConstants.Arena.PlatformRightPosition,
				GameConstants.Arena.PlatformRightWidth,
				GameConstants.Arena.PlatformRightHeight,
				GameConstants.Arena.PlatformColor);
		}

		private Entity CreatePlatform(string name, Vector2 position, float width, float height, Color color)
		{
			var platform = CreateEntity(name);
			platform.Transform.Position = position;

			var renderer = platform.AddComponent(new PrototypeSpriteRenderer(width, height));
			renderer.SetColor(color);

			var collider = platform.AddComponent(new BoxCollider(width, height));
			collider.PhysicsLayer = PhysicsLayers.Platforms;
			collider.CollidesWithLayers = PhysicsLayers.Player;

			return platform;
		}
	}
}
