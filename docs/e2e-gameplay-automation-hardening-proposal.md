# Proposal: Harden + expand E2E gameplay automation (make it actually run, and make the oracles trustworthy)

**Status:** awaiting review
**Follow-up to:** PR #13 / `docs/e2e-gameplay-automation-proposal.md` (the design that shipped the scripted-input + frame-stepping infrastructure)

## TL;DR

PR #13 built the right *architecture* — scripted input through the real `VirtualInput`
pipeline, deterministic frame-stepping, an HTTP command channel. But the gameplay E2E tests
**have never actually run**, and the one regression test that matters most has an oracle that
**can pass without testing anything**. This proposal fixes both, adds a trustworthy "did the
event happen" signal, and pins the highest-value cross-system regressions — including the
"game breaks when one player hits another" crash that slipped through.

This is a *correctness + trust* proposal, not a redesign. The PR #13 architecture stays.

## Diagnosis (what I found by building, running, and probing the live game)

I rebuilt the solution, launched the game with the automation flags, and drove it over HTTP
with a scripted probe. Four findings, in order of importance:

### 1. Scripted input is **not** broken. The "axis doesn't move the player" bug was a false alarm.

The hand-off note claimed scripted `moveX` didn't move the player. It does. With
`DebugAutomation=true`, my live probe showed:

| Action sent | Observed in `/state` |
|---|---|
| `moveX:1`, step 20 | `vx=277`, x: 256 → 324 |
| `moveX:-1`, step 20 | `vx=-277` (flips direction) |
| `attack` on adjacent player | target took **8 dmg + knockback `vx 0→215`**; attacker HP unchanged |

The pipeline (`ScriptedAxisNode` → `VirtualIntegerAxis.Value` → `WalkAbility`) is live and
correct, exactly as two static reviews concluded.

