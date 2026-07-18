# Acid Arena Design Proposal — "The Sump"

## Why this proposal exists

The acid hazard's **visual** layer is done (`docs/acid-deadly-polish-plan.md`, PRs #4–#10): a real Position-Based-Fluids metaball sim, bubbles, sizzle, heat-haze, submersion shader, damage-feedback post-processors. That sim is the single largest piece of bespoke tech in the project.

But the **arena around it does not let that tech perform.** Today:

- The map ([`Content/maps/arena1.tmx`](../GorelordsBrawler/Content/maps/arena1.tmx)) is a flat walled box: a full-width floor, a few symmetric ledges, walls on all four sides (no blast-zone pits).
- The acid is a **full-width** pool that rises from below after a 30 s delay ([`AcidPhaseManager`](../GorelordsBrawler/Systems/AcidPhaseManager.cs)), with a spawner that drops **one** log at a time ([`PlatformSpawner`](../GorelordsBrawler/Systems/PlatformSpawner.cs)).
- Contact is a flat **4 HP/s** drip with **no knockback and no depth lethality** ([`ContactHazard`](../GorelordsBrawler/Components/Hazards/ContactHazard.cs), `GameConstants.Hazards.AcidDamagePerSec = 4`). Being "knocked into the pool" currently means standing in it losing 4/s — it does not read as a kill.

**Goal:** redesign the arena so the *liquid itself is the central play feature* — a vessel that makes the fluid pool, pour, overflow, wave, and kill by depth. This combines the four things we agreed on: **knock-in** (spacing your opponent into the acid), a **rising flood as a round timer**, periodic **surges**, and **falling platforms** to scramble across.

This is a level-design + gameplay-systems proposal. It deliberately builds on the existing sim rather than touching the solver.

---

## Design pillars (the rules we commit to)

These are non-negotiable; every phase below is judged against them.

