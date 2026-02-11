# Proposal: Settings Menu & Resolution Management

## Problem

Screen resolution and fullscreen are hardcoded in `GorelordsBrawlerGame.cs`. There's no way for players to change display settings, and no infrastructure to persist preferences across sessions. As we add audio and other systems, we'll need a central place for user-configurable options.

## Nez APIs We'll Use

### Screen (Nez static class)
- `Screen.IsFullscreen` — get/set fullscreen state
- `Screen.HardwareModeSwitch` — `true` = exclusive fullscreen, `false` = borderless window (MonoGame 3.8+)
- `Screen.SetSize(width, height)` — sets back buffer size and applies
- `Screen.SynchronizeWithVerticalRetrace` — VSync toggle
- `Screen.MonitorWidth` / `Screen.MonitorHeight` — native display resolution (useful for capping resolution options)
- `Screen.ApplyChanges()` — flush pending changes

### KeyValueDataStore + FileDataStore (Nez.Persistence)
Nez has a built-in key-value preferences system designed exactly for this. `FileDataStore` handles file I/O (writes to `Environment.SpecialFolder.LocalApplicationData`), and `KeyValueDataStore` provides typed get/set with defaults:

```csharp
var store = new FileDataStore();
var prefs = KeyValueDataStore.Default;
prefs.Load(store);

bool fs = prefs.GetBool("fullscreen", false);
prefs.Set("fullscreen", true);

if (prefs.IsDirty)
    prefs.Flush(store);
```

This is simpler than rolling our own JSON settings file, handles dirty-checking automatically, and is the Nez-native way to do preferences.

### UI Widgets
- `SelectBox<T>` — dropdown for resolution selection
- `CheckBox` — fullscreen and VSync toggles
- `TextButton` — apply/back buttons
- `Table` — layout container (already used in MainMenuScene)

## Architecture

### 1. `SettingsManager` (static class)

Owns the `FileDataStore` and `KeyValueDataStore`. Provides typed accessors for each setting with sensible defaults. Responsible for applying settings to `Screen` and for save/load.

```
GorelordsBrawler/Systems/SettingsManager.cs
```

```csharp
public static class SettingsManager
{
    private static FileDataStore _fileStore;
    private static KeyValueDataStore _prefs;

    // Keys (private, no magic strings leak out)
    private const string KeyWidth = "screen_width";
    private const string KeyHeight = "screen_height";
    private const string KeyFullscreen = "fullscreen";
    private const string KeyBorderless = "borderless";
    private const string KeyVSync = "vsync";

    // Public accessors with defaults
    public static int ResolutionWidth  => _prefs.GetInt(KeyWidth, GameConstants.Screen.DesignWidth);
    public static int ResolutionHeight => _prefs.GetInt(KeyHeight, GameConstants.Screen.DesignHeight);
    public static bool IsFullscreen    => _prefs.GetBool(KeyFullscreen, false);
    public static bool IsBorderless    => _prefs.GetBool(KeyBorderless, true);
    public static bool VSync           => _prefs.GetBool(KeyVSync, true);

    public static void Initialize()
    {
        _fileStore = new FileDataStore();
        _prefs = KeyValueDataStore.Default;
        _prefs.Load(_fileStore);
    }

    public static void Set(string key, ...) { ... }

    public static void Apply()
    {
        Screen.IsFullscreen = IsFullscreen;
        Screen.HardwareModeSwitch = !IsBorderless;
        Screen.SynchronizeWithVerticalRetrace = VSync;
        Screen.SetSize(ResolutionWidth, ResolutionHeight);
    }

    public static void Save()
    {
        if (_prefs.IsDirty)
            _prefs.Flush(_fileStore);
    }
}
```

**Startup flow:**
1. `GorelordsBrawlerGame.Initialize()` calls `SettingsManager.Initialize()`
2. `SettingsManager.Apply()` applies saved settings (or defaults on first run)
3. Then load the main menu scene as usual

This means first-time players get the current 800x600 windowed defaults, and returning players get their saved preferences restored before any scene loads.

### 2. Resolution Options

