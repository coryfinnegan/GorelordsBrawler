# Player Manager — Implemented

## What Was Built

A player management system with four pieces: input profiles, a character factory, a player manager, and a brawler camera.

### Architecture

```
PlayerManager (SceneComponent)
├── PlayerSlot[] Slots              // 1-4 slots, each with InputProfile + entity ref
├── AddPlayer(slot, input, char)    // delegates to CharacterFactory
├── RemovePlayer(slot)              // deregisters input, destroys entity
└── GetActivePlayers()              // for camera tracking

CharacterFactory (static)
├── Create(scene, type, input, pos) // loads stats, builds entity, attaches abilities
└── AttachAbilities(entity, type)   // per-character ability selection

InputProfile (plain class)
├── VirtualIntegerAxis MoveX
├── VirtualButton Jump
├── VirtualButton Attack
└── Deregister()

InputProfileFactory (static)
├── CreateKeyboardWASD()
├── CreateKeyboardArrows()
└── CreateGamepad(int gamepadIndex)
```

### InputProfile

Owns one player's virtual inputs. Ability components read from it — they don't create their own inputs and don't know whether the player is on keyboard or gamepad.

**File:** `Input/InputProfile.cs`

### InputProfileFactory

Static methods that build profiles for each supported input device. All key/button bindings live here.

- `CreateKeyboardWASD()` — A/D move, W jump, F attack
- `CreateKeyboardArrows()` — Left/Right move, Up jump, RightControl attack
- `CreateGamepad(int index)` — Left stick + D-pad move, A jump, X attack

**File:** `Input/InputProfileFactory.cs`

### CharacterFactory

Static `Create()` method assembles a complete player entity:

1. Loads `CharacterStats` from JSON via `CharacterLoader`
2. Creates entity with renderer, collider, mover sized/colored from stats
3. Attaches `PhysicsBody` (shared gravity, collision, grounded state)
4. Calls `AttachAbilities()` which switches on character type to add the right ability mix

Adding a new character:
1. Create `Content/Characters/NewCharacter.json`
2. Add constant to `GameConstants.Characters`
3. Add case in `CharacterFactory.AttachAbilities()` with the ability set

**File:** `Data/CharacterFactory.cs`

### PlayerManager

A `SceneComponent` that owns 4 player slots. Delegates entity creation to `CharacterFactory`, manages lifecycle (input deregistration, entity destruction).

**File:** `Systems/PlayerManager.cs`

### PlayerSlot

Ties together slot index, input profile, entity reference, and character type string.

**File:** `Systems/PlayerSlot.cs`

### Component Architecture

```
Entity (player)
├── CharacterStats          // loaded from JSON, inspectable at runtime
├── PrototypeSpriteRenderer  // sized from stats.bodyWidth/bodyHeight
├── BoxCollider              // sized from stats
├── Mover                    // Nez collision-aware movement
├── PhysicsBody              // gravity, collision, grounded (UpdateOrder=100)
├── WalkAbility              // reads input → sets Velocity.X
├── JumpAbility              // reads input → sets Velocity.Y when grounded
└── MeleeAttack              // reads input → spawns hitbox entity
```

Abilities run at default `UpdateOrder` (0), writing to `PhysicsBody.Velocity`. `PhysicsBody` runs at `UpdateOrder=100`, applying gravity and resolving collisions after all abilities have set their velocity.

### How ArenaScene Uses It

```csharp
var playerManager = AddSceneComponent(new PlayerManager());

var p1 = playerManager.AddPlayer(0,
    InputProfileFactory.CreateKeyboardWASD(),
    GameConstants.Characters.Trollborg, new Vector2(300, 500));

var p2 = playerManager.AddPlayer(1,
    InputProfileFactory.CreateKeyboardArrows(),
    GameConstants.Characters.Trollborg, new Vector2(500, 500));
```

### Nez Input Details

- `Input.MaxSupportedGamePads` defaults to 1. Set to 4 in `GorelordsBrawlerGame.Initialize()`.
- `Input.GamePads[index]` is a `GamePadData[]` array with `IsConnected()`, button/stick/trigger state, and rumble.
- `Input.Emitter` fires `GamePadConnected`/`GamePadDisconnected` events with gamepad index — useful for hot-plug join.
- All virtual input `Add*` methods take a `gamepadIndex` parameter for per-controller binding.
- Virtual inputs auto-register globally and must be `Deregister()`'d on cleanup.

### Future: Gamepad Hot-Join

1. Listen for `Input.Emitter` `GamePadConnected` events.
2. Find the first empty slot.
3. Call `playerManager.AddPlayer(slot, InputProfileFactory.CreateGamepad(gamepadIndex), ...)`.
4. Add the new entity to `BrawlerCamera` targets.

A character select screen would populate slots with chosen character types and input profiles, then pass them to the arena.

### File Layout

```
Components/
    PhysicsBody.cs            // shared physics simulation
    CharacterStats.cs         // data component, loaded from JSON
    BrawlerCamera.cs          // frames all players
    Abilities/
        WalkAbility.cs        // horizontal movement
        JumpAbility.cs        // jump
    MeleeAttack.cs            // attack hitbox spawning
Input/
    InputProfile.cs           // per-player input bindings
    InputProfileFactory.cs    // factory methods for keyboard/gamepad
Data/
    CharacterLoader.cs        // JSON loading via Nez content pipeline
    CharacterFactory.cs       // entity assembly per character type
Systems/
    PlayerManager.cs          // slot management, lifecycle
    PlayerSlot.cs             // slot data
Content/
    Characters/
        Trollborg.json        // character stats
```

### Reference

- Nez input system: `Nez/Nez.Portable/Input/Input.cs`, `VirtualButton.cs`, `VirtualIntegerAxis.cs`
- Nez Samples platformer: [Caveman.cs](https://github.com/prime31/Nez-Samples/blob/master/Nez.Samples/Scenes/Samples/Platformer/Caveman.cs)
- Nez `SceneComponent`: scene-level logic without needing an entity
