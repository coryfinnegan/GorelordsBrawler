using System;
using Nez;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	/// <summary>
	/// Orchestrates the acid hazard timeline:
	///   1. Waits for AcidStartDelay then activates the acid (it begins rising).
	///   2. Once the acid surface reaches the top platform's height, tells
	///      PlatformSpawner to start dropping logs.
	///
	/// TMX platforms remain as static tiles throughout — they are never converted
	/// to DynamicPlatform entities.
	/// </summary>
	public class AcidPhaseManager : SceneComponent
	{
		private readonly AcidSurface     _acid;
		private readonly PlatformSpawner _spawner;
		private readonly float           _spawnTriggerY;  // acid CurrentLevel at which logs start

		private float _elapsed;
		private bool  _acidStarted;
		private bool  _spawnerStarted;

		public AcidPhaseManager(AcidSurface acid, PlatformSpawner spawner, int mapWidth, int mapHeight)
		{
			_acid    = acid;
			_spawner = spawner;

			// Find the platform with the smallest cy (highest on screen = the top platform).
			// Logs start dropping when acid reaches its top edge.
			float minCy = float.MaxValue;
			foreach (var (_, cy, _) in GameConstants.Hazards.Platforms)
				if (cy < minCy) minCy = cy;

			_spawnTriggerY = minCy * mapHeight - GameConstants.Hazards.PlatformHeight * 0.5f;
		}

		public override void Update()
		{
			_elapsed += Time.DeltaTime;

			float startDelay = AppSettings.DebugFastAcid ? 3f : GameConstants.Hazards.AcidStartDelay;
			if (_elapsed < startDelay) return;

			if (!_acidStarted)
			{
				_acidStarted = true;
				_acid.Activate();
			}

			if (!_spawnerStarted && _acid.CurrentLevel <= _spawnTriggerY)
			{
				_spawnerStarted = true;
				_spawner.StartSpawning(_acid);
			}
		}
	}
}