**Why the earlier probe saw nothing:** the checked-in `appsettings.json`
([GorelordsBrawler/appsettings.json](../GorelordsBrawler/appsettings.json)) does **not** contain
`DebugAutomation`. Only `GameDriver.StartAsync` injects it at test time. Run the game manually
without that flag and `GorelordsBrawlerGame.Initialize` (line ~88) sets
`automate = false` → both players are created with **keyboard** devices, not `Scripted0/1`.
`POST /input` then writes to a `ScriptedInputRegistry` entry that *no node reads*. The player
still falls under gravity (the note's "y changed") but never moves horizontally (the note's "vx
stays 0"). It was a test-rig misconfiguration, not a code bug. **Lesson: when static review and
behaviour disagree, the rig is suspect — get ground truth before theorising.**

### 2. The real blocker: the harness can't launch the game at all.

`GameDriver.FindGameExe()`
([tests/.../GameDriver.cs:168](../tests/GorelordsBrawler.E2E.Tests/GameDriver.cs)) searches
*only* for `GorelordsBrawler.exe`. A solution build produces **no `.exe`** — the three test
projects reference the game with `<AdditionalProperties>UseAppHost=false</AdditionalProperties>`
(to avoid clobbering a running game during test builds), and that is the only build of the game
the solution performs. So `GameDriver.StartAsync()` throws `FileNotFoundException` before a
single frame runs.

Because every gameplay test is a `[SkippableFact]` gated on `E2E_TESTS=1`, a normal
`dotnet test` **skips** them — and a skip is reported green. The "regression test reported PASS"
in the hand-off note was a skipped test, not a passing one. **The melee-vs-acid regression has
never executed.**

### 3. The melee regression test's oracle is unreliable (can pass while testing nothing).

`MeleeHit_WhileAcidPresent_DoesNotBreakTheFluid` proves a hit landed with
`after.Players[1].Hp < p1HpBefore`. But `DebugFastAcid` is on, and **both players lose HP to
acid every frame** — my probe watched an idle player drift 63 → 56 HP doing nothing. So that
assertion can be satisfied with **zero melee contact**. The test can go green while the punch
misses entirely, which is the worst kind of regression test: a false sense of safety.

The melee damage path and the acid damage path are completely separate in code:

- **Melee:** `Hurtbox.OnTriggerEnter` ([Components/Hurtbox.cs:24](../GorelordsBrawler/Components/Hurtbox.cs))
  → `Health.TakeDamage` **+ knockback velocity + `Hitstun.Trigger` + `CombatEffectsManager.TriggerHit` (hitstop)**.
- **Acid:** `ContactHazard.Update` ([Components/Hazards/ContactHazard.cs:49](../GorelordsBrawler/Components/Hazards/ContactHazard.cs))
  → `Health.TakeDamage` only. No knockback, no hitstun, no hit count.

So a counter incremented inside `Hurtbox.OnTriggerEnter` is incremented by melee hits and
**never** by acid — the basis of the trustworthy oracle in §Design.

### 4. The acid-on-hit crash itself is already fixed.

Through the staged hit + hitstop window, `acidFinite` stayed `true` and `acidParticleCount`
grew normally (3560 → 3649). The `dt<=0` guard in `FluidSimulation.Step` (PR #11) holds. The
regression test's job is therefore to **stay green and scream if anyone reintroduces the bug** —
which is exactly why its oracle must be trustworthy (finding #3).

## Goals / non-goals

**Goals**
- Make the gameplay E2E tests actually launch and run (fix harness discovery).
- Give tests a **trustworthy, environment-independent oracle** for "a melee hit connected,"
  "a player is in hitstun," "hitstop fired," "a player died."
- Pin the highest-value cross-system regressions as deterministic, stepped tests — including the
  player-vs-player crash and the four you selected (knockback-cancel, hitstun lockout,
  death→respawn, acid damage-over-time).
- Keep every test **deterministic** (stepped, fixed `dt`, state-based waits — no wall-clock
  `Task.Delay` in behaviour tests).

**Non-goals**
- Redesigning the PR #13 architecture (it's sound).
- Headless rendering (we keep a real window — same reasoning as the original proposal).
- Record/replay, CI wiring, all-character coverage (deferred; this is the "solid core," not
  "comprehensive" — per the agreed scope).
- Changing any release-build behaviour. Everything added is `#if DEBUG`, like the rest of
  DevTools.

## Design

### 1. Make the harness launch the game (robust launcher)

Replace `FindGameExe()` with a launcher that works whether or not an apphost exists:

```csharp
// Prefer a real exe if a build produced one; otherwise launch via `dotnet <dll>`,
// which is how the solution build actually ships the game (UseAppHost=false).
private static (string fileName, string args) FindGameLauncher()
{
    string dll = FindGameArtifact("GorelordsBrawler.dll");   // walk up from test bin, same as today
    string exe = Path.ChangeExtension(dll, ".exe");
    return File.Exists(exe)
        ? (exe, "")
        : ("dotnet", $"\"{dll}\"");
}
```

`StartAsync` uses both `FileName`/`Arguments`, keeping `WorkingDirectory` at the artifact dir
(content + `appsettings.json` resolve from `AppContext.BaseDirectory`, so cwd is irrelevant —
verified). No game-project or csproj change needed; the `UseAppHost=false` guard on the test
references stays exactly as the comment intends.

### 2. Trustworthy oracle — a small, DEBUG-only state seam

The principle (and the research consensus): **assert on a state change that only the
event-under-test can produce.** Three additions, all tiny and `#if DEBUG`-gated where they touch
the server boundary:

| Signal | Source | Why it's trustworthy |
|---|---|---|
| `meleeHitsTaken` (per-player `int`, monotonic) | new `public int HitsTaken` on `Hurtbox`, `HitsTaken++` at the dedup point ([Hurtbox.cs:36](../GorelordsBrawler/Components/Hurtbox.cs), right after `HitTargets.Add`) | Incremented once per distinct melee connection; **never** by acid (separate code path, finding #3) |
| `hitstun` (per-player `bool`) | existing `Hitstun.IsActive` ([Hitstun.cs:16](../GorelordsBrawler/Components/Hitstun.cs)) | Set only by `Hurtbox` on a melee hit; drives the lockout test |
| `dead` (per-player `bool`) | existing `Health.IsDead` ([Health.cs:14](../GorelordsBrawler/Components/Health.cs)) | Direct death signal for the respawn test |
| `hitstopActive` (global `bool`) | new `public bool IsHitstopActive => _hitstopTimer > 0` on `CombatEffectsManager` ([CombatEffectsManager.cs:15](../GorelordsBrawler/Systems/CombatEffectsManager.cs)) | Direct evidence the `TimeScale=0` / `dt=0` path — the exact failure mode — was exercised |

`HitsTaken` is the load-bearing one: it converts "did the punch land?" from an *inference*
(HP went down — contaminated) into a *fact* (this hurtbox registered N melee hits). These are
added to `DebugStateExporter.BuildPlayerSnapshots`
([DevTools/DebugStateExporter.cs:92](../GorelordsBrawler/DevTools/DebugStateExporter.cs)) and the
global block; `GameStateSnapshot` / `PlayerSnapshot` gain the matching fields.

> `Hurtbox.HitsTaken` is a plain public field on a gameplay component, compiled in all builds
> (it's harmless and avoids `#if DEBUG` islands inside hot combat code). Only its *exposure over
> HTTP* is DEBUG-only, consistent with how `AcidSurface.ParticleCount` is already handled.

### 3. One setup helper: `POST /damage` (death test, acid-independent)

To drive a death deterministically *without* coupling the respawn test to the acid hazard, add a
setup-only command mirroring `/teleport`:

```
POST /damage  { player, amount }   → DebugControl.EnqueueDamage → Health.TakeDamage(amount)
```

Setup-only, same handoff pattern as `/teleport`. (Alternative considered: drive the kill by
teleporting into acid. Rejected — it makes the death test depend on the acid sim's timing and
geometry, reintroducing exactly the kind of cross-contamination finding #3 warns against.)

### 4. The test suite (the agreed "solid core")

All run in **stepped** mode. All waits are `StepUntilAsync(<state predicate>)` — no wall-clock.
Each lists its trustworthy oracle.

| # | Test | Scenario | Oracle (what makes it solid) |
|---|---|---|---|
| A | `Move_LeftAndRight` | settle → hold `moveX=±1`, step | Δx sign + `vx` sign. Pure kinematics. |
| B | `Jump_LeavesGround` | settle (grounded) → tap `jump`, step | `vy<0` then `grounded=false`, later returns. |
| C | `MeleeHit_ConnectsDealsDamageAndKnockback` **(player-vs-player core)** | teleport P1 adjacent, face P0 at P1, one `attack` | `P1.meleeHitsTaken +1` **exactly**, `hp` drops ~`Damage`, `vx` spikes away, `hitstun=true`. |
| D | `MeleeHit_WhileAcidPresent_DoesNotBreakTheFluid` **(the missed regression)** | `StepUntil(acidParticleCount>N && acidFinite)` → staged hit → step the hitstop window 1 frame at a time | **Guard:** `meleeHitsTaken +1` **and** `hitstopActive` observed true (we hit the `dt=0` path). **Assert:** `acidFinite` stayed true throughout **and** count didn't collapse. |
| E | `Knockback_SurvivesHitstunFrames` **(knockback-cancel regression)** | land a hit on grounded P1, send P1 **no** input, step several frames | `|P1.vx|` stays large while `hitstun=true` (doesn't snap to ~0 on frame 2). Reverting the WalkAbility fix flips this red. |
| F | `Hitstun_SuppressesMovement` **(hitstun lockout)** | hit P1, then send `moveX` *against* the knockback while `hitstun=true`, then again after it ends | No input-driven accel **while** `hitstun=true`; movement resumes **after**. Keyed off the flag, not a frame count. |
| G | `Player_DiesAndRespawns` **(death→respawn)** | `POST /damage 9999` → `StepUntil(dead)` → step `RespawnDelay` worth of frames | `dead=true`, then `hp=maxHp` **and** position back at spawn. |
| H | `Acid_DamagesInside_NotOutside` **(acid DoT)** | `StepUntil(acidActive)` → teleport P0 into acid, P1 clear → step a fixed window | `P0.hp` decreased, `P1.hp` unchanged. Relative comparison isolates the hazard contract. |

C and D are the two that directly cover "a game-breaking bug when one player hit another": C
proves the hit *connects and behaves*; D proves the *acid survives* that connection.

### 5. Determinism & anti-flake (grounded in the research)

- **Stepped + fixed `dt`** for every behaviour test — reproducible run-to-run given the same
  inputs ([Gaffer, "Fix Your Timestep!"](https://gafferongames.com/post/fix_your_timestep/)).
- **State-based waits, never `Task.Delay`** in stepped tests — the #1 flakiness source per
  [Ranorex](https://www.ranorex.com/blog/flaky-tests/) and
  [MY.GAMES](https://medium.com/my-games-company/beyond-the-routine-in-qa-how-we-automated-regression-testing-2f6a98d98415).
  `StepUntilAsync` already exists; I'll route the acid-activation wait through it too.
- **Event-only oracles** (`meleeHitsTaken`, `hitstun`, `dead`, `hitstopActive`) over inferred
  ones (HP deltas) — assert on the state only the event can produce, the model-based-testing
  recommendation ([arXiv 2202.06271](https://arxiv.org/pdf/2202.06271)).
- **Margins, not exact values** for knockback magnitudes (assert `> threshold`, not `== 215`),
  since per-frame integration varies with where in the window we sample.
- **Seeded RNG** already present in `AcidSurface` / `FluidSimulation`.

## Risks / tradeoffs

| Risk | Mitigation |
|---|---|
| `dotnet <dll>` launch needs `dotnet` on PATH | It's present anywhere `dotnet test` runs. Launcher still prefers a real exe if one exists. |
| New `Hurtbox.HitsTaken` field touches a gameplay component | One `int` + one `++` at an existing dedup point; no behaviour change, covered by test C. |
| `hitstopActive` is transient (~4 frames) and a coarse step could skip it | Test D steps the post-attack window **one frame at a time** and latches "was it ever true," so a fast step can't miss it. |
| `/damage` adds another debug surface | Tiny, setup-only, same proven handoff as `/teleport`; DEBUG-only. |
| Stepped `dt=1/60` ≠ players' variable `dt` | Same tradeoff the original proposal accepted; `1/60` is within the clamp range the sims already use. |

## Files touched (estimate)

- **~** `tests/.../GameDriver.cs` — robust launcher (exe-or-dll); add `DamageAsync`; route acid-wait through `StepUntilAsync`.
- **~** `tests/.../GameStateSnapshot.cs` — `+ Hitstun`, `MeleeHitsTaken`, `Dead` (per player); `+ HitstopActive` (global).
- **~** `tests/.../GameplayAutomationE2ETests.cs` — rewrite the two existing tests with trustworthy oracles; add tests A–H.
- **~** `GorelordsBrawler/Components/Hurtbox.cs` — `public int HitsTaken` + increment.
- **~** `GorelordsBrawler/Systems/CombatEffectsManager.cs` — `public bool IsHitstopActive`.
- **~** `GorelordsBrawler/DevTools/DebugStateExporter.cs` — emit the new per-player + global signals.
- **~** `GorelordsBrawler/DevTools/DebugControl.cs` + `GameDebugServer.cs` — `EnqueueDamage` + `POST /damage`.
- **~** `GorelordsBrawler/appsettings.json` — *(optional)* add `"DebugAutomation": false` so the key is documented in-repo (harmless default; the harness still overrides it). Prevents the exact manual-probe confusion that caused finding #1.
- **→** move `docs/e2e-gameplay-automation-proposal.md` to `docs/implemented/` (its design shipped in PR #13).

## Suggested phasing (each independently shippable)

1. **Make it run + trustworthy** — launcher fix, the state seam, rewrite tests C & D with the
   `meleeHitsTaken`/`hitstopActive` oracle. This alone closes the "missed regression" gap.
2. **Pin the rest** — tests A, B, E, F, G, H + `/damage`.
3. **De-flake the acid lifecycle tests** — route `AcidHazardE2ETests` waits through
   `StepUntilAsync` (they still use `Task.Delay`).

## Sources

- [Gaffer On Games — "Fix Your Timestep!"](https://gafferongames.com/post/fix_your_timestep/) — fixed-`dt` reproducibility.
- [MY.GAMES — "Beyond the routine in QA: how we automated regression testing"](https://medium.com/my-games-company/beyond-the-routine-in-qa-how-we-automated-regression-testing-2f6a98d98415) — state/event waits over delays.
- [Ranorex — "Flaky Tests"](https://www.ranorex.com/blog/flaky-tests/) — static delays as the top flake source; quarantine/gating.
- [Model-based Testing of Scratch Programs (arXiv 2202.06271)](https://arxiv.org/pdf/2202.06271) — state-machine oracles abstract away timing/pixels; the basis for event-only assertions.
- [Regression Testing Strategies for Game Development (beefed.ai)](https://beefed.ai/en/regression-testing-game-development) — automate the deterministic core, leave the variable surface to exploratory testing.
