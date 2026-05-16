---
name: nez-particles
description: Use Nez's ParticleEmitter to add particle effects (blood, smoke, sparks, bubbles, splashes, fire, glow) to a Nez/MonoGame game. Covers the ParticleEmitter component, ParticleEmitterConfig knobs (lifetime, motion, colors, blend modes, collision), the "burst" vs "continuous" usage patterns, the pooled-entity pattern for reusable effects, and the gotchas you'll hit (Duration ≠ Lifetime, EmissionRate auto-calc, AlphaBlend vs Additive). Use whenever the user asks for "particles", "particle effect", "smoke", "sparks", "bubbles", "splash", "fire", "glow", "magic trail", or asks to add a visual effect that emits many small things.
allowed-tools: Read, Edit, Write, Grep, Glob
---

# Nez particle emitter — practical guide

Nez ships an OpenGL-friendly CPU particle system in `Nez.Particles`. It's the right tool for blood splatter, dust, sparks, smoke, bubbles, splash droplets, magic trails, fire, glow halos, snow, rain. **It is NOT a fluid simulator** — particles don't interact with each other, just with the physics-layer colliders if you opt in.

Reference impl in this repo: `GorelordsBrawler/Systems/HitParticleManager.cs` — blood splatter + impact flash via two pooled emitters per slot. Read it before writing a new effect; the pattern is solid and reusable.

## The two types

```csharp
ParticleEmitterType.Gravity   // Standard: angle + speed + gravity vector. 99% of effects.
ParticleEmitterType.Radial    // Particles orbit a center. Rare. Use for swirls / vortices.
```

## Pick a usage pattern

There are two ways to use emitters. Pick based on whether the effect is a one-shot or continuous.

### A) Continuous (auto-emits on add)

```csharp
var config = new ParticleEmitterConfig {
    EmitterType = ParticleEmitterType.Gravity,
    MaxParticles = 200,
    EmissionRate = 50,         // particles/sec (REQUIRED — see gotcha below)
    ParticleLifespan = 2.0f,
    // ... rest of config ...
};
var emitter = entity.AddComponent(new ParticleEmitter(config));   // plays on add
// later: emitter.Pause() / emitter.Stop() / emitter.PauseEmission()
```

Use for: ambient bubbles, torch flames, fountains, anything that runs as long as the entity exists.

### B) Burst (manual `Emit(n)`, pooled)

```csharp
var emitter = new ParticleEmitter(config, playOnAwake: false);   // important
entity.AddComponent(emitter);

// later, on the event you want particles:
entity.Transform.Position = whereItHappened;
emitter.Emit(32);   // immediate burst of 32 particles
```

Use for: hit splatter, impact sparks, splash on contact, smoke puff on damage. Pool the emitters (see HitParticleManager). Don't make-and-destroy per event.

**Pool template:**
```csharp
public class FooEffectManager : SceneComponent {
    private const int PoolSize = 6;
    private Entity[]            _entities;
    private ParticleEmitter[]   _emitters;
    private int _nextSlot;

    public override void OnEnabled() {
        _entities = new Entity[PoolSize];
        _emitters = new ParticleEmitter[PoolSize];
        for (int i = 0; i < PoolSize; i++) {
            var e = Scene.CreateEntity($"foo-fx-{i}");
            var em = new ParticleEmitter(BuildConfig(), playOnAwake: false);
            em.RenderLayer = GameConstants.Rendering.HitboxRenderLayer;
            e.AddComponent(em);
            _entities[i] = e;
            _emitters[i] = em;
        }
    }
    public void SpawnAt(Vector2 pos, int count) {
        int slot = _nextSlot;
        _nextSlot = (_nextSlot + 1) % PoolSize;
        _entities[slot].Transform.Position = pos;
        _emitters[slot].Emit(count);
    }
    private static ParticleEmitterConfig BuildConfig() { /* ... */ }
}
```

## `ParticleEmitterConfig` — the knobs

Group your settings mentally as **emission**, **motion**, **lifetime+size**, **color**, **rotation**, **rendering**.

### Emission
- `EmitterType` — `Gravity` or `Radial`.
- `EmissionRate` — particles/sec for continuous mode. **REQUIRED when building in code** (the .pex loader sets this automatically as `MaxParticles / ParticleLifespan`, but the in-code path does NOT).
- `MaxParticles` — pool size. Set ~1.5× your expected concurrent particle count.
- `Duration` — seconds before emission auto-stops (`-1` = infinite). Existing particles continue after this.
- `SourcePositionVariance` — `Vector2`, jitters spawn position around the entity transform.

### Motion (Gravity)
- `Speed` / `SpeedVariance` — initial speed in px/s.
- `Angle` / `AngleVariance` — initial direction in **degrees** (0 = right, 90 = down).
- `Gravity` — `Vector2` px/s². `(0, 500)` falls naturally.
- `RadialAcceleration` / `TangentialAcceleration` (+ variance) — pushes outward / sideways from the spawn point. Use for swirl or "blow apart" effects.

