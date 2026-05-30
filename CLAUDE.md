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

GorelordsBrawler is a 2D game built with **MonoGame 3.8 DesktopGL** on **.NET 8.0**. The **Nez framework** (included as a Git submodule in `Nez/`) provides an Entity-Component-System architecture and extensive 2D game utilities on top of MonoGame.

The project is in early development — the main game class (`GorelordsBrawlerGame.cs`) still uses the default MonoGame template and has not yet been converted to use `Nez.Core`.

## Build Commands

```bash
# Build the whole solution (game + Nez + all test projects) — always use the .slnx, never a single csproj
dotnet build GorelordsBrawler.slnx

# Run the game
dotnet run --project GorelordsBrawler/GorelordsBrawler.csproj

# Restore tools (MonoGame content pipeline)
dotnet tool restore --tool-manifest GorelordsBrawler/.config/dotnet-tools.json
```

Solution file: `GorelordsBrawler.slnx` (VS 2022+ format). Currently only contains the main project — Nez is not yet added to the solution.

## Architecture

**Entry point:** `Program.cs` → creates and runs `GorelordsBrawlerGame`

**Current state:** `GorelordsBrawlerGame` inherits from MonoGame's `Game` class directly. To integrate Nez, it should inherit from `Nez.Core` instead, which provides the full ECS pipeline.

**Nez ECS pattern (target architecture):**
- **Scene** — root container managing entities, renderers, and post-processors
- **Entity** — container for components, has a Transform
- **Component** — modular behavior/rendering attached to entities
- **SceneComponent** — scene-level logic without an entity

**Key Nez subsystems available:**
- Physics: lightweight collision (AABB, circle, polygon) with SpatialHash broadphase
- AI: BehaviorTree, FSM, GOAP, UtilityAI, A* pathfinding
- Graphics: sprite rendering, deferred lighting, post-processing, scene transitions
- UI: Scene2D-based widget system (from libGDX)
- Debug: in-game console (tilde key), runtime inspector, Dear ImGui integration

## Content Pipeline

Content is managed via MonoGame Content Builder (`Content/Content.mgcb`). Currently empty.

When Nez is integrated, its default content (effects/textures) from `Nez/DefaultContent/` needs to be copied or linked into `Content/nez/`.

## Code Style

Nez uses tabs (4-space width) per its `.editorconfig`. Follow the same convention for game code.

## Common Pitfalls

### Hitstop sets `Time.TimeScale = 0`, so `Time.DeltaTime` becomes 0 for several frames
`CombatEffectsManager.TriggerHit` freezes the game (`Time.TimeScale = 0`) for `GameConstants.Combat.HitstopDuration` (~4 frames) on every melee hit. Any `IUpdatable`/`SceneComponent` that reads the **scaled** `Time.DeltaTime` will therefore receive `dt = 0` during that window. Two consequences to design for:

- **Never divide by `dt`.** A per-frame integrator that derives velocity as `(newPos - oldPos) / dt` produces `1/0 = +Infinity → NaN` when `dt = 0`. Guard the whole update as a no-op when `dt <= 0`. This was the root cause of the "acid liquid vanishes + FPS tanks when you hit a player" bug: the PBF fluid's `UpdateVelocitiesAndPositions` did `invDt = 1f / dt`, NaN'd every particle on the first hitstop frame, and never recovered (see below). Fixed by an early return in `FluidSimulation.Step` for `dt <= 0`.
- **NaN is a silent, persistent killer in particle/grid systems.** Once a position goes NaN it usually never leaves: `NaN > cutoff` is `false` so off-screen despawn checks skip it, and `(int)float.NaN == 0` in C# collapses *every* NaN particle into a single spatial-hash cell, turning the neighbor search into O(n²) — that's the permanent FPS cliff, not a one-frame stutter. When a sim "dies" after an event, suspect NaN before anything else.
- If a system should keep animating *through* a hitstop freeze (e.g. hit-flash, screen-space FX decay), use `Time.UnscaledDeltaTime` deliberately — but most gameplay sims should freeze with everything else, so plain `Time.DeltaTime` + a `dt <= 0` guard is correct.

Regression coverage: `tests/Fluid.Tests/FluidSimulationTests.cs → Zero_Timestep_During_Hitstop_Does_Not_Produce_NaN`. A true end-to-end repro isn't feasible yet — the debug server (`GameDebugServer`) is read-only (`GET /state`, `GET /screenshot`) with no input-injection endpoint to script a melee hit — so this failure mode is pinned at the unit level instead.

## Planning Workflow

Feature development follows a plan → review → implement → archive cycle:

1. **Plan** — Create a proposal markdown in `docs/` (e.g. `docs/feature-name-proposal.md`)
2. **Review** — User reviews and requests changes to the proposal
3. **Implement** — Once approved, implement the feature per the proposal
4. **Archive** — After implementation is complete and verified, move the doc to `docs/implemented/`

Active proposals live in `docs/`. Completed features have their docs in `docs/implemented/`.

### Current Proposals
- `docs/e2e-gameplay-automation-proposal.md` — scripted-input device + deterministic frame-stepping so E2E tests can drive real gameplay (awaiting review)

### Implemented Features
- `docs/implemented/match-system-proposal.md` — Stock lives match system with ruleset pattern
- `docs/implemented/character-select-proposal.md` — Character select screen, modular stats refactor, Doc Marauder, scene transitions

## Nez Documentation

Framework docs are in `Nez/FAQs/` — key files: `Nez-Core.md`, `Scene-Entity-Component.md`, `Rendering.md`, `Physics.md`, `AI.md`, `UI.md`.
