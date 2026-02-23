# Gorelords: Digital Mayhem — Visual Impact Guide
## Achieving Huntdown-Level Polish in MonoGame / Nez

> **Pipeline note:** Characters are pre-rendered sprite sheets from Blender + Mixamo. Normal maps are exported as companion render passes at zero extra cost — no third party tools needed. Backgrounds are hand-painted. SpriteLamp is not needed.

---

## The Gap Between Nez and Huntdown

Nez gives you a solid ECS, sprite rendering, and basic post-processing hooks. What Huntdown has on top of that is a **layered visual language**: every hit, every movement, every idle moment has micro-feedback. The gap isn't one big missing feature — it's 6–8 systems working in concert. Here's what they are and exactly how to tackle them in your stack.

---

## 1. Dynamic 2D Lighting with Normal Maps

**What it does:** Gives flat sprites the illusion of volume. When a muzzle flash fires or an explosion goes off, nearby characters and environment pieces get lit from that source direction. This is one of Huntdown's most recognizable visual traits — the neon-soaked, source-lit grime.

**Is this achievable in MonoGame?** Yes. It requires rendering to multiple `RenderTarget2D` buffers (a diffuse pass and a light accumulation pass), then compositing them with a custom HLSL shader. This is a well-understood technique in the MonoGame community.

**How to implement:**
- Draw your scene normally to a diffuse render target.
- Draw a light map separately: for each light source, draw a radial gradient using `BlendState.Additive` to accumulate light contributions.
- Composite: pass both textures into a shader that multiplies them together.
- For normal-mapped lighting (true depth response), each sprite needs a companion normal map texture. The shader samples both and computes a dot product against the light direction vector.

**References:**
- **Working MonoGame deferred lighting engine** (point lights, spotlights, normal mapping): https://github.com/Felsir/LightingEngine
- **MonoGame 2D normal mapping with HLSL** (MIT license, full source): https://github.com/Arharim/Simple-2D-hlsl-shader-for-monogame
- **Simple lightmap tutorial for MonoGame** (good starting point before going full deferred): https://www.gamedev.net/tutorials/programming/engines-and-middleware/2d-lighting-system-in-monogame-r4131/
- **SpriteLamp** — tool for generating normal maps from sprite art: https://snakehillgames.com/spritelamp/

**Gorelords consideration:** Since you're rendering from Blender, export a Normal pass from the compositor at the same time as your diffuse sheet. It's pixel-perfect and free. In Blender's compositor, add a Normal render pass output node alongside your Image output. Every character gets accurate normal maps automatically — no extra work per animation.

---

## 2. Post-Processing Shader Stack

**What it does:** The "cinematic grunge" look of Huntdown — the scanlines, the slight color bleed, the glow around neon elements — all comes from a post-processing pass applied to the final composited frame. This is the single highest ROI visual upgrade available.

**Is this achievable in MonoGame?** Yes. Nez has a built-in `PostProcessor` system. You render your scene to a render target and then chain shader passes over it before presenting.

**The key effects to implement (roughly in order of impact):**

**Bloom:** Extracts bright pixels, Gaussian blurs them, and additively blends them back onto the scene. Makes lights, explosions, and glowing UI elements pop.
- Ready-made MonoGame bloom library: https://github.com/UnterrainerInformatik/BloomEffectRenderer
- Also available as a Nez PostProcessor — check `Nez.Portable/Graphics/PostProcessors/`

**Chromatic Aberration:** Splits RGB channels slightly apart, especially at screen edges. Adds a "camera lens" feel to impacts and transitions.
- MonoGame HLSL thread with working code: https://community.monogame.net/t/chromatic-aberration-effect/8458
- The logic: sample the red channel slightly shifted left, green centered, blue slightly right. Pulse the shift amount on hit events.
- MonoGame.Randomchaos.Services includes a post-processing framework with chromatic aberration baked in: https://github.com/NemoKradXNA/MonoGame.Randomchaos.Services

**CRT / Scanlines:** Darkens every other horizontal scanline. Free retro texture on top of your scene. Combine with subtle vignette (darken corners) for a contained, cinematic feel.

**Film Grain:** Adds per-frame noise to the final image. Very cheap in HLSL (a simple hash function on UV + time). Keeps the image from looking "clean" in a bad way.

**Monogame HLSL examples repo** — hands-on examples for all of the above: https://github.com/manbeardgames/monogame-hlsl-examples

**MonoGame official shader tutorial:** https://docs.monogame.net/articles/tutorials/building_2d_games/24_shaders/

