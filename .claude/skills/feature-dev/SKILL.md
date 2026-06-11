---
name: feature-dev
description: The end-to-end workflow for developing a GorelordsBrawler feature - plan → research → ground-in-code → implement → test at the right layer (unit / fluid / integration / E2E / smoke) → run the suites → prepare the teaching PR. Use when starting any feature or feature phase ("implement phase X", "add <mechanic>", "build <feature>"), when deciding what KIND of test a new behavior needs, or when wiring a feature into the verification harnesses (debug-server oracles, E2E page objects, smoke checks). Mechanics get automated tests; the user's hands are for FEEL.
allowed-tools: Read, Edit, Write, Grep, Glob, Bash, PowerShell, WebSearch, WebFetch
---

# Feature development — the full loop

Every feature/phase runs the same loop. None of these steps are optional; each one
exists because skipping it has already burned us at least once (receipts inline).

```
plan (docs/) → research online → ground in code → implement
   → test at the right layer → run ALL the suites → smoke + PR prep
```

## 0. Plan — one reviewable phase at a time

- Features live as proposal docs in `docs/` (CLAUDE.md "Planning Workflow"). The
  user reviews/approves before implementation. Multi-phase features ship **one PR
  per phase**, each independently smoke-tested (pattern: the acid-arena phases).
- Lock contested design decisions in the doc as **Decisions** with the user's words
  ("mash jump", "fast melt ~1.5-2s") — tests later pin THESE, not your embellishments.
- When done + verified, `git mv` the doc to `docs/implemented/` and update
  CLAUDE.md's proposal index.

## 1. Research before designing (and before every re-iteration)

Per CLAUDE.md: never guess a non-trivial algorithm — search, read 4-5 sources, cite
the chosen approach in the proposal/PR. This also applies MID-feature: when
functional-test feedback comes back, re-search before retrying from intuition.
Trust yourself on velocity math; do NOT trust yourself on fluid sims, solver
parallelization, or platform-fighter balance lore without looking it up.

## 2. Ground in the actual code — proposals lie, docs rot

Before writing code, READ every component the plan touches and verify its claims:

- Phase B's proposal claimed "JumpAbility only fires when grounded — no conflict
  with SwimAbility." Reading the code found a **double-jump branch that fires
  airborne** — unpatched it would have let players air-jump out of the acid.
  Three of the proposal's code claims were wrong; all three were caught by reading,
  none by tests-after-the-fact.
- Verify load-bearing FRAMEWORK assumptions in the Nez source (it's a submodule —
  `Nez/Nez.Portable/`), not from memory. Example: Phase A's whole premise (a TMX
  basin contains the PBF fluid) rested on `TiledMapRenderer.AddColliders()` →
  `GetCollisionRectangles()` → `BoxCollider`s on the Platforms physics layer.
  Confirmed in source BEFORE building the map, not after the acid leaked.
- Stale doc claims get FIXED in the same PR (e.g. "the debug server is read-only"
  nearly prevented the E2E swim suite from being written).

## 3. Implement — house conventions

- **Tabs** (Nez convention), **braces on every control statement**, magic values in
  `GameConstants` (gameplay) or the feature's config class (e.g. `FluidConfig`).
- Tuning knobs the user may touch live-tune: `[Inspectable, Range(...)]` fields.
- **Update-order is a contract.** Components that publish per-frame state others
  consume set `UpdateOrder` explicitly in `OnAddedToEntity` (e.g. `SubmersionFeel`
  at -10 so abilities at 0 and `PhysicsBody` at 100 read fresh values).
- **Deferred init** for anything needing engine state: constructor/`PreFill()`-style
  calls record intent; `OnAddedToEntity` executes once the entity/scene exists.
- **Closures as scene policy.** Generic components (`ContactHazard`) stay generic;
  feature-specific rules (depth curve, submersion gating) are closures the scene
  installs. Keeps hazards/components reusable.
- **Button ownership must be exclusive per state.** If a new ability claims an input
  in some state (SwimAbility owns jump underwater), the previous owner must
  explicitly yield in that state (JumpAbility early-outs while submerged) — check
  EVERY branch of the old owner, not just the obvious one.
