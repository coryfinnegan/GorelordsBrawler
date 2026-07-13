using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	/// <summary>
	/// The footing cycle (docs/platform-respawn-proposal.md — replaced the
	/// rockfall): when the acid eats a platform, a GHOST — a pulsing outline
	/// the exact size of a platform — flashes at the next spawn location for
	/// <see cref="AcidConfig.GhostSeconds"/>, then solidifies into a fresh
	/// erodible platform there. Population is conserved: one death schedules
	/// exactly one ghost, so footing is always being taken from where the
	/// acid is and re-offered somewhere else. Players chase the ghosts.
	///
	/// Placement is a seeded-random pick from the AcidConfig lattice, filtered
	/// by the CURRENT loop's rise ceiling (a fresh platform must outlive the
	/// rise it was born into), overlap with living platforms/ghosts, and a
	/// keep-away radius from the dead platform (the replacement must move the
	/// fight). In the storm only the top row passes the ceiling filter, so
	/// the endgame is a cramped chase over the last spawns (user call).
	///
	/// Deterministic under test: the rng is SEEDED, so stepped-mode E2E sees
	/// the same ghosts every run.
	/// </summary>
	public class PlatformRespawner : SceneComponent
	{
		/// <summary>Live escalation loop (wired to AcidPhaseManager.Loop).</summary>
		public Func<int> LoopProvider;

		/// <summary>True during the terminal storm (top-band placement).</summary>
		public Func<bool> IsStorm;

		// ── Automation oracles ────────────────────────────────────────────────
		public int PlatformsAlive => _alive.Count;
		public bool GhostActive => _ghosts.Count > 0;

		/// <summary>First active ghost's center, (-1,-1) when none.</summary>
		public Vector2 GhostPos =>
			_ghosts.Count > 0 ? _ghosts[0].Center : new Vector2(-1f, -1f);

		/// <summary>Center of the most recently spawned platform, (-1,-1) before any.</summary>
		public Vector2 LastSpawnPos { get; private set; } = new Vector2(-1f, -1f);

		/// <summary>
		/// Append the centers of every LIVING platform (the flood-aware respawn
		/// picker builds its dynamic candidate rungs from these).
		/// </summary>
		public void GetAlivePlatformCenters(List<Vector2> into)
		{
			foreach (var p in _alive)
			{
				if (p.Entity != null)
				{
					into.Add(p.Entity.Transform.Position);
				}
			}
		}

		private AcidSurface _acid;
		private readonly List<DissolvingPlatform> _alive = new();
		private readonly System.Random _rng = new System.Random(0x6057);

		private sealed class Ghost
		{
			public Vector2 Center;
			public float TimeLeft;
			public Entity Entity;
		}
		private readonly List<Ghost> _ghosts = new();

		private static readonly Collider[] _playerSensor = new Collider[4];

		public void Initialize(AcidSurface acid)
		{
			_acid = acid;
		}

		/// <summary>
		/// Adopt a platform into the cycle (the TMX starting pair and every
		/// platform this component spawns). Its death schedules the next ghost.
		/// </summary>
		public void Register(DissolvingPlatform platform)
		{
			_alive.Add(platform);
			var center = platform.Entity != null
				? platform.Entity.Transform.Position
				: Vector2.Zero;
			platform.OnDissolved = () =>
			{
				// Capture the death position BEFORE the entity is destroyed —
				// the keep-away filter measures from where footing was lost.
				var deadCenter = platform.Entity != null
					? platform.Entity.Transform.Position
					: center;
				_alive.Remove(platform);
				BeginGhost(deadCenter);
			};
		}

		public override void Update()
		{
			if (_ghosts.Count == 0)
			{
				return;
			}
			float dt = Time.DeltaTime;
			for (int i = _ghosts.Count - 1; i >= 0; i--)
			{
				_ghosts[i].TimeLeft -= dt;
				if (_ghosts[i].TimeLeft <= 0f)
				{
					var ghost = _ghosts[i];
					_ghosts.RemoveAt(i);
					ghost.Entity?.Destroy();
					SpawnPlatform(ghost.Center);
				}
			}
		}

		private void BeginGhost(Vector2 deadCenter)
		{
			var center = PickSpawnSpot(deadCenter);

			// Debug-fast compresses the ghost with every other phase timer so
			// the telegraph:action ratio survives the 4× lens.
			float duration = AcidConfig.GhostSeconds * AcidConfig.TimeScale();
			var entity = Scene.CreateEntity("platform-ghost");
			entity.Transform.Position = center;
			entity.AddComponent(new PlatformGhost(
				AcidConfig.PlatformW, AcidConfig.PlatformH, duration));

			_ghosts.Add(new Ghost { Center = center, TimeLeft = duration, Entity = entity });
		}

		private void SpawnPlatform(Vector2 center)
		{
			var entity = Scene.CreateEntity("platform-respawn");
			entity.Transform.Position = center;
			var platform = entity.AddComponent(new DissolvingPlatform(
				_acid, AcidConfig.PlatformW, AcidConfig.PlatformH, "respawn"));
			Register(platform);
			LastSpawnPos = center;

			// Spawn fairness: the ghost is intangible, so a player may be
			// standing inside the slab's volume at the moment it solidifies.
			// Snap them ON TOP (feet to the new surface, upward pop killed) —
			// the platform is a gift, never a crusher.
			float slabTop = center.Y - AcidConfig.PlatformH * 0.5f;
			var rect = new RectangleF(
				center.X - AcidConfig.PlatformW * 0.5f, slabTop,
				AcidConfig.PlatformW, AcidConfig.PlatformH);
			int count = Physics.OverlapRectangleAll(ref rect, _playerSensor, PhysicsLayers.Player);
			for (int i = 0; i < count; i++)
			{
				var pe = _playerSensor[i].Entity;
				if (pe == null)
				{
					continue;
				}
				// Entity position is the CENTER; feet sit half the collider
				// height below it — stand-on-top is top minus that half.
				float bodyHalfH = _playerSensor[i].Bounds.Height * 0.5f;
				pe.Transform.Position = new Vector2(
					pe.Transform.Position.X, slabTop - bodyHalfH - 1f);
				var body = pe.GetComponent<Components.PhysicsBody>();
				if (body != null && body.Velocity.Y > 0f)
				{
					body.Velocity = new Vector2(body.Velocity.X, 0f);
				}
			}
		}

		/// <summary>
		/// Pick the next spawn's center from the lattice. Filters, in order of
		/// principle: the platform TOP must clear the current ceiling by the
		/// clearance band (survive its own loop); no overlap with a living
		/// platform or an active ghost; and keep away from the death site so
		/// the fight moves. If the full filter set empties (cramped storm),
		/// relax in stages — keep-away first, then clearance — so there is
		/// ALWAYS a spawn; the cycle never stalls.
		/// </summary>
		private Vector2 PickSpawnSpot(Vector2 deadCenter)
		{
			bool storm     = IsStorm?.Invoke() ?? false;
			int loop       = LoopProvider?.Invoke() ?? 0;
			float ceilingY = storm ? AcidConfig.StormCeilingY : AcidConfig.RiseCeilingFor(loop);

			var candidates = new List<Vector2>();
			for (int stage = 0; stage < 3 && candidates.Count == 0; stage++)
			{
				float clearance = stage < 2 ? AcidConfig.PlatformSpawnClearance : 16f;
				bool keepAway   = stage < 1;
				foreach (float topY in AcidConfig.PlatformSlotTopY)
				{
					if (topY > ceilingY - clearance)
					{
						continue; // this row would be eaten by the current rise
					}
					foreach (float x in AcidConfig.PlatformSlotX)
					{
						var center = new Vector2(x, topY + AcidConfig.PlatformH * 0.5f);
						if (keepAway && Vector2.Distance(center, deadCenter) < AcidConfig.PlatformMinMoveDistance)
						{
							continue;
						}
						if (OverlapsExisting(center))
						{
							continue;
						}
						candidates.Add(center);
					}
				}
			}
			if (candidates.Count == 0)
			{
				// Every slot occupied at every relaxation (theoretical): stack
				// the top band's first column rather than stalling the cycle.
				return new Vector2(AcidConfig.PlatformSlotX[0],
					AcidConfig.PlatformSlotTopY[0] + AcidConfig.PlatformH * 0.5f);
			}
			return candidates[_rng.Next(candidates.Count)];
		}

		private bool OverlapsExisting(Vector2 center)
		{
			foreach (var p in _alive)
			{
				if (p.Entity != null && SlabsOverlap(center, p.Entity.Transform.Position))
				{
					return true;
				}
			}
			foreach (var g in _ghosts)
			{
				if (SlabsOverlap(center, g.Center))
				{
					return true;
				}
			}
			return false;
		}

		private static bool SlabsOverlap(Vector2 a, Vector2 b)
		{
			// A little breathing room beyond strict overlap so two slabs never
			// spawn edge-kissing into one visual mega-platform.
			return MathF.Abs(a.X - b.X) < AcidConfig.PlatformW + 32f
				&& MathF.Abs(a.Y - b.Y) < AcidConfig.PlatformH + 32f;
		}
	}

	/// <summary>
	/// The spawn telegraph: a pulsing outline the exact footprint of the
	/// platform that will materialize here, pulse quickening as the spawn
	/// approaches (the Phase-D tell grammar — same rule as the surge/rise
	/// tells: every change of footing announces itself first). Render-only;
	/// the ghost has no collider.
	/// </summary>
	public class PlatformGhost : RenderableComponent
	{
		public override float Width  => _w;
		public override float Height => _h;

		private readonly float _w;
		private readonly float _h;
		private readonly float _duration;
		private float _elapsed;

		private static readonly Color _warn = new Color(255, 200, 60);

		public PlatformGhost(float w, float h, float duration)
		{
			_w = w;
			_h = h;
			_duration = Math.Max(duration, 0.05f);
		}

		public override void OnAddedToEntity()
		{
			RenderLayer = GameConstants.Rendering.HitboxRenderLayer;
		}

		public override void Render(Batcher batcher, Camera camera)
		{
			_elapsed += Time.DeltaTime;
			float t     = MathHelper.Clamp(_elapsed / _duration, 0f, 1f);
			float flash = MathF.Sin(_elapsed * (10f + 26f * t)) * 0.5f + 0.5f;
			var strong  = _warn * (0.40f + 0.60f * flash);
			var faint   = _warn * (0.10f + 0.15f * flash);

			var pos  = Entity.Transform.Position;
			var rect = new RectangleF(pos.X - _w * 0.5f, pos.Y - _h * 0.5f, _w, _h);
			// Faint fill so the slab's full footprint reads, hard outline for
			// the edge players will actually land on.
			batcher.DrawRect(rect, faint);
			batcher.DrawHollowRect(rect, strong, 2f);
		}
	}
}
