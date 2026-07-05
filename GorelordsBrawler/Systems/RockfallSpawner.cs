using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components.Hazards;
using GorelordsBrawler.Constants;

namespace GorelordsBrawler.Systems
{
	/// <summary>
	/// The rockfall (docs/rockfall-proposal.md): from loop 1 the crumbling
	/// facility sheds boulders down TELEGRAPHED drop columns, escalating to a
	/// storm-time rockfall. Rocks pile into cairns whose caps breach the acid
	/// — the self-assembling recovery route.
	///
	/// Structured randomness (user decision): drops pick among the UNLOCKED
	/// columns (center channel always; tier-ghost columns open as their tiers
	/// die, so a rock never clips through a living platform), with a
	/// <see cref="AcidConfig.RockPileBias"/> chance of aiming at a living
	/// rock's column instead — chaos that still reliably builds islands.
	///
	/// Deterministic under test: positions/heights come from a SEEDED rng, so
	/// stepped-mode E2E sees the same drops every run.
	/// </summary>
	public class RockfallSpawner : SceneComponent
	{
		/// <summary>Live escalation loop (wired to AcidPhaseManager.Loop).</summary>
		public Func<int> LoopProvider;

		/// <summary>True during the terminal storm (rockfall cadence).</summary>
		public Func<bool> IsStorm;

		/// <summary>Column unlocks — true once the LOW / MID tier pairs are gone.</summary>
		public Func<bool> LowsDead;
		public Func<bool> MidsDead;

		/// <summary>Live rock count — automation oracle.</summary>
		public int RocksAlive => _rocks.Count;

		/// <summary>
		/// Resting rocks whose cap is proud of the measured standing surface —
		/// the recovery-route oracle ("is there an island right now?").
		/// </summary>
		public int RockIslands
		{
			get
			{
				float surface = _acid?.GetStandingSurfaceY() ?? float.MaxValue;
				int n = 0;
				for (int i = 0; i < _rocks.Count; i++)
				{
					if (_rocks[i].IsResting && _rocks[i].TopY <= surface - 8f)
					{
						n++;
					}
				}
				return n;
			}
		}

		/// <summary>First FALLING rock's position (x, y) — E2E uses it to stage
		/// the impact-damage scenario. (-1, -1) when none is falling.</summary>
		public Vector2 FirstFallingRockPos
		{
			get
			{
				for (int i = 0; i < _rocks.Count; i++)
				{
					if (!_rocks[i].IsResting && _rocks[i].Entity != null)
					{
						return _rocks[i].Entity.Transform.Position;
					}
				}
				return new Vector2(-1f, -1f);
			}
		}

		private AcidSurface _acid;
		private readonly List<FallingRock> _rocks = new();
		private readonly System.Random _rng = new System.Random(0x0C4A);

		private float _nextDropIn;
		private float _telegraphLeft;
		private float _pendingX;
		private float _pendingHeight;
		private Entity _marker;

		public void Initialize(AcidSurface acid)
		{
			_acid = acid;
		}

		public override void Update()
		{
			int loop = LoopProvider?.Invoke() ?? 0;
			bool storm = IsStorm?.Invoke() ?? false;

			// No rocks until the arena starts LOSING footing (loop 2+ / the
			// storm): the infinite interval alone is NOT enough — the drop
			// logic below fires once before scheduling, and that single free
			// boulder built a loop-1 pit tower whose collapse waves killed the
			// contested lows (E2E caught it twice).
			if (_acid == null || (loop < 2 && !storm))
			{
				return;
			}

			float ts = AcidConfig.TimeScale();
			float dt = Time.DeltaTime;

			// A drop in flight: run the telegraph down, then release the rock.
			if (_telegraphLeft > 0f)
			{
				_telegraphLeft -= dt;
				if (_telegraphLeft <= 0f)
				{
					ReleaseRock();
				}
				return;
			}

			_nextDropIn -= dt;
			if (_nextDropIn > 0f)
			{
				return;
			}

			float interval = storm
				? AcidConfig.StormRockIntervalSeconds
				: AcidConfig.RockIntervalFor(loop);
			if (float.IsInfinity(interval))
			{
				return;   // belt + suspenders: never spawn on an infinite beat
			}
			_nextDropIn = interval * ts;

			if (_rocks.Count >= AcidConfig.RockMaxAlive)
			{
				return;   // at the clutter/perf cap — skip this beat
			}

			if (!TryPickDropX(out float x))
			{
				return;   // no ghost column open yet — the arena hasn't lost footing
			}
			BeginTelegraph(x);
		}

