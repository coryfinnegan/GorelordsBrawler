# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Working with the developer

**Who I am.** Senior Software Engineer in Quality (8 YOE) at a AAA game company. Deep background in functional testing, web automation, API automation, and Windows automation (WPF/WinForms via FlaUI and Appium). Some experience writing .NET Framework and Core Web APIs, plus Blazor and Angular on the front end. Associate's in video game programming and visualization — earned part-time over 8 years ago, so the game-dev fundamentals are rusty even though the software-engineering muscle is fresh.

**What this project is.** Side project for a friend's toy line — a party brawler with simple attack mechanics and deadly environments. Two-player local. The bar is "looks professional and is fun with a friend."

**My role on this project.**
- I dictate planning of features with requirements.
- I iterate or approve plans you produce, then you execute.
- I perform manual functional testing on the running build, deliver results, you iterate.
- I don't have a lot of time to write code myself — I'm reading your PRs to learn, so they double as my game-dev study material.

**Your role.**
- You do not guess how to implement my plans. For anything non-trivial you search online, look at 4–5 different results, and pick the best fit for the requirements before proposing an approach. You then bring the findings into the plan.
- During implementation you iterate based on my functional-test feedback, and **before iterating you re-search the problem online** rather than retrying from intuition. Do not make up algorithms unless you are absolutely 100% confident — I trust you on a velocity calculation; I do not trust you on a realistic liquid simulation without looking it up.
- During feature development you use the `/smoke-test` skill to prepare me for functional testing — that includes recording a video, opening the IDE on the worktree, and writing the PR with enough teaching context that I can review-and-learn (see [.claude/skills/smoke-test/SKILL.md](.claude/skills/smoke-test/SKILL.md), section "Preparing the user for functional testing").
- PR descriptions are durable teaching documents. Lead with what changed and why, briefly explain any non-obvious technique or API with a one-line citation, and call out the trade-offs. I'll be reading these later instead of re-reading conversations.

**No "MVP" shortcuts. This is not an MVP project.** The bar is "looks professional and is fun with a friend." If during planning or implementation you identify the correct way to do something but feel tempted to ship a simpler version and "park it for later," DO NOT. Two things give this away to me:
1. Phrases like "MVP risk reasons," "park it for later," "good enough for now," "future iteration would be to..."
2. Descriptions of a known cosmetic / behavioural artifact you chose to ship instead of fixing.

If you catch yourself writing those phrases, that is the signal you are taking a shortcut. Stop, do the right way. The only acceptable reasons to defer the better approach are (a) it depends on something not yet in the codebase (then propose it as its own dedicated work, do not let it bleed into the current feature), or (b) you explicitly raise it to me and I approve the deferral. Self-rationalised deferrals are the failure mode — I have flagged this on PR #6 (Approach A vs B) and PR #10 (collider rect vs sprite mask) and I do not want to flag it a third time.

## Project Overview

GorelordsBrawler is a 2D party brawler built with **MonoGame 3.8 DesktopGL** on **.NET 8.0**. The **Nez framework** (Git submodule in `Nez/` — run `git submodule update --init --recursive` in fresh checkouts/worktrees) provides the Entity-Component-System architecture and 2D utilities on top of MonoGame.

The game is fully on Nez: `GorelordsBrawlerGame : Nez.Core` with a scene flow (MainMenu → Settings → CharacterSelect → Arena), data-driven characters (JSON + sprite atlases), a stock-lives match system, a parallelized PBF acid-fluid simulation with metaball rendering, and a Debug-build HTTP automation server used by the E2E and smoke harnesses.

## Build Commands

```bash
# Build the whole solution (game + Nez + all test projects) — always use the .slnx, never a single csproj
dotnet build GorelordsBrawler.slnx

# Run the game
dotnet run --project GorelordsBrawler/GorelordsBrawler.csproj

# Restore tools (mgfxc shader compiler, t4) — run per fresh checkout/worktree
dotnet tool restore --tool-manifest GorelordsBrawler/.config/dotnet-tools.json
```

Solution file: `GorelordsBrawler.slnx` (VS 2022+ format) — contains the game, `Nez/Nez.Portable/Nez.MG38.csproj`, and the four test projects (Unit, Integration, Fluid, E2E). A running game exe holds a file lock that fails the build — kill `GorelordsBrawler` processes before building.

## Testing

Four automated layers + one human layer. Full workflow, decision table, and recipes live in the **feature-dev skill** ([.claude/skills/feature-dev/SKILL.md](.claude/skills/feature-dev/SKILL.md)) — consult it for every feature.

