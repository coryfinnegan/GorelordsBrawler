# Character Select Screen + Second Character — Proposal

## Status: Implemented

## Goal

Add a character select scene between the main menu and the arena, and implement a second playable character to prove the roster system works end-to-end. This turns the game from a tech demo into something that feels like a real brawler.

## Overview

### What We're Building

1. **CharacterSelectScene** — A new scene where players join with their input device, pick a character, ready up, and launch into the arena
2. **Second Character (Doc Marauder)** — A ranged/projectile character to contrast with Trollborg's melee-only kit
3. **Wiring changes** — Main menu PLAY button goes to character select instead of arena; arena receives character choices instead of hardcoding Trollborg

### Nez Features We're Leveraging

| Feature | Where | Why |
|---------|-------|-----|
| **GlobalManager** | `MatchSetupManager` | Cross-scene state that survives scene transitions — the Nez-native answer to "where do character selections live" |
| **SceneTransitions** | Menu → Select → Arena | `FadeTransition` for polished scene navigation instead of hard cuts |
| **ProjectileMover** | Doc Marauder's crossbow | Built-in component that moves entities and checks trigger collisions without stopping on physics |
| **Core.Schedule()** | Projectile lifetime | Timer system for auto-destroying projectiles after N seconds |
| **UI Table/Stage** | Character select layout | Nez's Scene2D-based widget system for the selection grid |

---

## Part 1: Cross-Scene State — MatchSetupManager

### Why GlobalManager

Currently `LoadScene` uses `Activator.CreateInstance` by type name — no way to pass constructor args. A static class would work but lives outside the ECS entirely. Nez's `GlobalManager` is the framework's first-class solution for state that persists across scenes. It lives on `Core`, survives scene transitions, and is discoverable via `Core.GetGlobalManager<T>()`.

### Design

```csharp
public class MatchSetupManager : GlobalManager
{
	public List<PlayerSelection> Selections { get; } = new();

	public void Clear() => Selections.Clear();
}

public class PlayerSelection
{
	public int SlotIndex;            // 0-3
	public InputDeviceType Device;   // Which input device
	public string CharacterType;     // "Trollborg", "DocMarauder"
}

public enum InputDeviceType
{
	KeyboardWASD,
	KeyboardArrows,
	Gamepad0,
	Gamepad1,
	Gamepad2,
	Gamepad3,
}
```

### Registration

Registered once in `GorelordsBrawlerGame.Initialize()`:

```csharp
Core.RegisterGlobalManager(new MatchSetupManager());
```

### Usage

```csharp
// CharacterSelectScene writes:
var setup = Core.GetGlobalManager<MatchSetupManager>();
setup.Clear();
setup.Selections.Add(new PlayerSelection { ... });

// ArenaScene reads:
var setup = Core.GetGlobalManager<MatchSetupManager>();
foreach (var selection in setup.Selections) { ... }
```

### Files

- **`Systems/MatchSetupManager.cs`** — GlobalManager + PlayerSelection + InputDeviceType

### Changes to Existing Files

- **`GorelordsBrawlerGame.cs`** — Register `MatchSetupManager` in `Initialize()`
- **`Input/InputProfileFactory.cs`** — Add `CreateFromDevice(InputDeviceType)` method

---

## Part 2: Character Select Scene

### Scene Transitions

Instead of the current hard-cut `Activator.CreateInstance` approach, use Nez's built-in `FadeTransition` for all navigation from this scene:

```csharp
// Navigate to arena with fade
Core.StartSceneTransition(new FadeTransition(() => new ArenaScene()));

// Navigate back to menu
Core.StartSceneTransition(new FadeTransition(() => new MainMenuScene()));
```

We should also update MainMenuScene's PLAY button to use a transition:

```csharp
playButton.OnClicked += x =>
	Core.StartSceneTransition(new FadeTransition(() => new CharacterSelectScene()));
```

### Player Join Flow