### Motion (Radial-only)
- `MaxRadius` / `MinRadius` — particles spiral from max → min over lifespan.
- `RotatePerSecond` — orbital angular velocity in degrees/sec.

### Lifetime + size
- `ParticleLifespan` / `ParticleLifespanVariance` — seconds per particle.
- `StartParticleSize` / `FinishParticleSize` — pixels. Color and size lerp linearly over lifetime.

### Color
- `StartColor` / `FinishColor` — `Color`. Lerp over lifetime.
- `StartColorVariance` / `FinishColorVariance` — adds random per-component jitter.
- For fade-out: set `FinishColor.A = 0`.

### Rotation
- `RotationStart` / `RotationEnd` — **radians**. Sprite spins from one to the other.

### Rendering
- `Sprite` — Nez `Sprite` wrapping a `Texture2D`. **If null, renders as a white pixel** (good for tiny soft dots). Most game effects look better with a 16×16 soft alpha disc.
- `BlendFuncSource` / `BlendFuncDestination` — `Microsoft.Xna.Framework.Graphics.Blend`. The two important presets:
  - **Alpha blend (normal)**: `Blend.SourceAlpha` + `Blend.InverseSourceAlpha`. Use for opaque-ish things like smoke, droplets, blood.
  - **Additive (glow)**: `Blend.SourceAlpha` + `Blend.One`. Bright stacks brighter. Use for sparks, fire, lightning, magic, hot embers, anything that emits light.
- `SimulateInWorldSpace` — `true` = once spawned, particles ignore the parent transform. **You almost always want this true** unless the effect is "attached" to a moving entity (like a flame jet on a rocket).

## Rendering / layer ordering

`ParticleEmitter : RenderableComponent`, so it honors `RenderLayer` (lower = front, higher = back) and `LayerDepth` (0 = front, 1 = back) just like sprites. Examples from GorelordsBrawler:

```csharp
emitter.RenderLayer = GameConstants.Rendering.HitboxRenderLayer;  // in front of characters
emitter.RenderLayer = GameConstants.Rendering.DefaultRenderLayer; // mixed with world sprites
emitter.LayerDepth  = 0f;   // among the default layer's items, on top
```

If you have many simultaneous emitters, each issues its own draw call. Group by render layer + identical blend state to maximize batching at the renderer level.

## Collision (optional)

```csharp
emitter.CollisionConfig.Enabled            = true;
emitter.CollisionConfig.CollidesWithLayers = PhysicsLayers.Platforms;
emitter.CollisionConfig.Elasticity         = 0.5f;   // 0=stick, 1=perfect bounce
emitter.CollisionConfig.Friction           = 0.6f;
emitter.CollisionConfig.RadiusScale        = 0.8f;   // particle radius * this = collision radius
emitter.CollisionConfig.LifetimeLoss       = 0.0f;   // 0..1 of lifespan lost per hit
emitter.CollisionConfig.MinKillSpeedSquared = 100f;  // kill if v² drops below
```

Uses Nez's broadphase + shape collision, so the colliders being hit have to be real Nez `Collider` components on the matching physics layer. Custom-built collision (like our fluid sim's AABB list) is invisible to it.

## Recipes

### Smoke puff (gray rising soft circle)
```csharp
new ParticleEmitterConfig {
    EmitterType = ParticleEmitterType.Gravity,
    MaxParticles = 32,
    Speed = 30, SpeedVariance = 15,
    Angle = -90, AngleVariance = 25,            // straight up ± a bit
    Gravity = new Vector2(0, -40),              // negative Y = rises
    ParticleLifespan = 1.2f, ParticleLifespanVariance = 0.3f,
    StartColor = new Color(180, 180, 180, 200),
    FinishColor = new Color(80, 80, 80, 0),     // fade to invisible
    StartParticleSize = 6, FinishParticleSize = 18,  // expand as it rises
    SimulateInWorldSpace = true,
    BlendFuncSource = Blend.SourceAlpha,
    BlendFuncDestination = Blend.InverseSourceAlpha,
};
```

### Sparks (additive bright, gravity, fast)
```csharp
new ParticleEmitterConfig {
    EmitterType = ParticleEmitterType.Gravity,
    MaxParticles = 24,
    Speed = 250, SpeedVariance = 100,
    Angle = 0, AngleVariance = 180,             // any direction
    Gravity = new Vector2(0, 600),
    ParticleLifespan = 0.4f, ParticleLifespanVariance = 0.15f,
    StartColor = new Color(255, 230, 120, 255),
    FinishColor = new Color(255, 60, 0, 0),
    StartParticleSize = 3, FinishParticleSize = 1,
    SimulateInWorldSpace = true,
    BlendFuncSource = Blend.SourceAlpha,
    BlendFuncDestination = Blend.One,            // ADDITIVE — makes them glow
};
```