```bash
# Fast suites: unit + integration + fluid (no game window)
dotnet test GorelordsBrawler.slnx --nologo --filter "Category!=E2E&FullyQualifiedName!~Benchmark_Step"

# E2E: drives REAL gameplay via scripted input + frame-stepping (launches game windows, ~3 min)
E2E_TESTS=1 dotnet test tests/GorelordsBrawler.E2E.Tests/GorelordsBrawler.E2E.Tests.csproj --nologo

# Fluid perf benchmark — Release ONLY (Debug numbers are meaningless)
FLUID_BENCH=1 dotnet test tests/Fluid.Tests -c Release --filter FluidBenchmark

# Smoke: visual gate + recorded MP4 for the PR (-OpenIde on the pre-PR run)
pwsh .claude/skills/smoke-test/smoke_test.ps1 -Feature acid
```

**The rule: mechanics get automated tests; the user's hands are for FEEL.** Anything requiring input — movement, attacks, swimming, knock-ins — is E2E-testable through the debug server's write channel; never declare gameplay "manual-only." Changing arena geometry or movement/hazard mechanics requires running the **full** E2E assembly, not just the new tests.

## Architecture

**Entry point:** `Program.cs` → `GorelordsBrawlerGame : Nez.Core`. `Initialize()` registers global managers (`MatchSetupManager`), loads `AppSettings` (debug flags: `DebugServer`, `DebugDirectArena`, `DebugFastAcid`, `DebugAutomation`), and starts either `MainMenuScene` or — under `DebugDirectArena` — `ArenaScene` directly. In Debug builds, `Update()` hosts the deterministic frame-stepping gate the E2E harness uses (`DebugControl`, fixed 1/60 dt).

**Nez ECS pattern (in use everywhere):**
- **Scene** — root container managing entities, renderers, and post-processors (`Scenes/`, all extend `BaseScene`)
- **Entity** — container for components, has a Transform
- **Component** — modular behavior/rendering attached to entities
- **SceneComponent** — scene-level logic without an entity (managers like `PlayerManager`, `CombatEffectsManager`)

Project layout, component inventories, and system-by-system notes live in the auto-memory (`MEMORY.md` → Architecture / Project Structure) rather than being duplicated here.

**Key Nez subsystems available:**
- Physics: lightweight collision (AABB, circle, polygon) with SpatialHash broadphase
- AI: BehaviorTree, FSM, GOAP, UtilityAI, A* pathfinding
- Graphics: sprite rendering, deferred lighting, post-processing, scene transitions
- UI: Scene2D-based widget system (from libGDX)
- Debug: in-game console (tilde key), runtime inspector, Dear ImGui integration

## Content Pipeline

There is **no MGCB pipeline** — no `Content.mgcb`, and the `MonoGame.Content.Builder.Task` package was deliberately removed (re-adding it without a real `.mgcb` file breaks the build). Content loads through three direct paths, all copy-to-output:

- **Shaders**: HLSL in `Content/Effects/*.fx`, pre-compiled to `.mgfxo` via `dotnet mgfxc <in>.fx <out>.mgfxo /Profile:OpenGL`, loaded with `new Effect(GraphicsDevice, File.ReadAllBytes(...))`.
- **Sprites**: `tools/build_atlas.py` renders/packs character sheets into Nez `.atlas` files (+ `.sockets.json` / `.hurtboxes.json` sidecars), parsed by `SpriteAtlasLoader` at runtime.
- **Maps/JSON/fonts**: Tiled `.tmx` via `Content.LoadTiledMap()`, character JSON via Nez direct readers. `Content/maps/arena1.tmx` is **generated** by `tools/gen_sump_map.py` (constraint asserts included) — regenerate rather than hand-editing 3000 CSV cells.

## Code Style

- **Tabs** (4-space width), matching the Nez convention. Never run `dotnet format` on these files — it converts tabs→spaces and rewrites whole files; for mechanical multi-site edits use a byte-preserving script (template: `tools/parallelize_fluid.py`).
- **Always braces** on `if`/`else`/`for`/`foreach` — no braceless control statements.
- Magic values live in `GameConstants` (gameplay) or the owning feature's config class (e.g. `FluidConfig`); live-tunable knobs are `[Inspectable, Range(...)]` fields.

## Common Pitfalls

### Hitstop sets `Time.TimeScale = 0`, so `Time.DeltaTime` becomes 0 for several frames
`CombatEffectsManager.TriggerHit` freezes the game (`Time.TimeScale = 0`) for `GameConstants.Combat.HitstopDuration` (~4 frames) on every melee hit. Any `IUpdatable`/`SceneComponent` that reads the **scaled** `Time.DeltaTime` will therefore receive `dt = 0` during that window. Two consequences to design for:

