# Acid "Looks Deadly" — phased polish plan

## Why

The acid hazard is mechanically working (PR #4 metaball renderer) but doesn't read as DANGEROUS. It looks like a green liquid; it should look like a corrosive, alive, threatening hazard.

Techniques considered in [.claude/skills/2d-game-feel/SKILL.md](../.claude/skills/2d-game-feel/SKILL.md). This doc breaks the polish into ordered phases — each phase is its own PR, smoke-tested and reviewed before the next starts.

## Principles

- One PR per phase. Each phase is small enough to review in one sitting.
- Each PR includes a smoke-test video so the visual change is verifiable.
- Skip ahead at any time if a phase is overkill.

## Phases

### Phase 1 — Make the surface look alive (THIS PR)
Smallest visible change that immediately reads as "this is active." Two effects, both reusing existing patterns:

- A1. Pulsing surface glow — animate the existing `EdgeColor` brightness in `liquid.fx` with a `sin(Time)` driven `Pulse` uniform. The shader is already loaded; just add one float parameter and one line of math.
- A3. Rising bubble particles — single Nez `ParticleEmitter` at the surface, low rate. Reuses the `nez-particles` skill. Bubbles randomly along the visible surface line.

No new shaders, no new render passes, no scene-component restructuring. Two file touches plus the bubble emitter wiring.

### Phase 2 — Sell the burn on contact
Player feedback when standing in acid.

- C1. Sizzle/smoke particle puff at the contact point — fires from `ContactHazard.OnDamageApplied` (event needs to be added). Pool of emitters, same pattern as `HitParticleManager`.
- C2. Damage flash on the player sprite — wire to the same event. The `HitFlash` component already exists in this codebase.

This is what the user originally instinct-suggested ("smoking particles when it touches the player"). It's not Phase 1 because Phase 1 sells "the thing is alive" with zero risk; Phase 2 introduces a new event hook and a new manager.

### Phase 3 — Heat haze
The standout visual lift. Modify `liquid.fx` to add a noise-driven displacement of the scene texcoords in a band ABOVE the acid surface — the air shimmers like real heat.

Risk: shader iteration. Plan to commit the noise texture as part of the PR (procedurally generated at startup to avoid asset pipeline work).

### Phase 4 — Damage-feedback post-processors
"You are being hurt right now" via screen-space effects.

- C3. Brief screen shake on damage tick — extend `BrawlerCamera` with an `AddShake(amplitude, duration)` API.
- C5. Vignette intensifying with HP loss — new `HpVignettePostProcessor`.
- C6. Chromatic aberration pulse on damage — another `PostProcessor` or merge into vignette.

Risk: lots of post-processors stacking — easy to over-juice. Cap any single effect's strength; tune live during the smoke test.

### Phase 5 — Atmosphere (only if needed)
- B1. Additive glow halo above the surface — fake light bleed.
- B2. Ambient steam wisps — slow upward gray particles.
- B4. Floating motes — small drifting embers in the air near the acid.

Possibly skipped if Phases 1-4 already deliver. Decide after seeing Phase 4.

## Stop conditions

- If at any phase the smoke-test video already reads as "deadly" by the reviewer's eye, halt. Don't ship effects just because they're on the list.
- If two phases in a row produce minimal perceived improvement, the polish is done — further effort goes elsewhere.

## Phase 1 detail (this PR)

Files touched:
- `GorelordsBrawler/Content/Effects/liquid.fx` — add `Pulse` uniform; modify `EdgeColor` use to multiply by `(0.7 + 0.3 * Pulse)` so the surface highlight breathes.
- recompile `liquid.fx` → `liquid.mgfxo` via `dotnet mgfxc`.
- `GorelordsBrawler/Components/Hazards/Fluid/LiquidPostProcessor.cs` — set `Pulse = sin(Time.TotalTime * 2.5) * 0.5 + 0.5` every frame.
- new `GorelordsBrawler/Systems/AcidBubbleEmitter.cs` — `SceneComponent` owning one Nez `ParticleEmitter`, retargets to a random x on the surface every spawn.
- `GorelordsBrawler/Scenes/ArenaScene.cs` — `AddSceneComponent(new AcidBubbleEmitter(acidSurface, mw, mh))`.

Verification:
- `pwsh .claude/skills/smoke-test/smoke_test.ps1 -Feature acid` — all 5 checks pass, recording uploaded, screenshot inspected.
- Look for: visible bubbles rising off the surface, visible pulse on the bright surface highlight when watched over a few seconds.