---

## 3. Juice: Screen Shake, Hit Freeze, and Camera Impact

**What it does:** "Game feel." When a punch lands in Huntdown, the entire frame reacts — a brief freeze, a sharp shake, a flash. This is what separates a game that looks good from one that *feels* violent. For a brawler, this is non-negotiable.

**Is this achievable in MonoGame/Nez?** Yes — Nez already has `CameraShake` built in.

**The three-part hit response system:**

**Hit Freeze (Hitstop):** On a successful hit, pause the game simulation for 3–8 frames while still rendering. The attacker and defender freeze mid-motion. The brain reads this as weight and impact. Implement by gating your `Update()` logic behind a hitstop timer.

**Camera Shake:** Nez's `CameraShake` component handles this. Call `Shake(intensity, degradation)` on contact. Use directional shakes — if the hit travels right, shake the camera right-then-back, not just randomly.
- Nez source: https://github.com/prime31/Nez/blob/master/Nez.Portable/ECS/Components/CameraShake.cs

**Hit Flash:** Flash the struck character white (or your chosen palette color) for 1–2 frames. This requires a per-sprite shader that replaces all non-transparent pixels with a solid color. Simple HLSL, high readability impact.

**Screen punch (zoom micro-pulse):** On major hits, briefly zoom in 1.02x then snap back. Subtle but powerful. Modify camera zoom in your hit response handler.

---

## 4. Particle System — Hits, Blood, Debris

**What it does:** Every attack in Huntdown has a particle signature — sparks on metal hits, blood on flesh hits, dust on landing, smoke on death. These are what make moment-to-moment combat readable and satisfying.

**Is this achievable in MonoGame/Nez?** Yes. MonoGame.Extended includes a full particle system. Nez also has particle emitter support.

**How to approach it:**
- Use `MonoGame.Extended.Particles` for your emitters. It supports modifiers for velocity, color interpolation, scale over lifetime, rotation, and opacity fade.
- Quick start guide: https://www.monogameextended.net/docs/features/particles/quick_start/
- Build a `ParticleEffectLibrary` — a static registry of named effects (e.g. `"hit_flesh"`, `"hit_metal"`, `"explosion_small"`) that your combat system fires by name. This keeps particle calls data-driven and consistent with your existing JSON character architecture.
- Use `BlendState.Additive` for sparks and fire, `BlendState.AlphaBlend` for blood and debris.
- Keshi figures are plastic — lean into sparks, cracks, and paint chip effects rather than gore. This also fits the toy aesthetic authentically.

**Sprite sheet effects:** For complex one-shot effects (big explosions, death pops), hand-animated sprite sheets played back through a simple frame animator beat particle systems every time. Tools like Aseprite let you create these efficiently. Prioritize sprite sheet effects for signature moments, particle systems for continuous/reactive feedback.

---

## 5. Parallax Backgrounds and Environmental Depth

**What it does:** Huntdown's levels feel dense and alive because backgrounds have 3–5 depth layers moving at different rates. Characters feel grounded in a physical space rather than pasted on a flat image.

**Is this achievable in MonoGame?** Yes. This is pure camera math — multiply the camera's world offset by a depth coefficient (0.0 = locked to camera, 1.0 = moves with world, 0.2 = slow distant layer) for each layer.

**Implementation:** MonoGame.Extended's `OrthographicCamera` gives you a transform matrix. For parallax, maintain separate `SpriteBatch.Begin()` calls per layer, each with a different transform matrix derived from the camera position multiplied by that layer's parallax factor.

**Add life to backgrounds:**
- Animated tiles (flickering neon signs, dripping pipes) — even 2-frame animations make static environments feel alive
- Foreground layer (faster than world speed) of debris, chains, or atmospheric elements that pass in front of characters
- Subtle camera lag: instead of locking camera to player position directly, lerp toward it with a slight delay. Adds weight to movement.

---

## 6. Sprite Animation Quality and Frame Data Presentation

**What it does:** Huntdown characters feel weighty because they have squash/stretch, anticipation frames, and follow-through. With Blender + Mixamo you can achieve this directly by modifying the animation data before rendering.

**The key principle:** Mixamo animations are captured from real humans with realistic proportions and timing. Keshi figures are chunky and exaggerated. Before rendering, key-correct your Mixamo animations in Blender — push poses further, exaggerate anticipation frames, slow down windup and speed up the hit. The rendered sprite sheet bakes that exaggeration in permanently.

