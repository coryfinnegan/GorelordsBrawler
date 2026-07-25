using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	/// <summary>
	/// The footing DIRECTOR (docs/platform-respawn-proposal.md, pacing rework
	/// 2026-07-24): maintains a TARGET platform population from the first
	/// frame — an opening volley of ghosts flashes at t=0 (full footing ~3 s
	/// in) and any later shortfall is topped up on a staggered cadence —
	/// instead of only replacing platforms after the acid eats one. Every
	/// spawn is still led by a GHOST — a pulsing outline the exact size of a
	/// platform — for <see cref="AcidConfig.GhostSeconds"/> before it
	/// solidifies into a fresh erodible platform. A death raises its
	/// replacement ghost immediately while the population is short of target,
	/// so footing is still being taken from where the acid is and re-offered
	/// somewhere else. Players chase the ghosts.
	///
	/// Placement is a random pick from the AcidConfig lattice, filtered by
	/// the CURRENT loop's rise ceiling (a fresh platform must outlive the
	/// rise it was born into), the sliding spawn band (footing hugs the
	/// danger zone and climbs with the flood), overlap/stacking with living
	/// platforms and ghosts, and a keep-away radius from a dead platform
	/// (the replacement must move the fight). In the storm only the top row
	/// passes the ceiling filter and the target shrinks, so the endgame is a
	/// cramped chase over the last spawns (user call).
	///
	/// Deterministic under test: under DebugAutomation the rng is SEEDED, so
	/// stepped-mode E2E sees the same ghosts every run; a real match seeds
	/// from the clock so no two matches replay the same footing script.
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
		public int GhostCount => _ghosts.Count;

		/// <summary>The director's current population target (storm-aware).</summary>
		public int TargetAlive => CurrentTarget();

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
		private bool _openingDone;
		private float _nextTopUpIn;

		// Seeded ONLY under automation (stepped-mode E2E must see the same
		// ghosts every run); a real match varies so the footing script never
		// replays.
		private readonly System.Random _rng = new System.Random(
			AppSettings.DebugAutomation ? 0x6057 : Environment.TickCount);

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
				// A death replaces itself immediately ONLY while the population
				// is short of target — when the storm shrinks the target, the
				// surplus deaths burn off without replacement and the count
				// settles onto the new target on its own.
				if (_alive.Count + _ghosts.Count < CurrentTarget())
				{
					BeginGhost(deadCenter);
				}
			};
		}

		public override void Update()
		{
			// The OPENING VOLLEY: the match's first footing telegraphs on the
			// very first frame — the symmetric inward-mid pair (fixed slots so
			// neither player opens advantaged), with the director below topping
			// the population up to target right behind it.
			if (!_openingDone)
			{
				_openingDone = true;
				foreach (var slot in AcidConfig.PlatformOpeningCenters)
				{
					if (!OverlapsExisting(slot))
					{
						CreateGhost(slot);
					}
				}
				_nextTopUpIn = AcidConfig.PlatformTopUpStaggerSeconds * AcidConfig.TimeScale();
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

			// The DIRECTOR: top up any shortfall (ghosts count toward the
			// population — a flashing telegraph is footing already promised).
			// Successive top-ups cascade on the stagger so each telegraph
			// reads on its own; the timer idles at zero while the population
			// is full so the first top-up of a new shortfall fires promptly.
			int deficit = CurrentTarget() - _alive.Count - _ghosts.Count;
			if (deficit <= 0)
			{
				_nextTopUpIn = 0f;
			}
			else
			{
				_nextTopUpIn -= dt;
				if (_nextTopUpIn <= 0f)
				{
					BeginGhost(null);
					_nextTopUpIn = AcidConfig.PlatformTopUpStaggerSeconds * AcidConfig.TimeScale();
				}
			}
		}

		private int CurrentTarget() =>
			AcidConfig.PlatformTargetFor(IsStorm?.Invoke() ?? false);

		private void BeginGhost(Vector2? deadCenter)
		{
			CreateGhost(PickSpawnSpot(deadCenter));
		}

		private void CreateGhost(Vector2 center)
		{
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
		/// clearance band (survive its own loop) AND sit inside the sliding
		/// spawn band above it (footing hugs the danger zone); no overlap or
		/// same-column stacking with a living platform or an active ghost; and
		/// keep away from the death site (when there is one — director top-ups
		/// have none) so the fight moves. If the full filter set empties
		/// (cramped storm), relax in stages — keep-away first, then the band,
		/// then clearance — so there is ALWAYS a spawn; the cycle never stalls.
		/// </summary>
		private Vector2 PickSpawnSpot(Vector2? deadCenter)
		{
			bool storm     = IsStorm?.Invoke() ?? false;
			int loop       = LoopProvider?.Invoke() ?? 0;
			float ceilingY = storm ? AcidConfig.StormCeilingY : AcidConfig.RiseCeilingFor(loop);

			var candidates = new List<Vector2>();
			for (int stage = 0; stage < 4 && candidates.Count == 0; stage++)
			{
				bool keepAway   = stage < 1 && deadCenter.HasValue;
				bool bandCap    = stage < 2;
				float clearance = stage < 3 ? AcidConfig.PlatformSpawnClearance : 16f;
				foreach (float topY in AcidConfig.PlatformSlotTopY)
				{
					if (!AcidConfig.PlatformRowViable(topY, ceilingY, clearance, bandRelaxed: !bandCap))
					{
						continue; // eaten by the current rise, or off the band
					}
					foreach (float x in AcidConfig.PlatformSlotX)
					{
						var center = new Vector2(x, topY + AcidConfig.PlatformH * 0.5f);
						if (keepAway && Vector2.Distance(center, deadCenter.Value) < AcidConfig.PlatformMinMoveDistance)
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
			// Horizontal: a little breathing room beyond strict overlap so two
			// slabs never spawn edge-kissing into one visual mega-platform.
			// Vertical: the stack clearance — same-column adjacent rows are 64
			// px apart, a gap too short to stand in or jump through, so slabs
			// sharing a column must keep a full clearance between them and the
			// layout stays staggered staircases, never stacked shelves.
			return MathF.Abs(a.X - b.X) < AcidConfig.PlatformW + 32f
				&& MathF.Abs(a.Y - b.Y) < AcidConfig.PlatformH + AcidConfig.PlatformStackClearance;
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
