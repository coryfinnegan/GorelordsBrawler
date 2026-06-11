#if DEBUG
using System;
using System.Collections.Generic;
using System.Text.Json;
using Nez;
using GorelordsBrawler.Components;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.DevTools
{
	/// <summary>
	/// SceneComponent added to scenes in DEBUG builds. Pushes a JSON snapshot
	/// of game state to GameDebugServer every frame so smoke tests / external
	/// drivers can poll the live game over HTTP at <c>/state</c>.
	///
	/// Generic + extensible: <c>time</c> and <c>players</c> are always emitted
	/// (every feature wants those). Feature-specific state — acid level, combat
	/// round, whatever — is contributed by callers via <see cref="RegisterProvider"/>:
	///
	/// <code>
	/// var exporter = AddSceneComponent(new DebugStateExporter(playerManager));
	/// exporter.RegisterProvider("acidActive", () =&gt; acidSurface.IsRising);
	/// exporter.RegisterProvider("acidLevel",  () =&gt; (int)acidSurface.CurrentLevel);
	/// </code>
	///
	/// Provider exceptions are swallowed (one broken contributor doesn't kill
	/// the whole snapshot) and surface as <c>"_error": "..."</c> in the JSON.
	/// </summary>
	public class DebugStateExporter : SceneComponent
	{
		private readonly PlayerManager _players;
		private readonly Dictionary<string, Func<object>> _providers = new();
		private float _time;

		public DebugStateExporter(PlayerManager players)
		{
			_players = players;
		}

		/// <summary>
		/// Register a state key that will be serialized into the per-frame JSON
		/// snapshot under <c>/state</c>. Last registration wins for a given key.
		/// </summary>
		public void RegisterProvider(string key, Func<object> getter)
		{
			if (string.IsNullOrEmpty(key) || getter == null)
			{
				return;
			}
			_providers[key] = getter;
		}

		public override void Update()
		{
			_time += Time.DeltaTime;

			var state = new Dictionary<string, object>
			{
				["time"]    = Math.Round(_time, 1),
				["players"] = BuildPlayerSnapshots(),
			};

			foreach (var kvp in _providers)
			{
				try
				{
					state[kvp.Key] = kvp.Value();
				}
				catch (Exception e)
				{
					state[kvp.Key] = new { _error = e.Message };
				}
			}

			GameDebugServer.UpdateState(JsonSerializer.Serialize(state));
		}

		private List<object> BuildPlayerSnapshots()
		{
			var list = new List<object>();
			if (_players == null)
			{
				return list;
			}

			int id = 0;
			foreach (var p in _players.GetActivePlayers())
			{
				var health  = p.GetComponent<Health>();
				var body    = p.GetComponent<PhysicsBody>();
				var hurtbox = p.GetComponent<Hurtbox>();
				var hitstun = p.GetComponent<Hitstun>();
				var submersion = p.GetComponent<SubmersionFeel>();
				var pos     = p.Transform.Position;
				list.Add(new
				{
					id,
					x        = (int)pos.X,
					y        = (int)pos.Y,
					hp       = health?.CurrentHp ?? 0,
					maxHp    = health?.MaxHp    ?? 0,
					grounded = body?.Grounded   ?? false,
					vx       = (int)(body?.Velocity.X ?? 0),
					vy       = (int)(body?.Velocity.Y ?? 0),
					// Trustworthy combat oracles for the E2E harness (acid-independent):
					hitstun        = hitstun?.IsActive ?? false,
					meleeHitsTaken = hurtbox?.HitsTaken ?? 0,
					dead           = health?.IsDead ?? false,
					// FacingDirection is written ONLY by WalkAbility's normal movement branch,
					// which hitstun skips — so it's a clean, knockback-independent probe of
					// whether input is being processed (used by the hitstun-lockout test).
					facing         = body?.FacingDirection ?? 1,
					// Phase B oracles: depth-scaled acid lethality + swim escape.
					submerged      = submersion?.IsSubmerged ?? false,
					submergedDepth = (int)(submersion?.SubmergedDepth ?? 0f),
				});
				id++;
			}
			return list;
		}
	}
}
#endif
