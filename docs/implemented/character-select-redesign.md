# Character Select Redesign — Entity-Based

## Problem

The current CharacterSelectScene mixes two input models: Nez.UI (Stage-driven, focus-based) for rendering and `Nez.Input.IsKeyPressed()` for join detection. The Stage owns keyboard input in its update loop, creating edge-case bugs where key presses are lost. This is a fundamental architectural mismatch — character select is gameplay-adjacent (multi-device, simultaneous input, axis-driven navigation) and should use gameplay patterns, not UI widget patterns.

## Solution: Entity-Based Character Select

Replace UICanvas/Stage/Table/Label with **entities and components** — the same ECS + VirtualInput patterns used in gameplay. Zero raw keyboard polling. 100% VirtualInput.

### Architecture

```
CharacterSelectScene
├── SlotEntity (×4)              — one per player slot
│   ├── SlotController           — Component: join/select/ready logic
│   ├── SlotRenderer             — RenderableComponent: draws the slot panel
│   └── InputProfile (nullable)  — assigned on join, null when empty
├── HeaderEntity                 — title + status text
│   └── HeaderRenderer           — RenderableComponent: draws title and status
├── JoinListener (×6)            — lightweight VirtualButtons for each device's join key
└── Scene-level logic            — join routing, countdown, ESC, match start
```

### Key Design Decisions

#### 1. Implicit Empty State (No Enum)

SlotController does not use a state enum. State is derived from data:

```csharp
public bool IsJoined => Input != null;
public bool IsReady { get; private set; }
```

- `Input == null` → empty slot (show join prompts)
- `Input != null && !IsReady` → joined, can cycle/ready
- `Input != null && IsReady` → locked in

Cleaner and impossible to desync.

#### 2. Join Via VirtualButton (No Raw Keyboard)

Instead of raw `Keyboard.GetState()` with manual `_prevW` tracking, create **temporary join-listener VirtualButtons** per device:

```csharp
// One VirtualButton per unjoinable device
private VirtualButton _joinWASD;      // Listens for W
private VirtualButton _joinArrows;    // Listens for Up
private VirtualButton[] _joinGamepad; // Listens for A button per pad
```

These are created in the scene constructor and checked each frame:

```csharp
if (_joinWASD.IsPressed && !IsDeviceJoined(KeyboardWASD) && HasEmptySlot())
    JoinDevice(InputDeviceType.KeyboardWASD);
```

Once a device joins, its join-listener is deregistered (optional — or just stop checking). The slot gets a full `InputProfile` from `InputProfileFactory.CreateFromDevice()`.

This keeps **100% of input under VirtualInput**. No raw keyboard state anywhere.

#### 3. Post-Join Input Uses InputProfile

Once joined, the SlotController reads `Input.Attack.IsPressed`, `Input.MoveX.Value`, `Input.Jump.IsPressed` — identical to how WalkAbility, JumpAbility, and MeleeAttack work in gameplay. Reliable, buffered, device-agnostic.

### Component Details

#### `SlotController` (Component, IUpdatable)

```csharp
public class SlotController : Component, IUpdatable
{
    public InputProfile Input { get; private set; }
    public InputDeviceType Device { get; private set; }
    public int CharacterIndex { get; private set; }
    public bool IsReady { get; private set; }
    public int SlotIndex { get; }

    public bool IsJoined => Input != null;

    private readonly string[] _characters;

    public SlotController(int slotIndex, string[] characters) { ... }

    public void Join(InputDeviceType device, InputProfile input)
    {
        Device = device;
        Input = input;
        CharacterIndex = 0;
        IsReady = false;
    }

    public void Unjoin()
    {
        Input.Deregister();
        Input = null;
        IsReady = false;
    }

    public void Update()
    {
        if (!IsJoined) return;

        if (IsReady)
        {
            // Un-ready on Attack
            if (Input.Attack.IsPressed)
                IsReady = false;
        }
        else
        {
            // Cycle character using VirtualIntegerAxis built-in edge detection
            var dir = Input.MoveX.DirectionJustPushed;
            if (dir != 0)
            {
                CharacterIndex += dir;
                if (CharacterIndex < 0) CharacterIndex = _characters.Length - 1;
                if (CharacterIndex >= _characters.Length) CharacterIndex = 0;
            }

            // Ready up on Attack
            if (Input.Attack.IsPressed)
                IsReady = true;

            // Unjoin on Jump
            if (Input.Jump.IsPressed)
                Unjoin();
        }
    }
}
```

