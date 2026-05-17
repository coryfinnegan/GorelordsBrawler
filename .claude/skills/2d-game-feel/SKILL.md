---
name: 2d-game-feel
description: Catalogue of "game feel" / "juice" techniques for making 2D game elements look alive, dangerous, impactful, or polished — broken down by what each effect targets (the element itself, the air around it, or player feedback on contact). Triggers on "make X look deadly/dangerous/alive/menacing/impactful/juicy", "polish the look of X", "the player needs feedback when X happens", "this hazard doesn't feel threatening", "add screen shake/flash/vignette/chromatic aberration". Use whenever a feature is mechanically working but doesn't read viscerally — pick techniques from the catalogue based on budget vs impact.
allowed-tools: Read, Edit, Write, Grep, Glob
---

# 2D Game Feel — techniques catalogue

When a feature is mechanically correct but doesn't FEEL impactful, the fix is layering small effects. None of these on their own do much; combined they make the difference between "object on screen" and "thing in the world".

Three categories, ordered by where the effect targets:

1. ON the element itself — make the thing look alive/charged/menacing
2. AROUND the element — sells presence in the air, atmosphere
3. ON player feedback — when the thing affects the player, sell the impact

Pick effects from each category proportional to the budget. Cheap + high-impact ones first; layer more as needed.

## Category A — ON the element

### A1. Pulsing surface glow (animated shader uniform)
Animate a brightness/color uniform in the element's shader with `sin(Time * speed)`. Makes the thing look "alive" or "energized" — implies internal activity even when nothing's happening. Trivial to add if you already have a post-process shader on the element.

Implementation: pass `Time = Time.TotalTime` to the shader every frame; in the shader do `pulse = sin(Time * 3.0) * 0.5 + 0.5;` and use it to lerp between two brightness values.

