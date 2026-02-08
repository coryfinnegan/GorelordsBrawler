# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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

## Nez Documentation

Framework docs are in `Nez/FAQs/` — key files: `Nez-Core.md`, `Scene-Entity-Component.md`, `Rendering.md`, `Physics.md`, `AI.md`, `UI.md`.
