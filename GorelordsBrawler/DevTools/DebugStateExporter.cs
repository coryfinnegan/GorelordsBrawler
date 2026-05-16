#if DEBUG
using System;
using System.Text.Json;
using System.Collections.Generic;
using Nez;
using GorelordsBrawler.Components;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.DevTools
{
	/// <summary>
	/// SceneComponent added to ArenaScene in DEBUG builds.
	/// Serializes acid + player state into JSON every frame and pushes it to GameDebugServer.
	/// </summary>
	public class DebugStateExporter : SceneComponent
	{
		private readonly AcidSurface    _acid;
		private readonly PlayerManager  _players;
		private float                   _time;

		public DebugStateExporter(AcidSurface acid, PlayerManager players)
		{
			_acid    = acid;
			_players = players;
		}

		public override void Update()
		{
			_time += Time.DeltaTime;

			var playerList = new List<object>();
			int id = 0;
			foreach (var p in _players.GetActivePlayers())
			{
				var health = p.GetComponent<Health>();
				var body   = p.GetComponent<PhysicsBody>();
				var pos    = p.Transform.Position;
				playerList.Add(new
				{
					id,
					x        = (int)pos.X,
					y        = (int)pos.Y,
					hp       = health?.CurrentHp ?? 0,
					maxHp    = health?.MaxHp    ?? 0,
					grounded = body?.Grounded   ?? false,
					vx       = (int)(body?.Velocity.X ?? 0),
					vy       = (int)(body?.Velocity.Y ?? 0),
				});
				id++;
			}

			var state = new
			{
				time        = Math.Round(_time, 1),
				acidActive  = _acid.IsRising,
				acidLevel   = (int)_acid.CurrentLevel,
				acidSpeed   = _acid.IsRising ? 1 : 0,
				players     = playerList,
			};

			GameDebugServer.UpdateState(JsonSerializer.Serialize(state));
		}
	}
}
#endif
