# Proposal: Pause Screen

## Problem

There's no way to pause the game during a match. Players can't access controls reference, return to the main menu, or quit without closing the window.

## Design

The pause screen is an **overlay on the ArenaScene**, not a separate scene. It freezes gameplay via `Time.TimeScale = 0` while keeping UI responsive (Nez's `Stage.Update()` doesn't check TimeScale, so buttons and navigation continue working).

### Layout

```
    ╔══════════════════════════╗
    ║        PAUSED            ║
    ║                          ║
    ║        RESUME            ║
    ║        CONTROLS          ║
    ║        MAIN MENU         ║
    ║        QUIT              ║
    ║                          ║
    ╚══════════════════════════╝
```

A semi-transparent black overlay dims the game behind the menu (using Nez's `WindowStyle.StageBackground`).

When **CONTROLS** is selected, the pause menu content swaps to show a controls reference panel:

```
    ╔══════════════════════════╗
    ║        CONTROLS          ║
    ║                          ║
    ║  Player 1 (WASD)         ║
    ║    Move     A / D        ║
    ║    Jump     W             ║
    ║    Attack   F             ║
    ║                          ║
    ║  Player 2 (Arrows)       ║
    ║    Move     Left / Right ║
    ║    Jump     Up            ║
    ║    Attack   Right Ctrl    ║
    ║                          ║
    ║  Gamepad                  ║
    ║    Move     L-Stick / D-Pad║
    ║    Jump     A Button      ║
    ║    Attack   X Button      ║
    ║                          ║
    ║          BACK            ║
    ╚══════════════════════════╝
```

### Activation

- **ESC** key or **Start** button on any gamepad toggles pause on/off
- Pause input is checked using `Time.UnscaledDeltaTime` so it works even while paused
- Pause is only active in the ArenaScene (the main menu doesn't need pausing)

## Architecture

### `PauseManager` SceneComponent

Lives on the ArenaScene. Handles the pause toggle input, creates/manages the pause UI overlay, and controls `Time.TimeScale`.

```
GorelordsBrawler/Systems/PauseManager.cs
```

```csharp
public class PauseManager : SceneComponent, IUpdatable
{
    public bool IsPaused { get; private set; }

    private VirtualButton _pauseInput;
    private Entity _pauseEntity;
    private UICanvas _pauseCanvas;

    public override void OnEnabled()
    {
        _pauseInput = new VirtualButton();
        _pauseInput.AddKeyboardKey(Keys.Escape);
        for (int i = 0; i < GameConstants.Input.MaxGamePads; i++)
            _pauseInput.AddGamePadButton(i, Buttons.Start);
    }

    public override void OnDisabled()
    {
        _pauseInput.Deregister();
        if (IsPaused) Resume();
    }

    public void Update()
    {
        // Must check input regardless of TimeScale
        if (_pauseInput.IsPressed)
        {
            if (IsPaused) Resume();
            else Pause();
        }
    }

    private void Pause() { ... build UI, set TimeScale = 0 ... }
    private void Resume() { ... destroy UI, set TimeScale = 1 ... }
}
```

**Why a SceneComponent instead of a regular Component?** It's scene-level infrastructure, not tied to any specific entity. Same pattern as `PlayerManager`.

**Why create/destroy the UI on each pause instead of show/hide?** Simpler state management — no stale references, no need to reset the controls panel, no risk of the UI drifting out of sync. The UI is lightweight (a few labels and buttons), so there's no performance concern.

### Pause UI Construction

The pause overlay uses Nez's `Dialog` widget with `StageBackground` for the dimming effect:

```csharp
private void BuildPauseMenu()
{
    _pauseEntity = Scene.CreateEntity(GameConstants.EntityNames.PauseMenu);
    _pauseCanvas = _pauseEntity.AddComponent(new UICanvas());
    _pauseCanvas.RenderLayer = GameConstants.Rendering.PauseMenuRenderLayer;

    var windowStyle = new WindowStyle
    {
        Background = new PrimitiveDrawable(GameConstants.PauseMenu.BackgroundColor),
        StageBackground = new PrimitiveDrawable(GameConstants.PauseMenu.OverlayColor)
    };

    var dialog = new Dialog(GameConstants.UI.PausedTitleText, windowStyle);
    // ... add buttons (Resume, Controls, Main Menu, Quit)
    dialog.Show(_pauseCanvas.Stage);
}
```

### Controls Panel

When the player clicks **CONTROLS**, the dialog content is replaced with the controls reference. A **BACK** button returns to the main pause menu. All control labels come from `GameConstants`.

### Button Actions

| Button | Action |
|--------|--------|
| **RESUME** | `Resume()` — destroys pause UI, sets `TimeScale = 1` |
| **CONTROLS** | Swaps dialog content to show controls reference panel |
| **MAIN MENU** | `TimeScale = 1`, then `GorelordsBrawlerGame.LoadScene(MainMenuScene)` |
| **QUIT** | `Core.Exit()` |

**Main Menu** resets `TimeScale` before loading the scene so the main menu isn't frozen. Loading a new scene naturally cleans up the ArenaScene and all its components.

## GameConstants Additions

```csharp
public static class EntityNames
{
    public const string PauseMenu = "pause-menu";
    // ... existing
}

public static class Rendering
{
    public const int PauseMenuRenderLayer = -3; // in front of everything
    // ... existing
}

public static class UI
{
    public const string PausedTitleText = "PAUSED";
    public const string ResumeButtonText = "RESUME";
    public const string ControlsButtonText = "CONTROLS";
    public const string MainMenuButtonText = "MAIN MENU";
    public const string QuitButtonText = "QUIT";
    public const string ControlsTitleText = "CONTROLS";

    // Controls reference text
    public const string Player1Header = "Player 1 (WASD)";
    public const string Player2Header = "Player 2 (Arrows)";
    public const string GamepadHeader = "Gamepad";
    public const string MoveLabel = "Move";
    public const string JumpLabel = "Jump";
    public const string AttackLabel = "Attack";
    public const string Player1MoveKeys = "A / D";
    public const string Player1JumpKey = "W";
    public const string Player1AttackKey = "F";
    public const string Player2MoveKeys = "Left / Right";
    public const string Player2JumpKey = "Up";
    public const string Player2AttackKey = "Right Ctrl";
    public const string GamepadMoveInput = "L-Stick / D-Pad";
    public const string GamepadJumpInput = "A Button";
    public const string GamepadAttackInput = "X Button";
    // ... existing
}

public static class PauseMenu
{
    public static readonly Color OverlayColor = new Color(0, 0, 0, 150);
    public static readonly Color BackgroundColor = new Color(30, 30, 30);
    public const float DialogPadding = 20f;
    public const float ButtonSpacing = 8f;
}
```

## Files Summary

| File | Action |
|------|--------|
| `GorelordsBrawler/Systems/PauseManager.cs` | **Create** — pause toggle, UI creation, TimeScale control |
| `GorelordsBrawler/Scenes/ArenaScene.cs` | **Modify** — add `PauseManager` as SceneComponent |
| `GorelordsBrawler/Constants/GameConstants.cs` | **Modify** — add PauseMenu constants, UI strings, entity name, render layer |

## Edge Cases

- **Multiple pause presses:** `_pauseInput.IsPressed` only fires once per press (not held), so rapid pressing toggles cleanly.
- **Scene transition while paused:** `Resume()` is called in `OnDisabled()`, so `TimeScale` is always reset when the scene is torn down (e.g., returning to main menu).
- **Respawn timer during pause:** `RespawnHandler` uses `Time.DeltaTime` which becomes 0 when paused — respawn timers freeze correctly.
- **Attack cooldowns during pause:** `MeleeAttack` uses `Time.DeltaTime` — also freezes correctly.
- **Pause input registration:** The `VirtualButton` for ESC/Start is registered in `OnEnabled()` and deregistered in `OnDisabled()`, following the same lifecycle pattern as `InputProfile`.
