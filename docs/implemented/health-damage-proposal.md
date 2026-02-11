# Proposal: Health & Damage System

## Problem

The melee attack spawns a hitbox entity, but nothing happens when it overlaps another player. There's no health, no damage, no knockback, and no death/respawn. Combat is purely visual right now.

## Design Goals

- Players take damage when hit by an opponent's attack hitbox
- Hits apply knockback with direction and force defined per-attack in data
- Players have visible health displayed above their character
- A player can only be hit once per attack swing (no multi-frame damage)
- When health reaches zero, the player dies and respawns after a delay
- The system should support future damage sources beyond melee (projectiles, area effects, environmental hazards)

## Architecture

### New Components

#### 1. `Health` Component
Lives on every player entity. Tracks current/max HP and handles taking damage.

```
GorelordsBrawler/Components/Health.cs
```

```csharp
public class Health : Component
{
    [Inspectable] [Range(0, 500)]
    public int MaxHp;

    [Inspectable]
    public int CurrentHp;

    public bool IsDead => CurrentHp <= 0;

    // Called by anything that deals damage
    public void TakeDamage(int amount)
    {
        if (IsDead) return;
        CurrentHp = Math.Max(0, CurrentHp - amount);
        OnDamaged?.Invoke(amount);
        if (IsDead)
            OnDeath?.Invoke();
    }

    public void Heal(int amount)
    {
        CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
    }

    public void Reset() { CurrentHp = MaxHp; }

    public event Action<int> OnDamaged;   // passes damage amount
    public event Action OnDeath;
}
```

**Why events instead of direct coupling?** `OnDamaged` lets the HUD, screen shake, hit flash, and sound effects all respond to damage without `Health` knowing about any of them. `OnDeath` lets the respawn system listen without tight coupling.

#### 2. `Hurtbox` Component
A trigger collider on the player entity that receives hits. Implements `ITriggerListener` to react when a hitbox overlaps it.

```
GorelordsBrawler/Components/Hurtbox.cs
```

```csharp
public class Hurtbox : Component, ITriggerListener
{
    private Health _health;
    private PhysicsBody _body;

    public override void OnAddedToEntity()
    {
        _health = Entity.GetComponent<Health>();
        _body = Entity.GetComponent<PhysicsBody>();
    }

    public void OnTriggerEnter(Collider other, Collider local)
    {
        // Only react to hitbox layer
        if ((other.PhysicsLayer & PhysicsLayers.Hitbox) == 0) return;

        // Ignore hits if already dead
        if (_health.IsDead) return;

        // Get the attack data from the hitbox
        var attackData = other.Entity.GetComponent<AttackData>();
        if (attackData == null) return;

        // Don't get hit by your own attacks
        if (attackData.OwnerEntity == Entity) return;

        _health.TakeDamage(attackData.Damage);

        // Apply knockback — direction is fully data-driven from the attack
        var knockback = attackData.KnockbackAngle;
        knockback.X *= attackData.FacingDirection;  // flip horizontally based on attacker facing
        if (knockback != Vector2.Zero)
            knockback.Normalize();
        _body.Velocity = knockback * attackData.KnockbackForce;
        _body.Grounded = false;
    }

    public void OnTriggerExit(Collider other, Collider local) { }
}
```

**Why a separate Hurtbox instead of putting ITriggerListener on PhysicsBody?** Separation of concerns — PhysicsBody handles movement and gravity. The hurtbox is specifically about receiving damage. Future characters might have hurtboxes that differ from their physics collider (e.g., ducking shrinks the hurtbox but not the physics box).

#### 3. `AttackData` Component
Lives on the hitbox entity spawned by MeleeAttack. Carries all the information a Hurtbox needs to process a hit. This is a pure data bag — no logic.

```
GorelordsBrawler/Components/AttackData.cs
```

```csharp
public class AttackData : Component
{
    public Entity OwnerEntity;       // who threw this attack (prevents self-damage)
    public int Damage;
    public float KnockbackForce;
    public Vector2 KnockbackAngle;   // e.g. (1, -0.5) = forward and slightly up
    public int FacingDirection;      // -1 or 1, used to flip KnockbackAngle.X
}
```