Character cycling uses `VirtualIntegerAxis.DirectionJustPushed` — Nez's built-in edge detection for axes. Returns -1 or 1 only on the frame the axis transitions from neutral, handling deadzones and digital/analog input correctly. No manual `_prevMoveX` tracking needed.

#### `SlotRenderer` (RenderableComponent)

Draws directly with Batcher. No UI widgets.

```csharp
public class SlotRenderer : RenderableComponent
{
    private readonly BitmapFont _font;
    private readonly string[] _characters;
    private readonly Dictionary<string, CharacterData> _characterDataCache;

    public override float Width => GameConstants.CharacterSelect.SlotWidth;
    public override float Height => GameConstants.CharacterSelect.SlotHeight;

    public override void Render(Batcher batcher, Camera camera)
    {
        var slot = Entity.GetComponent<SlotController>();
        var pos = Entity.Transform.Position;

        if (!slot.IsJoined)
        {
            // Draw available join prompts
            DrawJoinPrompts(batcher, pos);
            return;
        }

        // Draw "P1" label
        var playerText = string.Format(GameConstants.UI.PlayerLabelFormat, slot.SlotIndex + 1);
        batcher.DrawString(_font, playerText, pos, Color.White);

        // Draw character color preview (filled rect)
        var charData = GetCharacterData(slot);
        var previewColor = new Color(charData.colorR, charData.colorG, charData.colorB);
        var previewRect = new Rectangle(
            (int)(pos.X - charData.bodyWidth), (int)(pos.Y + nameOffset),
            (int)(charData.bodyWidth * previewScale), (int)(charData.bodyHeight * previewScale));
        batcher.DrawRect(previewRect, previewColor);

        // Draw "< CharName >" with arrows (only if not ready)
        var charName = charData.name ?? _characters[slot.CharacterIndex];
        var displayName = slot.IsReady ? charName : "< " + charName + " >";
        batcher.DrawString(_font, displayName, namePos, Color.White);

        // Draw "READY" if ready
        if (slot.IsReady)
            batcher.DrawString(_font, "READY", readyPos, Color.Green);
    }
}
```

Same pattern as HealthBar — override `Width`/`Height` for culling, full control over drawing.

#### `HeaderRenderer` (RenderableComponent)

```csharp
public class HeaderRenderer : RenderableComponent
{
    public string StatusText { get; set; }
    public Color StatusColor { get; set; }

    public override float Width => GameConstants.Screen.DesignWidth;
    public override float Height => GameConstants.Screen.DesignHeight;

    public override void Render(Batcher batcher, Camera camera)
    {
        // Draw "CHARACTER SELECT" title (centered, top)
        // Draw status text (centered, bottom) — "Need at least 2 players" / "All players ready!"
    }
}
```

### Scene Structure

