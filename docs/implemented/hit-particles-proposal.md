# Hit Particles Proposal — Blood Splatter + Impact Flash

## Goal

Add two particle effects on every hit to sell the impact:

1. **Blood splatter** — small red droplets that spray outward from the hit point, fall with gravity, and fade out
2. **Impact flash** — a brief bright burst at the point of contact (additive blended, white/yellow glow)

Both use Nez's built-in `ParticleEmitter` system — no external tools or `.pex` files needed. Configs are created programmatically in code.

---

## How Nez Particles Work

Nez provides `ParticleEmitter` (a `RenderableComponent` + `IUpdatable`) and `ParticleEmitterConfig` (a data object with all the knobs). Key concepts:

- **`Emit(int count)`** — burst N particles on demand (no continuous emission needed)
- **`ParticleEmitterConfig`** — controls speed, angle, gravity, color fade, size, lifetime, blending
- **`SimulateInWorldSpace = true`** — particles stay where they spawn (don't follow the entity)
- **No sprite required** — particles render as filled circles/dots by default (no texture file needed)
- **Additive blending** — `BlendFuncSource = Blend.SourceAlpha, BlendFuncDestination = Blend.One` creates a glow effect

---

## Architecture

### Option A: Pooled Emitter Entities (Recommended)

Create a `HitParticleManager` as a **SceneComponent** (like `CombatEffectsManager`). It pre-creates a small pool of reusable emitter entities. On each hit, it grabs one, positions it, calls `Emit()`, and the particles play out on their own.

```
Hit detected (Hurtbox.OnTriggerEnter)
  → Hurtbox calls CombatEffectsManager.TriggerHit(...)
    → CombatEffectsManager calls HitParticleManager.SpawnHitEffect(position, direction, intensity)
      → Positions a pooled emitter entity at the hit point
      → Calls bloodEmitter.Emit(count) and flashEmitter.Emit(count)
      → Particles simulate in world space, fade, and die on their own
```

### Why SceneComponent + Pool?

- Emitter entities are reused across hits (no garbage from creating/destroying entities per hit)
- SceneComponent persists for the scene lifetime — same pattern as `CombatEffectsManager`
- Each pool slot has two emitters (blood + flash) on the same entity
- Pool size of 4-6 handles rapid multi-hit scenarios

---

## Particle Configs

### Blood Splatter

| Property | Value | Notes |
|---|---|---|
| EmitterType | Gravity | Linear motion with gravity |
| MaxParticles | 16 | Per emitter instance |
| Speed | 80-120 | Pixels/sec, randomized via SpeedVariance |
| Angle | *dynamic* | Points away from attacker (knockback direction) |
| AngleVariance | 30-45 | Cone spread in degrees |
| Gravity | (0, 300) | Falls downward |
| ParticleLifespan | 0.4s | Short-lived |
| ParticleLifespanVariance | 0.15s | Some die earlier |
| StartColor | Dark red (180, 0, 0, 255) | Blood |
| FinishColor | Dark red (180, 0, 0, 0) | Fades to transparent |
| StartParticleSize | 3 | Pixels |
| FinishParticleSize | 1 | Shrinks as it fades |
| SimulateInWorldSpace | true | Stays where spawned |
| BlendFuncSource | SourceAlpha | Normal blending |
| BlendFuncDestination | InverseSourceAlpha | Normal blending |
| Burst count | 6-10 | Per hit, scaled by knockback intensity |

### Impact Flash

| Property | Value | Notes |
|---|---|---|
| EmitterType | Gravity | Simple expand-and-fade |
| MaxParticles | 8 | Small burst |
| Speed | 40-60 | Slow expansion |
| Angle | 0 | Doesn't matter much |
| AngleVariance | 180 | Radiates in all directions |
| Gravity | (0, 0) | No gravity — stays centered |
| ParticleLifespan | 0.08s | Very brief (visible during hitstop) |
| StartColor | White/yellow (255, 255, 200, 255) | Bright flash |
| FinishColor | Yellow (255, 200, 50, 0) | Fades to transparent |
| StartParticleSize | 6 | Starts big |
| FinishParticleSize | 2 | Shrinks |
| SimulateInWorldSpace | true | Stays where spawned |
| BlendFuncSource | SourceAlpha | Additive glow |
| BlendFuncDestination | One | Additive glow |
| Burst count | 4-6 | Per hit |

---

## Hit Position Calculation

At the moment of hit in `Hurtbox.OnTriggerEnter()`, we have two colliders:
- `other` — the hitbox (attacker's weapon)
- `local` — the hurtbox zone (defender's body part)

**Hit point** = midpoint between `other.AbsolutePosition` and `local.AbsolutePosition`. This places the effect right at the contact boundary rather than dead center on either collider.

**Spray direction** = derived from `attackData.KnockbackAngle` with `FacingDirection` applied. Blood sprays away from the attacker (same direction the defender gets knocked). Convert the Vector2 to degrees for the emitter's `Angle` property.

**Intensity** = `scaledKnockbackForce / 600f` (same formula as camera shake). Higher knockback = more particles.

---

## Integration Points

### 1. `HitParticleManager.cs` (New — SceneComponent)

```
GorelordsBrawler/Systems/HitParticleManager.cs
```

- Pre-creates pool of emitter entities in `OnEnabled()`
- Public method: `SpawnHitEffect(Vector2 position, Vector2 direction, float intensity)`
- Converts direction to angle degrees: `MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI)`
- Scales burst count by intensity (e.g., `(int)(6 + intensity * 4)` for blood)

### 2. `CombatEffectsManager.cs` (Modified)

Expand `TriggerHit()` signature to include hit position and direction:

```csharp
public void TriggerHit(Entity defender, float scaledKnockbackForce,
                       Vector2 hitPosition, Vector2 knockbackDirection)
```

Then call `HitParticleManager.SpawnHitEffect(hitPosition, knockbackDirection, intensity)`.

### 3. `Hurtbox.cs` (Modified)

Compute hit position and pass it through to `TriggerHit()`:

```csharp
var hitPosition = (other.AbsolutePosition + local.AbsolutePosition) / 2f;
var knockDir = knockback;  // already computed and normalized
_effectsManager?.TriggerHit(Entity, knockbackForce * knockbackScale,
                            hitPosition, knockDir);
```

### 4. Scene Setup

`ArenaScene` (or `BaseScene`) registers the `HitParticleManager` as a SceneComponent, same as `CombatEffectsManager`.

### 5. `GameConstants.cs` (Modified)

Add particle tuning constants to `Combat`:

```csharp
// Blood splatter particles
public const int BloodBaseCount = 6;
public const int BloodMaxExtra = 4;
public const float BloodSpeed = 100f;
public const float BloodSpeedVariance = 30f;
public const float BloodAngleVariance = 35f;
public const float BloodLifespan = 0.4f;
public const float BloodGravity = 300f;
public const float BloodStartSize = 3f;

// Impact flash particles
public const int FlashCount = 5;
public const float FlashSpeed = 50f;
public const float FlashLifespan = 0.08f;
public const float FlashStartSize = 6f;
```

---

## Hitstop Timing Consideration

Particles spawn during `Time.TimeScale = 0` (hitstop). Nez's `ParticleEmitter.Update()` uses `Time.DeltaTime`, which is affected by TimeScale. This means:

- **Impact flash**: Spawns and freezes in place during hitstop — actually looks great, the bright dots hang in the air for 60ms
- **Blood splatter**: Also freezes during hitstop, then resumes falling when time resumes

This is the desired behavior — the particles appear instantly on hit, freeze during the hitstop for dramatic effect, then animate out.

---

## Files Summary

| File | Status | Change |
|---|---|---|
| `GorelordsBrawler/Systems/HitParticleManager.cs` | **New** | SceneComponent with emitter pool |
| `GorelordsBrawler/Systems/CombatEffectsManager.cs` | Modified | Add hit position/direction to TriggerHit |
| `GorelordsBrawler/Components/Hurtbox.cs` | Modified | Compute hit position, pass to TriggerHit |
| `GorelordsBrawler/Constants/GameConstants.cs` | Modified | Add particle tuning constants |
| `GorelordsBrawler/Scenes/ArenaScene.cs` | Modified | Register HitParticleManager |

## Verification

1. `dotnet build GorelordsBrawler/GorelordsBrawler.csproj`
2. Hit a character — should see red blood droplets spray away from the hit point and fall with gravity
3. Should see a brief white/yellow flash at the point of impact
4. Particles should freeze during hitstop (60ms), then resume
5. Rapid hits should each spawn their own particles (pool handles overlap)
6. Verify no particle leak — particles die after their lifespan, emitter entities are reused
