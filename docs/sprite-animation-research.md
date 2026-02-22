# Sprite Animation Research: Industry Standard Approaches for Nez/MonoGame

## Problem Statement

GorelordsBrawler uses Blender-rendered 3D models as 2D sprite sheets. The current implementation uses `Sprite.SpritesFromAtlas()` to slice uniform grid sheets, but this approach has issues with alignment, wasted space, and inflexible animation configuration. This document surveys how professional projects handle sprite animation and recommends a production-quality pipeline.

---

## Two Approaches in Nez

### Approach A: `Sprite.SpritesFromAtlas()` (Current — Grid Sheets)

Slices a uniform grid sprite sheet into frames at runtime:

```csharp
var texture = Content.LoadTexture("run.png");
var sprites = Sprite.SpritesFromAtlas(texture, 256, 256, 0, 42);
animator.AddAnimation("run", sprites.ToArray(), 24f);
```

- Every cell is the same size, even if content varies
- No per-sprite origin data — defaults to center
- Manual animation assignment (you pick which indices go where)
- Multiple texture binds per character (one sheet per animation)

**Verdict:** Fine for prototyping. Not suitable for production with pre-rendered 3D sprites.

### Approach B: `SpriteAtlas` via Nez SpriteAtlasPacker (Recommended)

Nez ships with a built-in texture packer at `Nez/Nez.SpriteAtlasPacker/`. It takes a folder of individual frame PNGs organized into animation subdirectories and produces a packed atlas PNG + `.atlas` metadata file.

**Folder structure** (subdirectories become animation names automatically):

```
FutureAxe/
  idle/
    frame_0001.png
    frame_0002.png
    ...
  run/
    frame_0001.png
    frame_0002.png
    ...
  attack/
    frame_0001.png
    ...
```

**Packing command:**

```bash
dotnet run --project Nez/Nez.SpriteAtlasPacker -- \
  -image:Content/Sprites/FutureAxe/FutureAxe.png \
  -map:Content/Sprites/FutureAxe/FutureAxe.atlas \
  -fps:10 \
  -pad:2 \
  -originX:0.5 \
  -originY:1.0 \
  FutureAxe-frames/
```

**Runtime loading:**

```csharp
var atlas = scene.Content.LoadSpriteAtlas("Content/Sprites/FutureAxe/FutureAxe.atlas");
var animator = entity.AddComponent<SpriteAnimator>();
animator.AddAnimationsFromAtlas(atlas);  // all animations in one call
animator.Play("idle");
```

**Why this is better:**

| Feature | Grid Sheet | Packed Atlas |
|---------|-----------|--------------|
| Wasted space | Every cell padded to max size | Tight-fit per frame |
| Texture binds | One per animation | One per character |
| Origins | Center only (default) | Per-sprite, baked into `.atlas` |
| Animation setup | Manual per-animation code | `AddAnimationsFromAtlas()` — one call |
| Frame rates | Single fps per animation | Per-frame timing via `SpriteAnimation.FrameRates` |
| Build integration | None | Packer runs as build step |

---

## Sprite Origin / Pivot Point

This is the root cause of the "feet under platform" issue.

### The Problem

With center origin (default), the entity position sits at the sprite's visual center. But a character's logical anchor point should be at their **feet**, not their belly button. When the sprite cell has empty space above the character, the visual center shifts down, pushing feet below the collider.

### The Fix: Bottom-Center Origin (0.5, 1.0)

Professional 2D brawlers/platformers use **bottom-center** as the pivot:

- `originX: 0.5` — horizontal center
- `originY: 1.0` — bottom edge of the sprite

This means:
- Entity position = character's feet
- Sprite renders **upward** from that point
- BoxCollider uses `LocalOffset = new Vector2(0, -height/2)` to extend upward from feet
- All animations share the same origin, so switching between idle/run/attack doesn't cause the character to jump around

The Nez SpriteAtlasPacker supports this with `-originX:0.5 -originY:1.0`, which bakes the origin into every sprite in the atlas.

---

## Animation Frame Rates

