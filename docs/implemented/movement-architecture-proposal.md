# Movement Architecture Proposal

## Problem

`PlayerController` bakes physics simulation and movement intent into one monolithic component:

```csharp
_velocity.X = moveDir * _stats.moveSpeed;      // horizontal walk
_velocity.Y += _stats.gravity * Time.DeltaTime; // gravity
if (_grounded && _input.Jump.IsPressed)          // jump
    _velocity.Y = -_stats.jumpSpeed;
```

Every character moves the same way: walk left/right, jump. But the roster has characters with fundamentally different movement:

- **Trollborg** — basic walk + jump (what we have now)
- A character with a **temporal shift** — teleport/phase through space
- A character with a **jump-lunge attack** — launch forward in an arc, combining movement and damage
- Future characters could have dashes, wall jumps, flight, grappling, etc.

None of these fit into the current `velocity.X = moveDir * speed` model. We need to separate **what is universal** (gravity, collision resolution, grounded detection) from **what varies per character** (how input translates to movement).

## Core Insight

There are two distinct concerns mashed together:

1. **Physics Body** — gravity, collision resolution, grounded state, velocity. Universal. Every character has mass and collides with platforms.
2. **Abilities** — how input maps to velocity changes, teleports, lunges. Per-character. These are the things that make Trollborg feel different from Razorfang.

The physics body should be a shared component. Abilities should be individual components that *write to* the physics body. Each character gets a different set of ability components.

## Architecture

```
PhysicsBody (Component, IUpdatable)
├── Velocity (Vector2, public)
├── Grounded (bool, public)
├── FacingDirection (int, public)
├── Applies gravity
├── Calls Mover.Move()
├── Resolves collisions
└── Updates grounded state

Ability Components (each is its own Component, IUpdatable)
├── WalkAbility         — reads input, sets Velocity.X
├── JumpAbility         — reads input, sets Velocity.Y when grounded
├── MeleeAttack         — already exists, stays as-is
├── TemporalShift       — teleports entity position on input
├── LungeAttack         — overrides velocity with an arc trajectory
└── etc.
```

### PhysicsBody

Owns the shared state that abilities read from and write to. Runs **after** abilities (via `UpdateOrder`) to apply the final velocity.

```csharp
public class PhysicsBody : Component, IUpdatable
{
    public Vector2 Velocity;
    public bool Grounded;
    public int FacingDirection = 1; // 1 = right, -1 = left

    private Mover _mover;
    private CharacterStats _stats;

    public override void OnAddedToEntity()
    {
        _mover = Entity.GetComponent<Mover>();
        _stats = Entity.GetComponent<CharacterStats>();
        UpdateOrder = 100; // run after abilities
    }

    public void Update()
    {
        // Gravity is universal
        Velocity.Y += _stats.gravity * Time.DeltaTime;

        // Move with collision
        var motion = Velocity * Time.DeltaTime;
        var collided = _mover.Move(motion, out var collisionResult);

        if (collided)
        {
            if (collisionResult.Normal.Y < -0.5f)
            {
                Grounded = true;
                Velocity.Y = 0;
            }
            if (collisionResult.Normal.Y > 0.5f)
                Velocity.Y = 0;
        }
        else
        {
            Grounded = false;
        }

        // Flip sprite
        if (FacingDirection != 0)
        {
            var renderer = Entity.GetComponent<PrototypeSpriteRenderer>();
            if (renderer != null)
                renderer.FlipX = FacingDirection < 0;
        }
    }
}
```

### Ability Components

Each ability is a small, focused component. It reads input, reads `PhysicsBody` state, and writes to `PhysicsBody.Velocity` (or directly to `Transform.Position` for teleports).

**WalkAbility** — the simplest ability. Every character that walks has this.

```csharp
public class WalkAbility : Component, IUpdatable
{
    private InputProfile _input;
    private PhysicsBody _body;
    private CharacterStats _stats;

    public WalkAbility(InputProfile input) { _input = input; }

    public override void OnAddedToEntity()
    {
        _body = Entity.GetComponent<PhysicsBody>();
        _stats = Entity.GetComponent<CharacterStats>();
    }

    public void Update()
    {
        var moveDir = _input.MoveX.Value;
        _body.Velocity.X = moveDir * _stats.moveSpeed;
        if (moveDir != 0)
            _body.FacingDirection = moveDir;
    }
}
```

