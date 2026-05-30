# Acid damage feedback — Phase 4 of acid-deadly-polish-plan

Parent plan: [docs/acid-deadly-polish-plan.md](acid-deadly-polish-plan.md) (Phase 4).

## Why

After Phases 1-3 the acid hazard now LOOKS dangerous (pulsing surface,
ambient bubbles, contact-puff sizzle, see-through-with-low-grav while
submerged). But when the damage actually applies the player gets:

- a red `HitFlash` on their sprite (Phase 2)
- a yellow sizzle puff at the contact line (Phase 2)

Both are LOCAL to the player. There is no screen-space "the world is
reacting to what just happened to you" signal. Two specific gaps that
players reading the screen don't get today:

1. **No proprioceptive thunk.** Each acid damage tick is silent for the
   camera. Melee hits trigger `CombatEffectsManager.TriggerHit` which
   shakes + freezes + flashes — acid ticks bypass that path because
   acid is contact-DOT-based, not hit-based. The result is that wading
   in acid taking 4 dmg/sec feels free, mechanically, even though it
   is chewing your stock.
2. **No "you are in trouble" gauge.** A player who has burned 75% of
   their HP from acid still sees a normal screen. The HUD bar shrinks,
   but the screen frame itself does not communicate urgency. Compare
   Hollow Knight's low-HP black vignette + "Fury of the Fallen" red
   border, which closes around the play area exactly to drive that
   panic signal.

Phase 4 adds three screen-space effects to close those gaps:

- **Per-tick screen shake** on every acid damage application. Small,
  additive into the existing trauma model, so it reads as a continuous
  shudder while standing in acid rather than a single jolt.
- **HP-driven vignette** that closes in (radial darkening from screen
  edges inward) as the worst-off alive player's HP drops. Always
  present at low HP, not just on the moment of damage — it is the
  "ambient panic" signal.
- **Chromatic aberration pulse** on every damage tick. Spike to a small
  peak offset then decay over ~250ms. Reads as "your eyes just took a
  hit" — momentary signal that pairs with the shake.

These three layer to give one cohesive language: contact = camera
flinch + RGB split (acute), worst-player-low-HP = closing vignette
(ambient).

## Sources

### Screen shake on damage

