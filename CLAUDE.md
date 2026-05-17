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

## Project Overview

GorelordsBrawler is a 2D game built with **MonoGame 3.8 DesktopGL** on **.NET 8.0**. The **Nez framework** (included as a Git submodule in `Nez/`) provides an Entity-Component-System architecture and extensive 2D game utilities on top of MonoGame.

The project is in early development — the main game class (`GorelordsBrawlerGame.cs`) still uses the default MonoGame template and has not yet been converted to use `Nez.Core`.

## Build Commands

```bash
# Build the project
dotnet build GorelordsBrawler/GorelordsBrawler.csproj

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

## Planning Workflow

Feature development follows a plan → review → implement → archive cycle:

1. **Plan** — Create a proposal markdown in `docs/` (e.g. `docs/feature-name-proposal.md`)
2. **Review** — User reviews and requests changes to the proposal
3. **Implement** — Once approved, implement the feature per the proposal
4. **Archive** — After implementation is complete and verified, move the doc to `docs/implemented/`

Active proposals live in `docs/`. Completed features have their docs in `docs/implemented/`.

### Current Proposals
(none)

### Implemented Features
- `docs/implemented/match-system-proposal.md` — Stock lives match system with ruleset pattern
- `docs/implemented/character-select-proposal.md` — Character select screen, modular stats refactor, Doc Marauder, scene transitions

## Nez Documentation

Framework docs are in `Nez/FAQs/` — key files: `Nez-Core.md`, `Scene-Entity-Component.md`, `Rendering.md`, `Physics.md`, `AI.md`, `UI.md`.
