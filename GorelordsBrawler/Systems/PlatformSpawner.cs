using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	/// <summary>
	/// Spawns log platforms from above once the acid has mostly eaten the LOW
	/// tier pair. Keeps the live-log population at the PER-LOOP target
	/// (AcidConfig.ScramblePlatformTargetFor — 2 early, up to 4 late, so the
	/// arena transforms into a debris field as static footing dissolves), with
	/// a sporadic stagger between replacement drops.
	/// </summary>
	public class PlatformSpawner : SceneComponent
	{
		private readonly float _platWidth;
		private readonly float _platHeight;

		/// <summary>Live escalation loop (wired to AcidPhaseManager.Loop by ArenaScene).</summary>
		public System.Func<int> LoopProvider;

		private AcidSurface _acid;
		private bool  _active;
		private int   _totalTrackedCount;
		private bool  _spawnPending;
		private float _spawnTimer;
		private bool  _nextDropLeft = true;   // alternate sides so logs never clump

		public PlatformSpawner(int mapWidth, int mapHeight)
		{
			_platWidth  = GameConstants.Hazards.PlatformWidth * mapWidth;
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

			// Keep the platform population at the PER-LOOP target: 2 logs the
			// loop the tiers start falling, growing to 4 by the storm — the
			// late game gains moving footing as the static footing dies
			// (subtraction → transformation). Replacements queue with a
			// SPORADIC stagger (functional-test decision) so drops feel like
			// debris falling in, not a metronome.
			int target = AcidConfig.ScramblePlatformTargetFor(LoopProvider?.Invoke() ?? 0);
			if (_totalTrackedCount < target && !_spawnPending)
			{
				_spawnPending = true;
				_spawnTimer   = Random.Range(
					AcidConfig.PlatformDropStaggerMin,
					AcidConfig.PlatformDropStaggerMax) * AcidConfig.TimeScale();
			}

			if (!_spawnPending) return;

			_spawnTimer -= Time.DeltaTime;
			if (_spawnTimer > 0) return;

			_spawnPending = false;
			SpawnPlatform();
		}

		private void SpawnPlatform()
		{
			// Side-alternating drops anchored at the dissolved tiers' former X
			// positions (functional-test correction: random full-width spawns
			// clumped logs mid-air; the logs should arrive where platforms used
			// to live, one side then the other).
			var slots = _nextDropLeft ? AcidConfig.LogSpawnXLeft : AcidConfig.LogSpawnXRight;
			_nextDropLeft = !_nextDropLeft;
			float x = slots[Random.Range(0, slots.Length)]
			        + Random.Range(-AcidConfig.LogSpawnXJitter, AcidConfig.LogSpawnXJitter);
			float y = GameConstants.Hazards.PlatformFallSpawnY;

			var entity = Scene.CreateEntity("platform-drop");
			entity.Transform.Position = new Vector2(x, y);

			var platform = entity.AddComponent(
				new DynamicPlatform(_platWidth, _platHeight, _acid));

			Track(platform);
		}
	}
}