**Practical techniques:**
- **Motion blur:** Enable Blender's motion blur in render settings for fast attack animations. It bakes into the sprite sheet and reads as speed in game at zero runtime cost.
- **Foot sync:** Use the animation speed scaling technique (distance-based playback) in MonoGame to keep feet matched to movement speed — drive `_animator.Speed` from actual velocity magnitude.
- **Landing squash:** Either key it in Blender before rendering, or handle it in MonoGame by briefly scaling the sprite on Y (0.85) and X (1.15) on landing contact. Both work.
- **Impact freeze frame:** Render a dedicated "hit received" pose as a 1-frame animation. Swap to it in MonoGame during hitstop frames.
- **Anticipation on heavy attacks:** Exaggerate the windup in Blender before rendering. Even 2–3 frames of backward lean before a heavy hit reads clearly at 60fps.

---

## 7. UI and HUD Design Language

**What it does:** In Huntdown, the HUD is part of the art direction — chunky pixel health bars, animated stock counters, screen-edge framing. A polished HUD signals production value before the first punch is thrown.

**Recommendations:**
- Keep HUD elements pixel-aligned at your native resolution before scaling. Sub-pixel HUD movement looks broken.
- Animate health bar changes: flash white on damage, drain with a slight delay (ghost bar technique — a trailing "last health" bar that slowly catches up to current).
- Stock/lives indicators as iconographic sprites of the characters, not numbers. Fits the keshi toy theme perfectly — mini figure silhouettes.
- Screen-edge darkening (vignette in your post-processing pass) naturally frames the HUD zone.

---

## 8. Color Grading and Palette Discipline

**What it does:** Huntdown has a very controlled neon-on-dark palette. Every level has a dominant color temperature. This isn't accidental — it's a LUT (Look-Up Table) applied in post, and it's the fastest way to make your game look "directed" rather than assembled.

**Is this achievable in MonoGame?** Yes. A color LUT is just a texture lookup — sample your final rendered color, use its RGB values as coordinates into a 256x16 or 512x512 LUT texture, output the remapped color. Add as the final pass in your post-processing chain.

**Implementation approach:**
- Start by authoring your palette in Aseprite or Photoshop. Define your level color temperatures (e.g., Level 1 = cyan/teal neon on near-black, Level 2 = magenta/orange on dark grey).
- Export as a LUT texture and load it into your post-processor.
- Swap LUT textures per level for instant mood shifts.

**Reference:** Search "HLSL color LUT post processing MonoGame" — the implementation is ~10 lines of HLSL.

---

---

## 9. Title Screen and Publisher Screen Visuals

### The Aesthetic

Three distinct visual registers that each have a purpose:

**Violence Toy publisher splash** — This is Zach's brand, not yours to define. Show it with respect. The synthwave grid and chrome logo are Violence Toy's established identity from 2015. Display it cleanly, let it play, then transition out.

**Gorelords title screen** — 90s CD-ROM FMV grime. This is Gorelords' own identity, distinct from Violence Toy's synthwave cool. References: Nemesis/Cyborg Cop, Death Machine, Lawnmower Man, 7th Guest, Night Trap, Dark Angel. VHS artifacts, cyborg POV overlays, cheap digitized video texture, signal noise. Feels like booting up a forbidden CD-ROM in 1994. The title card should feel dangerous and broken, not slick.

**Gameplay visuals** — Huntdown benchmark. The FMV aesthetic lives in the menus and transitions. Once you're in a match it's clean, readable, and responsive.

---

### Effect 1: VHS Tape Shader

The defining effect for publisher splash screens. Combines four sub-effects — all independently toggleable.

**Horizontal jitter (tape tracking error):** Randomly offset entire horizontal scanlines on X by a few pixels. Worse at top and bottom of screen like a real tape. Driven by noise that changes every 2–4 frames, not every frame — real VHS glitches hold briefly before shifting.

**Color bleeding:** Smear only the red channel 3–5 pixels to the right. VHS had poor chroma resolution. Horizontal blur on R only.

**Luminance banding:** Slight brightness variation that scrolls slowly upward — a sine wave on Y plus time, modulating brightness ±5–10%. Simulates VHS head pass.

**Signal dropout:** Occasionally replace a thin horizontal strip with white for 1–2 frames. Random and rare. The most "broken tape" effect — keep it subtle.