**Why `KnockbackAngle` + `FacingDirection` instead of a pre-computed vector?** Different attacks launch at different angles — a melee punch sends forward-and-up `(1, -0.5)`, an uppercut sends mostly up `(0.3, -1)`, a sweep sends flat `(1, 0)`. The angle is defined per-attack in `CharacterStats` (and ultimately JSON), and the facing direction flips it at spawn time. This keeps the data fully in configuration, not code.

#### 4. `RespawnHandler` Component
Lives on each player entity. Listens to `Health.OnDeath`, disables combat/visual components, waits, then respawns.

```
GorelordsBrawler/Components/RespawnHandler.cs
```

```csharp
public class RespawnHandler : Component, IUpdatable
{
    private Health _health;
    private Vector2 _spawnPosition;
    private float _respawnTimer;
    private bool _waitingToRespawn;

    public RespawnHandler(Vector2 spawnPosition) { _spawnPosition = spawnPosition; }

    public override void OnAddedToEntity()
    {
        _health = Entity.GetComponent<Health>();
        _health.OnDeath += OnDeath;
    }

    private void OnDeath()
    {
        _waitingToRespawn = true;
        _respawnTimer = GameConstants.Combat.RespawnDelay;
        SetCombatComponentsEnabled(false);
    }

    public void Update()
    {
        if (!_waitingToRespawn) return;
        _respawnTimer -= Time.DeltaTime;
        if (_respawnTimer <= 0)
        {
            _health.Reset();
            Entity.Transform.Position = _spawnPosition;
            Entity.GetComponent<PhysicsBody>().Velocity = Vector2.Zero;
            SetCombatComponentsEnabled(true);
            _waitingToRespawn = false;
        }
    }

    private void SetCombatComponentsEnabled(bool enabled)
    {
        // Disable/enable individual components — NOT Entity.SetEnabled(),
        // which would also disable RespawnHandler and stop the timer.
        Entity.GetComponent<PhysicsBody>().SetEnabled(enabled);
        Entity.GetComponent<Hurtbox>().SetEnabled(enabled);
        Entity.GetComponent<HealthBar>().SetEnabled(enabled);

        // Disable all abilities (WalkAbility, JumpAbility, MeleeAttack, etc.)
        foreach (var updatable in Entity.GetComponents<IUpdatable>())
        {
            if (updatable is Component c && c != this && c is not Health)
                c.SetEnabled(enabled);
        }

        // Hide/show visuals
        Entity.GetComponent<PrototypeSpriteRenderer>().SetEnabled(enabled);

        // Disable colliders so the dead player doesn't block anything
        foreach (var collider in Entity.GetComponents<Collider>())
            collider.SetEnabled(enabled);
    }
}
```

**Key detail:** We disable individual components rather than the whole entity. This keeps `RespawnHandler` (and `Health`) ticking so the respawn timer works and the death state is preserved. Colliders are disabled so the dead player's physics body and hurtbox don't interact with anything.

#### 5. `HealthBar` Component (Renderable)
A simple colored bar drawn above the player's head. Reads from the `Health` component.

```
GorelordsBrawler/Components/HealthBar.cs
```

```csharp
public class HealthBar : RenderableComponent
{
    private Health _health;

    public override void OnAddedToEntity()
    {
        _health = Entity.GetComponent<Health>();
        RenderLayer = GameConstants.Rendering.HealthBarRenderLayer;
    }

    public override float Width => GameConstants.Combat.HealthBarWidth;
    public override float Height => GameConstants.Combat.HealthBarHeight;

    public override void Render(Batcher batcher, Camera camera)
    {
        var pos = Entity.Transform.Position;
        var barX = pos.X - GameConstants.Combat.HealthBarWidth / 2;
        var barY = pos.Y - GameConstants.Combat.HealthBarOffsetY;

        // Background (dark)
        batcher.DrawRect(barX, barY,
            GameConstants.Combat.HealthBarWidth, GameConstants.Combat.HealthBarHeight,
            GameConstants.Combat.HealthBarBackgroundColor);

        // Fill (green to red based on health %)
        var fillPercent = (float)_health.CurrentHp / _health.MaxHp;
        var fillColor = Color.Lerp(GameConstants.Combat.HealthBarLowColor,
            GameConstants.Combat.HealthBarHighColor, fillPercent);
        batcher.DrawRect(barX, barY,
            GameConstants.Combat.HealthBarWidth * fillPercent, GameConstants.Combat.HealthBarHeight,
            fillColor);
    }
}
```

