# Phase 3 — "In-acid presence" — visibility silhouette + low-grav physics

## Why

Functional-test feedback after Phase 2 (PR #6 merged): once the player is submerged in the green metaball acid, you can't tell where they are, and the movement still feels like dry-land platforming. Two pieces:

1. **Visibility.** Some kind of effect that shows where the character is when they're inside the liquid.
2. **Physics feel.** Low-grav-ish — not floating, not swimming, but a clearly different feel from running on a platform.

Quote: *"Once you're in the acid it's very hard to tell where you are. I think it would be beneficial if we had some kind of effect that would show your character in the liquid. I also think gameplay wise we should kind of have low grav physics or something that indicates they are in liquid. They won't float or swim but just you know a difference."*

This replaces the heat-haze item that was Phase 3 in [acid-deadly-polish-plan.md](acid-deadly-polish-plan.md) — heat haze is bumped (still on the list, just not next).

## What I researched

Before proposing, I searched the web for prior art on both pieces (per the role expectation in [CLAUDE.md](../CLAUDE.md)). Four-plus sources each:

**Visibility — character-through-occluder techniques:**
- [GDQuest: drawing a character's silhouette in Godot](https://www.gdquest.com/tutorial/godot/shaders/silhouette-2d/) — viewport mask + silhouette shader. Render the character to a viewport, use it as a mask, draw a silhouette where it's occluded.
- [Cyanilux: 2D water shader breakdown](https://www.cyanilux.com/tutorials/2d-water-shader-breakdown/) — distortion + threshold + tinted body. Their post says: *"outlines were becoming visible below water, which was identified as an important problem to resolve for any future screen reading effects"* — confirms that any submerged-character effect has to coexist with the existing liquid composite, can't just be drawn naively.
- [Unity discussion: 2D character silhouette behind objects](https://discussions.unity.com/t/2d-urp-character-silhouette-behind-the-object/878274) — render-target tricks, custom transparency sort modes.
- [GDevelop: 2D silhouette visible through walls](https://forum.gdevelop.io/t/2d-silhouette-visible-throw-walls/40501) — sprite duplicated on a higher layer with depth-test tweaks.

The common thread: **you can't just turn up the render layer**, because the liquid post-process composites the metaball field over the entire scene render target. To put pixels ON TOP of the acid, the rendering has to happen AFTER the post-processor — either in a second post-processor that runs later, or by modifying the liquid shader to mask out player regions.

**Physics — in-water platformer feel:**
- [error454: Platformer Physics 101](https://error454.com/2013/10/23/platformer-physics-101-and-the-3-fundamental-equations-of-platformers/) — terminal velocity + drag fundamentals.
- [Lem Apperson: Beginning Game Development - Fluid Dynamics](https://medium.com/@lemapp09/beginning-game-development-fluid-dynamics-de4b1c301a6c) — buoyancy formulas; explicitly notes the simpler approach of "gravity scale" for arcade-y feel.
- [Unity discussion: Specific question about underwater physics (2D)](https://discussions.unity.com/t/specific-question-about-underwater-physics-2d/129423) — author advocates a separate physics path when submerged.
- [Job Talle: 2D platformer physics](https://jobtalle.com/2d_platformer_physics.html) — drag as a per-frame velocity damping.
- Classic prior art (no link, well-known): **Super Mario Bros underwater** does two things — reduced gravity (so falls are slower) and reduced jump strength (so each tap of A gives a smaller bump). **Sonic underwater** adds drag (Sonic decelerates faster when not pressing direction).

The cleanest minimum to deliver "you can tell the player is in liquid" without swimming or floating: **scale gravity down** (slower fall) + optionally **slow horizontal velocity** (drag). Don't add upward buoyancy — that violates the "won't float" constraint.

## Approach

### Piece A — Visibility: extend `liquid.fx` to reduce the metaball mask inside player rectangles

**Revised from v1 of this plan.** v1 proposed a second `PostProcessor` running after the liquid one and drawing player silhouettes on top. That approach was rejected during review for being lazy / non-optimal:

| | v1 (2nd post-processor) | v2 (modify `liquid.fx`) — chosen |
|---|---|---|
| Extra fullscreen passes/frame | **+1** (copy source then sprite draws) | 0 |
| Extra render targets in flight | **+1** ping-pong buffer (~3 MB at 1280×800 RGBA) | 0 |
| Pixel-shader cost | unchanged | +4 AABB tests/pixel (max 4 players, unrolled) |
| GPU bandwidth (integrated / Steam Deck class) | significant — every pixel of an extra full-res RT written per frame | negligible |
| Visual quality | silhouette layered over acid (clearly "above" it) | scene shows THROUGH the acid in player regions (clearly "in" it) |

v2 is both cheaper and visually more correct ("you see the player IN the liquid", not "on top of the liquid").

**Implementation:**

1. Extend `liquid.fx` with three new uniforms:
   ```hlsl
   #define MAX_PLAYERS 4
   float4 PlayerRects[MAX_PLAYERS];    // xy = uvMin, zw = uvMax (UV space, 0..1)
   int    PlayerCount;                 // 0..MAX_PLAYERS
   float  PlayerMaskStrength;          // 0..1, how much to reduce bodyMask in player regions
   ```

2. In `LiquidPS`, after `bodyMask` is computed, reduce it where a player overlaps:
   ```hlsl
   float playerMask = 0.0;
   [unroll]
   for (int i = 0; i < MAX_PLAYERS; i++) {
       float4 r = PlayerRects[i];
       // Inactive slots default to (0,0,0,0) — never match an interior pixel
       bool inside = (i < PlayerCount)
                    && uv.x >= r.x && uv.x <= r.z
                    && uv.y >= r.y && uv.y <= r.w;
       playerMask = max(playerMask, inside ? 1.0 : 0.0);
   }
   bodyMask *= lerp(1.0, 1.0 - PlayerMaskStrength, playerMask);
   ```
   With `PlayerMaskStrength` ≈ 0.7, the acid becomes ~30% opacity over players → the player sprite shows through clearly while the acid still tints it green.

3. Optional polish in the same shader pass: tint the scene a bit greener inside player regions, so the player reads as "stained by the acid" rather than fully clear:
   ```hlsl
   float3 underwaterTint = lerp(float3(1,1,1), float3(0.6, 1.0, 0.7), playerMask * 0.4);
   scene.rgb *= underwaterTint;
   ```

4. Extend `LiquidPostProcessor`:
   - Constructor takes a `PlayerManager` reference (already constructed in `ArenaScene` before the post-processor is added).
   - `Process(source, destination)`:
     - Gather active players, get each one's collider Bounds and transform corners to UV via `Camera.WorldToScreenPoint(...) / new Vector2(source.Width, source.Height)`.
     - Pack as `Vector4[]`; pad unused slots with zeros.
     - `Effect.Parameters["PlayerRects"].SetValue(rects);` + count + strength.
     - Recompile `liquid.fx` → `liquid.mgfxo` via `dotnet mgfxc Content/Effects/liquid.fx Content/Effects/liquid.mgfxo /Profile:OpenGL`.

5. `PlayerMaskStrength` is plumbed from `FluidConfig` (a new constant), live-editable on rebuild — same pattern as `LiquidThresholdMin/Max`.

**Sizzle/HitFlash compatibility:** sizzle smoke renders on `HitboxRenderLayer` (in the scene RT, before the post-process). When `bodyMask` is reduced in player regions, sizzle smoke at the contact line becomes MORE visible there — strictly an improvement to existing damage feedback, not a regression. HitFlash tints the player sprite, which now shows through the acid; the red flash will be visible-but-greened by `underwaterTint` (which is fine for "you're being damaged underwater" reading).

**Risks specific to v2:**
- `ps_4_0_level_9_1` profile is conservative; 4-iteration unrolled loop + simple AABB tests are well within its capability (verified against MonoGame shader profile docs). If by some weirdness it doesn't compile, fallback is to fully manually unroll to four copy-paste `if`s.
- Player collider bounds vs sprite bounds may not match exactly — sprite might extend slightly past the rect or vice versa. For MVP, rect-based is good enough (the user reads the body shape inside the rect). If pixel-perfect is needed later, the upgrade is to render players to a small mask RT and pass that as a texture — that's a real second pass and we'd accept the cost knowingly.
- Camera transform per player is a tiny C# cost (4 players × 1 matrix multiply) — negligible.

Sources for this approach (researched after the v1 pushback):
- [MonoGame Community: float4 array passing to HLSL](https://community.monogame.net/t/float4-array-is-too-slow-hlsl-shader/15502) — confirms `Effect.Parameters["..."].SetValue(Vector4[])` works; perf concerns kick in only at hundreds of elements (we have 4).
- [MonoGame Community: SOLVED — Array'ed effect parameters](https://community.monogame.net/t/solved-arrayed-effect-parameters-are-not-working/8912) — pitfall: array parameters get optimized out if the shader doesn't actually use them. Not a problem here, we use them every pixel.
- [Cyanilux: 2D water shader breakdown](https://www.cyanilux.com/tutorials/2d-water-shader-breakdown/) — earlier source, the "outlines becoming visible below water" hint applies to the inverse problem (effects above showing through water); for us we want the player to show through the acid, which is exactly the mask-in-shader pattern.

### Piece B — Physics: gravity scale + optional drag

Two small changes in [PhysicsBody.cs](../GorelordsBrawler/Components/PhysicsBody.cs):

```csharp
public float GravityScale = 1f;   // multiplied into the gravity sum each frame
public float LinearDrag   = 0f;   // per-second velocity dampening, applied as (1 - drag*dt)
```

Both default to no-op behaviour (1.0 and 0.0), so existing dry-land physics is unchanged.

A new `SubmersionFeel` component on each player entity:
- Each frame, query `acid.GetLocalSurfaceLevelAtX(playerX, playerHeadY)` — same helper the sizzle uses.
- If the player's collider bottom is below that surface, the player is submerged: set `physicsBody.GravityScale = SubmergedGravityScale` (default `0.45`, inspector slider) and `physicsBody.LinearDrag = SubmergedDrag` (default `0.5`, slider).
- Otherwise restore both to dry-land values (1.0 / 0.0).

**Why this matches the user's constraints:**
- *"Won't float"* — no upward buoyancy force. Gravity is reduced, not reversed; player still falls.
- *"Won't swim"* — no new animations, no buoyancy-toward-surface, no Y-velocity injection. Jump still works (player can still leave the acid by jumping); it just doesn't push them up automatically.
- *"A difference"* — at 45% gravity the fall is visibly slower, and the drag bleeds horizontal momentum so dashing through the acid feels syrupy instead of dry-land snappy.

`SubmergedFallMultiplier` and the existing `FastFallMultiplier` already in `PhysicsBody` still apply on top of `GravityScale` — fast-falling underwater still fast-falls, just from a smaller base. (Open question: do we want fast-fall disabled in acid? My read: yes leave it as escape valve. Easy to change.)

Tunable defaults, all `[Inspectable, Range(...)]` on the `SubmersionFeel` component:
- `SubmergedGravityScale = 0.45` (range 0..1)
- `SubmergedDrag = 0.5` (range 0..4)
- (Optional Phase 3.5: `SubmergedJumpMultiplier`, `SubmergedWalkSpeedMultiplier`)

## File-by-file changes

### New
- `GorelordsBrawler/Components/SubmersionFeel.cs` — per-player component, sets gravity scale + drag when in acid

### Modified
- `GorelordsBrawler/Content/Effects/liquid.fx` — new uniforms (`PlayerRects[4]`, `PlayerCount`, `PlayerMaskStrength`) and mask-reduction branch in `LiquidPS`
- `GorelordsBrawler/Content/Effects/liquid.mgfxo` — recompiled via `dotnet mgfxc liquid.fx liquid.mgfxo /Profile:OpenGL`
- `GorelordsBrawler/Components/Hazards/Fluid/LiquidPostProcessor.cs` — accept `PlayerManager`; each frame, project player collider bounds → screen-space UV rects, push to shader uniforms
- `GorelordsBrawler/Components/Hazards/Fluid/FluidConfig.cs` — `LiquidPlayerMaskStrength` constant
- `GorelordsBrawler/Components/PhysicsBody.cs` — add `GravityScale` and `LinearDrag` fields; multiply / apply in `Update()`
- `GorelordsBrawler/Scenes/ArenaScene.cs` — pass `playerManager` into `LiquidPostProcessor` constructor
- `GorelordsBrawler/Data/CharacterFactory.cs` — `entity.AddComponent(new SubmersionFeel(acidSurface))` on each player

### Untouched
- `AcidSurface` — already exposes `GetLocalSurfaceLevelAtX` from PR #6, no new API needed
- `LiquidFieldRenderer`, `FluidSimulation`, `FluidCollider` — physics + particle pipeline unchanged
- All Phase 1/2 effects (bubbles, sizzle, HitFlash, pulse) — unaffected

## Verification

### Existing smoke-test still passes
- 5/5 checks green (unchanged checks: acid lifecycle, damage tick, player-pixel regression).
- The red-pixel regression check (`Check 5`) should now show **higher** counts when players are submerged, because the silhouette adds visible pixels above the acid. That's fine — the threshold is `>= 100`, the count should comfortably stay above.

### New manual functional test (you, on the desk)
- Walk a player into the deep pool. Expect: clearly-visible green-tinted silhouette of the player while submerged.
- Jump while in acid. Expect: jump lifts the player out (no swimming), but fall back into acid is visibly slower than fall through air.
- Walk left/right while submerged. Expect: more sluggish than dry-land — horizontal momentum dies faster.
- Sizzle smoke (Phase 2) should still emit at the contact line — verify it stays on the air-water boundary and doesn't get hidden by the new silhouette.

### Inspector knobs for live tuning
Both `SubmersionFeel` (`SubmergedGravityScale`, `SubmergedDrag`) and `PlayerPresencePostProcessor` (silhouette tint color, alpha) get `[Inspectable]` sliders so we can iterate without rebuilds — same pattern as bubbles and sizzle.

## Risks

- **Shader compatibility on the lowest profile (`ps_4_0_level_9_1`)** — 4-iteration `[unroll]` loop with simple AABB tests is well within profile limits, but unrolled-loop quirks in DX9-class shader compilers exist. Fallback if `mgfxc` rejects it: hand-unroll to four copy-paste `if` blocks. Risk is "implementation has to be slightly different," not "approach doesn't work."
- **Player collider rect vs visible sprite** — the BoxCollider's rect may not exactly match the sprite's silhouette (e.g., sprite has a weapon sticking out). For MVP, rect-based "show through this rectangle" is acceptable. If the gap reads as off, the upgrade is to render each player to a small mask render-target and pass that mask as a texture sampler — that IS a real second pass and we'd take the cost knowingly.
- **Two players whose collider rects overlap** — the shader uses `max(playerMask, ...)`, so overlap is just "still 1, still showing through." No double-darkening or stacking artifact.
- **Drag + jump interaction** — heavy drag could make jumping out of acid feel mushy. If functional test shows this, easy fix: apply drag only to horizontal axis (`Velocity.X *= 1 - drag*dt` instead of full vector).
- **Pulse / sizzle coexistence** — the new player-mask branch in `LiquidPS` runs in the same fragment shader as the Phase 1 pulse highlight. Pulse still drives `EdgeColor` intensity; the player-region branch only affects the body-mask multiplier. They compose cleanly.

## Approval flow

This is the proposal doc — your turn:
1. Approve as-is, or
2. Request changes (e.g. "skip the drag, just gravity scale", "silhouette should be outline-only", "split into two PRs", "I want the shader-uniform alternative for visibility"), or
3. Ask for more research on a specific point.

Once approved, I implement on `claude/acid-in-liquid-presence`, smoke-test, commit + PR with the usual teaching description, and hand back to you (with `-OpenIde` since you'll be testing manually).