Rather than listing every possible resolution, we query available display modes from MonoGame's `GraphicsAdapter` and filter to standard 16:9 and 16:10 ratios at or below the monitor's native resolution. Common results:

| Resolution | Aspect |
|-----------|--------|
| 1280x720  | 16:9   |
| 1280x800  | 16:10  |
| 1366x768  | 16:9   |
| 1440x900  | 16:10  |
| 1600x900  | 16:9   |
| 1680x1050 | 16:10  |
| 1920x1080 | 16:9   |
| 1920x1200 | 16:10  |
| 2560x1440 | 16:9   |

We'll also always include 800x600 (our design resolution) as a fallback. The list is built once at scene creation from `GraphicsAdapter.DefaultAdapter.SupportedDisplayModes`.

### 3. `SettingsScene`

A new scene accessible from the main menu. Uses Nez UI widgets in a `Table` layout.

```
GorelordsBrawler/Scenes/SettingsScene.cs
```

**Layout:**
```
         SETTINGS

  Resolution   [1920x1080 v]
  Fullscreen   [ ]
  Borderless   [ ]
  VSync        [ ]

       [APPLY]  [BACK]
```

**Behavior:**
- Scene reads current values from `SettingsManager` to populate widget states on creation
- Changing a widget updates a local pending state (not applied immediately)
- **APPLY** writes pending values to `SettingsManager`, calls `Apply()` and `Save()`
- **BACK** discards pending changes and returns to main menu
- Borderless checkbox is only enabled when Fullscreen is checked (borderless only matters in fullscreen mode)
- If the player selects a resolution larger than their monitor, we cap it to the monitor's native resolution

### 4. Main Menu Update

Add a "SETTINGS" button below "PLAY" that loads the `SettingsScene`:

```csharp
var settingsButton = new TextButton(GameConstants.UI.SettingsButtonText, buttonStyle);
settingsButton.OnClicked += x => GorelordsBrawlerGame.LoadScene(GameConstants.SceneNames.SettingsScene);
table.Add(settingsButton);
```

### 5. GameConstants Additions

```csharp
public static class SceneNames
{
    public const string SettingsScene = "SettingsScene";
    // ... existing
}

public static class UI
{
    public const string SettingsButtonText = "SETTINGS";
    public const string SettingsTitleText = "SETTINGS";
    public const string ApplyButtonText = "APPLY";
    public const string BackButtonText = "BACK";
    public const string ResolutionLabel = "Resolution";
    public const string FullscreenLabel = "Fullscreen";
    public const string BorderlessLabel = "Borderless";
    public const string VSyncLabel = "VSync";
    // ... existing
}
```

## Files to Create/Modify

| File | Action |
|------|--------|
| `GorelordsBrawler/Systems/SettingsManager.cs` | **Create** — persistence + apply logic |
| `GorelordsBrawler/Scenes/SettingsScene.cs` | **Create** — settings UI scene |
| `GorelordsBrawler/Scenes/MainMenuScene.cs` | **Modify** — add SETTINGS button |
| `GorelordsBrawler/Constants/GameConstants.cs` | **Modify** — add UI strings, scene name |
| `GorelordsBrawler/GorelordsBrawlerGame.cs` | **Modify** — call `SettingsManager.Initialize()` + `Apply()` at startup, remove hardcoded Screen.SetSize |

## Why KeyValueDataStore Over JSON

- **Built into Nez.Persistence** — no extra dependencies or custom serialization
- **Dirty-checking** — only writes to disk when values actually change
- **Typed accessors with defaults** — `GetBool("key", defaultValue)` means no null-checking or missing-key errors
- **Binary format** — players can't easily hand-edit it into an invalid state
- **Standard save location** — uses `LocalApplicationData`, the OS-correct place for user preferences

## Adding Future Settings

When we add audio (or any other setting), the pattern is:

1. Add a key constant and accessor in `SettingsManager`
2. Add a widget in `SettingsScene` (the Table layout makes adding rows trivial)
3. Add the label string in `GameConstants.UI`

No new classes, no new files, no architectural changes needed.