### Bubble (rises slowly, transparent + outline)
```csharp
new ParticleEmitterConfig {
    EmitterType = ParticleEmitterType.Gravity,
    MaxParticles = 16,
    Speed = 12, SpeedVariance = 5,
    Angle = -90, AngleVariance = 10,
    Gravity = new Vector2(0, -25),               // up
    ParticleLifespan = 0.9f, ParticleLifespanVariance = 0.2f,
    StartColor = new Color(200, 255, 180, 220),
    FinishColor = new Color(200, 255, 180, 0),
    StartParticleSize = 3, FinishParticleSize = 6,
    SimulateInWorldSpace = true,
    BlendFuncSource = Blend.SourceAlpha,
    BlendFuncDestination = Blend.InverseSourceAlpha,
};
```

### Splash droplets (spray sideways + up, fall)
```csharp
new ParticleEmitterConfig {
    EmitterType = ParticleEmitterType.Gravity,
    MaxParticles = 48,
    Speed = 180, SpeedVariance = 80,
    Angle = -90, AngleVariance = 50,             // up + sideways
    Gravity = new Vector2(0, 900),               // hard fall
    ParticleLifespan = 0.45f, ParticleLifespanVariance = 0.1f,
    StartColor = new Color(140, 230, 90, 230),
    FinishColor = new Color(70, 160, 50, 0),
    StartParticleSize = 5, FinishParticleSize = 2,
    SimulateInWorldSpace = true,
    BlendFuncSource = Blend.SourceAlpha,
    BlendFuncDestination = Blend.InverseSourceAlpha,
};
```

## Procedural particle textures (no asset pipeline)

If you don't want to ship a PNG asset, generate a soft-disc texture once at startup. This is what most effects in this codebase need:

```csharp
public static Sprite CreateSoftDiscSprite(int size = 16) {
    var tex = new Texture2D(Core.GraphicsDevice, size, size);
    var data = new Color[size * size];
    float half = size * 0.5f;
    for (int y = 0; y < size; y++) {
        for (int x = 0; x < size; x++) {
            float dx = (x + 0.5f) - half;
            float dy = (y + 0.5f) - half;
            float d = MathF.Sqrt(dx*dx + dy*dy) / half;
            float a = MathHelper.Clamp(1f - d*d, 0f, 1f);
            a = MathF.Pow(a, 1.5f);
            byte alpha = (byte)(a * 255f);
            data[y * size + x] = new Color((byte)255, (byte)255, (byte)255, alpha);
        }
    }
    tex.SetData(data);
    return new Sprite(tex);
}
```

Then `config.Sprite = CreateSoftDiscSprite();`. The white texel + per-particle tint = soft colored blob.

## Gotchas

1. **`Duration` is emission duration, not particle lifetime.** Setting `Duration = 2` means "stop creating new particles after 2 s"; existing particles keep going for `ParticleLifespan` more.
2. **`EmissionRate` must be set explicitly when building config in code.** The `.pex` loader auto-derives it; the in-code path leaves it 0 → no particles. Set it yourself.
3. **`playOnAwake: true` is the default.** If you want a pooled burst-emitter, pass `playOnAwake: false` or it'll start emitting immediately when added.
4. **`Emit(n)` does NOT trigger emission via `EmissionRate`.** It's a separate "burst these n right now" pathway. Use it from any code, even on an emitter that's `Stop()`ped.
5. **`SimulateInWorldSpace = true` is almost always what you want.** Default is `false`, which couples particle positions to the parent transform — fine for "flames on a moving torch", not fine for "splash on impact".
6. **Additive blend looks washed out in linear color spaces.** Nez uses standard sRGB, so additive with high alpha will overbright quickly. Use lower alpha on additive emitters (start with `StartColor.A = 180`).
7. **Each emitter is its own draw call.** Many simultaneous unique configs = many draws. Reuse configs across pools where possible.
8. **`ResumeEmission()` silently no-ops** if the emitter is Stopped or its `Duration` expired. Use `Play()` for a guaranteed restart.
9. **Collision callbacks**: there are no per-collision events. If you need "play a sound when particle hits", you have to roll your own (track particle positions externally or branch the source).
10. **`Bounds` is computed from active particles each frame**, so a paused emitter with no particles has zero bounds and might get culled. Mostly harmless — it'll start drawing again as soon as particles exist.

## When NOT to use ParticleEmitter

- The "particles" need to interact with each other (use a fluid sim like `FluidSimulation`).
- You need pixel-perfect cellular automaton behavior (Noita-style).
- The effect is a single sprite with a curve (use `Tween`).
- You need 50k+ particles (write a GPU-accelerated renderer).

## Reference impls in this repo

- `GorelordsBrawler/Systems/HitParticleManager.cs` — pool of 6 entities, each with a blood emitter + a flash emitter. Reads inspectable `BloodSpraySettings`. Spawned by `CombatEffectsManager.TriggerHit()`.
- `GorelordsBrawler/Systems/AcidEffectsManager.cs` *(if present)* — pool of bubble/splash/smoke emitters for the acid hazard.
