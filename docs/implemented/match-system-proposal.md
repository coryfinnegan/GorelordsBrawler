# Match/Round System (Stock Lives) — Implemented

## Status: Implemented + Refactored to Ruleset Pattern

## Context
The arena needed a match system with stock lives, elimination, victory screens, and announcements. Initially implemented with stock logic hardcoded in MatchManager/MatchHUD. Refactored to use a Rules/Ruleset abstraction so new game modes (Timed, custom) can be added without touching MatchManager.

## Architecture

### Ruleset Abstraction

**`IMatchRuleset`** (`Systems/Rules/IMatchRuleset.cs`) — interface for game modes:
- `Initialize(players, onMatchWon, announcements)` — attach mode-specific components, subscribe to events
- `BuildHUD(root, font, players)` — populate HUD table with mode-specific widgets
- `Update()` — called every frame during Active state
- `OnPlayerDeath(player)` — returns true = respawn, false = stay dead
- `GetVictoryText(player, winner)` — result text per player (e.g. "VICTOR" / "DEFEAT")
- `Shutdown()` — cleanup

**`AnnouncementAccess`** — simple callback wrapper so rulesets can trigger announcements without accessing MatchManager internals.

**`StockRuleset`** (`Systems/Rules/StockRuleset.cs`) — stock lives implementation:
- `Initialize()`: adds StockTracker to each player, subscribes to OnEliminated
- `OnPlayerDeath()`: calls LoseStock(), returns result
- On elimination: shows "K.O.!" announcement; fires onMatchWon when ≤1 alive
- `BuildHUD()`: creates "P1 ♥♥♥" labels, subscribes to stock change events
- `GetVictoryText()`: returns "VICTOR" for winner, "DEFEAT" for losers
- `Update()`/`Shutdown()`: no-op (event-driven mode)

### Core Components

1. **StockTracker** (`Components/StockTracker.cs`) — Per-player component tracking remaining lives
   - Fields: `RemainingStocks`, `IsEliminated`
   - Events: `OnStockLost(int remaining)`, `OnEliminated()`
   - Methods: `LoseStock()` returns true if stocks remain; `Reset(int count)`
   - Added by StockRuleset during Initialize (not by CharacterFactory)

2. **MatchManager** (`Systems/MatchManager.cs`) — SceneComponent orchestrating match flow
   - Constructor takes `IMatchRuleset`
   - State machine: Countdown → Active → Victory
   - `CanPause`: true only during Active state
   - `NotifyPlayerDeath(Entity)`: delegates to ruleset, returns bool for respawn decision
   - `ShowAnnouncement(text, color)`: public for ruleset access via AnnouncementAccess
   - Victory screen: split-screen layout with VICTOR/DEFEAT per player + Rematch/Main Menu buttons

3. **AnnouncementOverlay** (`Components/AnnouncementOverlay.cs`) — RenderableComponent for centered text
   - Three phases: fade-in → hold → fade-out
   - Uses `Time.UnscaledDeltaTime` (works during TimeScale=0)

4. **MatchHUD** (`Systems/MatchHUD.cs`) — SceneComponent displaying mode-specific HUD
   - Constructor takes `IMatchRuleset`
   - Delegates all widget creation to `_ruleset.BuildHUD()`

5. **RespawnHandler** (`Components/RespawnHandler.cs`) — Decoupled from StockTracker
   - On death: calls `MatchManager.NotifyPlayerDeath()` to determine respawn
   - Falls back to always-respawn if no MatchManager present (training mode etc.)

### Flow
1. ArenaScene creates `StockRuleset`, passes to both `MatchManager` and `MatchHUD`
2. MatchManager.OnEnabled() calls `ruleset.Initialize()` which adds StockTrackers
3. Countdown: "FIGHT!" announcement, abilities disabled
4. Active: combat enabled, deaths → `NotifyPlayerDeath()` → ruleset → StockTracker
5. K.O.! on elimination, "GAME!" when ≤1 alive
6. Victory: split-screen VICTOR/DEFEAT layout with Rematch/Main Menu

### Files
- **`Systems/Rules/IMatchRuleset.cs`** — Ruleset interface + AnnouncementAccess
- **`Systems/Rules/StockRuleset.cs`** — Stock lives implementation
- **`Systems/MatchManager.cs`** — Match orchestration (delegates to ruleset)
- **`Systems/MatchHUD.cs`** — HUD shell (delegates to ruleset)
- **`Components/StockTracker.cs`** — Stock lives tracking
- **`Components/AnnouncementOverlay.cs`** — Centered text announcements
- **`Components/RespawnHandler.cs`** — Death/respawn, delegates to MatchManager
- **`Data/CharacterFactory.cs`** — No longer adds StockTracker (ruleset handles it)
- **`Scenes/ArenaScene.cs`** — Creates StockRuleset, passes to MatchManager/MatchHUD
- **`Constants/GameConstants.cs`** — Match constants (VictorText, DefeatText, ResultScale, VictorColor, DefeatColor)

### Adding a New Game Mode
1. Create a new class implementing `IMatchRuleset` (e.g. `TimedRuleset`)
2. Pass it to `MatchManager` and `MatchHUD` in the scene constructor
3. No changes needed to MatchManager, MatchHUD, or RespawnHandler