### General Principle

**Frame timing matters more than frame count.** Four well-timed frames beat twelve uniformly-timed ones. Games run at 60fps but sprite animations typically play at 8–15fps.

### Recommended Values for a Brawler

| Animation | Frame Count | FPS | Notes |
|-----------|-----------|-----|-------|
| Idle | 4–8 | 6–8 | Subtle breathing loop, slow and relaxed |
| Run | 6–8 | 10–15 | 8 frames is the standard run cycle length |
| Attack (wind-up) | 2–3 | ~6 (150ms/frame) | Anticipation, readable |
| Attack (strike) | 1–2 | ~30 (33ms/frame) | Fast, snappy |
| Attack (recovery) | 2–3 | ~10 (100ms/frame) | Cooldown |
| Hit reaction | 2–3 | 15–20 | Flash + recoil |
| Death | 4–6 | 8–10 | One-shot, no loop |

### Per-Frame Timing in Nez

`SpriteAnimation` supports per-frame rates via `float[] FrameRates`. This is critical for attacks where the wind-up is slow but the strike is instant. The packed atlas approach via `SpriteAtlasPacker` doesn't support per-frame rates in the `.atlas` file, but you can override them at runtime:

```csharp
var atlas = scene.Content.LoadSpriteAtlas("character.atlas");
var attackAnim = atlas.GetAnimation("attack");
// Override: slow anticipation, fast strike, medium recovery
attackAnim.FrameRates = new float[] { 6f, 6f, 30f, 10f, 10f };
```

### What This Means for Current Animations

The current sprite sheets have **42 frames for run** and **58 frames for idle**. These are very high frame counts — rendered at every keyframe from Blender. For a snappier feel, either:

1. **Reduce frame count in Blender** — render every 2nd or 3rd frame. Target 8 frames for run, 6–8 for idle.
2. **Skip frames at pack time** — only include every Nth frame PNG in the packer input folders.
3. **Keep all frames but lower fps** — 42 frames at 10fps = 4.2 second cycle (too slow for run). At 30fps = 1.4 seconds (might work but uses lots of memory for minimal visual gain over 8 frames at 10fps).

**Recommendation:** Re-render with fewer frames. 8-frame run at 12fps and 6-frame idle at 8fps will feel much snappier and use far less memory.

---

## Pre-Rendered 3D Sprites (Blender Pipeline)

GorelordsBrawler uses the "Donkey Kong Country" approach — 3D models rendered as 2D sprites. This has specific requirements:

### Render Settings