The scene starts with no players joined. Each input slot has a "Press [key/button] to join" prompt.

**Join inputs:**
- Slot 0 (WASD): Press W or F to join
- Slot 1 (Arrows): Press Up or RightCtrl to join
- Slots 2-5 (Gamepads): Press A or Start to join

Once joined, the slot shows the player's character selection panel. Minimum 2 players required to start.

**Input detection for join:** We poll raw `Nez.Input` (keyboard/gamepad state) before creating InputProfiles. Once a player joins, we create their `InputProfile` via `InputProfileFactory.CreateFromDevice()` and use it for navigation.

### Per-Player Panel

Each joined player gets a column in a Nez UI `Table` showing:
- **Player label** ("P1", "P2", etc.)
- **Character preview** — colored rectangle matching the character's body color/size
- **Character name** below the preview
- **Left/Right navigation** — MoveX input cycles through available characters
- **Ready indicator** — Attack button to ready/un-ready, panel label changes color (green = ready)

### Start Match

When all joined players are ready (minimum 2), a short countdown auto-starts (2 seconds). If anyone un-readies during countdown, it cancels. When countdown completes:

1. Write selections to `MatchSetupManager`
2. Transition to arena: `Core.StartSceneTransition(new FadeTransition(() => new ArenaScene()))`

### Leave/Back

- A joined player who is NOT ready can press Jump to leave (unjoin)
- If no players are joined, pressing ESC/B-button transitions back to MainMenuScene

### Available Characters List

A simple static list for now — the scene reads available character types from `GameConstants.Characters`:

```csharp
public static class Characters
{
	public const string Trollborg = "Trollborg";
	public const string DocMarauder = "DocMarauder";

	public static readonly string[] All = { Trollborg, DocMarauder };
}
```

Each panel cycles through this array.

### Scene Layout (UI)

Built with Nez `Table` layout on a `UICanvas`:

```
┌──────────────────────────────────────────────────────┐
│                  CHARACTER SELECT                     │
│                                                      │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────┐│
│  │    P1    │  │    P2    │  │   Press   │  │Press ││
│  │ ┌──────┐ │  │ ┌──────┐ │  │  Up or   │  │ A to ││
│  │ │      │ │  │ │      │ │  │  RCtrl   │  │ join ││
│  │ │ char │ │  │ │ char │ │  │ to join  │  │      ││
│  │ │      │ │  │ │      │ │  │          │  │      ││
│  │ └──────┘ │  │ └──────┘ │  │          │  │      ││
│  │ < NAME > │  │ < NAME > │  │          │  │      ││
│  │  READY   │  │          │  │          │  │      ││
│  └──────────┘  └──────────┘  └──────────┘  └──────┘│
│                                                      │
│              All players ready to start!              │
└──────────────────────────────────────────────────────┘
```

### Character Preview Rendering

For each joined player's panel, we need to show what the character looks like. Two approaches:

**Option A — UI-only with colored Label/Image (simple)**
A colored rectangle in the UI table using `PrimitiveDrawable` sized to the character's body dimensions. Quick, but can't show the actual entity with components.

**Option B — Spawn preview entities in world space (recommended)**
Create actual character entities (without abilities/physics) positioned behind each panel. This lets us reuse `PrototypeSpriteRenderer` and later swap in real sprites. A separate world-space renderer layer shows them behind the UI.

### Files

- **`Scenes/CharacterSelectScene.cs`** — The new scene (UI + join logic + preview entities)
- **`Constants/GameConstants.cs`** — New constants: scene name, UI strings, join prompts, character list

### Changes to Existing Files

- **`Scenes/MainMenuScene.cs`** — PLAY button uses `FadeTransition` to `CharacterSelectScene`
- **`Scenes/ArenaScene.cs`** — Read from `MatchSetupManager` instead of hardcoding players
- **`GorelordsBrawlerGame.cs`** — Consider updating `LoadScene()` to use `FadeTransition` as default, or deprecate in favor of direct `Core.StartSceneTransition` calls