### Modifications to Existing Code

#### `MeleeAttack.cs` — Attach AttackData to hitbox
When spawning the hitbox entity, add an `AttackData` component populated from `CharacterStats`:

```csharp
// In SpawnHitbox():
_hitboxEntity.AddComponent(new AttackData
{
    OwnerEntity = Entity,
    Damage = _stats.meleeDamage,
    KnockbackForce = _stats.meleeKnockbackForce,
    KnockbackAngle = _stats.MeleeKnockbackAngle,
    FacingDirection = _body.FacingDirection
});
```

The hitbox collider also needs to be updated: `CollidesWithLayers = PhysicsLayers.Hurtbox` (currently set to 0).

#### `CharacterStats.cs` — Add combat stats

```csharp
// Combat
[Inspectable] [Range(0, 500)]
public int maxHp = 100;

[Inspectable] [Range(0, 100)]
public int meleeDamage = 20;

[Inspectable] [Range(0, 1000)]
public float meleeKnockbackForce = 300f;

// Knockback angle for melee (X = forward, Y = vertical, negative Y = upward)
[Inspectable] [Range(-1, 1)]
public float meleeKnockbackAngleX = 1f;

[Inspectable] [Range(-1, 1)]
public float meleeKnockbackAngleY = -0.5f;

[JsonExclude]
public Vector2 MeleeKnockbackAngle => new Vector2(meleeKnockbackAngleX, meleeKnockbackAngleY);
```

**Why separate X/Y floats instead of a Vector2?** Nez.Persistence serializes public fields, but `Vector2` is a MonoGame struct that may not round-trip cleanly through Nez JSON. Two floats serialize reliably and are individually tunable in the inspector via `[Inspectable]`/`[Range]`. The computed `MeleeKnockbackAngle` property (excluded from JSON) gives consuming code a clean `Vector2`.

#### `Trollborg.json` — Add combat values
```json
{
    "maxHp": 150,
    "meleeDamage": 25,
    "meleeKnockbackForce": 250,
    "meleeKnockbackAngleX": 1.0,
    "meleeKnockbackAngleY": -0.3,
    ...existing fields...
}
```

Trollborg: high HP (150), high damage (25), low knockback force (250) with a mostly-horizontal launch angle — he's heavy, hits hard, but doesn't send you flying. The slight upward angle (-0.3) pops the victim just enough to interrupt their grounding.

#### `PhysicsLayers.cs` — Add Hurtbox layer
```csharp
public const int Hurtbox = 1 << 3;
```

#### `CharacterFactory.cs` — Wire up new components
In `Create()`, after adding PhysicsBody:
```csharp
// Hurtbox (separate trigger collider for receiving damage)
var hurtboxCollider = entity.AddComponent(new BoxCollider(stats.bodyWidth, stats.bodyHeight));
hurtboxCollider.PhysicsLayer = PhysicsLayers.Hurtbox;
hurtboxCollider.CollidesWithLayers = PhysicsLayers.Hitbox;
hurtboxCollider.IsTrigger = true;

// Health system
var health = entity.AddComponent(new Health { MaxHp = stats.maxHp, CurrentHp = stats.maxHp });
entity.AddComponent(new Hurtbox());
entity.AddComponent(new HealthBar());
entity.AddComponent(new RespawnHandler(spawnPosition));
```

