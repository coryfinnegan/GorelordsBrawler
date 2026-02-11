# Character Stats Proposal (v2 — Nez-Native)

## Problem

Hardcoded constants are scattered across two components:

**PlayerController.cs** — `MoveSpeed = 100`, `JumpSpeed = 250`, `Gravity = 900`
**MeleeAttack.cs** — `AttackCooldown = 0.5`, `HitboxDuration = 0.15`, `HitboxWidth = 40`, `HitboxHeight = 30`, `HitboxOffsetX = 30`
**PlayerManager.cs** — `PrototypeSpriteRenderer(32, 48)`, `BoxCollider(32, 48)`, `SlotColors`

Every new character means duplicating components or adding branching logic. Tuning Trollborg means recompiling. None of these values are visible to a designer or easy to tweak at runtime.

## Approach: ECS Component + Nez JSON + Runtime Inspector

Instead of an external data system bolted on top, stats live where they belong — as a **Component on the player entity**. This plugs directly into Nez's ECS, its built-in JSON serializer (`Nez.Persistence`), its content pipeline (`NezContentManager.LoadJson`), and its runtime debug inspector (`[Inspectable]`).

### Why this is better than System.Text.Json + POCOs

| | System.Text.Json + POCO | Nez-native |
|---|---|---|
| Serialization | External dependency (works, but separate from framework) | `Nez.Persistence.Json` — same serializer Nez uses internally |
| Content loading | `File.ReadAllText` — manual, not scene-aware | `Scene.Content.LoadJson()` — auto-unloaded with scene |
| Runtime tuning | Edit JSON, restart | `[Inspectable]` fields editable live via debug console (`inspect player1`) |
| ECS integration | Data passed through constructors, lives outside the entity | Data is a Component on the entity, siblings query it with `Entity.GetComponent<CharacterStats>()` |
| Inspector sliders | Not possible | `[Range(min, max)]` gives you sliders in the debug inspector |
| Post-load hooks | Manual init code | `[AfterDecode]` attribute, called automatically |
| Overwrite support | Not built-in | `Json.FromJsonOverwrite()` patches values onto existing objects |

## Data Shape

Same JSON files as before, but flat public fields to match how `Nez.Persistence.Json` works (it serializes public fields by default, not properties).

```
Content/Characters/Trollborg.json
```

```json
{
    "name": "Trollborg",
    "description": "Super heavy, slow, high armor, melee only. Simple for people who are simple.",
    "moveSpeed": 100,
    "jumpSpeed": 250,
    "gravity": 900,
    "attackCooldown": 0.5,
    "hitboxDuration": 0.15,
    "hitboxWidth": 40,
    "hitboxHeight": 30,
    "hitboxOffsetX": 30,
    "bodyWidth": 32,
    "bodyHeight": 48,
    "colorR": 50,
    "colorG": 120,
    "colorB": 50
}
```

Flat structure because Nez's JSON handles nested objects fine, but for a stats blob that the inspector will display, flat fields are simpler to browse and tweak. No nesting ceremony for 15 values.

Second character — just another file:

```
Content/Characters/Razorfang.json
```

```json
{
    "name": "Razorfang",
    "description": "Fast, fragile, long reach.",
    "moveSpeed": 200,
    "jumpSpeed": 350,
    "gravity": 700,
    "attackCooldown": 0.3,
    "hitboxDuration": 0.1,
    "hitboxWidth": 60,
    "hitboxHeight": 20,
    "hitboxOffsetX": 45,
    "bodyWidth": 24,
    "bodyHeight": 44,
    "colorR": 180,
    "colorG": 40,
    "colorB": 40
}
```

## CharacterStats Component

A Nez `Component` with `[Inspectable]` and `[Range]` attributes. Attached to the player entity, queryable by sibling components.

```csharp
// Components/CharacterStats.cs
using Nez;
using Nez.Persistence;

public class CharacterStats : Component
{
    // Identity
    [NotInspectable]
    public string name;

    [NotInspectable]
    public string description;

    // Movement
    [Inspectable] [Range(0, 500)]
    public float moveSpeed = 100f;

    [Inspectable] [Range(0, 600)]
    public float jumpSpeed = 250f;

    [Inspectable] [Range(0, 2000)]
    public float gravity = 900f;

    // Melee
    [Inspectable] [Range(0, 2)]
    public float attackCooldown = 0.5f;

    [Inspectable] [Range(0, 1)]
    public float hitboxDuration = 0.15f;

    [Inspectable] [Range(0, 100)]
    public float hitboxWidth = 40f;

    [Inspectable] [Range(0, 100)]
    public float hitboxHeight = 30f;

    [Inspectable] [Range(0, 100)]
    public float hitboxOffsetX = 30f;

    // Body
    [Inspectable] [Range(8, 128)]
    public float bodyWidth = 32f;

    [Inspectable] [Range(8, 128)]
    public float bodyHeight = 48f;

    // Color
    [Inspectable] [Range(0, 255)]
    public int colorR = 50;

    [Inspectable] [Range(0, 255)]
    public int colorG = 120;

    [Inspectable] [Range(0, 255)]
    public int colorB = 50;

    [JsonExclude]
    public Color BodyColor => new Color(colorR, colorG, colorB);
}
```

All fields are public (Nez JSON serializes public fields by default). The defaults match Trollborg so even without a JSON file the game runs. `[JsonExclude]` keeps computed properties out of serialization.