1. **The fluid's real shape is the danger.** [`ContactHazard.GetBounds`](../GorelordsBrawler/Components/Hazards/ContactHazard.cs:14) is wired to [`AcidSurface.GetDamageBounds()`](../GorelordsBrawler/Components/Hazards/AcidSurface.cs:262), which returns the **live wet-cell bounds** of the simulation. So when the surface heaves in a surge or laps over a lip, the kill zone moves with it. We design *around* this rather than faking a static damage rectangle.
2. **Every phase showcases a different fluid behaviour** — resting body, pouring stream, overflow-over-a-lip, buoyancy/splash, travelling wave, drain. The sim never stops earning screen time.
3. **Depth = death, but there is always a way out.** A toe-dip at the surface is a painful scare you can jump out of; being *launched deep* (or caught under a surge) melts you fast. Crucially, submersion is **escapable by skilled input** — a thrash/swim-up (see Phase B) lets a reacting player claw back to the surface. Death is the result of being launched deep *and at high damage* *and* failing to react in time — a consequence of a read, never an instant-kill-on-touch. Depth-of-launch + reaction speed becomes the spacing skill.
4. **Every dynamic beat is telegraphed.** Per the level-design literature, players need a warning proportional to a hazard's severity, delivered through **two or more channels**. A slow rise barely needs one; an instant surge needs a clear ~0.75–1 s tell (visual + audio + camera). Cheap deaths kill the fun faster than any difficulty.
5. **Always a legible safe spot** until the deliberate round-ending flood. Chaos is fine *if* the player can always read where "not dying" is. (Sakurai's stage philosophy: hazards are tactical variables, not unavoidable punishment.)

---

## The arena: "The Sump"

A **central acid basin** that doubles as the knock-in pit *and* the flood reservoir, flanked by two solid banks where players fight, with refuge tiers climbing toward the ceiling. Same 1280×800 / 40×25 @ 32px grid as today.

```
col  0         1         2         3
     0123456789012345678901234567890123456789
r0   ########################################  ceiling / wall
r1   #............▼..........▼............#    TWO sim inlets, above the basin mouth corners
r2   #............|..........|............#    two streams fall straight into the basin
r5   #.=====......v..........v....=====...#   TOP tiers — last refuge
r9   #...............======...............#   CENTER-HIGH tier
r13  #.....=====..............=====.......#   MID tiers
r17  #.....S..................S...........#   spawns, on the banks (face center)
r18  ##############............##############  BANKS (solid). Gap = basin mouth
r19  ##############............##############
r20  ##############............##############
r21  ##############~~~~~~~~~~~~##############  acid rests here at match start
r22  ##############~~~~~~~~~~~~##############
r23  ##############~~~~~~~~~~~~##############
r24  ########################################  basin floor
```

(`#` solid/collision, `=` one-way refuge ledge, `~` starting acid, `▼` spout/inlet, `S` spawn. Tier widths/positions are a starting blockout to refine in Tiled.)

**On the two inlets (functional-test correction, Phase C).** The streams pour from the **very top corners of the map** (cols 2 / 37, clear of every tier span): they fall the full arena height, land on the banks, sheet across them, and **cascade over the lips into the pit** — the pour itself is a spectacle, and the banks being wetted by it is intended (the acid is coming for everything). An earlier draft placed lip-level "wall valves" at the basin mouth to dodge the top tiers; the corrected design supersedes it. Inlet positions/velocities are pure data in `AcidConfig.Inlets`.

**Why this shape:**

- **Central basin (cols 14–25).** Holds a real, deep, *visible* body of acid from second one. The mouth of the basin (the bank edges at cols 13 / 26) is the **knock-in lip**: launch an opponent off a bank into the mouth and they go into the deep. This is the "knock each other into the pool" fantasy, live for the entire match — not just during the flood.
- **The basin is also the reservoir.** When the rise phase begins, the inlet fills the basin until it **overflows the lip onto the banks** — a moment only a real fluid sim can render (liquid spilling over an edge and spreading). That single visual sells the whole feature.
- **Stepped refuge tiers.** As the flood climbs, it swallows the banks, then the mid tiers, then the high tiers — one readable step at a time (pillar 5). Each lost tier is an "it just got worse" beat.
- **Banks as the neutral fighting floor.** Symmetric, so neither player has a positional edge at spawn; spawns face the center so the basin is the contested space between them.

**Camera:** stays `Static` (whole-map, fit-to-fit — [`ArenaScene.cs:178`](../GorelordsBrawler/Scenes/ArenaScene.cs:178)). For a rising flood this is *correct*: the player sees the threat climbing toward them across the entire arena. No change needed.

**Coupling to keep in sync (important):** the acid inlet X and the flood/spawn trigger heights are currently derived from the normalized `GameConstants.Hazards.Platforms` array, **not** from the TMX ([`AcidSurface` ctor](../GorelordsBrawler/Components/Hazards/AcidSurface.cs:79), [`AcidPhaseManager` ctor](../GorelordsBrawler/Systems/AcidPhaseManager.cs:32)). If we redraw the TMX without updating that array, the inlet pours in the wrong place and the flood triggers at the wrong height. Phase A keeps them in sync by hand; Phase C decouples them properly into an explicit acid config (no shortcut — the hand-sync is a known debt we pay down).

---

## The match timeline

Each phase foregrounds a different fluid behaviour and is driven by a system we already have.

| Phase | Player experience | Fluid showcase | Driven by | Telegraph |
|---|---|---|---|---|
| **0 — Calm** | Fight on the banks. Shallow acid simmers in the basin; knock-ins are live. | Resting body, bubbles, edge glow | Pre-filled basin + `AcidBubbleEmitter` | — (always-on hum) |
| **1 — Rise** | Both inlets gush; basin fills and **overflows the lip** onto the banks. Players retreat up. | Two pouring streams + overflow-over-edge | Dual inlet pour (`SpawnInlet`); `Activate()` | Klaxon + Monitorr line; spouts drip→gush |
| **2 — Scramble** | Banks gone; platforms fall and **float**; hop tier to tier. | Buoyancy, tilt, splash-on-impact | `PlatformSpawner` + `DynamicPlatform` (already calls `Disturb` on landing) | Platform shadow / spawn cue |
| **3 — Surge** | A wave heaves up out of the basin; the kill zone spikes for ~1 s. **More frequent and more violent every loop.** Be high or get clipped. | **Travelling wave — the money shot** | New `AcidSurface.TriggerSurge()` (volume burst + upward impulse) | Shake build + bright edge + bubbles, ~1 s lead |
| **4 — Drain → loop / Final flood** | A sluice drains the level (relief), then it climbs again **harder** (loop back to 1–3). If no one has died by the time cap, it floods to the top tier and ends the round. | Draining flow → re-fill; final total submersion | Drain inlet + escalation curve; level-cap = round end | Final-flood warning |

The **rise is the round timer**; **surges** spike on top of it and **intensify every loop** (your call — "feel the panic as things get crazier"); **falling platforms** are the scramble mechanic; **drain-to-loop** extends the match while ratcheting tension, with the **final flood as the decisive escalation at the time cap**. This is the hybrid you asked for, expressed as one *escalating* curve, not a flat loop.

---

## Implementation phases (one PR each)

Following the established "one reviewable, smoke-tested PR per phase" pattern from the polish plan.

### Phase A — Greybox the vessel
**Build the Sump geometry and prove the fluid lives in it.**

- Redraw [`arena1.tmx`](../GorelordsBrawler/Content/maps/arena1.tmx): central basin, two banks, refuge tiers, refuge tiers as one-way platforms (`nez:isOneWayPlatform` tile property so you can jump up through them). Greybox tiles are fine here.
- Update `GameConstants.Hazards.Platforms` + spawn objects to match (keep the inlet/trigger coupling correct — see note above).
- Add `AcidSurface.PreFill(restLevelY)`: spawn a block of particles into the basin at scene start so Phase 0 has resting acid (today the map starts dry and only fills after the 30 s inlet delay). The basin's TMX collision tiles contain them via the existing `FluidCollider.RebuildFromPhysics` path; the first few `Step` collider queries must cover the basin region.
- **Lethality stays as-is this PR** (flat 4 HP/s) — we are only proving geometry + pooling + containment + no-NaN here.

*Verification:* `pwsh .claude/skills/smoke-test/smoke_test.ps1 -Feature acid`. Acid pools in the basin and stays put; a player walked into the basin takes damage; `acidParticleCount` stable and `acidFinite` true (no regression of the hitstop→NaN failure mode — see CLAUDE.md "Common Pitfalls").

*Risk:* tile collision gaps letting particles leak out of the basin. Mitigate by sealing the basin walls in the collision layer and smoke-testing the resting pool for 30 s.

### Phase B — Depth-scaled lethality + an escape mechanic + knock-in payoff
**Make contact actually matter, the right way (pillar 3) — deadly *and* escapable (your call).**

The model is **depth-scaled damage + a thrash/swim escape.** Two halves that must ship together:

**1. Depth scales the damage.** Shallow contact is survivable; deep submersion ramps to lethal. Combined with the existing knockback scaling — up to 3× at low HP ([`CombatMath.KnockbackScale`](../GorelordsBrawler/Combat/CombatMath.cs:10)) — a high-damage opponent launched off the lip goes *deep* and is in serious trouble. Depth-of-launch = the spacing skill.

**2. There is always a way out — a swim-up (your call: "there should be a way to escape").** Today [`SubmersionFeel`](../GorelordsBrawler/Components/SubmersionFeel.cs) only reduces gravity to 0.45 and adds drag — it has *no* upward force, so a deeply-launched player just sinks and dies, which contradicts "escapable." Add a sibling **`SwimAbility`** ability component (data-driven, attached in `ArenaScene` next to `SubmersionFeel`, reading the same `IsSubmerged` signal):
  - While submerged, **pressing/mashing jump applies a strong upward impulse** (claw toward the surface) instead of the normal grounded jump. This is standard 2D swim physics — a velocity impulse on input, the kind of calc that doesn't need a sim — modelled on Mario-style underwater "stroke to rise."
  - Clean integration: [`JumpAbility`](../GorelordsBrawler/Components/Abilities/JumpAbility.cs:33) only fires its jump when `_body.Grounded`, and a submerged player isn't grounded, so there is **no double-apply conflict** — `SwimAbility` simply owns the jump button while `IsSubmerged`.
  - The race this creates is the whole point: launched **shallow** → one or two strokes out, minor chip; launched **deep at high damage** → you take a big bite and *might* not stroke out in time = the earned KO. Skill and reaction decide it, not a coin flip.

This is what reconciles "depth = death" with "there should be a way to escape": the exit always exists, but the deeper you are and the more damage you've taken, the smaller the window.

- Extend [`ContactHazard`](../GorelordsBrawler/Components/Hazards/ContactHazard.cs):
  - Add optional `Func<Entity, float> DamagePerSecondScale` (default null → 1×). `ArenaScene` sets it to a depth curve using `acidSurface.GetLocalSurfaceLevelAtX(x, headY)` vs the player's body — exactly the query `SubmersionFeel` already uses.
  - Make the integer damage buffer **per-entity** (`Dictionary<Entity,float>`) instead of one shared `_damageBuffer`, since players now take damage at different rates. (Today the shared buffer truncates a single global rate — fine for flat damage, wrong for per-player depth scaling.)
  - Keep firing `OnDamageApplied` unchanged so `AcidSizzleManager` (puff + flash) and `DamageFeedbackController` (shake/CA/vignette) keep working with zero changes.

*Starting tuning curve (refine live in the smoke test — these are gameplay numbers, the kind we tune by feel). The DPS and the swim-up strength are tuned **together**: the swim impulse must be able to out-climb the shallow/mid damage so the escape is real, while the deep damage out-paces it for someone launched deep at low HP.*

| Depth of body below local surface | Damage/sec | Feel (with swim-up available) |
|---|---|---|
| 0 px (surface lap) | ~10 | painful chip; one stroke and you're out |
| ~24 px (knee deep) | ~30 | act *now* — a couple of strokes to escape |
| ~48 px+ (submerged) | ~80–100 | strokes barely keep up; survivable only if you react instantly and weren't already high-% — otherwise the KO |

*Verification:* smoke test — toe-dip trivially escapable; a deep launch at low HP is lethal if you're slow but survivable if you mash out instantly; a melee launch off the lip into the deep reads as a kill against a low-HP opponent. Add a unit test on the depth→DPS curve **and** a check that the swim impulse net-beats the mid-tier DPS (pure functions, no engine deps, same style as `CombatMathTests`).

*Risk:* the two halves are coupled — too-strong damage or too-weak swim makes it an instant-death ledge (breaks "escapable"); too-weak damage or too-strong swim makes the acid harmless (breaks "deadly"). Tune them as a pair in the same smoke-test session; don't ship one without the other.

### Phase C — The phase machine + dual inlets + escalation
**Turn `AcidPhaseManager` from "delay → rise → drop logs" into the looping, intensifying Calm → Rise → Scramble → Surge → (Drain → loop) → Final-flood state machine.**

- Refactor [`AcidPhaseManager`](../GorelordsBrawler/Systems/AcidPhaseManager.cs) into explicit states with durations/triggers (`Calm`, `Rise`, `Scramble`, `Surge`, `Drain`, `FinalFlood`), and a **loop counter** that drives the escalation curve.
- **Dual inlets (your call: pour from both corners).** Generalize [`AcidSurface`](../GorelordsBrawler/Components/Hazards/AcidSurface.cs:79) from a single `_inletX` to a **list of inlets** (positions above the two basin-mouth corners). `SpawnInlet` iterates them. Keep the **total** flow rate budgeted (split across the two inlets, not doubled) so the particle budget below still holds. This is a real change to `AcidSurface`, in scope for v1 — it's what makes the two converging waterfalls physical, not painted.
- **Escalation curve (your call: "crazier and crazier").** Each loop iteration ratchets the chaos as an explicit function of the loop counter: **surge frequency up, surge strength up, and rise speed/height up.** Encode it so it's one readable curve (e.g. `surgeInterval = base * pow(decay, loop)`, `surgeStrength = base * pow(growth, loop)`), tuned live. This is the macro "panic" the round builds toward; the swim-escape (Phase B) is the per-knock-in micro counter-play. They compose.
- Add `AcidSurface.TriggerSurge(strength)`: a thin façade over the sim — a short, intense burst from the inlets (raises the level transiently) **plus** an upward `ApplyImpulseInRadius` across the basin to throw a visible wave. Note today's [`Disturb`](../GorelordsBrawler/Components/Hazards/AcidSurface.cs:219) pushes *down* (a splash dimple); the surge needs the opposite, hence a new method.
- Add a **drain**: temporarily reverse net flow (stop inlets + despawn from the bottom, or open a floor sluice that removes particles) so the level visibly recedes between loops — the relief beat before it climbs again harder.
- Tune `PlatformSpawner` to keep **N≥2–3 platforms alive** during Scramble (today it keeps only 1, [`_totalTrackedCount <= 1`](../GorelordsBrawler/Systems/PlatformSpawner.cs:57)) so "jump platform to platform" is actually possible.
- Decouple acid inlet/fill/trigger heights from `GameConstants.Hazards.Platforms` into an explicit `AcidConfig` (inlet positions, rest level, overflow level, tier-flood levels, drain target, surge/escalation curve, time cap). Pays down the Phase-A hand-sync debt.

*Verification:* drive a full match via the debug server; observe each phase transition and at least two loop iterations; confirm overflow-over-the-lip occurs during Rise, both inlets pour, a wave occurs during Surge, the level recedes during Drain, and surges measurably intensify loop-over-loop.

*Risk — particle budget (real constraint):* `FluidConfig.MaxParticles = 5000`. A brim-full arena needs ~15 000 particles — **out of budget.** So each rise is **capped at the top-tier line**, the **drain frees particles** before the next loop re-uses them, and the **final flood caps at the top tier = the round-end trigger** — we never need to fill the whole map. Surges are transient impulses, not permanent volume. Dual inlets split the existing flow budget rather than adding to it. The cap is a design feature (rounds end decisively), not a workaround.

#### Phase C.1 addendum — calibration & pacing (from the 2026-07-01 real-speed capture)

The as-built Phase C was re-measured at real speed and corrected; four findings changed the design:

1. **Measured particle density.** Every particles↔height conversion assumed hex packing (55.4 px²/particle); the solver actually settles at ~31 px² (`FluidConfig.EffectiveParticleArea`, pinned by a headless settle test). Under the old math no ceiling ever reached a platform in a real match. All caps/flows now derive from the measured value, and the `pacing` smoke feature asserts cap↔surface end-to-end.
2. **Contest-then-consume rise schedule.** Contact erosion self-limits at the waterline, so a ceiling that LAPS a tier (432 across the low tiers' bodies) produces "fighting on dissolving ground," while a ceiling PAST the tops (392) shell-erodes it in ~3 s. Ceilings: 528 (banks awash) → 432 (contest lows) → 392 (consume lows). Inlet flow escalates per loop (60→222 particles/s) so every rise lands in ~30 s; the drain derives its rate at entry for a fixed ~9 s relief beat.
3. **The terminal phase is a STORM, not a fill.** A literal full-map fill needs ~25k particles at measured density — 1.7× the budget. Instead the storm pours to a standing surface just under the mid tiers (~10.7k, in budget) while recurring high-strength crests break over the mid tiers (destroying them) and claw down the top perches — the match ends because the last footing crumbles, which also kills top-tier camping. Surge sweeps follow the live wet span (not just the basin) so crests actually reach the refuges.
4. **Floating logs erode at their own rate with hull-tracked buoyancy.** A floater keeps fresh wood at the waterline forever (no self-limit), so logs get ~4× slower erosion (~20 s life) and buoyancy reads the surviving hull — eaten logs ride lower instead of hovering above the water. Log population now escalates with the loop (2→4): the late game gains debris as it loses tiers.

### Phase D — Telegraphing & juice (pillar 4)
**Make every dynamic beat fair and satisfying.** All cues already exist in the codebase:

- **Surge tell (~0.75–1 s lead):** bubbles intensify + surface bulges at the source, `EdgeColor` pulse brightens (already in `liquid.fx`), camera-shake build-up via [`BrawlerCamera.AddShake`](../GorelordsBrawler/Components/BrawlerCamera.cs:27), rising audio whine — *then* the wave.
- **Rise/flood tell:** a klaxon + a **Monitorr** voice line ("The grid is hungry…") + spouts that drip before they gush. This doubles as the fairness warning *and* a narrative beat — per the GDD, the acid is one of **Total Master's grid traps**, so triggering it on a timer/comeback is on-lore.
- Redundancy across visual + audio + camera is the point; a single channel isn't enough.

*Verification:* smoke-test video review — can the reviewer's eye predict each surge ~1 s out? If a death ever feels unwarned, the phase isn't done.

*Risk:* over-juicing. The polish plan already warns about the "Christmas tree" failure mode; cap each effect and tune live.

### Phase E — Art pass (the on-brand step)
**Replace the greybox with a rendered tileset that matches the digitized-toy look.**

We already render 3D toy models to 2D sprites in Blender (`tools/sprite_sheet_baker.py`, `tools/build_atlas.py`). Render the **vessel tileset the same way** — corroded acid-stained metal troughs, riveted grid panels, dripping spouts, a pitted basin lip — cut to the 32px grid. It will match the characters perfectly and be unique to the Death Grid. Per the GDD the painterly backdrop stays hand-painted (Scott-Wills style); only the *interactive grid* is rendered.

*Free packs to greybox/blockout with now* (the genuinely pre-rendered-3D side-view look is rare for free, so treat these as scaffolding, not final): [Kenney — Platformer Pack Industrial (CC0)](https://kenney.nl/assets/platformer-pack-industrial), [OpenGameArt — CC0 3D Platform Tiles](https://opengameart.org/content/cc0-3d-platform-tiles), [Game Art 2D — Free Sci-Fi Tileset](https://www.gameart2d.com/free-sci-fi-platformer-tileset.html).

---

#### Phases C.2 / D / E addendum — as implemented (2026-07-02, approved "do the rest of the phases at once")

- **C.2** landed per `docs/sump-layout-redesign-proposal.md` (see its as-built
  status note): 8 tiers in four bands — diving boards over the pit (the first
  destruction beat, drowned by loop 1), lows contested→consumed as before,
  mids pulled inward behind a committed gap-jump, tops narrowed with a 128 px
  center gap.
- **D** shipped as ONE telegraph channel (`AcidSurface.BeginTell/TellProgress`)
  driving three synchronized cues — bubbles boil harder, the meniscus pulse
  quickens/brightens (prime-ratio frequency vs the idle breath), the camera
  builds a trauma rumble — armed before every surge, storm crest, AND rise
  (the valves open only after the rumble, Brinstar-style). Deterministic E2E
  asserts the tell precedes the wave via the `acidTellActive` oracle.
  **Audio cues are deliberately deferred**: the codebase has no audio system
  at all yet; per the working agreement that's a dependency for its own
  proposal, not something to half-wire here.
- **E** shipped as a GENERATED art pass (`tools/gen_arena_tileset.py`,
  Pillow, deterministic seed): 8-tile corroded-steel vessel tileset (machined
  top plates, riveted walls, acid-stained basin variants, low-contrast
  background) + textured destructibles (bolted-steel tier slab, rough-timber
  log) that the erosion carves through as stable images. Regenerable and
  parameterized like the map itself. The painterly BACKDROP stays hand-painted
  per the GDD and is out of scope; the final look is the user's call from the
  smoke recording / manual pass.

| File | Phase | Change |
|---|---|---|
| `Content/maps/arena1.tmx` | A, E | Redraw to the Sump; final art tiles |
| `Content/tilesets/arena.*` | A, E | Greybox tiles → rendered vessel tileset |
| `GameConstants.cs` (`Hazards`) | A, C | Platform array sync; then explicit `AcidConfig` |
| `AcidSurface.cs` | A, C | `PreFill()`, dual-inlet list, `TriggerSurge()`, drain |
| `ContactHazard.cs` | B | `DamagePerSecondScale`, per-entity buffer |
| New `SwimAbility.cs` | B | Submerged swim-up impulse (the escape mechanic) |
| `SubmersionFeel.cs` | B | Expose `IsSubmerged`/depth for `SwimAbility` + damage curve (already exposes `IsSubmerged`) |
| `ArenaScene.cs` | A–C | Wire pre-fill, depth curve, swim ability, phase machine |
| `AcidPhaseManager.cs` | C | Calm/Rise/Scramble/Surge/Drain/FinalFlood loop + escalation |
| `PlatformSpawner.cs` | C | Keep N platforms alive |
| New `AcidConfig.cs` | C | Decoupled inlets/levels/drain/escalation/time-cap config |
| audio + `liquid.fx` cues | D | Telegraph polish |

---

## Decisions (locked in — answered by Cory)

1. **Round-end model → drain-and-loop, final flood as the escalation at a time cap.** The level rises, surges, then drains for relief and climbs again *harder*; if no one has died by the time cap, a final flood to the top tier decides it. (Phase C: `Drain` + `FinalFlood` states.)
2. **Lethality → deadly but escapable.** Full submersion melts fast, but a thrash/swim-up always gives an exit; the window shrinks with depth and damage. (Phase B: `SwimAbility` shipped *together* with the depth-damage curve.)
3. **Surge cadence → intensifies over the match (time/loop-based), not match-state-based.** The explicit goal is escalating panic — surges get more frequent and more violent every loop. (Phase C: escalation curve on the loop counter.)
4. **Corner spouts → real dual inlets, in v1.** Acid physically pours from both basin-mouth corners and converges in the deep; `AcidSurface` generalizes to an inlet list (total flow budgeted, not doubled). (Phase A geometry + Phase C inlet list.)

## Still genuinely open (lower-stakes, can settle during implementation)

- **Exact time cap** before the final flood (90 s? 120 s?) — tune once the loop *feels* right in a smoke test.
- **Drain trigger** — fixed timer per loop, or only drain if both players are still alive (so a clean match keeps escalating)? Leaning timer for predictability.
- **Spawn safety on respawn during a flood** — a respawning player must not appear inside the acid. Likely respawn onto the highest dry tier; worth a dedicated check in Phase C.

---

## Sources

Design references behind the pillars and timeline:
- [Stage Design in Melee & Beyond — Source Gaming](https://sourcegaming.info/2017/07/22/melee_stage_design/) — readability, size-as-tool, safe-platform consistency, hazards as tactical variables.
- [Stage hazard — SmashWiki](https://www.ssbwiki.com/Stage_hazard) — hazard taxonomy (vehicle / static / weather / transformation).
- [Ten Principles of Good Level Design — Game Developer](https://www.gamedeveloper.com/design/ten-principles-of-good-level-design-part-2-) — "tell the player what, not how"; readability.
- [Survival Game Design — gamedesignskills](https://gamedesignskills.com/game-design/survival/) — telegraphing incoming hazards; escalating pressure.
- Knock-into-hazard precedent worth studying: SoulCalibur ring-outs, Dead or Alive danger zones, Mortal Kombat acid/pit stages.

Internal:
- `docs/acid-deadly-polish-plan.md` — the completed visual layer this builds on.
- `docs/environment-system-proposal.md` — the Tiled map foundation.
- CLAUDE.md "Common Pitfalls" — the hitstop `dt=0` → NaN failure mode the smoke test must keep guarding.