- Never run `dotnet format` on Nez-style files (tabs→spaces, whole-file rewrite).
  For mechanical multi-site edits, use a byte-preserving Python transform with
  brace-balance asserts (`tools/parallelize_fluid.py` is the template).

## 4. Pick the test layer — the decision table

| The new behavior is… | Test layer | Pattern to copy |
|---|---|---|
| A pure formula / curve / scaling rule | **Unit** (`tests/GorelordsBrawler.Unit.Tests`) | `CombatMathTests` — keep formulas in `Combat/CombatMath.cs` (no engine deps) precisely so they're unit-testable |
| A design PROMISE between systems ("deadly but escapable", "stroke must out-climb mid-depth DPS") | **Unit, as a relationship test** | `CombatMathTests.Swim_StrokeIsMeaningful_…` — compute both sides from the real `GameConstants` so retuning that breaks the promise fails CI |
| Constants sanity (bands, orderings) | **Unit** | `GameConstantsTests` |
| Particle/sim behavior (containment, settling, NaN, parallel equivalence) | **Fluid.Tests** (headless sim, no engine) | `FluidSimulationTests` — build worlds with `FluidCollider.SetAabbs`, step N frames, assert invariants |
| Sim performance | **FluidBenchmark** (env-gated) | `FLUID_BENCH=1`, **Release only** — Debug is several× slower and lies |
| Cross-component logic without the engine loop | **Integration** (`tests/GorelordsBrawler.Integration.Tests`) | `KnockbackScalingPipelineTests` |
| ANYTHING requiring input, movement, physics-in-anger, or multi-system runtime behavior | **E2E** (`tests/GorelordsBrawler.E2E.Tests`) | see §5 — never declare gameplay "manual-only" |
| Visual lifecycle + recorded evidence for the PR | **Smoke feature module** | `.claude/skills/smoke-test/features/*.ps1` |
| How it FEELS (pacing, rhythm, fairness) | **The user's hands** | the ONLY thing reserved for manual testing |

Honest-test rules (each learned the hard way):
- **Pin the agreed design, not a stricter invention.** A Phase B test asserted
  "deep+hurt must be unescapable even with perfect play" — never agreed, and the
  tuning didn't deliver it. Rewrite the test to the user's actual decision; don't
  tune the game to satisfy your embellishment, and NEVER delete the assert just to
  go green.
- **No vacuous passes.** If a test's scenario might not occur, assert that it DID
  (the phantom-damage test asserts the player was *inside the damage AABB* via a
  dedicated oracle before asserting zero damage) and `Skip.If(...)` when genuinely
  inconclusive (a real splash wetting the player) rather than asserting a falsehood.
- Runtime dynamics that closed-form math can't model (hitstun windows + human
  reaction) belong in E2E/playtest, not in a unit test pretending to know.

## 5. E2E recipes — the write channel

The debug server drives REAL gameplay (Debug builds, `DebugAutomation: true` —
`GameDriver` writes the right appsettings next to the exe automatically):
`POST /input` (per-player buttons/axes), `POST /step` (exactly N fixed-dt frames),
`POST /run` (free|stepped), `POST /teleport`, `POST /damage`. Tests go through the
Page Object Model — `ArenaPage` / `PlayerObject` — with Shouldly + ARRANGE/ACT/ASSERT.

Recipes:
- **Stepped mode for determinism**: `EnterSteppedModeAsync()` → nothing advances
  except your `StepAsync(n)`. Same script ⇒ same frames, every run.
- **Input has ~1 frame of latency.** Never `press → step(1) → read`. Use
  `press → StepUntilAsync(condition, maxFrames: ~5, batch: 1)` — and a timeout
  becomes a crisp "it never happened" failure.
- **Stage scenarios with `/teleport` + `/damage`**, don't platform your way there.
  Place actors OFF special columns (e.g. clear of the acid inlet at x≈640).
- **New observable state ⇒ new oracle, BOTH sides:** register it in
  `DebugStateExporter` (per-player fields in `BuildPlayerSnapshots`, scene fields
  via `exporter.RegisterProvider(...)` in `ArenaScene`) AND mirror it in the test
  DTO `GameStateSnapshot`. An oracle that exists in only one side is invisible.