```hlsl
float4 MainPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoord;

    // Tape jitter
    float jitter = (noise(uv.y * 200.0 + Time * 3.0) - 0.5) * JitterAmount;
    uv.x += jitter;

    float4 col = tex2D(SpriteTextureSampler, uv);

    // Red channel bleed
    col.r = tex2D(SpriteTextureSampler, uv + float2(BleedAmount, 0)).r;

    // Luminance banding
    col.rgb += sin(uv.y * 2.0 + Time * 0.5) * 0.04;

    // Scanlines
    float scanline = fmod(floor(uv.y * ScreenHeight), 2.0) * ScanlineIntensity;
    col.rgb *= 1.0 - scanline;

    return col;
}
```

---

### Effect 2: Cyborg POV Overlay (Nemesis-style)

The cheesy cyborg vision effect from Nemesis. Perfect for character select, intro sequences, or a special move activation. Composite of cheap tricks that together read as "machine seeing through electronic eyes."

**Targeting reticle overlay:** A fullscreen texture with crosshairs, grid lines, partial hex framing — rendered in desaturated green or amber with additive blend so it glows over everything underneath.

**HUD text crawl:** Scrolling monospace text in bright green or amber — fake telemetry, damage readouts, targeting coordinates. None of it needs to mean anything. Scroll Y position over time.

**Tint desaturate pass:**
```hlsl
float gray = dot(col.rgb, float3(0.299, 0.587, 0.114));
col.rgb = lerp(col.rgb, gray * TintColor.rgb, TintStrength);
```
Green tint = night vision. Amber tint = infrared. Toggle between them per character or scene.

**Sobel edge scan:** Run edge detection on the scene, render edges as a glowing overlay. Characters look like they're being scanned. Samples 8 neighboring pixels, returns edge intensity — high contrast edges glow, flat areas disappear.

**Sweep line:** A bright horizontal line that sweeps slowly down the screen, briefly brightening pixels it passes over. A sin wave on Y position pulsing intensity over time.

---

### Effect 3: Static Noise Transition

Transition between publisher splash screens and title. Image dissolves through static — starts as pure noise, scene pushes through it.

```hlsl
float noise = frac(sin(dot(uv, float2(12.9898, 78.233)) + Time) * 43758.5453);
float4 staticColor = float4(noise, noise, noise, 1.0);
return lerp(staticColor, sceneColor, step(noise, TransitionProgress));
```

Drive `TransitionProgress` from 0.0 to 1.0 in C# over your desired transition duration.

---

### Effect 4: CRT Screen Warp

For the title screen — full CRT treatment with screen curvature and bezel.

**Barrel distortion:**
```hlsl
float2 centered = uv - 0.5;
float dist = dot(centered, centered);
uv = uv + centered * dist * WarpAmount;
```

**Bezel vignette:** Hard dark falloff at screen edges. Makes it look like a monitor in a dark room rather than a signal floating in space.

**RGB phosphor mask:** Tiling 3-pixel-wide RGB stripes at very low opacity. Reads as texture at low resolutions, invisible at 1080p — always adds subtle depth.

---

### Effect 5: Gorelords Title Screen — 90s CD-ROM FMV Aesthetic

This is not synthwave. This is the specific visual language of early FMV games — cheap digitized video, heavy post-processing artifacts, cyborg telemetry overlays, the feeling of a computer that's barely containing something dangerous. The Gorelords logo should feel like it was captured off a VHS tape of a Pyun film and digitized onto a CD-ROM in 1993.

**The logo treatment:**

Rather than a clean render, the Gorelords title should look like it was composited in Video Toaster on an Amiga. Render the logo in Blender but with intentional imperfection — a slight camera angle rather than dead flat, dramatic underlighting from below (the classic horror/industrial lighting setup), deep shadows, maybe a hint of motion blur baked in like the camera moved slightly during capture.

Then in MonoGame, layer the FMV effects on top of it:

**Interlace artifact:** Render the logo at half vertical resolution and scale it back up, or simulate it in the shader by sampling every other scanline from a slightly offset UV. This is the specific look of digitized video from that era — not clean pixel art, not smooth HD, but that chunky interlaced video texture.

```hlsl
// Interlace simulation
float scanline = floor(uv.y * ScreenHeight);
float2 sampleUV = uv;
if (fmod(scanline, 2.0) > 0.5)
    sampleUV.y += InterlaceOffset; // slight vertical offset on odd lines
float4 col = tex2D(SpriteTextureSampler, sampleUV);
col.rgb *= (fmod(scanline, 2.0) > 0.5) ? 0.85 : 1.0; // darken odd lines
```

