# Movement Refinement — Acceleration + Momentum Jumps

## Context

Movement is currently instant: `Velocity.X = moveDir * MoveSpeed` every frame, with no acceleration or deceleration. Jump is a fixed vertical impulse that doesn't interact with horizontal speed at all. This makes the game feel "floaty" and arcade-like — characters snap to full speed and stop on a dime.

For a brawler with knockback and platform play, two things would improve feel significantly:

1. **Acceleration-based ground movement** — characters ramp up to full speed and slow down with traction. Gives weight and makes movement feel physical.
2. **Momentum-preserved jumps** — horizontal velocity carries into jumps naturally. Running + jump = longer arc. Standing + jump = straight up. This is how Smash Bros, Rivals of Aether, and most platform fighters work.

**Not in scope:** Tiled map integration (separate task), blast zones (separate task), new platform layouts.

---

## Changes

### 1. `MovementStats.cs` — Add acceleration/friction parameters

```csharp
public float GroundAcceleration = 800f;   // px/sec² — how fast you reach MoveSpeed
public float GroundFriction = 600f;       // px/sec² — how fast you stop (no input)
public float AirAcceleration = 400f;      // px/sec² — reduced air control
public float AirFriction = 100f;          // px/sec² — very low air drag
```

All `[Inspectable]` so they can be tuned at runtime in the Nez console. These values are starting points — the inspector lets us dial them in during play.

**Design notes:**
- High `GroundAcceleration` (800) means you reach full speed in ~0.15s — snappy but not instant
- Lower `AirAcceleration` (400) means less air control — committed to your jump arc
- High `GroundFriction` (600) means you stop quickly on the ground — responsive
- Very low `AirFriction` (100) means momentum is preserved in air — momentum jumps work

### 2. `WalkAbility.cs` — Replace instant velocity with acceleration

**Current** (instant):
```csharp
_body.Velocity.X = moveDir * _movement.MoveSpeed;
```

**New** (acceleration-based):
```csharp
float targetVelocity = moveDir * _movement.MoveSpeed;
float accel, friction;

if (_body.Grounded)
{
    accel = _movement.GroundAcceleration;
    friction = _movement.GroundFriction;
}
else
{
    accel = _movement.AirAcceleration;
    friction = _movement.AirFriction;
}

if (moveDir != 0)
{
    // Accelerate toward target
    _body.Velocity.X = MathHelper.MoveTowards(
        _body.Velocity.X, targetVelocity, accel * Time.DeltaTime);
}
else
{
    // Decelerate with friction
    _body.Velocity.X = MathHelper.MoveTowards(
        _body.Velocity.X, 0, friction * Time.DeltaTime);
}
```

`MathHelper.MoveTowards` (Nez utility) moves a value toward a target by a max delta — perfect for acceleration.

**Hitstun handling stays the same** — the existing `Lerp(Velocity.X, 0, 10 * dt)` friction during hitstun already works well with this approach since it operates on the velocity directly.

### 3. `JumpAbility.cs` — No changes needed

The jump already sets `Velocity.Y = -JumpSpeed` as a vertical impulse. Since `WalkAbility` now uses acceleration instead of overwriting `Velocity.X` every frame, horizontal momentum is **automatically preserved** through jumps. When you're running at full speed and jump:

- In air: `AirAcceleration` (400) applies instead of `GroundAcceleration` (800)
- If you release horizontal input mid-jump: `AirFriction` (100) slowly decelerates — you keep most of your momentum
- If you hold input in the same direction: you maintain speed (air accel keeps you at max)
- If you reverse direction: slower turnaround in air (air accel is lower)

This naturally creates the "running jump goes further" behavior without any special coupling code.

### 4. `MathHelper.MoveTowards` — Verify availability

Nez's Mathf extensions should have this. If not, it's a one-liner:
```csharp
float MoveTowards(float current, float target, float maxDelta)
    => Math.Abs(target - current) <= maxDelta ? target : current + Math.Sign(target - current) * maxDelta;
```

We can add it as a local helper in WalkAbility if needed.

---

## Files Modified

| File | Change |
|---|---|
| `GorelordsBrawler/Components/Stats/MovementStats.cs` | Add `GroundAcceleration`, `GroundFriction`, `AirAcceleration`, `AirFriction` |
| `GorelordsBrawler/Components/Abilities/WalkAbility.cs` | Replace instant velocity with acceleration/friction model |

## Character JSON

Existing characters will use the new defaults automatically (no JSON change needed). Characters can override acceleration values per-character in their JSON for different feel (heavy vs. nimble).

## Verification

1. `dotnet build GorelordsBrawler/GorelordsBrawler.csproj`
2. Run and move — should feel snappy but not instant. Slight ramp to full speed, slides slightly when releasing.
3. Jump while running — should carry horizontal momentum into the arc (longer jump)
4. Jump from standstill — should go mostly straight up
5. Release input mid-air — should slowly decelerate, not snap to zero
6. Reverse direction mid-air — should feel sluggish (lower air accel)
7. Open Nez inspector, select a character, tweak acceleration values in real-time
8. Knockback during hitstun — should still work correctly (existing friction path)