- **Trace on failure.** When an E2E assert would fail mysteriously, capture a
  per-frame `StringBuilder` trace (y/vy/flags) inside the loop and embed it in the
  assertion message. A Phase B trace turned "launched was null" into "the surface
  bob gives a 2-frame dry window and short-hop gravity slams you back" — which
  became the breach-jump mechanic. **An E2E failure can be a DESIGN finding; read
  the trace before "fixing" the test.**
- **One game, one port.** Every test launches its own game on :7777;
  `xunit.runner.json` (`parallelizeTestCollections: false`) serializes classes.
  Do not remove it; do not run two E2E invocations concurrently.
- Readiness: `LaunchAsync` waits for live grounded players — cold boots (fresh
  build → shader/atlas load) take seconds; the server answers before the scene
  exists, so early `/state` reads are EMPTY, not wrong.

## 6. Run everything — commands + gotchas

```bash
# kill any running game FIRST — a live exe holds the DLL and fails the build
pwsh -Command "Get-Process GorelordsBrawler -EA SilentlyContinue | Stop-Process -Force"

dotnet build GorelordsBrawler.slnx                      # always the solution

# fast suites (unit + integration + fluid; excludes E2E and the timing benchmark)
dotnet test GorelordsBrawler.slnx --nologo --filter "Category!=E2E&FullyQualifiedName!~Benchmark_Step"

# E2E — real game windows, serialized, ~3 min for the full assembly
E2E_TESTS=1 dotnet test tests/GorelordsBrawler.E2E.Tests/GorelordsBrawler.E2E.Tests.csproj --nologo
#   (PowerShell: $env:E2E_TESTS="1" first)
#   iterate on one class: --filter "FullyQualifiedName~AcidPhaseB"

# fluid perf benchmark — RELEASE ONLY, numbers from Debug are meaningless
FLUID_BENCH=1 dotnet test tests/Fluid.Tests -c Release --filter FluidBenchmark

# smoke — visual gate + MP4 evidence; -OpenIde on the run that precedes PR creation
pwsh .claude/skills/smoke-test/smoke_test.ps1 -Feature acid [-NoBuild] [-OpenIde]
```

Gotchas: changed arena geometry or movement/hazard mechanics ⇒ **run the full E2E
assembly**, not just your new class (Phase A skipped it and silently broke a
legacy test whose scenario the new geometry made impossible). The smoke harness
and `GameDriver` both overwrite the *output-dir* `appsettings.json`; the tracked
source file must stay clean — `git diff GorelordsBrawler/appsettings.json` before
committing.

## 7. Definition of done (the pre-PR checklist)

1. `dotnet build GorelordsBrawler.slnx` — 0 warnings, 0 errors.
2. Fast suites green; **full E2E assembly green**; smoke green with recording URL.
3. New behavior has tests at its correct layer(s), including at least one
   regression-shaped test for any bug fixed en route.
4. Smoke run with `-OpenIde` so the user can F5 straight into functional FEEL
   testing.
5. PR body is a teaching doc (CLAUDE.md "Your role"): what/why first, non-obvious
   techniques explained with one-line citations, trade-offs called out, **only
   verified numbers** (no guessed URLs/counts — update the body if a follow-up
   commit changes facts).
6. Proposal doc updated/archived; CLAUDE.md index current; stale doc claims fixed.
7. Working tree audit: stage files **explicitly by name** (never `git add -A` — a
   linked-worktree quirk can show the Nez submodule as deleted, and a blanket add
   would commit that); revert incidental `ContentPathGenerator.cs` T4 churn.

## Anti-patterns (all real, all this repo)

- "This needs manual testing" for a MECHANIC → it needed an E2E test (swim escape).
- Trusting a proposal/doc claim about code → read the code (double-jump bypass).
- Asserting beyond the agreed design → test the user's decision, not your ideal.
- Reading game state 1 frame after input → respect the latency, poll.
- Shipping the cap/workaround and calling perf "fixed" → profile in Release,
  fix the real lever (the solver was single-threaded on a 24-core box).
- Editing the PR body with expected-but-unverified numbers → wait for the run.
