# Proposal: E2E gameplay automation (scripted input + deterministic frame-stepping)

## Summary

Today our E2E harness can only *launch* the game and *read* state — `GameDebugServer`
exposes `GET /state` and `GET /screenshot` and nothing else. That's why the
hitstop→acid NaN bug (PR #11) could not get a real end-to-end test: there was no way
to make a player throw a punch from a test.

This proposal adds a **write channel** so tests can drive actual gameplay:

1. A **scripted input device** — a new `InputDeviceType.Scripted` whose buttons/axes are
   set programmatically over HTTP. Input flows through the *real* `VirtualButton` /
   `VirtualIntegerAxis` pipeline, so abilities (`WalkAbility`, `JumpAbility`,
   `CombatController`) see it exactly as keyboard/gamepad input — buffering, edge
   detection, jump-coyote, attack-buffer windows all behave identically.
2. **Deterministic frame-stepping** — a pause/`step N frames` command that advances the
   game with a *fixed* `dt`, so tests control time instead of racing wall-clock `Task.Delay`
   (the current source of E2E flakiness).
3. A small **command channel** on `GameDebugServer` (`POST /input`, `/step`, `/run`, plus a
   couple of setup helpers) reusing the existing thread-safe handoff pattern.

All of it is `#if DEBUG`-only, like the rest of DevTools.

## Motivation

- **Catch the bugs we just shipped.** The acid-vanishes-on-melee-hit bug lived entirely in
  the interaction between melee hitstop (`Time.TimeScale = 0`) and the fluid sim. A unit
  test now pins the `Step(dt=0)` failure mode, but nothing exercises the *wiring* —
  "land a real hit, confirm the acid survives." That class of cross-system bug is exactly
  what E2E should cover.
- **Reduce flakiness.** Existing E2E tests poll `/state` with `await Task.Delay(2000)`.
  That's timing-dependent and will get worse as we add tests. Frame-stepping makes a test a
  deterministic sequence: set input → step N frames → assert.
- **Reuse for smoke tests.** The `/smoke-test` skill currently can't script gameplay either;
  scripted input lets a smoke test play a short scripted bout instead of recording an idle
  scene.

## Goals / non-goals

**Goals**
- Drive players (move, jump, attack, special) from the test process through the real input path.
- Deterministic, repeatable runs (fixed `dt`, seeded systems, input isolation).
- Keep `/state` + `/screenshot` working unchanged.
- Zero changes to gameplay components — they keep reading `InputProfile`.

**Non-goals**
- Headless rendering. We keep a real window (the `/screenshot` path needs the back buffer,
  and Godot's headless input/render limitation — see Sources — confirms windowed is the safe
  choice). Tests already run with a visible window today.
- A full record/replay system. Out of scope; this is the primitive layer it would build on.
- Networked/lockstep determinism. We only need single-machine repeatability.

## Background — how input and the loop work today (verified in code)

**Input** ([Input/InputProfile.cs](../GorelordsBrawler/Input/InputProfile.cs),
[Input/InputProfileFactory.cs](../GorelordsBrawler/Input/InputProfileFactory.cs)):
- `InputProfile` holds `VirtualIntegerAxis MoveX/MoveY` and `VirtualButton Jump/Attack/Special`.
- A `VirtualButton` is a list of `VirtualButton.Node`s (abstract: `IsDown/IsPressed/IsReleased`);
  a `VirtualIntegerAxis` is a list of `VirtualAxis.Node`s (abstract `Value`, with edge
  detection — `DirectionJustPushed`/`JustPushed` — already implemented in the base via
  `_previousValue`).
- `VirtualInput` self-registers into `Input._virtualInputs` and Nez calls `Update()` on each
  every frame, which calls each node's `Update()`. Button buffering uses `Time.UnscaledDeltaTime`.
- **Key consequence:** if we add scripted *nodes*, every consumer is unchanged. `CombatController`
  ([Components/CombatController.cs:133](../GorelordsBrawler/Components/CombatController.cs)) reads
  `_input.Attack.IsPressed`; it can't tell a scripted node from a keyboard key.

**Game loop** ([GorelordsBrawlerGame.cs](../GorelordsBrawler/GorelordsBrawlerGame.cs)):
- `GorelordsBrawlerGame : Nez.Core`. MonoGame drives `Update(GameTime)` → `base.Update` advances
  Nez `Time`, input, and the scene; `Draw(GameTime)` renders and (when a screenshot is pending)
  captures the back buffer.
- `Time.DeltaTime` is derived from the `GameTime.ElapsedGameTime` Nez receives — so if we *pass our
  own* `GameTime` with a fixed elapsed, `dt` is deterministic regardless of wall clock.

**Determinism is already within reach.** The acid sim seeds its RNG (`new Random(0xACED)` in both
`AcidSurface` and `FluidSimulation`), and `AcidSurface.Update` clamps `dt` to `MaxDeltaTime`. With a
fixed step `dt` and isolated input, the same script should produce the same run.

## Research findings (and what we take from each)

Both major engines inject synthetic input at the **action/abstraction layer**, not by faking OS
key events, and both give tests **control over time**. That's the design we mirror.

- **Unity `InputTestFixture`** — provides `Press`/`Release`/`Set`/`Trigger` against the Input
  System, *isolated from platform input* so the test machine's real keyboard can't interfere, and
  exposes a settable current-time for deterministic timing. → We mirror this with scripted nodes
  (the abstraction layer) and the scripted device being independent of the keyboard (isolation).
- **Godot `Input.action_press` / `action_release`** — simulate at the *action* level. Caveat:
  in `--headless` propagated input/render events misbehave. → Confirms (a) inject at the action
  layer and (b) keep a real window.
- **Gaffer On Games, "Fix Your Timestep!"** — the accumulator + fixed-`dt` pattern; fixed steps
  give "exact reproducibility from one run to the next given the same inputs." → Our `/step` uses a
  fixed `dt` per frame.
- **Jakub Tomšů, "Reliable fixed timestep & inputs"** and **André Leite, "Taming Time in Game
  Engines"** — practical notes on sampling input *per fixed step* (not per render frame) so input
  and simulation stay phase-aligned. → We set scripted input, then step; input is sampled inside
  the stepped frame, never between.

Caveat both Gaffer and the Eidos-Montréal automated-testing writeup raise: replaying canned inputs
only works if the game is deterministic. Our acid RNG is seeded, but anything that later introduces
unseeded randomness would need a test seed hook — noted under Risks.

## Design

### 1. Scripted input device

A thread-safe per-player state object, plus node types that read from it:

```csharp
// Input/Scripted/ScriptedInputState.cs  (#if DEBUG)
public sealed class ScriptedInputState
{
    // volatile / interlocked or guarded by a lock — written by the HTTP thread,
    // read by the game thread.
    public volatile int  MoveX, MoveY;     // -1 / 0 / 1
    public volatile bool Jump, Attack, Special;
}

// A registry so the debug server can find a player's state by index.
public static class ScriptedInputRegistry
{
    private static readonly ConcurrentDictionary<int, ScriptedInputState> _states = new();
    public static ScriptedInputState ForPlayer(int i) => _states.GetOrAdd(i, _ => new());
    public static void Clear() => _states.Clear();
}
```

```csharp
// Input/Scripted/ScriptedNodes.cs  (#if DEBUG)
// Axis: base class already does edge detection from Value, so we only supply Value.
public sealed class ScriptedAxisNode : VirtualAxis.Node
{
    private readonly ScriptedInputState _s; private readonly bool _isY;
    public ScriptedAxisNode(ScriptedInputState s, bool isY) { _s = s; _isY = isY; }
    public override float Value => _isY ? _s.MoveY : _s.MoveX;
}

// Button: VirtualButton.Node has NO base edge logic, so we latch edges in Update().
// VirtualButton.Update() calls node.Update() THEN reads IsPressed — so we must
// compute the edge inside Update() and expose latched booleans.
public sealed class ScriptedButtonNode : VirtualButton.Node
{
    private readonly Func<bool> _read;
    private bool _down, _prevDown, _pressed, _released;
    public ScriptedButtonNode(Func<bool> read) { _read = read; }
    public override void Update()
    {
        _down     = _read();
        _pressed  = _down && !_prevDown;
        _released = !_down && _prevDown;
        _prevDown = _down;
    }
    public override bool IsDown     => _down;
    public override bool IsPressed  => _pressed;
    public override bool IsReleased => _released;
}
```

```csharp
// InputProfileFactory.CreateScripted(int playerIndex)  (#if DEBUG)
var s = ScriptedInputRegistry.ForPlayer(playerIndex);
return new InputProfile {
    MoveX   = new VirtualIntegerAxis(new ScriptedAxisNode(s, isY:false)),
    MoveY   = new VirtualIntegerAxis(new ScriptedAxisNode(s, isY:true)),
    Jump    = new VirtualButton(GameConstants.Input.JumpBufferTime, new ScriptedButtonNode(() => s.Jump)),
    Attack  = new VirtualButton(new ScriptedButtonNode(() => s.Attack)),
    Special = new VirtualButton(new ScriptedButtonNode(() => s.Special)),
};
```

Add `InputDeviceType.Scripted0 / Scripted1` to the enum
([Systems/MatchSetupManager.cs](../GorelordsBrawler/Systems/MatchSetupManager.cs)) and a
`CreateFromDevice` switch arm. An automation entry point (new `AppSettings.DebugAutomation`, or
reuse `DebugDirectArena` with scripted devices) selects scripted devices for both players.

Because the scripted nodes don't touch keyboard/gamepad, real input on the test machine cannot
interfere — the isolation property Unity's fixture is careful about.

### 2. Deterministic frame-stepping

Add a run-mode gate to `GorelordsBrawlerGame.Update`:

```csharp
// run mode: Free (normal) | Stepped (advance only when frames are requested)
protected override void Update(GameTime gameTime)
{
    DebugControl.DrainCommands();        // apply queued /input, /teleport, mode changes (game thread)

    if (DebugControl.Mode == RunMode.Stepped)
    {
        if (DebugControl.PendingSteps > 0)
        {
            DebugControl.PendingSteps--;
            base.Update(FixedStepGameTime); // elapsed = 1/60s, deterministic dt
            if (DebugControl.PendingSteps == 0)
                DebugControl.CompleteStepBarrier(); // unblock the awaiting /step request
        }
        // else: frozen — do nothing (Draw still runs, so /screenshot works while paused)
    }
    else
    {
        base.Update(gameTime);
    }
}
```

- `FixedStepGameTime` is a cached `GameTime` with `ElapsedGameTime = 1/60s`. Passing it to
  `base.Update` makes Nez `Time.DeltaTime` constant per step — independent of how fast the host
  actually loops. This is the determinism lever.
- We do **not** use `Time.TimeScale = 0` to pause (that's the very thing that NaN'd the fluid, and
  it doesn't stop `SceneComponent.Update`). Gating `base.Update` is a true freeze.
- The in-game `PauseManager` (gameplay pause menu) is unaffected — different layer.

### 3. Command channel on `GameDebugServer`

Reuse the existing background-thread + game-thread handoff. Today `/screenshot` enqueues a
`TaskCompletionSource` that the game thread completes in `Draw`. We generalize that into a tiny
command queue drained at the top of `Update`:

| Endpoint | Body | Game-thread effect | Completion |
|---|---|---|---|
| `POST /input` | `{player, moveX, moveY, jump, attack, special}` | write `ScriptedInputState` | immediate |
| `POST /step` | `{frames}` | set `PendingSteps`, mode=Stepped | when the N frames finish (barrier TCS) |
| `POST /run`  | `{mode:"free"\|"stepped"}` | set run mode | immediate |
| `POST /teleport` | `{player, x, y}` | set `Transform.Position` + zero velocity | immediate (setup only) |
| `GET /state`, `GET /screenshot` | — | unchanged | unchanged |

`POST /step` is the important one: it blocks the HTTP response until the requested frames have
actually executed, so the test can `await StepAsync(n)` and then read a coherent `/state`.

### 4. Setup helpers (input is for *behaviour*, teleport is for *setup*)

Walking a player across the arena to reach the other player is slow and fragile. `POST /teleport`
places players deterministically for the scenario, then the *interaction under test* (the punch,
the wade into acid) is driven by real scripted input. This keeps fidelity where it matters while
keeping setup cheap — the hybrid the directional decision called for.

### 5. Expanded state for assertions

Add providers in `ArenaScene`'s `#if DEBUG` block (the `RegisterProvider` pattern already there):

- `acidParticleCount` → needs a public `int ParticleCount` on `AcidSurface` (forwarding
  `_sim.Count`). The bug's signature is this collapsing/НaN'ing.
- `acidFinite` → `true` if all particle positions are finite (cheap guard sampling; or a flag the
  sim sets). Lets a test assert "no NaN" directly rather than inferring from count.

`GameStateSnapshot` gains the matching fields.

## Test-harness changes (`GameDriver`)

```csharp
await game.RunAsync("stepped");
await game.TeleportAsync(player:0, x:600, y:300);
await game.TeleportAsync(player:1, x:640, y:300);   // P2 right next to P1
await game.SetInputAsync(player:0, attack:true);
await game.StepAsync(1);                              // the punch frame
await game.SetInputAsync(player:0, attack:false);
await game.StepAsync(20);                             // hitstop + recovery
var s = await game.GetStateAsync();
```

## Worked example — the regression that would have caught PR #11

```csharp
[SkippableFact]
public async Task MeleeHit_WhileAcidPresent_DoesNotBreakTheFluid()
{
    Skip.IfNot(GameDriver.IsEnabled, $"Set {GameDriver.EnableEnvVar}=1 to run E2E tests.");
    await using var game = await GameDriver.StartAsync();   // scripted devices + fast acid

    await game.RunAsync("stepped");
    await game.WaitForAsync(s => s.AcidActive, …);          // let acid activate (stepping)
    await game.StepUntilAsync(s => s.AcidParticleCount > 200);

    int before = (await game.GetStateAsync()).AcidParticleCount;

    // Land a real hit: place players adjacent, punch.
    await game.TeleportAsync(0, 600, 300);
    await game.TeleportAsync(1, 638, 300);
    await game.SetInputAsync(0, attack: true);
    await game.StepAsync(1);
    await game.SetInputAsync(0, attack: false);
    await game.StepAsync(20);                                // through the hitstop window

    var after = await game.GetStateAsync();
    Assert.True(after.AcidFinite, "Acid went NaN after a melee hit (hitstop dt=0).");
    Assert.InRange(after.AcidParticleCount, (int)(before * 0.5), int.MaxValue); // didn't vanish
}
```

Pre-fix: `AcidFinite` is false / `AcidParticleCount` collapses. Post-fix: stable. This complements
(does not replace) the unit test — the unit test pins the cause, this pins the wiring.

## Determinism considerations

- **Fixed `dt` per step** (the core lever) — see Gaffer/Jakub/Leite.
- **Input isolation** — scripted nodes ignore real devices (Unity-fixture property).
- **Seeded RNG** — acid already seeds; if future systems add randomness, add a `DebugSeed`
  setting the automation path forces.
- **Sample input inside the stepped frame** — `/input` is applied in `DrainCommands` at the top of
  `Update`, before `base.Update`, so the stepped frame sees it; never applied mid-`base.Update`.

## Threading & safety

- All game-state mutation happens on the game thread inside `DrainCommands`/`Update`. The HTTP
  thread only enqueues commands + awaits a TCS — identical to the proven `/screenshot` handshake.
- `ScriptedInputState` fields are `volatile` (simple scalars); the command queue is a
  `ConcurrentQueue`. No locks on the hot path.
- Everything is `#if DEBUG`; release builds have no server, no scripted nodes, no `Update` gate.

## Risks / tradeoffs

| Risk | Mitigation |
|---|---|
| Stepping diverges from real-time feel (fixed `dt` ≠ the variable `dt` players get) | Acid already clamps `dt` to `MaxDeltaTime`; 1/60 is within normal range. Smoke-test *recordings* can still use free-run mode. |
| `/teleport` bypasses input → tests less of the movement path | It's setup-only; the behaviour under test is always real input. Movement itself can be covered by separate "press right, step, assert x increased" tests. |
| Future unseeded randomness breaks replays | Add a forced `DebugSeed` on the automation path (flagged, not built now). |
| Maintenance: another debug surface to keep working | It's small and reuses existing patterns; CI already builds Debug. |

## Suggested phasing

1. **Scripted input + `/input`** — minimal: drive movement/jump/attack in *free-run* mode. Enables
   most behavioural tests immediately.
2. **Frame-stepping (`/step`, `/run`) + fixed `dt`** — determinism; convert flaky `Task.Delay`
   polls to `StepAsync`.
3. **`/teleport` + expanded acid state + the worked-example regression test.**
4. **(Optional later)** smoke-test skill uses scripted input to play a scripted bout.

Each phase is independently useful and shippable.

## Files touched (estimate)

- **+** `Input/Scripted/ScriptedInputState.cs`, `ScriptedNodes.cs`, `ScriptedInputRegistry.cs`
- **+** `DevTools/DebugControl.cs` (run mode, step barrier, command queue)
- **~** `Input/InputProfileFactory.cs` (+`CreateScripted`), `Systems/MatchSetupManager.cs` (enum)
- **~** `GorelordsBrawlerGame.cs` (Update gate, fixed-step GameTime)
- **~** `DevTools/GameDebugServer.cs` (POST routing), `DevTools/DebugStateExporter.cs` (no change if
  providers registered in scene)
- **~** `Scenes/ArenaScene.cs` (register `acidParticleCount`/`acidFinite`), `AcidSurface.cs`
  (`ParticleCount`/finiteness accessor), `AppSettings.cs` (`DebugAutomation`)
- **~** `tests/.../GameDriver.cs` (+`SetInputAsync`/`StepAsync`/`RunAsync`/`TeleportAsync`),
  `GameStateSnapshot.cs` (+fields)
- **+** `tests/.../GameplayAutomationE2ETests.cs` (incl. the melee-vs-acid regression)

## Sources

- [Class InputTestFixture — Unity Input System docs](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.1/api/UnityEngine.InputSystem.InputTestFixture.html) — synthetic input at the action layer, isolation from platform input, settable time.
- [Input testing — Unity Input System manual](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.1/manual/Testing.html) — Press/Release/Set/Trigger helpers; play-mode test pattern.
- [Godot `Input.parse_input_event` does not work in headless mode (#73557)](https://github.com/godotengine/godot/issues/73557) and [Godot forums — simulate a button press](https://godotforums.org/d/19196-how-to-simulate-a-button-press-in-code) — `action_press`/`action_release` at the action layer; headless input/render caveat → keep a real window.
- [Gaffer On Games — "Fix Your Timestep!"](https://gafferongames.com/post/fix_your_timestep/) — fixed-`dt` accumulator; exact reproducibility given the same inputs.
- [Jakub Tomšů — "Reliable fixed timestep & inputs"](https://jakubtomsu.github.io/posts/input_in_fixed_timestep/) and [André Leite — "Taming Time in Game Engines"](https://andreleite.com/posts/2025/game-loop/fixed-timestep-game-loop/) — sampling input per fixed step.
- [Eidos-Montréal — "Automated Game Testing"](https://www.eidosmontreal.com/news/automated-game-testing/) — canned-input replay needs determinism (motivates the seed hook + isolation).