---

## Part 3: Modular Stats Refactor

### Problem

Currently `CharacterStats` is a monolithic component holding every stat for every ability type. Adding projectile fields for Doc Marauder would mean Trollborg carries unused projectile fields, and every future ability type bloats the shared class further. This isn't modular and doesn't leverage the EC pattern.

### Solution — Ability-Specific Stat Components

Split stats into composable components, one per concern:

- **`CharacterStats`** — Identity + body only (name, description, bodyWidth, bodyHeight, color, maxHp)
- **`MovementStats`** — Component with moveSpeed, jumpSpeed, gravity
- **`MeleeStats`** — Component with melee-specific stats (damage, knockback, hitbox, cooldown, duration)
- **`ProjectileStats`** — Component with projectile-specific stats (speed, damage, knockback, dimensions, lifetime, cooldown)

Each ability reads from its own sibling stat component instead of the monolithic `CharacterStats`:
- `WalkAbility` reads `MovementStats` (moveSpeed)
- `JumpAbility` reads `MovementStats` (jumpSpeed)
- `PhysicsBody` reads `MovementStats` (gravity)
- `MeleeAttack` reads `MeleeStats`
- `ProjectileAttack` reads `ProjectileStats`

### Nested JSON Format

Nez.Persistence's `Json.FromJson<T>()` fully supports nested object deserialization. Missing keys stay null (reference type default), no special attributes needed for public fields.

**Trollborg.json** (melee character — no `projectile` section):
```json
{
	"name": "Trollborg",
	"description": "Super heavy, slow, high armor, melee only. Simple for people who are simple.",
	"maxHp": 150,
	"bodyWidth": 32,
	"bodyHeight": 48,
	"colorR": 50,
	"colorG": 120,
	"colorB": 50,
	"movement": {
		"moveSpeed": 100,
		"jumpSpeed": 250,
		"gravity": 900
	},
	"melee": {
		"damage": 25,
		"knockbackForce": 250,
		"knockbackAngleX": 1.0,
		"knockbackAngleY": -0.3,
		"hitboxWidth": 40,
		"hitboxHeight": 30,
		"hitboxOffsetX": 30,
		"cooldown": 0.5,
		"hitboxDuration": 0.15
	}
}
```

**DocMarauder.json** (ranged character — no `melee` section):
```json
{
	"name": "DocMarauder",
	"description": "Fast, fragile ranged fighter. Keep your distance or lose your head.",
	"maxHp": 100,
	"bodyWidth": 28,
	"bodyHeight": 44,
	"colorR": 140,
	"colorG": 40,
	"colorB": 40,
	"movement": {
		"moveSpeed": 140,
		"jumpSpeed": 280,
		"gravity": 900
	},
	"projectile": {
		"speed": 400,
		"width": 12,
		"height": 6,
		"damage": 15,
		"knockbackForce": 200,
		"knockbackAngleX": 1.0,
		"knockbackAngleY": -0.2,
		"maxLifetime": 1.5,
		"cooldown": 0.7
	}
}
```

A future character with both melee AND ranged would simply have both `"melee"` and `"projectile"` sections.

### Deserialization — CharacterData Intermediate Class

`CharacterLoader` deserializes to a `CharacterData` class (plain data object, NOT a Component), which holds the nested stat objects. `CharacterFactory` then unpacks the pieces into separate components.

```csharp
// Deserialization target — NOT a Component, just a data bag
public class CharacterData
{
	public string name;
	public string description;
	public int maxHp = 100;
	public float bodyWidth = 32f;
	public float bodyHeight = 48f;
	public int colorR = 128;
	public int colorG = 128;
	public int colorB = 128;
	public MovementStats movement;     // always present
	public MeleeStats melee;           // null if character has no melee
	public ProjectileStats projectile; // null if character has no projectile
}
```

### Stat Components