```csharp
public class CharacterSelectScene : BaseScene
{
    private SlotController[] _slots;
    private HeaderRenderer _header;

    // Join listeners — VirtualButtons for each device's join key
    private VirtualButton _joinWASD;
    private VirtualButton _joinArrows;
    private VirtualButton[] _joinGamepad;
    private VirtualButton _escButton;

    public CharacterSelectScene()
    {
        var font = Content.LoadBitmapFont(Nez.Content.Fonts.GoreFont);
        var titleFont = Content.LoadBitmapFont(Nez.Content.Fonts.Sludgeborn);

        // Create join listeners (VirtualButtons, not raw keyboard)
        _joinWASD = new VirtualButton();
        _joinWASD.AddKeyboardKey(Keys.W);
        _joinWASD.AddKeyboardKey(Keys.F);

        _joinArrows = new VirtualButton();
        _joinArrows.AddKeyboardKey(Keys.Up);
        _joinArrows.AddKeyboardKey(Keys.RightControl);

        _joinGamepad = new VirtualButton[GameConstants.Input.MaxGamePads];
        for (int i = 0; i < GameConstants.Input.MaxGamePads; i++)
        {
            _joinGamepad[i] = new VirtualButton();
            _joinGamepad[i].AddGamePadButton(i, Buttons.A);
            _joinGamepad[i].AddGamePadButton(i, Buttons.Start);
        }

        _escButton = new VirtualButton();
        _escButton.AddKeyboardKey(Keys.Escape);

        // Header entity (centered)
        var headerEntity = CreateEntity("header", headerPosition);
        _header = headerEntity.AddComponent(new HeaderRenderer(font, titleFont));

        // Slot entities (spaced horizontally)
        _slots = new SlotController[PlayerManager.MaxPlayers];
        for (int i = 0; i < PlayerManager.MaxPlayers; i++)
        {
            var slotEntity = CreateEntity($"slot-{i}", CalculateSlotPosition(i));
            _slots[i] = slotEntity.AddComponent(new SlotController(i, GameConstants.Characters.All));
            slotEntity.AddComponent(new SlotRenderer(font, GameConstants.Characters.All));
        }
    }

    public override void Update()
    {
        CheckJoin();     // Handle new joins first (mutates scene structure)
        base.Update();   // Then update SlotControllers (VirtualInput)
        UpdateStatus();  // Check all-ready, countdown

        // Countdown
        if (_countdownActive)
        {
            _countdownTimer -= Time.DeltaTime;
            if (_countdownTimer <= 0)
                StartMatch();
        }

        // ESC to go back (only if no one is joined)
        if (_escButton.IsPressed && JoinedCount() == 0)
            GorelordsBrawlerGame.TransitionToScene<MainMenuScene>();
    }

    private void CheckJoin()
    {
        if (_joinWASD.IsPressed && !IsDeviceJoined(InputDeviceType.KeyboardWASD))
            JoinDevice(InputDeviceType.KeyboardWASD);

        if (_joinArrows.IsPressed && !IsDeviceJoined(InputDeviceType.KeyboardArrows))
            JoinDevice(InputDeviceType.KeyboardArrows);

        for (int i = 0; i < GameConstants.Input.MaxGamePads; i++)
        {
            var device = InputDeviceType.Gamepad0 + i;
            if (_joinGamepad[i].IsPressed && !IsDeviceJoined(device))
                JoinDevice(device);
        }
    }

    private void JoinDevice(InputDeviceType device)
    {
        var emptySlot = FindEmptySlot();
        if (emptySlot == null) return;

        var input = InputProfileFactory.CreateFromDevice(device);
        emptySlot.Join(device, input);
    }
}
```

### Cleanup (Scene Exit)

VirtualInputs must be deregistered when the scene exits to prevent phantom input across scene transitions:

```csharp
public override void Unload()
{
    base.Unload();

    _joinWASD.Deregister();
    _joinArrows.Deregister();
    _escButton.Deregister();

    foreach (var vb in _joinGamepad)
        vb.Deregister();

    foreach (var slot in _slots)
    {
        if (slot.IsJoined)
            slot.Unjoin();  // Deregisters the InputProfile
    }
}
```

### No UICanvas

The scene does **not** create a UICanvas or use Stage/Table/Label. All rendering is via RenderableComponents on the default render layer. Text is drawn with `BitmapFont` via `Batcher.DrawString()`.

BaseScene's ScreenSpaceRenderer remains available but is unused here.

### What Stays The Same

- `InputProfile` / `InputProfileFactory` — unchanged
- `MatchSetupManager` (GlobalManager) — unchanged
- `CharacterLoader` / `CharacterData` — unchanged, used for preview colors and names
- `GameConstants.CharacterSelect` — reused for text strings and scales
- Scene transitions via `GorelordsBrawlerGame.TransitionToScene<T>()` — unchanged

### Files to Create

| File | Type | Description |
|------|------|-------------|
| `Components/UI/SlotController.cs` | Component, IUpdatable | Join/select/ready logic per slot |
| `Components/UI/SlotRenderer.cs` | RenderableComponent | Draws slot panel with Batcher |
| `Components/UI/HeaderRenderer.cs` | RenderableComponent | Draws title and status text |

### Files to Modify

| File | Change |
|------|--------|
| `Scenes/CharacterSelectScene.cs` | Full rewrite — entity-based, no UICanvas |
| `Constants/GameConstants.cs` | Add SlotWidth/SlotHeight constants if needed |

### Why This Fixes the Bug

1. **Join detection uses VirtualButton** — same system as gameplay, updated by `Nez.Input.Update()` before `Scene.Update()`, never interfered with by any UI layer
2. **Post-join input uses InputProfile** — identical to WalkAbility/JumpAbility/MeleeAttack, reliable and buffered
3. **No UICanvas/Stage** — removes the component whose input processing conflicted with direct `Nez.Input.IsKeyPressed()` calls
4. **Zero raw keyboard state** — everything goes through VirtualInput, one unified input model
