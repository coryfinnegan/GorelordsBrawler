#if DEBUG
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Nez;
using GorelordsBrawler.Components;
using GorelordsBrawler.Input.Scripted;
using GorelordsBrawler.Systems;

namespace GorelordsBrawler.DevTools
{
	public enum RunMode
	{
		/// <summary>Normal real-time loop (the default).</summary>
		Free,

		/// <summary>Frozen between explicit step requests — advances a fixed dt per requested frame.</summary>
		Stepped
	}

	/// <summary>
	/// Game-thread control surface for E2E automation. The HTTP <see cref="GameDebugServer"/>
	/// runs on a background thread and must never touch live game state directly, so every
	/// mutation is enqueued here as an <see cref="Action"/> and drained on the game thread at
	/// the top of <c>GorelordsBrawlerGame.Update</c> — the same thread-safe handoff principle
	/// as the existing screenshot handshake.
	///
	/// Two responsibilities:
	///   1. <b>Command queue</b> — scripted input writes, teleports, run-mode changes.
	///   2. <b>Deterministic stepping</b> — a barrier the test awaits while the game advances
	///      N fixed-dt frames. Pausing here (gating <c>base.Update</c>) is a TRUE freeze, unlike
	///      <c>Time.TimeScale = 0</c> which still ticks SceneComponents and — as the acid NaN
	///      bug proved — can break dt-dividing systems.
	///
	/// DEBUG-only.
	/// </summary>
	public static class DebugControl
	{
		private static readonly ConcurrentQueue<Action> _commands = new();

		public static volatile RunMode Mode = RunMode.Free;

		private static readonly object _stepLock = new object();
		private static int _pendingSteps;
		private static TaskCompletionSource<bool> _stepBarrier;

		// ── Command queue ─────────────────────────────────────────────────────

		public static void Enqueue(Action cmd)
		{
			if (cmd != null)
			{
				_commands.Enqueue(cmd);
			}
		}

		/// <summary>Game thread only: run every queued command (call at the top of Update).</summary>
		public static void DrainCommands()
		{
			while (_commands.TryDequeue(out var cmd))
			{
				try
				{
					cmd();
				}
				catch (Exception e)
				{
					Nez.Debug.Warn($"[DebugControl] command failed: {e.Message}");
				}
			}
		}

		// ── Run mode ──────────────────────────────────────────────────────────

		public static void SetMode(RunMode mode)
		{
			if (mode == RunMode.Free)
			{
				// Releasing the freeze: unblock any in-flight stepper so its await returns.
				lock (_stepLock)
				{
					Mode          = RunMode.Free;
					_pendingSteps = 0;
					var b         = _stepBarrier;
					_stepBarrier  = null;
					b?.TrySetResult(true);
				}
			}
			else
			{
				Mode = RunMode.Stepped;
			}
		}

		// ── Deterministic stepping ────────────────────────────────────────────

		/// <summary>
		/// HTTP thread: request <paramref name="frames"/> fixed-dt frames. The returned task
		/// completes once the game thread has actually executed them. Intended for sequential
		/// use (await each request before the next); overlapping requests share one barrier and
		/// all complete together when the accumulated frame count drains.
		/// </summary>
		public static Task RequestStepsAsync(int frames)
		{
			lock (_stepLock)
			{
				Mode = RunMode.Stepped;
				if (frames <= 0)
				{
					return Task.CompletedTask;
				}
				_pendingSteps += frames;
				_stepBarrier ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				return _stepBarrier.Task;
			}
		}

		/// <summary>Game thread: is a step owed this frame?</summary>
		public static bool HasPendingStep()
		{
			lock (_stepLock)
			{
				return _pendingSteps > 0;
			}
		}

		/// <summary>Game thread: call exactly once after each stepped <c>base.Update</c>.</summary>
		public static void CompleteStep()
		{
			lock (_stepLock)
			{
				if (_pendingSteps > 0)
				{
					_pendingSteps--;
				}
				if (_pendingSteps == 0)
				{
					var b        = _stepBarrier;
					_stepBarrier = null;
					b?.TrySetResult(true);
				}
			}
		}

		// ── High-level commands (enqueued, applied on the game thread) ──────────

		/// <summary>
		/// Set scripted input for a player. Null arguments leave that field unchanged so a test
		/// can, e.g., toggle Attack without disturbing a held movement direction.
		/// </summary>
		public static void EnqueueInput(int player, int? moveX, int? moveY, bool? jump, bool? attack, bool? special)
		{
			Enqueue(() =>
			{
				var s = ScriptedInputRegistry.ForPlayer(player);
				if (moveX.HasValue)   s.MoveX   = Math.Sign(moveX.Value);
				if (moveY.HasValue)   s.MoveY   = Math.Sign(moveY.Value);
				if (jump.HasValue)    s.Jump    = jump.Value;
				if (attack.HasValue)  s.Attack  = attack.Value;
				if (special.HasValue) s.Special = special.Value;
			});
		}

		/// <summary>
		/// Place a player at a world position with zero velocity. Setup-only (bypasses input) —
		/// used to stage a scenario (e.g. two players adjacent) before driving the interaction
		/// under test with real scripted input.
		/// </summary>
		public static void EnqueueTeleport(int player, float x, float y)
		{
			Enqueue(() =>
			{
				var pm      = Core.Scene?.GetSceneComponent<PlayerManager>();
				var players = pm?.GetActivePlayers();
				if (players == null || player < 0 || player >= players.Count)
				{
					return;
				}
				var e = players[player];
				e.Transform.Position = new Vector2(x, y);
				var body = e.GetComponent<PhysicsBody>();
				if (body != null)
				{
					body.Velocity = Vector2.Zero;
				}
			});
		}

		/// <summary>
		/// Apply raw damage to a player's <see cref="Health"/>. Setup-only — used to stage a
		/// scenario deterministically (e.g. drive a player to 0 HP to exercise the death/respawn
		/// cycle) without leaning on a hazard's timing or geometry, which would couple the test to
		/// the hazard sim. Mirrors the <c>/teleport</c> handoff.
		/// </summary>
		public static void EnqueueDamage(int player, int amount)
		{
			Enqueue(() =>
			{
				var pm      = Core.Scene?.GetSceneComponent<PlayerManager>();
				var players = pm?.GetActivePlayers();
				if (players == null || player < 0 || player >= players.Count)
				{
					return;
				}
				players[player].GetComponent<Health>()?.TakeDamage(amount);
			});
		}
	}
}
#endif