**`MovementStats`** (always present on every character):
```csharp
public class MovementStats : Component
{
	[Inspectable] [Range(0, 500)]
	public float moveSpeed = 100f;

	[Inspectable] [Range(0, 600)]
	public float jumpSpeed = 250f;

	[Inspectable] [Range(0, 2000)]
	public float gravity = 900f;
}
```

**`MeleeStats`** (only on melee characters):
```csharp
public class MeleeStats : Component
{
	[Inspectable] [Range(0, 100)]
	public int damage = 20;

	[Inspectable] [Range(0, 1000)]
	public float knockbackForce = 300f;

	[Inspectable] [Range(-1, 1)]
	public float knockbackAngleX = 1f;

	[Inspectable] [Range(-1, 1)]
	public float knockbackAngleY = -0.5f;

	[JsonExclude]
	public Vector2 KnockbackAngle => new Vector2(knockbackAngleX, knockbackAngleY);

	[Inspectable] [Range(0, 100)]
	public float hitboxWidth = 40f;

	[Inspectable] [Range(0, 100)]
	public float hitboxHeight = 30f;

	[Inspectable] [Range(0, 100)]
	public float hitboxOffsetX = 30f;

	[Inspectable] [Range(0, 2)]
	public float cooldown = 0.5f;

	[Inspectable] [Range(0, 1)]
	public float hitboxDuration = 0.15f;
}
```

**`ProjectileStats`** (only on ranged characters):
```csharp
public class ProjectileStats : Component
{
	[Inspectable] [Range(0, 800)]
	public float speed = 400f;

	[Inspectable] [Range(0, 50)]
	public float width = 12f;

	[Inspectable] [Range(0, 50)]
	public float height = 6f;

	[Inspectable] [Range(0, 100)]
	public int damage = 15;

	[Inspectable] [Range(0, 1000)]
	public float knockbackForce = 200f;

	[Inspectable] [Range(-1, 1)]
	public float knockbackAngleX = 1f;

	[Inspectable] [Range(-1, 1)]
	public float knockbackAngleY = -0.2f;

	[JsonExclude]
	public Vector2 KnockbackAngle => new Vector2(knockbackAngleX, knockbackAngleY);

	[Inspectable] [Range(0, 5)]
	public float maxLifetime = 1.5f;

	[Inspectable] [Range(0, 2)]
	public float cooldown = 0.7f;
}
```

### Slim CharacterStats

`CharacterStats` shrinks to identity + body only. It no longer holds any ability-specific fields:

```csharp
public class CharacterStats : Component
{
	[NotInspectable]
	public string name;

	[NotInspectable]
	public string description;

	[Inspectable] [Range(0, 500)]
	public int maxHp = 100;

	[Inspectable] [Range(8, 128)]
	public float bodyWidth = 32f;

	[Inspectable] [Range(8, 128)]
	public float bodyHeight = 48f;

	[Inspectable] [Range(0, 255)]
	public int colorR = 128;

	[Inspectable] [Range(0, 255)]
	public int colorG = 128;

	[Inspectable] [Range(0, 255)]
	public int colorB = 128;

	[JsonExclude]
	public Color BodyColor => new Color(colorR, colorG, colorB);
}
```

### Data-Driven CharacterFactory

The biggest win: `CharacterFactory` no longer needs a switch statement per character. Ability attachment is driven by what the JSON provides:

```csharp
public static Entity Create(Scene scene, string characterType, InputProfile input, Vector2 spawnPosition)
{
	var data = CharacterLoader.Load(scene, characterType);

	var entity = scene.CreateEntity(characterType);
	entity.Transform.Position = spawnPosition;

	// Identity + body (always present)
	var stats = new CharacterStats
	{
		name = data.name,
		description = data.description,
		maxHp = data.maxHp,
		bodyWidth = data.bodyWidth,
		bodyHeight = data.bodyHeight,
		colorR = data.colorR,
		colorG = data.colorG,
		colorB = data.colorB,
	};
	entity.AddComponent(stats);

	// Renderer + physics collider
	var renderer = entity.AddComponent(new PrototypeSpriteRenderer(stats.bodyWidth, stats.bodyHeight));
	renderer.SetColor(stats.BodyColor);

	var collider = entity.AddComponent(new BoxCollider(stats.bodyWidth, stats.bodyHeight));
	collider.PhysicsLayer = PhysicsLayers.Player;
	collider.CollidesWithLayers = PhysicsLayers.Platforms;

	entity.AddComponent(new Mover());

	// Movement (always present)
	entity.AddComponent(data.movement);
	entity.AddComponent(new PhysicsBody());
	entity.AddComponent(new WalkAbility(input));
	entity.AddComponent(new JumpAbility(input));

	// Hurtbox + Health (always present)
	var hurtboxCollider = entity.AddComponent(new BoxCollider(stats.bodyWidth, stats.bodyHeight));
	hurtboxCollider.PhysicsLayer = PhysicsLayers.Hurtbox;
	hurtboxCollider.CollidesWithLayers = PhysicsLayers.Hitbox;
	hurtboxCollider.IsTrigger = true;

	entity.AddComponent(new Health { MaxHp = stats.maxHp, CurrentHp = stats.maxHp });
	entity.AddComponent(new Hurtbox());
	entity.AddComponent(new HealthBar());
	entity.AddComponent(new RespawnHandler(spawnPosition));

	// Data-driven abilities — attach based on what the JSON provides
	if (data.melee != null)
	{
		entity.AddComponent(data.melee);
		entity.AddComponent(new MeleeAttack(input));
	}

	if (data.projectile != null)
	{
		entity.AddComponent(data.projectile);
		entity.AddComponent(new ProjectileAttack(input));
	}

	return entity;
}
```

No switch statement. Adding a new character type is now **purely data**: create a JSON file, define which ability sections it has, done. Adding a new ability type means adding a new stat component + ability component + a null-check in the factory.

### CharacterLoader Changes

`CharacterLoader` now returns `CharacterData` instead of `CharacterStats`:

```csharp
public static CharacterData Load(Scene scene, string characterType)
{
	var path = GameConstants.ContentPaths.CharactersFolder + characterType + GameConstants.ContentPaths.JsonExtension;
	var jsonString = scene.Content.LoadJson(path);
	return Json.FromJson<CharacterData>(jsonString);
}
```

### Ability Refactors

Each ability reads from its own stat component:

**`WalkAbility`** — `Entity.GetComponent<MovementStats>()` instead of `Entity.GetComponent<CharacterStats>()`
**`JumpAbility`** — `Entity.GetComponent<MovementStats>()` instead of `Entity.GetComponent<CharacterStats>()`
**`PhysicsBody`** — `Entity.GetComponent<MovementStats>()` for gravity
**`MeleeAttack`** — `Entity.GetComponent<MeleeStats>()` instead of `Entity.GetComponent<CharacterStats>()`

### Files (New)

- **`Data/CharacterData.cs`** — Deserialization target with nested stat objects
- **`Components/Stats/MovementStats.cs`** — Movement stat component
- **`Components/Stats/MeleeStats.cs`** — Melee stat component
- **`Components/Stats/ProjectileStats.cs`** — Projectile stat component

### Files (Modified)

- **`Components/CharacterStats.cs`** — Slimmed down to identity + body only
- **`Data/CharacterLoader.cs`** — Returns `CharacterData` instead of `CharacterStats`
- **`Data/CharacterFactory.cs`** — Data-driven ability attachment, no switch statement
- **`Components/Abilities/WalkAbility.cs`** — Reads `MovementStats`
- **`Components/Abilities/JumpAbility.cs`** — Reads `MovementStats`
- **`Components/PhysicsBody.cs`** — Reads `MovementStats` for gravity
- **`Components/MeleeAttack.cs`** — Reads `MeleeStats`
- **`Content/Characters/Trollborg.json`** — Restructured to nested format