- **Never divide by `dt`.** A per-frame integrator that derives velocity as `(newPos - oldPos) / dt` produces `1/0 = +Infinity → NaN` when `dt = 0`. Guard the whole update as a no-op when `dt <= 0`. This was the root cause of the "acid liquid vanishes + FPS tanks when you hit a player" bug: the PBF fluid's `UpdateVelocitiesAndPositions` did `invDt = 1f / dt`, NaN'd every particle on the first hitstop frame, and never recovered (see below). Fixed by an early return in `FluidSimulation.Step` for `dt <= 0`.
- **NaN is a silent, persistent killer in particle/grid systems.** Once a position goes NaN it usually never leaves: `NaN > cutoff` is `false` so off-screen despawn checks skip it, and `(int)float.NaN == 0` in C# collapses *every* NaN particle into a single spatial-hash cell, turning the neighbor search into O(n²) — that's the permanent FPS cliff, not a one-frame stutter. When a sim "dies" after an event, suspect NaN before anything else.
- If a system should keep animating *through* a hitstop freeze (e.g. hit-flash, screen-space FX decay), use `Time.UnscaledDeltaTime` deliberately — but most gameplay sims should freeze with everything else, so plain `Time.DeltaTime` + a `dt <= 0` guard is correct.

Regression coverage: unit (`tests/Fluid.Tests/FluidSimulationTests.cs → Zero_Timestep_During_Hitstop_Does_Not_Produce_NaN`) **and** end-to-end (`GameplayAutomationE2ETests → MeleeHit_WhileAcidPresent_DoesNotBreakTheFluid`, which scripts a real melee connect and steps through the dt=0 freeze).

### The debug server is NOT read-only — gameplay is fully automatable
`GameDebugServer` has a write channel (since PR #13, hardened #15/#16): `POST /input` (per-player moveX/moveY/jump/attack/special on the scripted-input device), `POST /step` (advance exactly N fixed-dt frames), `POST /run` (free vs stepped mode), `POST /teleport`, `POST /damage`. It requires `DebugAutomation: true` in appsettings — without that flag, players get keyboard devices and `/input` is silently ignored. Tests drive it through the `ArenaPage`/`PlayerObject` Page Object Model in `tests/GorelordsBrawler.E2E.Tests/` (enable with `E2E_TESTS=1`; each test launches its own game window; `xunit.runner.json` serializes classes so they don't fight over port 7777). **If a behavior needs input to verify — movement, attacks, swimming, knock-ins — write an E2E test; do not declare it "manual-only."**

## Planning Workflow

Feature development follows a plan → review → implement → archive cycle:

1. **Plan** — Create a proposal markdown in `docs/` (e.g. `docs/feature-name-proposal.md`); research online first per "Your role"; lock contested design choices as **Decisions** in the user's words
2. **Review** — User reviews and requests changes to the proposal
3. **Implement & verify** — Once approved, follow the **feature-dev skill** ([.claude/skills/feature-dev/SKILL.md](.claude/skills/feature-dev/SKILL.md)): ground the plan in the actual code, implement to house conventions, test at the right layer (unit / fluid / integration / E2E / smoke), run all suites, and prepare the teaching PR (smoke `-OpenIde` on the pre-PR run)
4. **Archive** — After implementation is complete and verified, `git mv` the doc to `docs/implemented/` and update the index below

Active proposals live in `docs/`. Completed features have their docs in `docs/implemented/`.

### Current Proposals
- `docs/acid-arena-design-proposal.md` — "The Sump" acid arena. **Phase A (basin geometry + pre-fill + parallelized PBF solver) merged in PR #17. Phase B (depth-scaled lethality + swim/breach escape) in review.** Remaining: Phase C (phase machine + dual inlets + escalation), D (telegraphing), E (art pass)

### Implemented Features (highlights — full list in `docs/implemented/`)
- `match-system-proposal.md` — stock lives match system with ruleset pattern
- `character-select-proposal.md` — character select, modular stats refactor, scene transitions
- `e2e-gameplay-automation-proposal.md` — scripted-input device + deterministic frame-stepping (PR #13)
- `e2e-gameplay-automation-hardening-proposal.md` — launcher fix, acid-independent oracles, solid-core regressions (PRs #15/#16)
- `environment-system-proposal.md` — Tiled map integration (collision auto-colliders, spawn objects)
- `acid-deadly-polish-plan.md` + `acid-damage-feedback-proposal.md` + `acid-in-liquid-presence-proposal.md` — the acid visual/feedback stack: bubbles, sizzle, submersion feel, see-through shader, damage post-processors (PRs #4–#10)

## Nez Documentation

Framework docs are in `Nez/FAQs/` — key files: `Nez-Core.md`, `Scene-Entity-Component.md`, `Rendering.md`, `Physics.md`, `AI.md`, `UI.md`.