		private void BeginTelegraph(float x)
		{
			_pendingX      = x;
			_pendingHeight = AcidConfig.RockHeights[_rng.Next(AcidConfig.RockHeights.Length)];
			_telegraphLeft = AcidConfig.RockTelegraphSeconds * AcidConfig.TimeScale();

			_marker = Scene.CreateEntity("rock-telegraph");
			_marker.Transform.Position = new Vector2(_pendingX, 48f);
			_marker.AddComponent(new RockTelegraphMarker(AcidConfig.RockTelegraphSeconds * AcidConfig.TimeScale()));
		}

		private void ReleaseRock()
		{
			_marker?.Destroy();
			_marker = null;

			var entity = Scene.CreateEntity("falling-rock");
			entity.Transform.Position = new Vector2(
				_pendingX, GameConstants.Hazards.RockFallSpawnY - _pendingHeight * 0.5f);

			var rock = entity.AddComponent(
				new FallingRock(AcidConfig.RockWidth, _pendingHeight, _acid));
			_rocks.Add(rock);
			rock.OnDestroyed += () =>
			{
				_rocks.Remove(rock);
				rock.Entity?.Destroy();
			};
		}

		private bool TryPickDropX(out float x)
		{
			// GHOST columns only (see AcidConfig.RockSlot* — living-platform
			// clearance + standing-probe integrity both hang on this policy).
			_slots.Clear();
			if (LowsDead?.Invoke() == true)
			{
				_slots.AddRange(AcidConfig.RockSlotLowGhostX);
			}
			if (MidsDead?.Invoke() == true)
			{
				_slots.AddRange(AcidConfig.RockSlotMidGhostX);
			}
			if (_slots.Count == 0)
			{
				x = 0f;
				return false;
			}

			// Pile bias: aim at a living rock's column so cairns actually form.
			if (_rocks.Count > 0 && _rng.NextDouble() < AcidConfig.RockPileBias)
			{
				var target = _rocks[_rng.Next(_rocks.Count)];
				if (target.Entity != null)
				{
					x = target.Entity.Transform.Position.X + Jitter();
					return true;
				}
			}

			x = _slots[_rng.Next(_slots.Count)] + Jitter();
			return true;
		}

		private readonly List<float> _slots = new();

		private float Jitter() =>
			((float)_rng.NextDouble() * 2f - 1f) * AcidConfig.RockSlotJitter;
	}

	/// <summary>
	/// The rockfall warning: a pulsing hazard chevron at the drop column for
	/// the telegraph window (Phase-D rule — every hit announces itself).
	/// Greybox visual: stacked shrinking bars pointing down; the art pass can
	/// restyle it without touching the timing.
	/// </summary>
	public class RockTelegraphMarker : RenderableComponent
	{
		public override float Width  => 48f;
		public override float Height => 48f;

		private readonly float _duration;
		private float _elapsed;

		private static readonly Color _warn = new Color(255, 200, 60);

		public RockTelegraphMarker(float duration)
		{
			_duration = Math.Max(duration, 0.05f);
		}

		public override void OnAddedToEntity()
		{
			RenderLayer = GameConstants.Rendering.HitboxRenderLayer;
		}

		public override void Render(Batcher batcher, Camera camera)
		{
			// Pulse quickens as the drop approaches (2 → 8 Hz feel via the
			// elapsed-scaled flash), alpha never fully vanishes so the column
			// stays marked.
			_elapsed += Time.DeltaTime;
			float t     = MathHelper.Clamp(_elapsed / _duration, 0f, 1f);
			float flash = (MathF.Sin(_elapsed * (12f + 24f * t)) * 0.5f + 0.5f);
			var c = _warn * (0.45f + 0.55f * flash);

			var pos = Entity.Transform.Position;
			// Downward chevron: three stacked bars, narrowing.
			batcher.DrawRect(new RectangleF(pos.X - 18f, pos.Y,       36f, 8f), c);
			batcher.DrawRect(new RectangleF(pos.X - 11f, pos.Y + 12f, 22f, 8f), c);
			batcher.DrawRect(new RectangleF(pos.X - 5f,  pos.Y + 24f, 10f, 8f), c);
		}
	}
}