---

## Part 4: Second Character — Doc Marauder

### Character Identity

Doc Marauder is a ranged attacker — lighter, faster, less HP, but has a crossbow projectile. Contrasts with Trollborg's tanky melee style. Faster movement (140 vs 100), higher jump (280 vs 250), less HP (100 vs 150), smaller body, dark red color.

### JSON

See `DocMarauder.json` in Part 3 above. The JSON defines a `movement` + `projectile` section (no `melee`), so CharacterFactory automatically attaches `ProjectileAttack` without `MeleeAttack`.

### Projectile Ability — Using Nez's ProjectileMover

Nez has a built-in `ProjectileMover` component (`Nez.Portable/ECS/Components/Physics/ProjectileMover.cs`) designed exactly for this. It:
- Moves entities through space each frame
- Checks for trigger collisions via `Physics.BoxcastBroadphase`
- Notifies `ITriggerListener` components on both the projectile and the hit entity
- Always moves the full distance (never stops on physics) — caller decides what to do on impact
- Returns `bool` from `Move()` indicating if a collision occurred

**`ProjectileAttack`** (`Components/Abilities/ProjectileAttack.cs`) — Ability component on the character entity:
- On Attack input press (with cooldown from `ProjectileStats`): spawn a projectile entity
- Reads stats from sibling `ProjectileStats` component
- Spawns projectile at character position + offset in facing direction

**`Projectile`** (`Components/Projectile.cs`) — Component on the spawned projectile entity, implements `IUpdatable`:
- Holds reference to its `ProjectileMover` sibling
- Each frame: calls `_mover.Move(velocity * Time.DeltaTime)`
- If `Move()` returns true (hit something), destroy the entity
- Uses `Core.Schedule()` for max lifetime auto-destroy as a safety net:

```csharp
public override void OnAddedToEntity()
{
	_mover = Entity.GetComponent<ProjectileMover>();
	Core.Schedule(_maxLifetime, timer => {
		if (Entity != null && !Entity.IsDestroyed)
			Entity.Destroy();
	});
}

void IUpdatable.Update()
{
	if (_mover.Move(_velocity * Time.DeltaTime))
		Entity.Destroy(); // Hit something
}
```

### Projectile Entity Structure

Each projectile entity spawned by `ProjectileAttack`:

```
Entity "projectile"
├── PrototypeSpriteRenderer (width x height from ProjectileStats, character color)
├── BoxCollider (width x height from ProjectileStats)
│   ├── PhysicsLayer = Hitbox
│   └── CollidesWithLayers = Hurtbox
├── ProjectileMover (Nez built-in — handles movement + trigger detection)
├── AttackData (damage, knockback force, knockback angle, facing)
└── Projectile (custom — drives ProjectileMover each frame, handles lifetime)
```

When the projectile's `BoxCollider` overlaps a `Hurtbox`, `ProjectileMover` notifies the target's `ITriggerListener` (our existing `Hurtbox` component). The existing damage pipeline (Hurtbox → Health → RespawnHandler) handles the rest with zero changes.

### Files (New)

- **`Content/Characters/DocMarauder.json`** — Stats file (see Part 3)
- **`Components/Abilities/ProjectileAttack.cs`** — Projectile firing ability
- **`Components/Projectile.cs`** — Projectile entity behavior (drives ProjectileMover, lifetime)
- **`Constants/GameConstants.cs`** — Add `DocMarauder` character constant

---

## Part 4: Wiring / Integration

### Menu Flow Change

All scene transitions use Nez's `FadeTransition`:

```
MainMenu ──FadeTransition──→ CharacterSelect ──FadeTransition──→ Arena
                                    ↑                               │
                                    ├── "Rematch" ──────────────────┘
                                    └── "Main Menu" → MainMenu
```