#### `GameConstants.cs` — Combat and rendering constants
```csharp
public static class Combat
{
    public const float RespawnDelay = 2f;
    public const float HealthBarWidth = 40f;
    public const float HealthBarHeight = 4f;
    public const float HealthBarOffsetY = 35f;
    public static readonly Color HealthBarBackgroundColor = new Color(40, 40, 40);
    public static readonly Color HealthBarHighColor = Color.Green;
    public static readonly Color HealthBarLowColor = Color.Red;
}

// Add to existing Rendering class:
public const int HealthBarRenderLayer = -2;  // in front of hitbox layer (-1) and default (0)
```

### Collision Layer Diagram

```
Entity          Collider          Layer        CollidesWith       IsTrigger
-----------     --------          -----        ------------       ---------
Player          BoxCollider       Player       Platforms          false
Player          BoxCollider       Hurtbox      Hitbox             true
Melee Hitbox    BoxCollider       Hitbox       Hurtbox            true
Platform        BoxCollider       Platforms    Player             false
```

Key points:
- Player's physics collider only interacts with Platforms (for movement)
- Hurtbox is a second collider on the player, trigger-only, listens for Hitbox overlap
- Hitbox (attack) is trigger-only, targets Hurtbox layer
- Since the player entity has a `Mover` that calls `Move()`, trigger events fire automatically via Nez's `ColliderTriggerHelper`

### Hit Prevention (No Multi-Hit)

The existing MeleeAttack already handles this naturally — each attack spawns one hitbox entity that lives for `hitboxDuration` seconds, then is destroyed. Since `OnTriggerEnter` fires once per overlap start, and the hitbox only exists for one swing, a single attack can only trigger one enter event per victim. No `HashSet` tracking needed.

## Files Summary

| File | Action |
|------|--------|
| `GorelordsBrawler/Components/Health.cs` | **Create** |
| `GorelordsBrawler/Components/Hurtbox.cs` | **Create** |
| `GorelordsBrawler/Components/AttackData.cs` | **Create** |
| `GorelordsBrawler/Components/RespawnHandler.cs` | **Create** |
| `GorelordsBrawler/Components/HealthBar.cs` | **Create** |
| `GorelordsBrawler/Components/MeleeAttack.cs` | **Modify** — attach AttackData, update CollidesWithLayers |
| `GorelordsBrawler/Components/CharacterStats.cs` | **Modify** — add maxHp, meleeDamage, meleeKnockbackForce, meleeKnockbackAngleX/Y |
| `GorelordsBrawler/Data/CharacterFactory.cs` | **Modify** — wire up Health, Hurtbox, HealthBar, RespawnHandler |
| `GorelordsBrawler/Constants/PhysicsLayers.cs` | **Modify** — add Hurtbox layer |
| `GorelordsBrawler/Constants/GameConstants.cs` | **Modify** — add Combat constants, HealthBarRenderLayer |
| `GorelordsBrawler/Content/Characters/Trollborg.json` | **Modify** — add combat stats |

## Future Extensibility

- **Projectile damage:** Projectile entities just need an `AttackData` component with their own angle/force. The Hurtbox processes it identically.
- **Environmental damage:** Lava/spikes add an `AttackData` with no `OwnerEntity` (null). Hurtbox skips the self-damage check and applies the hit.
- **Per-attack knockback profiles:** When characters gain multiple attacks, each attack type gets its own knockback angle/force fields in CharacterStats (e.g., `uppercut_KnockbackAngleX`, `uppercut_KnockbackAngleY`). The ability component populates AttackData from the appropriate fields.
- **Armor/resistance:** `Health.TakeDamage()` can read an armor stat from `CharacterStats` and reduce incoming damage before applying.
- **Invincibility frames:** Add a `_invincibleTimer` in `Hurtbox` that's set after taking damage. During that window, `OnTriggerEnter` returns early. Also useful for brief post-respawn invulnerability.
- **Shield abilities:** A future shield component could set a flag on `Hurtbox` to absorb or redirect hits.