1. **Squirrel Eiserloh, GDC 2016 — "Juicing Your Cameras With Math"**
   ([slides](http://www.mathforgameprogrammers.com/gdc2016/GDC2016_Eiserloh_Squirrel_JuicingYourCameras.pdf),
   [video](https://www.youtube.com/watch?v=tu-Qe66AvtY)) — canonical
   trauma-based model: trauma ∈ [0, 1], decays linearly per frame,
   shake amplitude = `trauma²` or `trauma³`, multiple events
   accumulate via `trauma = min(trauma + amount, 1.0)`. Eiserloh also
   argues coherent noise (Perlin/Simplex) > random for smoother shake
   feel, and that in 2D both translation AND rotation shake compose
   well. Our existing `BrawlerCamera.AddShake` already implements the
   first three (trauma², linear decay, additive clamp) — it just uses
   random instead of Perlin and is translation-only. Phase 4 adds call
   sites, does not change the model. See "Trade-offs" for whether to
   upgrade to Perlin in this PR.
2. **Borderline Blog — "All purpose screenshake, the right way"**
   ([link](http://blog.borderline.games/tutorials/gettinghit!/trauma-based-screenshake.html))
   — practical write-up of the Eiserloh model with the exact additive
   accumulation formula we already use.
3. **kidscancode Godot Recipes — "Screen Shake"**
   ([link](https://kidscancode.org/godot_recipes/4.x/2d/screen_shake/index.html))
   — Godot port of the same model. Confirms `pow(trauma, 2-3)` is the
   industry default and that OpenSimplexNoise vs random is a tuning
   choice, not a correctness choice.
4. **Roystan — "Unity Camera Shake Tutorial"**
   ([link](https://roystan.net/articles/camera-shake/)) — Unity
   implementation, same model, same conclusions.
5. **Jan Willem Nijman, Vlambeer — "The Art of Screenshake"**
   ([summary](https://theengineeringofconsciousexperience.com/jan-willem-nijman-vlambeer-the-art-of-screenshake/))
   — the foundational game-feel talk. Key takeaway used here: shake on
   the player taking hits is as important as shake on landing hits.
   Acid ticks are "the player taking a hit", just slowly.
6. **Super Smash Bros — Special Zoom**
   ([SmashWiki](https://www.ssbwiki.com/Special_Zoom))
   — the "shared-screen feedback for important moments" principle.
   In 2-player matches, Special Zoom fires regardless of who took the
   hit; in 3+ player matches it's suppressed (info would be too
   chaotic). Direct precedent for our `min(alive HP%)` vignette
   decision: a shared screen-space signal that both players in our
   2P local-coop scenario care about. Also the "feedback should be
   rare/important to retain impact" principle motivates the acid-only
   scope — we don't want screen-space damage feedback firing on every
   melee hit when hits already have their own dedicated channel.

### HP-driven vignette

1. **Hollow Knight — low health vignette + Fury of the Fallen**
   ([wiki](https://hollowknight.wiki/w/Fury_of_the_Fallen),
   [disable-vignette mod source](https://github.com/luizzeroxis/DisableLowHealthVignetteMod))
   — the canonical reference. HK shows a black vignette at very low HP
   regardless of charm, and when Fury of the Fallen (1 HP + charm) is
   active, adds wispy red lines around the screen border. Two distinct
   layers, both driven by HP%. The disable-mod source confirms it is
   implemented as a full-screen overlay tied to the HP value, not a
   shader trick.
2. **Polydin — "The Psychology of Colors in Games"**
   ([link](https://polydin.com/psychology-of-colors-in-games/))
   — colour-convention reference: red signals danger, blood,
   aggressiveness; the "warm colours create urgency and tension"
   principle that motivates our black→dark-red lerp at low HP rather
   than pure-black dimming. Cites Titanfall 2's greyscale + red
   overlay as the modern reference for low-HP screen treatment.
3. **GameDev.net — "HLSL Vignetting"**
   ([link](https://gamedev.net/forums/topic/648646-hlsl-vignetting-solved/5099343/))
   — standard radial-distance + smoothstep implementation in HLSL.
4. **Mina Pêcheux — "Shader Journey #3: Basic Post-Processing Effects"**
   ([link](https://medium.com/geekculture/shader-journey-3-basic-post-processing-effects-e9feb900ceff))
   — covers vignette as one of the introductory post-process effects;
   same `length(uv - 0.5)` + smoothstep pattern.
5. **Microsoft HLSL docs — `smoothstep`**
   ([link](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-smoothstep))
   — formal definition. We will use a single `smoothstep(radius -
   softness, radius, dist)` to drive the darken multiplier.

### Chromatic aberration pulse

1. **Lettier — "3D Game Shaders For Beginners: Chromatic Aberration"**
   ([link](https://lettier.github.io/3d-game-shaders-for-beginners/chromatic-aberration.html))
   — radial CA with three independent per-channel offsets, sampled
   along `direction = uv - focusPoint`. Confirms the "split RGB at
   offset texcoords" technique with concrete GLSL code and discusses
   when to use radial vs cardinal direction.
2. **Harry Alisavakis — "My take on shaders: Chromatic Aberration"**
   ([link](https://halisavakis.com/my-take-on-shaders-chromatic-aberration-introduction-to-image-effects-part-iv/))
   — diagonal cardinal-offset variant in Cg/HLSL with the sample code
   we're cribbing from. Article notes: keep offsets very small (<0.01
   in normalized UV) or it reads as a glitch effect instead of a hit.
3. **Geeks3D Shader Library — Chromatic Aberration GLSL Demo**
   ([link](https://www.geeks3d.com/20101008/shader-library-chromatic-aberration-demo-glsl/))
   — another GLSL reference, useful for sanity-checking the radial
   form against the cardinal one.
4. **spite/Wagner — chromatic-aberration-fs.glsl**
   ([link](https://github.com/spite/Wagner/blob/master/fragment-shaders/chromatic-aberration-fs.glsl))
   — production-tested GLSL implementation with vector offset and
   strength uniforms, the same shape we'll use.
5. **LinkedIn — "How can you use a shader to create a chromatic
   aberration effect in a game?"**
   ([link](https://www.linkedin.com/advice/1/how-can-you-use-shader-create-chromatic-aberration-bszbf))
   — corroborates the pattern of driving CA strength from gameplay
   events (camera speed, damage) via a single scalar uniform.
6. **Bytevex — "Chromatic Aberration in Games — Love It or Hate It?"**
   ([link](https://bytevex.com/chromatic-aberration-in-games/))
   — overuse warning that motivates our conservative defaults.
   "Fast-paced action games and platformers can feel worse with
   chromatic aberration; the effect adds blur during rapid camera
   movements, making it harder to track objects and navigate
   precisely. Players report eye strain when the effect is combined
   with camera shake." Phase 4 combines CA + shake on every tick,
   so this is the exact scenario the article warns about — hence
   the tiny default (`CaPulsePeak = 0.004` UV) and short half-life
   (~87ms), with the explicit Risk #1 mitigation "smoke-test the
   worst case and back off." Confirms our acid-only decision —
   layering CA on top of melee's existing feedback would be the
   "overuse" pattern.

## Approach

Three layered screen-space effects, all gated through one
controller component for cohesive tuning.

### Effect 1 — Per-tick screen shake (no shader)

The existing `BrawlerCamera.AddShake(intensity)` already implements
the Eiserloh trauma model: clamps to 1.0, decays linearly via
`GameConstants.Combat.ShakeDecay` (6 trauma/sec), amplitude scales
`trauma²` to a max of `MaxShakeOffset` (8 px). No API change needed
— just add a call site on `ContactHazard.OnDamageApplied`.

Tick math: `ContactHazard` fires `OnDamageApplied` whenever
`damageBuffer` crosses 1.0. At 4 dmg/sec that is ~4 Hz. With decay
6 trauma/sec, the inter-tick window of 250ms erases 1.5 trauma.
That means the shake naturally PULSES once per tick rather than
sustaining a continuous hum — each thunk decays to near zero before
the next thunk hits. Good: that is the desired "you got bit again"
feel.

Per-tick intensity is `[Inspectable, Range]`, defaulting low (≈ 0.30
trauma per tick → peak amplitude 0.30² × 8 = ~0.72 px shake).

### Effect 2 — HP-driven vignette + Effect 3 — CA pulse (combined shader)

**One shader, one post-processor, one pass.** Both effects read from
the scene RT and write to the destination RT, applying CA first then
darkening with the vignette. Combining them is cheaper than two
sequential passes (one fewer fullscreen-quad draw, one fewer
intermediate RT swap) and keeps the order of operations explicit in
one file. Same pattern the existing `liquid.fx` uses — multiple
distinct visual effects composited into one shader.

Filename: `Content/Effects/damage_feedback.fx`.

**Vignette** is a radial colour blend: `dist = length(uv - 0.5) * 2`
(range 0 at centre → ~√2 at corners), then `mask =
smoothstep(radius - softness, radius, dist)`, then
`scene.rgb = lerp(scene.rgb, VignetteColor.rgb, mask * intensity)`.

`VignetteColor` itself is interpolated CPU-side based on HP%: at the
engage threshold (60% HP) the colour is black (pure dimming), at 0%
HP the colour is dark red. The red signals DANGER specifically, while
black is just dimming — convention in Titanfall 2, Hollow Knight's
Fury of the Fallen, and most modern action games is that LOW HP
should read RED, not just dark. The lerp gives both: calm tunnel at
~50% HP, panicked red at <10% HP.

**CA pulse** offsets each colour channel along a radial direction
from screen centre, magnitude scaled by the live `CAStrength` uniform:

```hlsl
float2 dir = (uv - 0.5) * 2; // outward direction from centre
float2 offset = dir * CAStrength;
float r = tex2D(SceneSampler, uv + offset).r;
float g = tex2D(SceneSampler, uv).g;
float b = tex2D(SceneSampler, uv - offset).b;
```

Radial (not cardinal) because radial is how real lens aberration
appears — visually more grounded than the diagonal split, per
Lettier. C# side drives `CAStrength` with an exponential decay from
peak on each tick.

**Why one shader instead of one post-processor per effect:** the plan
doc explicitly flagged "lots of post-processors stacking — easy to
over-juice." Combining keeps the budget honest. The two effects
compose cleanly anyway (CA on the colour, vignette on the brightness
— independent dimensions), and individual disable is still possible
by zeroing each effect's strength uniform.

### Controller component

`DamageFeedbackController : Component, IUpdatable` — one entity, one
component, holds:

- `[Inspectable, Range]` tunables for every knob (see Tunables
  section)
- Refs to: the `DamageFeedbackPostProcessor`, `PlayerManager`,
  `ContactHazard`, `BrawlerCamera`
- Subscribes to `ContactHazard.OnDamageApplied` in `OnAddedToEntity`,
  unsubscribes in `OnRemovedFromEntity`
- On damage event: `AddShake(AcidTickShakeIntensity)`, `CaPulse =
  CaPulsePeak`
- `Update()`: decays `CaPulse` with `CaPulse *= exp(-CaDecayRate *
  unscaledDelta)`, computes worst-alive-player HP%, derives vignette
  intensity, pushes both values to the post-processor

Same pattern as `AcidSizzleManager` — Component on an entity (not a
SceneComponent) so Nez's runtime inspector picks up the tunables.

### PostProcessor execution order

`LiquidPostProcessor` runs at order 0. Damage feedback runs AFTER
liquid so the vignette and CA apply to the FINAL composited image
(including the acid). Order 10.

### Hitstop interaction

`Time.UnscaledDeltaTime` for CA decay so that on melee hits (which
set `Time.TimeScale = 0` during hitstop), the CA does not freeze
mid-decay. The shake's existing `ApplyShake` uses `Time.DeltaTime` —
that holds shake during hitstop (which actually feels right for melee
since the freeze + held shake reads as "impact moment"). For acid
there's no hitstop so it does not matter. Keeping the existing shake
decay path unchanged.

## Trade-offs called out

1. **Combined shader vs two separate post-processors.** Combined wins
   on perf (one fullscreen pass) and keeps the post-processor stack
   shorter. Loses on modularity — disabling vignette but keeping CA
   means zeroing a uniform, not removing a stage. Acceptable: both
   effects exist together for the same reason (damage), so coupling
   them is not artificial.
2. **Random shake vs Perlin shake.** Eiserloh recommends coherent
   noise. Existing `ApplyShake` is random. Phase 4 does NOT upgrade
   this — the existing shake is shipping fine and a noise overhaul is
   a separate concern that should ship in its own PR (and would
   affect melee hits too, which is a behaviour change we don't want
   bundled with acid polish). Flagging as future work.
3. **One vignette for both players, driven by min(alive HP%).** Local
   co-op with shared screen means one vignette intensity for two
   players. Decision: drive intensity from `min(alive players' HP%)`
   — vignette closes when WORST player is dying. Smash Bros' Special
   Zoom is the closest precedent: shared-screen visual feedback that
   fires for "important moments" both players care about, regardless
   of who's involved. In a party brawler, "someone is about to die"
   is competitive information both players want.
4. **Acid-only damage feedback, not generalised to melee.** Decision:
   the new shake + CA pulse subscribe to
   `ContactHazard.OnDamageApplied`, not `Health.OnDamaged`. Melee
   already has the full Special-Zoom-equivalent (hitstop + shake +
   flash + particles via `CombatEffectsManager.TriggerHit`). Layering
   more feedback on top would hit the "Christmas tree" failure the
   parent plan explicitly warned about. Acid is exactly where the
   screen-space signal is missing today, so Phase 4 plugs that gap
   without touching what works. Generalisation to other damage
   sources lives in its own PR if ever wanted.
5. **CA pulses on every acid tick.** At 4 Hz with default exponential
   decay (~250ms half-life), pulses stack visibly without ever fully
   resetting. This is desired — wading in acid SHOULD feel like the
   world is shimmering at you. Tunable down if it overwhelms. Strong
   research signal here: CA is the effect MOST prone to overuse in
   fast-paced 2D games (causes readability problems + eye strain when
   combined with shake). Default peak offset is intentionally tiny
   (0.004 normalised UV ≈ 3.2 px at 800-wide design res); tune up
   only if invisible.
6. **Vignette colour lerps black → dark red as HP drops.** Decision
   per the Q&A round: pure black is just "dimming," but red is the
   convention for low-HP urgency (Titanfall 2 greyscale + red overlay,
   Hollow Knight's Fury of the Fallen). Lerp is CPU-side so the
   shader stays generic. Calm tunnel at moderate HP, panicked red
   close to death.
7. **Vignette engages at low HP only, not always-on.** A vignette
   that's faintly visible at 100% HP would dim the playfield with no
   signal value. `VignetteEngagesBelowHpRatio` default 0.60 keeps the
   screen clean while players are healthy and ramps 0→1 from there
   down to 0% HP. Could be linear from 100% — kept threshold because
   our HP values are small (default 100 HP, so 60% = 60 HP =
   ~15 acid ticks of headroom before vignette appears).

## File-by-file changes

### New files

| Path | Purpose |
|---|---|
| `GorelordsBrawler/Content/Effects/damage_feedback.fx` | Combined CA + radial-vignette pixel shader. Same `#if OPENGL` / sampler / pixel-shader / technique template as `liquid.fx`. |
| `GorelordsBrawler/Content/Effects/damage_feedback.mgfxo` | Compiled output from `dotnet mgfxc damage_feedback.fx damage_feedback.mgfxo /Profile:OpenGL`. Committed alongside the .fx. |
| `GorelordsBrawler/Components/PostProcessors/DamageFeedbackPostProcessor.cs` | `PostProcessor` subclass. Public mutable fields for shader uniforms (`CaStrength`, `VignetteIntensity`, `VignetteRadius`, `VignetteSoftness`). `Process()` pushes them all into `Effect.Parameters` and calls `DrawFullscreenQuad`. (New `PostProcessors` folder — none exist today; current shader-driven post-processor lives under `Components/Hazards/Fluid/`, which would be wrong home for a generic damage effect.) |
| `GorelordsBrawler/Systems/DamageFeedbackController.cs` | `Component, IUpdatable` on its own entity. Holds tunables + refs + event subscription. Drives the post-processor. Modelled directly on `AcidSizzleManager`. |

### Modified files

| Path | Change |
|---|---|
| `GorelordsBrawler/Scenes/ArenaScene.cs` | After `LiquidPostProcessor` is added: load `damage_feedback.mgfxo`, construct `DamageFeedbackPostProcessor` at order 10, `AddPostProcessor` it. After the `acid-sizzle` entity is created: `CreateEntity("damage-feedback")` + `AddComponent(new DamageFeedbackController(...))` with refs to the post-processor, `playerManager`, `contactHazard`, and `brawlerCam`. |
| `GorelordsBrawler/Constants/GameConstants.cs` | Add `DamageFeedbackPostProcessorOrder = 10` to `Rendering`. No new `Combat` constants — defaults live as field initialisers on the controller so live tuning starts from them, matching `AcidSizzleManager`. |

No changes to `BrawlerCamera`, `ContactHazard`, `Health`,
`LiquidPostProcessor`, `liquid.fx`. The contract for Phase 4 is:
existing `OnDamageApplied` event + existing `AddShake` API are the
sole hooks, and the new shader is independent of the liquid pipeline.

## Tunables

All on `DamageFeedbackController` with `[Inspectable, Range(min,
max)]` so they appear in the runtime inspector. Defaults shown are
starting values for the smoke-test; expect to live-tune during
review.

### Shake

| Field | Default | Range | What it controls |
|---|---|---|---|
| `AcidTickShakeIntensity` | `0.30f` | `[0, 1]` | Trauma added to `BrawlerCamera` per acid damage tick. Higher = bigger per-thunk shake. |

### Vignette

| Field | Default | Range | What it controls |
|---|---|---|---|
| `VignetteRadius` | `0.85f` | `[0.3, 1.5]` | Radial distance (in normalised "half-diagonal" units, 0 at centre, 1 at edge midpoint) where the darken band ENDS. Lower = vignette closes further in. |
| `VignetteSoftness` | `0.40f` | `[0.01, 1.0]` | Width of the smoothstep band. Higher = softer falloff. |
| `VignetteMaxIntensity` | `0.85f` | `[0, 1]` | Maximum blend-toward-VignetteColor at the outer edge when HP=0. 0 = no vignette, 1 = fully replaced. |
| `VignetteEngagesBelowHpRatio` | `0.60f` | `[0, 1]` | HP fraction below which the vignette starts to engage. Above this = vignette off. Linear ramp from this value down to 0%. |
| `VignetteColorAtEngage` | `Color.Black` | (Color picker) | Vignette colour at the engagement threshold — pure black = calm "tunnel of vision" dimming as urgency starts. |
| `VignetteColorAtZeroHp` | `new Color(120, 0, 0)` | (Color picker) | Vignette colour at 0% HP. Dark red signals DANGER per Titanfall 2 / HK Fury convention. Controller lerps `VignetteColorAtEngage` → this based on HP%; result is pushed to shader as a single `VignetteColor` uniform. |

### Chromatic aberration

| Field | Default | Range | What it controls |
|---|---|---|---|
| `CaPulsePeak` | `0.004f` | `[0, 0.02]` | Peak RGB-channel offset in normalised UV (one screen-width = 1.0). Per Alisavakis, very small values keep it reading as "damage" rather than "glitch." |
| `CaDecayRate` | `8.0f` | `[1, 30]` | Exponential decay rate per unscaled second. ~ln(2) / half-life. At 8 the half-life ≈ 87ms, full decay before next 250ms tick — pulses stack only mildly. |

## Risks

1. **Visual over-juice.** Three new effects at once is the classic
   "Christmas tree" failure. Mitigation: every effect has a max-intensity
   knob, all default low. Smoke-test the worst case (one player at 1 HP
   wading mid-acid) and back off any knob that drowns the screen.
2. **Shader compile failure on first compile.** `liquid.fx` was the
   first shader in the project and took multiple iterations. Mitigation:
   commit the compiled `.mgfxo` alongside the `.fx` (same pattern as
   liquid). Verify visually by setting `VignetteMaxIntensity = 1.0` +
   `VignetteRadius = 0.1` (small bright pinhole at centre) as a
   diagnostic during first run — proves the shader is bound and the
   radial calc is correct before claiming the composited look works.
   This addresses the past pattern noted in the task brief of
   "inferring shader correctness from final composite output."
3. **Perf on integrated GPU / Steam Deck class.** One added fullscreen
   pass at design resolution (800×600). The shader does 3 tex2D for CA
   + 1 length + 1 smoothstep + a couple multiplies — trivially under
   half a millisecond on any HW that runs the liquid pipeline. No
   risk, noting for completeness.
4. **Vignette behaviour during respawn.** Brief moment between
   `Health.IsDead` and respawn, the player's HP is 0 and they are
   "alive" for one frame? Need to verify the `IsDead` gating runs
   before vignette intensity is computed. Mitigation: filter out
   `IsDead` players from the worst-HP query.
5. **`Time.TimeScale = 0` during melee hitstop.** CA decays on unscaled
   time (intentional). Vignette is steady-state from HP — HP doesn't
   change during hitstop, so the vignette is stable. Shake decays on
   scaled time — same as today, no change.

## Verification plan

Per the workflow norms, this is what proves Phase 4 ships correctly.

### Pre-merge (during this PR)

1. **Shader diagnostic** (per Risk 2): on first run set
   `VignetteMaxIntensity = 1` + `VignetteRadius = 0.1` + `CaPulsePeak
   = 0.02` and verify (a) a small bright pinhole at screen centre with
   black everywhere else, and (b) visible RGB fringing on every tick.
   Both must be visible BEFORE tuning toward the production defaults.
   Then revert tunables to defaults.
2. **Smoke test**: `pwsh .claude/skills/smoke-test/smoke_test.ps1
   -Feature acid -OpenIde`. Existing 5-check sequence must still pass
   (no regressions in the existing acid feel). The recorded MP4 must
   show:
   - Vignette closes visibly as the player's HP drops past ~60%
   - A small camera thunk on every acid tick (look at the FPS
     counter at the screen edge — its position should wobble)
   - RGB fringe visible at the screen edge on each tick
3. **Worst-case visual stress**: in the same smoke test, observe a
   scene with player at <10% HP, mid-acid, taking ticks. Read: does
   the screen still feel readable? If not, lower
   `VignetteMaxIntensity` or `CaPulsePeak`.
4. **Build clean**: no new warnings (project already has 2 pre-existing
   warnings per CLAUDE.md, target is "no new").

### Post-merge

Decision criterion per the parent plan: after Phase 4 ships and is
played, decide whether Phase 5 (atmosphere) is needed or whether the
acid-deadly polish is done. If "feels deadly" already → stop.

## Out of scope (deliberately deferred)

- **Audio**: no new SFX in Phase 4. Audio is its own pass.
- **Per-player vignette / split-screen treatment**: too invasive for
  the shared-camera architecture, and not what the parent plan
  asked for.
- **Perlin/Simplex noise in `ApplyShake`**: see trade-offs #2.
- **Generalising damage feedback to melee hits**: see open Q2 / Q4.
- **Vignette colour driven by hazard type** (green for acid, red for
  fire, etc.): future hazards problem, no hazards plural yet.