**JumpAbility** — basic jump. Trollborg has this. A flying character wouldn't.

```csharp
public class JumpAbility : Component, IUpdatable
{
    private InputProfile _input;
    private PhysicsBody _body;
    private CharacterStats _stats;

    public JumpAbility(InputProfile input) { _input = input; }

    public override void OnAddedToEntity()
    {
        _body = Entity.GetComponent<PhysicsBody>();
        _stats = Entity.GetComponent<CharacterStats>();
    }

    public void Update()
    {
        if (_body.Grounded && _input.Jump.IsPressed)
        {
            _body.Velocity.Y = -_stats.jumpSpeed;
            _body.Grounded = false;
            _input.Jump.ConsumeBuffer();
        }
    }
}
```

**TemporalShift** — teleport a fixed distance in the facing direction. Completely different from walk/jump — it doesn't touch velocity at all.

```csharp
public class TemporalShift : Component, IUpdatable
{
    private InputProfile _input;
    private PhysicsBody _body;
    private float _shiftDistance = 150f; // from CharacterStats later
    private float _cooldown = 2f;
    private float _cooldownTimer;

    public TemporalShift(InputProfile input) { _input = input; }

    public override void OnAddedToEntity()
    {
        _body = Entity.GetComponent<PhysicsBody>();
    }

    public void Update()
    {
        _cooldownTimer -= Time.DeltaTime;
        if (_input.Attack.IsPressed && _cooldownTimer <= 0)
        {
            // Teleport in facing direction
            Entity.Transform.Position += new Vector2(
                _shiftDistance * _body.FacingDirection, 0);
            _cooldownTimer = _cooldown;
        }
    }
}
```

**LungeAttack** — override velocity with a forward arc. Takes over movement for a duration.

```csharp
public class LungeAttack : Component, IUpdatable
{
    private InputProfile _input;
    private PhysicsBody _body;
    private bool _lunging;
    private float _lungeTimer;
    private float _lungeDuration = 0.3f;
    private float _lungeSpeedX = 400f;
    private float _lungeSpeedY = -200f;

    public LungeAttack(InputProfile input) { _input = input; }

    public override void OnAddedToEntity()
    {
        _body = Entity.GetComponent<PhysicsBody>();
    }

    public void Update()
    {
        if (_lunging)
        {
            _lungeTimer -= Time.DeltaTime;
            // Override horizontal velocity during lunge
            _body.Velocity.X = _lungeSpeedX * _body.FacingDirection;
            if (_lungeTimer <= 0)
                _lunging = false;
        }
        else if (_input.Attack.IsPressed && _body.Grounded)
        {
            _lunging = true;
            _lungeTimer = _lungeDuration;
            _body.Velocity.Y = _lungeSpeedY;
            _body.Grounded = false;
        }
    }
}
```

### Update Order

Abilities run before `PhysicsBody` so they set velocity, then physics applies it:

```
UpdateOrder 0 (default): WalkAbility, JumpAbility, LungeAttack, etc.
UpdateOrder 100:          PhysicsBody (applies gravity, moves, resolves collisions)
```

This means abilities can freely write to `Velocity` without worrying about when gravity/collision happens.

### Ability Conflicts

