using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	/// <summary>
	/// Spawns log platforms from above once the acid has reached the top platform.
	/// Drops exactly one new log whenever only one (or zero) logs remain, after a
	/// brief interval so the player always has a landing target.
	/// </summary>
	public class PlatformSpawner : SceneComponent
	{
		private readonly float _minX;
		private readonly float _maxX;
		private readonly float _platWidth;
		private readonly float _platHeight;

		private AcidSurface _acid;
		private bool  _active;
		private int   _totalTrackedCount;
		private bool  _spawnPending;
		private float _spawnTimer;

		public PlatformSpawner(int mapWidth, int mapHeight)
		{
			_minX       = GameConstants.Hazards.PlatformSpawnMinX * mapWidth;
			_maxX       = GameConstants.Hazards.PlatformSpawnMaxX * mapWidth;
			_platWidth  = GameConstants.Hazards.PlatformWidth     * mapWidth;
			_platHeight = GameConstants.Hazards.PlatformHeight;
		}

		/// <summary>Called by AcidPhaseManager when acid reaches the top platform level.</summary>
		public void StartSpawning(AcidSurface acid)
		{
			_acid   = acid;
			_active = true;
		}

		/// <summary>
		/// Track a platform so its destruction decrements the live count.
		/// Call after SpawnPlatform (done internally) or whenever an external platform
		/// should be counted (currently unused — logs are the only tracked platforms).
		/// </summary>
		public void Track(DynamicPlatform platform)
		{
			_totalTrackedCount++;
			platform.OnDestroyed += () => _totalTrackedCount--;
		}

		public override void Update()
		{
			if (!_active) return;

			// Queue a new drop whenever only one (or zero) logs are alive.
			if (_totalTrackedCount <= 1 && !_spawnPending)
			{
				_spawnPending = true;
				_spawnTimer   = GameConstants.Hazards.PlatformSpawnInterval;
			}

			if (!_spawnPending) return;

			_spawnTimer -= Time.DeltaTime;
			if (_spawnTimer > 0) return;

			_spawnPending = false;
			SpawnPlatform();
		}

		private void SpawnPlatform()
		{
			float x = Random.Range(_minX, _maxX);
			float y = GameConstants.Hazards.PlatformFallSpawnY;

			var entity = Scene.CreateEntity("platform-drop");
			entity.Transform.Position = new Vector2(x, y);

			var platform = entity.AddComponent(
				new DynamicPlatform(_platWidth, _platHeight,
					GameConstants.Hazards.PlatformBurnDuration, _acid,
					autoBurnDelay: GameConstants.Hazards.PlatformBurnDelay));

			Track(platform);
		}
	}
}