**Chroma smear:** More aggressive than the VHS bleed — pull the color channels apart more noticeably around high-contrast edges. The Gorelords title reveal can start with heavy chroma separation that resolves down to normal over 1–2 seconds as the "signal locks in."

**Digitization noise:** Random pixel-level noise that's chunkier than film grain — 2x2 or 4x4 pixel blocks of noise rather than per-pixel. This is what cheap video digitizer cards produced. 

```hlsl
// Chunky digitizer noise
float2 blockUV = floor(uv * ScreenSize / NoiseBlockSize) * NoiseBlockSize / ScreenSize;
float noise = frac(sin(dot(blockUV, float2(12.9898, 78.233)) + Time * 0.1) * 43758.5453);
col.rgb += (noise - 0.5) * NoiseAmount;
```

**The reveal sequence:** The title doesn't just appear. It resolves — static first, then the image pushes through in bands from top to bottom like a VHS tape tracking into sync. Use the static noise transition from Effect 3 but make it directional, sweeping down the screen rather than random across it.

**Cyborg POV framing for the title:** Instead of a clean centered logo on black, frame the title screen with the Nemesis-style HUD overlay — targeting brackets around the logo text, a readout in the corner that says something like `COMBAT SYSTEM ACTIVE` or `FIGHTERS DETECTED: 4`, a slowly scanning sweep line. This ties the title screen to the game's identity. You're in the cyborg's perspective from the first frame.

---

### Effect 6: Violence Toy Publisher Splash

Display Zach's established brand cleanly. The Violence Toy animation already exists — if you have the video file, play it directly using a video texture or pre-rendered frame sequence. Don't try to recreate it in shaders. Show it as-is, let it run, then transition to the Gorelords title via the static noise transition.

If you need to build it programmatically (no video file), the key elements are the two-plane perspective grid with blue-purple and red-pink layered grids, the neon triangle framing device with heavy pink bloom, and the chrome VIOLENCE text with scrolling reflection. But the authentic animation already exists — use it.

---

## Recommended Implementation Order

**Gameplay visuals:**
1. **Screen shake + hit freeze + hit flash** — one afternoon, immediate feel improvement
2. **Foot sync animation speed scaling** — drive animator speed from velocity magnitude
3. **Particle system library** — hits, deaths, landing dust
4. **Post-processing pipeline: bloom + vignette + scanlines** — transforms the visual register
5. **Parallax backgrounds** — makes levels feel like places
6. **Dynamic lighting (lightmap first, then deferred)** — highest complexity, highest payoff
7. **Color LUT** — grade after all other visuals are final
8. **Normal maps from Blender** — export companion pass at render time, integrate last

**Title and publisher screens:**
1. **VHS shader + static noise transition** — publisher splash to title transition, high impact immediately
2. **Interlace + chroma smear + digitization noise** — the core Gorelords FMV title aesthetic
3. **Cyborg POV framing overlay** — targeting brackets and HUD text around the title logo
4. **Gorelords logo** — render in Blender with intentional imperfection, dramatic underlighting
5. **Violence Toy splash** — display the existing animation as a frame sequence or video texture

---

## Key Libraries & Resources Summary

| System | Resource | URL |
|---|---|---|
| Shader intro (MonoGame official) | Pixel shaders, SpriteBatch integration | https://docs.monogame.net/articles/tutorials/building_2d_games/24_shaders/ |
| Bloom post-process | Ready-to-use MonoGame library | https://github.com/UnterrainerInformatik/BloomEffectRenderer |
| Chromatic aberration + post fx framework | MonoGame.Randomchaos.Services | https://github.com/NemoKradXNA/MonoGame.Randomchaos.Services |
| HLSL examples collection | Multiple working shader demos | https://github.com/manbeardgames/monogame-hlsl-examples |
| Deferred 2D lighting | Felsir's LightingEngine for MonoGame | https://github.com/Felsir/LightingEngine |
| Normal map 2D lighting | Simple HLSL shader, MIT license | https://github.com/Arharim/Simple-2D-hlsl-shader-for-monogame |
| Penumbra (soft shadows) | Windows only, but reference implementation | https://github.com/discosultan/penumbra |
| Particle system | MonoGame.Extended official docs | https://www.monogameextended.net/docs/features/particles/quick_start/ |
| Camera shake | Built into Nez, CameraShake.cs | https://github.com/prime31/Nez/blob/master/Nez.Portable/ECS/Components/CameraShake.cs |