## Loading

Use `NezContentManager.LoadJson()` which caches the raw string and unloads with the scene, then `Nez.Persistence.Json.FromJson<T>()` to deserialize.

```csharp
// Data/CharacterLoader.cs
using Nez;
using Nez.Persistence;

public static class CharacterLoader
{
    public static CharacterStats Load(Scene scene, string characterType)
    {
        var jsonString = scene.Content.LoadJson($"Characters/{characterType}");
        return Json.FromJson<CharacterStats>(jsonString);
    }
}
```

That's it. No manual `File.ReadAllText`, no custom cache. `Scene.Content` handles caching and cleanup.

## How Components Change

**PlayerController** — reads stats from the entity instead of hardcoded constants:

```csharp
public class PlayerController : Component, IUpdatable
{
    private readonly InputProfile _input;
    private Mover _mover;
    private CharacterStats _stats;
    private Vector2 _velocity;
    private bool _grounded;

    public PlayerController(InputProfile input)
    {
        _input = input;
    }

    public override void OnAddedToEntity()
    {
        _mover = Entity.GetComponent<Mover>();
        _stats = Entity.GetComponent<CharacterStats>();
    }

    public void Update()
    {
        _velocity.X = _input.MoveX.Value * _stats.moveSpeed;
        _velocity.Y += _stats.gravity * Time.DeltaTime;

        if (_grounded && _input.Jump.IsPressed)
            _velocity.Y = -_stats.jumpSpeed;
        // ...
    }
}
```

**MeleeAttack** — same pattern:

```csharp
public override void OnAddedToEntity()
{
    _stats = Entity.GetComponent<CharacterStats>();
}

// In SpawnHitbox:
var hitboxRenderer = _hitboxEntity.AddComponent(
    new PrototypeSpriteRenderer(_stats.hitboxWidth, _stats.hitboxHeight));
```

**PlayerManager.AddPlayer** — loads stats, attaches as component, uses body/color values for setup:

```csharp
public Entity AddPlayer(int slotIndex, InputProfile input,
    string characterType, Vector2 spawnPosition)
{
    var stats = CharacterLoader.Load(Scene, characterType);

    var player = Scene.CreateEntity($"player{slotIndex + 1}");
    player.Transform.Position = spawnPosition;

    // Stats component — must be added first so siblings can find it
    player.AddComponent(stats);

    var renderer = player.AddComponent(
        new PrototypeSpriteRenderer(stats.bodyWidth, stats.bodyHeight));
    renderer.SetColor(stats.BodyColor);

    var collider = player.AddComponent(
        new BoxCollider(stats.bodyWidth, stats.bodyHeight));
    collider.PhysicsLayer = PhysicsLayers.Player;
    collider.CollidesWithLayers = PhysicsLayers.Platforms;

    player.AddComponent(new Mover());
    player.AddComponent(new PlayerController(input));
    player.AddComponent(new MeleeAttack(input));

    // ...
}
```

## Runtime Tuning Workflow

With this setup, while the game is running:

1. Press **tilde (~)** to open the Nez debug console
2. Type `inspect player1`
3. See all `CharacterStats` fields with sliders
4. Drag `moveSpeed` from 100 to 200 — instant effect, no restart
5. Once you like the values, update the JSON file to persist them

This is the standard Nez workflow. The `[Inspectable]` attributes and `[Range]` sliders exist exactly for this purpose.

## File Layout

```
Content/
    Characters/
        Trollborg.json
        Razorfang.json          (when ready)
Components/
    CharacterStats.cs           (new — ECS Component with inspectable fields)
    PlayerController.cs         (modified — reads from CharacterStats)
    MeleeAttack.cs              (modified — reads from CharacterStats)
Data/
    CharacterLoader.cs          (new — thin wrapper around Scene.Content + Json)
Systems/
    PlayerManager.cs            (modified — loads and attaches CharacterStats)
```

## Copy-to-output

The JSON files need to be copied to the build output. Add to `.csproj`:

```xml
<ItemGroup>
    <Content Include="Content\Characters\**\*.json">
        <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
</ItemGroup>
```

## What This Doesn't Cover (Yet)

- **Live JSON hot-reload** — could add a file watcher that calls `Json.FromJsonOverwrite()` on the existing `CharacterStats` component. The API exists, just needs a trigger.
- **Per-slot color overrides** — when two players pick the same character. Could add a tint/outline system later.
- **Validation** — bad JSON blows up at load time. Fine for early dev.
- **Body size changes at runtime** — inspector can change `bodyWidth`/`bodyHeight` but the collider/renderer won't resize without extra code. Movement stats work instantly though, which is what you'll tune most.

## Nez APIs Used

| API | Purpose |
|-----|---------|
| `Nez.Persistence.Json.FromJson<T>()` | Deserialize JSON to CharacterStats |
| `Nez.Persistence.Json.FromJsonOverwrite()` | Future: hot-reload stats onto existing component |
| `NezContentManager.LoadJson()` | Load JSON string through scene content pipeline |
| `[Inspectable]` | Expose fields in runtime debug inspector |
| `[Range(min, max)]` | Slider UI in inspector |
| `[JsonExclude]` | Keep computed properties out of serialization |
| `[AfterDecode]` | Future: post-load initialization if needed |
| `Entity.GetComponent<CharacterStats>()` | Sibling components query stats from their entity |