- **Rematch** goes back to character select (players can re-pick or jump straight back in)
- **Main Menu** goes back to main menu

### ArenaScene Refactor

Replace hardcoded players with MatchSetupManager-driven setup:

```csharp
public ArenaScene()
{
	// ... renderers, pause manager ...
	var playerManager = AddSceneComponent(new PlayerManager());
	var setup = Core.GetGlobalManager<MatchSetupManager>();

	foreach (var selection in setup.Selections)
	{
		var input = InputProfileFactory.CreateFromDevice(selection.Device);
		var spawn = GameConstants.Arena.SpawnPositions[selection.SlotIndex];
		playerManager.AddPlayer(selection.SlotIndex, input, selection.CharacterType, spawn);
	}

	// ... camera, ruleset, match manager ...
}
```

### LoadScene Replacement

Remove `GorelordsBrawlerGame.LoadScene(string)` entirely. All scene navigation uses `Core.StartSceneTransition` with `FadeTransition`. Add a convenience helper:

```csharp
public static void TransitionToScene<T>() where T : Scene, new()
{
	Core.StartSceneTransition(new FadeTransition(() => new T()));
}
```

All existing `LoadScene` call sites migrate to `TransitionToScene<T>()`.

### Spawn Positions

Expand from 2 fixed positions to an array supporting 2-4 players:

```csharp
public static class Arena
{
	public static readonly Vector2[] SpawnPositions = new[]
	{
		new Vector2(200, 500),  // Slot 0
		new Vector2(600, 500),  // Slot 1
		new Vector2(350, 500),  // Slot 2
		new Vector2(450, 500),  // Slot 3
	};
}
```

---

## Implementation Order

### Phase A: Stats Refactor (must land first — existing features depend on it)

1. **Stat components** — Create `MovementStats`, `MeleeStats`, `ProjectileStats` in `Components/Stats/`
2. **CharacterData** — Create deserialization class in `Data/CharacterData.cs`
3. **Slim CharacterStats** — Remove ability fields, keep identity + body only
4. **CharacterLoader** — Return `CharacterData` instead of `CharacterStats`
5. **CharacterFactory** — Data-driven ability attachment, remove switch statement
6. **Ability refactors** — Update `WalkAbility`, `JumpAbility`, `PhysicsBody`, `MeleeAttack` to read from their stat components
7. **Trollborg.json** — Restructure to nested format
8. **Verify** — Existing gameplay works identically with refactored stats

### Phase B: Character Select + Scene Navigation

9. **MatchSetupManager** — GlobalManager + PlayerSelection + InputDeviceType, register in game init
10. **InputProfileFactory.CreateFromDevice()** — New factory method
11. **CharacterSelectScene** — Join flow, character cycling, ready up
12. **ArenaScene refactor** — Read from MatchSetupManager, spawn position array
13. **Scene transitions** — FadeTransition for all scene navigation (MainMenu → Select → Arena)
14. **GameConstants updates** — Scene name, UI strings, character list, spawn positions

### Phase C: Doc Marauder

15. **DocMarauder.json** — Stats file with `movement` + `projectile` sections
16. **ProjectileAttack** — Ability component reading `ProjectileStats`
17. **Projectile** — Entity behavior component using Nez `ProjectileMover` + `Core.Schedule()`
18. **Verify** — Doc Marauder plays correctly, projectiles use existing damage pipeline

---

## Resolved Decisions

1. **Mirror match handling** — Tint overlay per player slot to differentiate same-character picks.
2. **Rematch flow** — Rematch goes back to character select (not directly to arena).
3. **Character select input** — Create InputProfiles on join, reuse for panel navigation, pass through to arena via MatchSetupManager.
4. **Max projectiles** — Cooldown-gated only, no cap on simultaneous projectiles.
5. **LoadScene migration** — Replace all `GorelordsBrawlerGame.LoadScene(string)` calls with `Core.StartSceneTransition`. Remove `LoadScene` method entirely.
