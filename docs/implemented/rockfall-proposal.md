# Rockfall — tumbling rocks replace the drop-logs

**Status: APPROVED ("yep do it", 2026-07-05) — implementing.**

## The problem (functional-test findings)

The drop-logs never had a job. They spawned on a damage trigger (low tiers
half-chewed — which loop 1's *contest* fires while every platform still
stands), at side anchors where nothing was happening, in counts that forced
stacking. Players saw decorative debris, stacked bobbing pairs, and no reason
to care. Meanwhile the float physics was our most bug-prone system (three
rounds of waterline fixes).

## Decisions (locked, in the user's words)

1. **"Instead of logs… big rocks, big enough that they stick out of the
   surface"** — rocks REST ON THE GROUND and protrude; no buoyancy at all.
   The entire float/waterline problem class is deleted, not fixed.
2. **"Tumbling down in random places, increasing the chaos"** — telegraphed
   rockfall (the Phase-D tell pattern, localized: a warning marker at the
   drop column), with **impact damage + knockback** so the chaos has teeth.
   Randomness is *structured*: a set of drop columns with a ~40% bias toward
   piling on existing rocks — chaos that still reliably builds footing.
3. **"Giving a way to recover"** — rocks pile into cairns; a tall-enough
   cairn breaches the surface and becomes a temporary island (the acid chews
   the submerged base, so islands sink back — the archipelago churns).
   Knocked-in players breach onto a cap and climb the route back (the
   platform-fighter recovery-route pattern: a path back that the opponent
   can contest).

## Shape & tumble (follow-up user direction: "rock-shaped boulders so they can
tumble", then "good rock physics — tumble, settle, interact with the acid")

The boulder texture's ALPHA CHANNEL is load-bearing: ErodibleSurface
initializes its cell mask from it, so render, collision, and erosion all
share the exact silhouette. The art is pixel art authored at quarter scale
and upscaled x4 nearest — one art pixel == one 4px erosion cell, so the acid
eats the stone pixel-by-pixel. Silhouette = irregular convex polygon (straight
edges read as chipped stone); facets = straight chord cuts (planes meet at
dead-straight chisel creases); shading = 5-shade hue-shifted ramp with bands
assigned by AREA QUANTILE down the light direction, which keeps the reference
value balance on every roll.

The tumble is STEERED (the animator's pre-timed-rotation trick): each falling
frame projects time-to-impact from closed-form kinematics (gravity, terminal
speed — halved in liquid; growing piles refresh the estimate) and adjusts the
spin rate so a whole number of turns completes exactly at touchdown. The
leftover angular velocity feeds a damped spring around the rest pose — the
boulder tips past upright and rocks back to settle, never rewinding. Spin is
visual-only; collision stays the axis-aligned cell mask; rates are seeded per
drop, so the stepped E2E stays deterministic.

## Sizing math (against the C.1 rise schedule)

Rock: 96 wide, height 96 or 128 (varied silhouettes). Bank ground y=544.

| Water | Surface y | Footing that breaches |
|---|---|---|
| Loop 0 awash | 528 | any single rock (top 448/416) |
| Loop 1 contest | 432 | a 128-rock alone (top 416, precarious) or any 2-pile |
| Loop 2 consume | 392 | 2-pile with a 128 in it (top ≤ 352) |
| **Storm** | **272** | **3-pile** (e.g. 96+96+128 → top 224; 3×96 → top 256, precarious) |

Pit-floor piles (floor 736) need ~5 rocks — pit islands are rare jackpots;
bank cairns are the reliable route, which matches where knocked-in players
breach.

## Drop columns (as implemented: GHOST columns only)

Rocks fall exclusively where the acid ate the arena's supports — each column
activates when its tier pair dies:

- **x 224 / 1056** (low-tier ghosts): unlock when the LOW pair dies (early
  loop 2 — the rockfall's real start).
- **x 344 / 936** (mid-tier ghosts): unlock when the MID pair dies (storm).

The originally-proposed always-open center channel was CUT during
verification, twice over: a center pit tower's collapse waves killed the
contested lows in loop 1 (so rockfall now starts at loop 2, protecting the
contest beat), and a center cairn breaching the surface sat in the
standing-surface probe's columns — its cap puddles corrupted the closed-loop
fill, which over-poured and drowned every island. Ghost-only placement solves
both structurally, and bank cairns (3 rocks to breach the storm) make better
recovery footing than pit towers (5) anyway. Collapsing piles also SETTLE
(slow sink) rather than re-plunging — a gravity re-fall made the collider a
hydraulic piston.

## Cadence

None until the arena starts LOSING footing: loops 0-1 stay rock-free (loop 0
teaches, loop 1 is the protected contest beat — early pit towers' collapse
waves were killing the contested lows). From loop 2: one rock per ~12 s → **~3.5 s in the storm** (rockfall + crests =
the apocalypse endgame). Live cap 10 rocks; stone erodes slowly
(~0.6 passes/s → a submerged 96 px rock lasts ~60 s), so islands persist
about a loop before the sea reclaims them.

## What gets deleted

`DynamicPlatform` (float spring, hull-tracked buoyancy, drift, water-entry
retention), the buoyancy constants, the log spawn anchors/stagger/population
curve, the wood texture, and the `logsAirborne` oracle — rocks cannot hover
by construction. The erodible-surface system, fall gravity constants, and
splash-on-entry all carry over unchanged.

## Verification plan

- Unit: drop columns clear of live tier spans + inlet columns; cadence
  monotonic; the sizing invariant (a 3-pile must breach the storm surface).
- E2E (seeded spawner RNG → deterministic in stepped mode): no rocks before
  loop 1; rocks land and rest; **≥1 island forms during the storm**; falling
  rocks damage a parked player under a column.
- Probe + smoke recording for the feel/visual gate.