Cited: standard real-time graphics technique, ubiquitous in 2D shader collections like [Godot Shaders](https://godotshaders.com/).

### A2. Heat haze / refraction distortion
Sample a scrolling noise texture, displace texcoords slightly when reading the scene texture. Makes the air-above-the-element shimmer like real heat. Big visual lift; shader work but contained.

Implementation: in the post-process shader, `uv += noise(uv + Time*speed).xy * strength;`. Often combined with the [XorDev fluid shader](https://github.com/XorDev/2DFluids) pattern (already used in this codebase's `liquid.fx`).

Cited: [GameDev.net HLSL heat-haze thread](https://www.gamedev.net/forums/topic/421196-heat-hazedistortion-shader-question-hlsl/), [Godot Shaders heat haze](https://godotshaders.com/shader/heat-haze-shader/).

### A3. Bubble / fizz particles
Continuous slow `ParticleEmitter` at the surface — particles spawn at random positions, drift up, pop. Best for liquids; works for "energy fields" or "magical" effects too with different colors. Use the `nez-particles` skill in this repo.

Implementation: spawn rate 5–15/sec, lifespan 1–2s, slight upward velocity, alpha fades to 0.

Cited: [pixel-art bubbly acid pool tutorial](https://www.youtube.com/watch?v=xXYBMTnTems).

### A4. Color animation / hue drift
Slow `sin`-driven hue shift on the element's tint — ±10–20° around the base color. Reads as "unstable" or "alive". Trivial.

Implementation: in C# every frame, compute `hue = baseHue + sin(Time * 0.5) * 15;`, convert back to RGB, pass as a uniform.

### A5. Edge highlight / meniscus pulse
For liquids specifically — animate the surface highlight strip's width or brightness. Sells "boiling" / "charged surface".

### A6. Surface ripples
Procedural noise displacement of the visible surface (different from heat haze — this is the surface itself, not the air above). Sells "agitated".

## Category B — AROUND the element

### B1. Glow halo / additive light
The element emits light into the air around it. Two implementations:
- Additive sprite layer (cheap, fake): draw a large soft halo sprite under the element on a back render layer.
- Real 2D lighting pass (involved): see [MonoGame Example04 2D Lighting](https://github.com/manbeardgames/monogame-hlsl-examples) — render light masks to a target, multiply against scene in a post-process.

Cited: [PixelJunk Shooter 2's environment lighting](https://kotaku.com/pixeljunk-shooter-2-plays-with-light-and-acid-5565408).

### B2. Ambient steam / wisps
Low-rate `ParticleEmitter` of gray alpha-blend particles drifting upward off the element. Different from "smoke on damage" — this is constant atmosphere.

### B3. Ambient sound (out of scope for visual skills but cite it)
Low rumble or hiss loop near the hazard. Game-feel is multimodal; the visual catalogue is incomplete without the audio reminder.

### B4. Floating motes / sparks
Slow-drifting little additive dots in the air near the element. Cheap, sells "this is dangerous" for magic/electric/toxic hazards.

## Category C — ON player feedback

### C1. Contact particles (sizzle, sparks, splash)
`ParticleEmitter` triggered by the contact event. For acid → smoke puffs. For lava → sparks. For ice → frost shards. Hook into your damage event (e.g. `ContactHazard.OnDamageApplied(victim, contactPoint, damage)`).

This codebase has the `nez-particles` skill for the implementation.

### C2. Damage flash on character
Tint the player sprite white or red for a few frames per hit. The `HitFlash` component already exists in this codebase — wire it to the contact event.

### C3. Screen shake on damage tick
Brief, small (2–6 px) camera offset. [Feel Documentation on screen shakes](https://feel-docs.moremountains.com/screen-shakes.html) classifies them — pick "damped sine" for crisp impact, "noise" for sustained rumble. Implementation: add `Camera.AddShake(amplitude, duration)` API and decay over frames.

Cited: [Just Things Made by Dave — Analysis of Screenshake Types](http://www.davetech.co.uk/gamedevscreenshake).

### C4. Hit-stop / freeze frame
Briefly stop game time (50–100ms) when a hit lands. Sells weight. This codebase already has `CombatEffectsManager.TriggerHit()` doing this for melee — reuse for hazards.

### C5. Vignette intensifies with HP loss
Post-process. Edges of screen darken progressively as the player's HP drops. Battlefield-style. Implementation: add a vignette `PostProcessor`, drive its intensity from a `MaxHp - CurrentHp` reading.

Cited: [Mega Cat Studios Juice Guide](https://megacatstudios.com/blogs/game-development/tagged/game-juice).

### C6. Chromatic aberration pulse
On hit, separately shift the R and B channel sampling by a few pixels for one frame. Reads as "ouch". Post-process. Same Juice Guide source.

### C7. Controller rumble / haptic feedback
For controller play, brief rumble pulse on damage. (Keyboard-only games skip this.)

## Picking a combo

Diminishing returns: each effect on its own is meh; the first 3–4 together transform the feel; beyond ~6 it gets noisy.

A reliable formula for "this is a deadly hazard":
- 1 effect from category A (it lives)
- 1 effect from category B (it occupies space)
- 2 effects from category C (it hurts when touched)

E.g. for a deadly acid pool: pulsing glow (A1) + rising bubbles (A3) + contact sizzle (C1) + screen shake (C3). Add heat haze (A2) when you have shader budget. Add vignette (C5) when polish is the only thing left to do.

## Implementation references in this repo

- Nez `ParticleEmitter` pattern: `.claude/skills/nez-particles/SKILL.md`
- Post-process shader pattern: `.claude/skills/nez-liquid-rendering/SKILL.md`
- Smoke-test for visual verification: `.claude/skills/smoke-test/SKILL.md` (always run AND look at the screenshot before pushing visual changes)

## Gotchas

- Render layer matters. Particle emitters and overlays on the wrong `RenderLayer` can silently disable other renderables on the same layer (see PR #3 history). Default safe choices: contact effects on `HitboxRenderLayer`, ambient effects on `DefaultRenderLayer` (in front of background but behind characters via `LayerDepth`).
- Direct `GraphicsDevice.BlendState` swaps mid-render corrupt the Batcher's cached state for subsequent renderables. Always use Nez `Material` to switch blend modes.
- Pulsing animations look bad at the same frequency. If you have multiple animated uniforms, use prime-ratio frequencies (e.g. 1.0, 1.7, 2.3 Hz) so they never sync up.
- Screen shake amplitudes >8 px feel like a bug, not a feature. Stay subtle.
- Hit-stop > 150 ms makes the game feel laggy. Keep ≤ 100 ms.
- "Too much juice" is a real failure mode — see [Wayline on the juice problem](https://www.wayline.io/blog/the-juice-problem-how-exaggerated-feedback-is-harming-game-design). When in doubt, ship less.

## Sources

- [Mega Cat Developer Juice Guide v1.0](https://megacatstudios.com/blogs/game-development/mega-cat-developer-juice-guide-v1-0-08-23-17)
- [Juice in Game Design — Blood Moon Interactive](https://www.bloodmooninteractive.com/articles/juice.html)
- [Feel Documentation — Screen Shakes](https://feel-docs.moremountains.com/screen-shakes.html)
- [Analysis of Screenshake Types — davetech.co.uk](http://www.davetech.co.uk/gamedevscreenshake)
- [The Juice Problem — Wayline](https://www.wayline.io/blog/the-juice-problem-how-exaggerated-feedback-is-harming-game-design)
- [Godot Shaders Heat Haze](https://godotshaders.com/shader/heat-haze-shader/)
- [GameDev.net HLSL heat-haze](https://www.gamedev.net/forums/topic/421196-heat-hazedistortion-shader-question-hlsl/)
- [GM Shaders Mini — Fluids](https://mini.gmshaders.com/p/gm-shaders-mini-fluids-1507038)
- [XorDev / 2DFluids](https://github.com/XorDev/2DFluids)
- [PixelJunk Shooter 2 lighting — Kotaku](https://kotaku.com/pixeljunk-shooter-2-plays-with-light-and-acid-5565408)
- [MonoGame HLSL Examples — Example04 2D Lighting](https://github.com/manbeardgames/monogame-hlsl-examples)
- [Bubbly pixel-art acid pool tutorial](https://www.youtube.com/watch?v=xXYBMTnTems)
