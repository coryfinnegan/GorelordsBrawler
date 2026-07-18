# The Sump — Layout Redesign (Phase C.2 proposal)

**Status: IMPLEMENTED (approved "do it all", 2026-07-02).** As-built spans
refined slightly from the sketch below during implementation — the mids sit at
x 352–480 / 800–928 (a 32 px committed gap-jump from the lows), the boards at
x 448–544 / 736–832, the tops at x 416–576 / 704–864 (128 px center gap), and
the mid log-drop anchors moved to the OUTER thirds of the old mid spans
(376/904) so falling logs don't clip the new top tiers' edges. Everything else
landed as proposed. Follows the Phase C.1 calibration/pacing pass. Geometry
only; no fluid, phase-machine, or combat changes.

## Why (from the measured evaluation, 2026-07-01/02)

The current tier layout is Phase-A greybox scaffolding that was never designed
as a fighting stage:

1. **Two mirrored staircases with full horizontal overlap.** Low → mid → top on
   each side overlap in X, so climbing is a zero-risk walk. There is no jump
   that commits you to anything.
2. **One choke.** Both staircases funnel to the single 64 px gap between the
   top tiers at center. Every late fight happens in the same spot.
3. **The pit is scenery between floods.** Nothing playable overhangs it, so
   until a rise the central hazard is just a hole you avoid.

And the load-bearing constraint C.1 discovered: **the acid's standing surface
can only afford to reach y≈392** (15k-particle budget at the measured density).
Everything above that line is crest/storm territory. The current map puts only
ONE tier class (the lows) inside the band the acid can actually contest —
most of the layout can never interact with the standing fluid at all.

**Design rule that falls out: put the mid-game structure INSIDE the standing-
reach band (y 392–544), and use the heights above it for storm-era refuges.**

## What stays (deliberately)

- **The vessel: banks, basin, walls — untouched.** Collision tiles, PreFill,
  particle caps, `ClampIntoVessel`, and the corner inlet columns all key off
  this geometry; keeping it makes C.2 a *tiers-object-layer + spawns* change
  only.
- **Mirror symmetry** — 2-player fairness (Melee stage-design convention).
- **Far-corner spawns** (96/1184) and the dead-drop corner streams.
- The contest-then-consume schedule and all C.1 constants — the new tiers are
  placed to fit the existing bands, not the other way around.

## Proposed layout

```
   0123456789012345678901234567890123456789
 1 @......................................@
 5 @............DDDDD....EEEEE............@   TOP perches (y=160)  x 416-576 / 704-864
 9 @........CCCC..............CCCC........@   MID perches (y=288)  x 288-416 / 864-992
13 @...BBBBBB....................BBBBBB...@   LOW tiers   (y=416)  x 128-320 / 960-1152
15 @.............AAA....AAA...............@   DIVING BOARDS (y=480) x 448-544 / 736-832
17 @#############............#############@   banks (lip y=544)
23 @######################################@   basin floor
   ^ cols 2/37 = inlet drop columns (clear)    basin mouth: cols 14-25
```

Band by band (all one row / 32 px thick, all `Tier` objects → `DissolvingPlatform`):

| Band | Y (top) | Position | Role in the loop schedule |
|---|---|---|---|
| **A — diving boards** *(new)* | 480 | jutting over the pit edges | Dry only in Calm/loop 0's awash. **Drowned by loop 1's rise** — the first, most visible destruction beat, and until then the risk/reward perch over the pit |
| **B — low tiers** *(kept)* | 416 | same spans as today | Contested loop 1 (waterline lap), consumed loop 2 — unchanged, the C.1 schedule is written for them |
| **C — mid perches** *(moved inward, narrowed 5→4 tiles)* | 288 | shifted to overhang the pit edges | Crest-harassed from late loops, broken by the storm. Reaching them from B is now a **real gap jump over the pit edge** (dx ≈ 64–160 px, dy 128) instead of a staircase step |
| **D/E — top perches** *(kept, narrowed 6→5 tiles)* | 160 | roughly as today | Storm-era last refuge; crests claw them down. Narrower = less comfortable to share, and the wider center gap (128 px) makes the top-center choke an actual jump |

What this buys, mapped to the evaluation's findings:

- **Traversal risk exists.** B→C and C→D are committed jumps with the pit or a
  crest-swept gap underneath, not overlap-steps.
- **Two cross-pit routes** (A early, C late) make the center a place you fight
  *over*, not just drown in.
- **The dynamic zone is populated.** Bands A and B both live inside the
  standing-reach band, so every loop's rise interacts with real structure from
  minute one.
- **No permanently-dominant spot.** The top perches are narrower, further
  apart, and (since C.1) erodible by storm crests — camping ends because the
  ground does.

## Numbers that must move together (C.2 implementation checklist)

- `tools/gen_sump_map.py` — new TIERS table (+ assert: no tier crosses the
  inlet columns; A-band clear of the basin *mouth* so falling logs still have
  water to land in).
- `AcidConfig` tier constants — add `ShallowTierTopY/BottomY = 480/512`; MID
  span moves in X only (Y unchanged). Unit tests pin the schedule against the
  bands (`RiseCeilings_FollowContestThenConsume` gains the A-band asserts:
  dry at loop 0's 528, submerged by loop 1's 432).
- `LogSpawnXLeft/Right` — re-anchor to the new former-tier X centers.
- `RespawnPoints` — mid candidates follow the C-band X shift.
- E2E — `tiersRemaining` totals change (6 → 8); the contested-then-consumed
  test gains the A-band beat (8 → 6 during loop 1's rise, before the lows
  fall).
- Full E2E assembly + both smoke features (`acid`, `pacing`) — the geometry
  rule.

## Decisions for you

1. **Adopt the A-band diving boards?** They're the biggest change: early-game
   risk/reward over the pit + the first destruction beat. (Recommended.)
2. **Mid perches inward** (cross-pit mid route) vs. leaving them over the
   banks as today? (Recommended: inward.)
3. **Top perches: narrow + widen the center gap** as proposed, or keep as-is
   and let the storm alone fix camping? (Recommended: narrow — the storm is
   the backstop, the layout shouldn't rely on it.)
4. **Banks stay flat.** I considered knee-high steps to break the neutral and
   recommend against: there's no step-up mechanic (a 32 px ledge blocks walking
   → jump spam in neutral), and the loop-0 awash already animates the banks.
   Say the word if you want them anyway.