When multiple abilities want to set `Velocity.X`, the last one wins. This is fine when abilities are mutually exclusive (you don't walk during a lunge). For cases where abilities need to coordinate:

- **Abilities can disable each other.** `LungeAttack` sets `WalkAbility.Enabled = false` during a lunge, re-enables when done.
- **Abilities check PhysicsBody state.** `JumpAbility` only fires when `Grounded` is true. `LungeAttack` only fires when grounded. They naturally don't conflict.
- **Priority via UpdateOrder.** If a lunge must override walk, give it a higher `UpdateOrder` so it writes to velocity after walk does.

This is simpler than a central state machine for movement, because most abilities are independent — walk and jump don't conflict, attack and walk don't conflict. Only a few special moves need to suppress others.

### Character Assembly

Each character gets a different set of ability components. `PlayerManager` (or a future character factory) assembles the right set based on `characterType`:

```csharp
// In PlayerManager.AddPlayer or a CharacterFactory:

player.AddComponent(new PhysicsBody());

switch (characterType)
{
    case "Trollborg":
        player.AddComponent(new WalkAbility(input));
        player.AddComponent(new JumpAbility(input));
        player.AddComponent(new MeleeAttack(input));
        break;

    case "Chronofiend":
        player.AddComponent(new WalkAbility(input));
        player.AddComponent(new JumpAbility(input));
        player.AddComponent(new TemporalShift(input));
        break;

    case "Gorestag":
        player.AddComponent(new WalkAbility(input));
        player.AddComponent(new JumpAbility(input));
        player.AddComponent(new LungeAttack(input));
        break;
}
```

Later, the ability list could come from the JSON file too:

```json
{
    "name": "Trollborg",
    "abilities": ["WalkAbility", "JumpAbility", "MeleeAttack"],
    "moveSpeed": 100,
    ...
}
```

But hardcoding the assembly per character in a switch/factory is fine for now — the important thing is that the abilities themselves are reusable components, not that the assembly is data-driven.

### CharacterStats Extension

Each ability type can have its own stats in the JSON. `CharacterStats` grows as we add abilities:

```json
{
    "name": "Gorestag",
    "moveSpeed": 180,
    "jumpSpeed": 300,
    "gravity": 800,
    "lungeDuration": 0.3,
    "lungeSpeedX": 400,
    "lungeSpeedY": -200,
    "shiftDistance": 0,
    "shiftCooldown": 0
}
```

Unused stats (shiftDistance on a character without TemporalShift) just sit at 0 and don't matter. Alternatively, abilities that only one character has could read from a nested object, but flat is simpler for inspector tuning.

### What About Nez's Built-in State Machines?

Nez has two FSM options:

- `SimpleStateMachine<TEnum>` — Component-based, uses naming convention for state methods (`Walking_Enter`, `Walking_Tick`, `Walking_Exit`)
- `StateMachine<T>` — object-oriented with separate `State<T>` classes

These are useful for **individual ability state** (e.g., a lunge that has Windup → Launch → Recovery phases), not for the top-level "which abilities does this character have" question. An ability like `LungeAttack` could use `SimpleStateMachine` internally if its logic gets complex enough to warrant states.

They're not the right tool for character-level movement architecture because:
- States are mutually exclusive — you're in one at a time. But walk + jump + attack happen simultaneously.
- Adding a new ability means adding new states + transitions to every character's state machine. Components are additive — you just attach another one.

### What PlayerController Becomes

`PlayerController` goes away entirely. Its responsibilities split into:

| Current PlayerController code | New home |
|-------------------------------|----------|
| `_velocity.X = moveDir * moveSpeed` | `WalkAbility` |
| `_velocity.Y = -jumpSpeed` | `JumpAbility` |
| `_velocity.Y += gravity * deltaTime` | `PhysicsBody` |
| `Mover.Move()` + collision | `PhysicsBody` |
| Grounded detection | `PhysicsBody` |
| Sprite flip | `PhysicsBody` |
| PlayerState enum | Removed (or derived from PhysicsBody.Velocity if needed) |

### File Layout

```
Components/
    PhysicsBody.cs           (new — shared physics simulation)
    CharacterStats.cs        (unchanged)
    Abilities/
        WalkAbility.cs       (new)
        JumpAbility.cs       (new)
        MeleeAttack.cs       (modified — reads from CharacterStats, already mostly right)
        TemporalShift.cs     (future)
        LungeAttack.cs       (future)
    PlayerController.cs      (deleted)
```

### Migration

1. Create `PhysicsBody` — extract gravity, collision, grounded, sprite flip from `PlayerController`
2. Create `WalkAbility` — extract horizontal movement
3. Create `JumpAbility` — extract jump logic
4. Update `PlayerManager` to assemble `PhysicsBody` + abilities instead of `PlayerController`
5. Delete `PlayerController`
6. Verify build + gameplay unchanged (Trollborg should feel identical)
7. `MeleeAttack` already works independently — no changes needed