1. **Transparent background:** Render Properties > Film > Transparent
2. **Output format:** PNG, RGBA
3. **Render resolution:** Render at **2x–4x target size** then downscale. For 128x128 target cells, render at 512x512 and use Catrom/bicubic filter to downscale. This produces cleaner anti-aliased edges.
4. **Output as individual frames:** Not a contact sheet — one PNG per frame (Blender's default when using CTRL+F12)

### Recommended Cell Size

For a character that's ~48px tall in-game at 800x600 design resolution, **128x128** cells are the sweet spot:
- Enough detail for smooth anti-aliased edges
- Scale factor of `48/128 = 0.375` (reasonable)
- 8 frames in a 4x2 grid = 512x256 texture per animation (or packed tighter in an atlas)

256x256 cells are overkill for 48px characters — you're storing 5x more pixels than needed per frame.

### Key Differences from Pixel Art

| Aspect | Pre-Rendered 3D | Pixel Art |
|--------|-----------------|-----------|
| Frame sizes | May vary between animations | Uniform grid |
| Edge quality | Anti-aliased, needs 2px+ padding | Hard pixel edges, 1px padding fine |
| Packing | Packed atlas essential | Grid sheets viable |
| Origin consistency | Critical — bake into atlas | Natural in uniform grids |
| Cell sizes | 64–256px | 16–64px |
| Frame count | Easy to over-generate (rendering is free) | Each frame is labor |

---

## Tools

### Already Available (In Nez Submodule)

- **Nez.SpriteAtlasPacker** (`Nez/Nez.SpriteAtlasPacker/`) — built-in packer with folder-to-animation mapping, custom origins, configurable padding. No external dependencies needed.

### Worth Considering

- **ImageMagick** (free) — batch downscaling, trimming, montage. Useful as a post-render step: `magick convert frame.png -resize 128x128 -filter Catrom out.png`
- **TexturePacker** ($40) — MaxRectsBinPack, sprite trimming/rotation, edge extrusion, has a MonoGame exporter and NuGet loader. Best-in-class but costs money.
- **Free Texture Packer** (free, open source) — cross-platform alternative to TexturePacker.

### For Blender Automation

- **Sprite 2D** (itch.io add-on) — multi-angle rendering for side-scrollers, auto sprite sheet generation
- **Pre Render Creator** (itch.io) — direction presets, built-in spritesheet packing
- Custom Blender Python scripts for batch rendering specific frame ranges

---

## Recommended Pipeline for GorelordsBrawler

### Phase 1: Fix Current Issues (Immediate)

1. Switch from `SpritesFromAtlas` to `SpriteAtlas` + `SpriteAtlasPacker`
2. Export Blender frames as individual PNGs into animation subdirectories
3. Run the packer with `-originX:0.5 -originY:1.0 -pad:2`
4. Replace `CharacterFactory` sprite loading with `Content.LoadSpriteAtlas()` + `AddAnimationsFromAtlas()`
5. Adjust BoxCollider to use `LocalOffset` for bottom-center entity positioning

### Phase 2: Optimize Blender Export (Short-Term)

1. Reduce frame counts: 6–8 for idle, 8 for run, 4–6 for attack
2. Render at 2x target resolution (e.g., 256x256) and downscale to 128x128 with ImageMagick
3. Add a build script that runs the packer automatically

### Phase 3: Full Pipeline (Future)

1. Per-frame timing for attack animations (slow wind-up, fast strike)
2. Blender automation add-on for batch rendering all characters
3. CI/CD integration — packer runs on asset changes
4. Consider TexturePacker if atlas management becomes complex

---

## Sources

- [Nez SpriteAtlasPacker](https://github.com/prime31/Nez/tree/master/Nez.SpriteAtlasPacker) — built-in packer docs and source
- [Nez-Samples](https://github.com/prime31/Nez-Samples) — reference project with SpriteAnimator usage
- [Nez SpriteAnimator Source](https://github.com/prime31/Nez/blob/master/Nez.Portable/ECS/Components/Renderables/Sprites/SpriteAnimator.cs)
- [Nez SpriteAtlas Loader Source](https://github.com/prime31/Nez/blob/master/Nez.Portable/Assets/SpriteAtlases/Loader/SpriteAtlasLoader.cs)
- [MonoGame GraphicsProfile Docs](https://docs.monogame.net/articles/getting_to_know/whatis/graphics/WhatIs_GraphicsProfile.html) — texture size limits
- [MonoGame Optimizing Texture Rendering](https://docs.monogame.net/articles/tutorials/building_2d_games/07_optimizing_texture_rendering/index.html)
- [TexturePacker MonoGame Tutorial](https://www.codeandweb.com/texturepacker/tutorials/how-to-create-sprite-sheets-and-animations-with-monogame)
- [MonoGame.Aseprite Library](https://github.com/AristurtleDev/monogame-aseprite)
- [Fighting Game Animation Guide](https://kevurugames.com/blog/how-to-animate-a-fighting-game/)
- [Sprite Animation Frame Count Guide](https://www.sprite-ai.art/blog/sprite-animation-frames)
- [3D Rendered Pixel Sprites](https://cxong.github.io/2017/03/3d-rendered-pixel-sprites) — DKC-style pipeline analysis
- [Donkey Kong Country Technical Analysis](https://www.gamegrin.com/articles/why-donkey-kong-country-was-a-technical-marvel/)
- [Free Texture Packer](http://free-tex-packer.com/)
- [Texture Atlas Best Practices](https://www.numberanalytics.com/blog/mastering-texture-atlasing-game-dev)
