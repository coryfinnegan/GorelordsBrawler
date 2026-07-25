# Telegraphed platform respawns — the footing cycle replaces the rockfall

**Status: APPROVED (2026-07-11) — requirements dictated in the user's words
below; the two open calls resolved by the user: starting tier is a PAIR
(one platform per side), and storm respawns CONTINUE, confined to the top
band. Ghost duration defaults to 3.0 s (middle of the requested 2-5 band),
live-tunable.**

> **REVISION — the footing DIRECTOR (2026-07-24, functional-test verdict).**
> The death-triggered-only cycle shipped below left the arena bare but for
> the starting pair until the acid's first consume beat, 60+ seconds in
> ("we sort of wait around for the platforms to spawn and it really brings
> down the gameplay"). Superseded by a population-target director
> (`PlatformRespawner` + the `AcidConfig` "Platform respawn cycle" block):
>
> - **Target population from t=0** — 4 platforms in regular play (the pair +
>   two spawns; with the banks that is Battlefield's proven surface count),
>   3 in the storm (the cramped-chase call stands). An opening volley of
>   ghosts flashes on frame one at the symmetric inward-mid slots (x 448/832,
>   row 352 — over the pit, fair to both players), so full footing stands
>   ~3 s into the match.
> - **Deficit top-ups** — any shortfall beyond death-replacements is topped
>   up on a staggered cadence (0.8 s between telegraphs). "Population is
>   conserved: one death always schedules exactly one ghost" is superseded:
>   a death replaces itself immediately only while the population is short
>   of target, so the storm's smaller target burns off surplus on its own.
> - **The sliding spawn band** — candidates must sit within 240 px above the
>   current rise ceiling (as well as clearing it), so fresh footing hugs the
>   danger zone: low rows early, climbing with the loops, top row in the
>   storm. Same-column adjacent-row spawns are blocked (stack clearance) —
>   staggered staircases, never shelves with unusable 64 px gaps.
> - Prior art: Left 4 Dead's AI Director maintains intensity by spawning
>   against a target rather than per-event; Battlefield's tri-plat layout is
>   the density benchmark for a fair, frantic platform fight.

## The requirement (user, 2026-07-11)

> As a platform is removed, we flash a "ghost" of where a platform is going
> to be spawning. It should flash for maybe 2-5 seconds and then a new
> platform will spawn. The idea is that we are telegraphing where a platform
> will be. The players will want to try to get to those platforms as they
> play. We need to update the map: instead of a three tier platform, we have
> just the one tier. This will get eaten with acid and the players will need
> to get to the newly spawning platforms.

## The loop

1. The acid eats a platform (existing DissolvingPlatform erosion — unchanged).
2. The moment it dies, a GHOST — a pulsing outline the exact size of a
   platform — appears at the next spawn location. It flashes for
   `GhostSeconds` (default 3.0, the middle of the requested 2-5 band), pulse
   quickening as spawn approaches (the Phase-D tell grammar, same as the
   surge/rise tells).
3. The ghost solidifies into a real platform: same erodible slab as today's
   tiers, same texture, full collision. Any player overlapping the slab at
   that instant is snapped ON TOP (never crushed, never stuck inside).
4. The acid eventually eats it too — back to step 2. Population is conserved:
   one death always schedules exactly one ghost.

Players chase the ghosts: footing is always being taken away from where the
acid is and re-offered somewhere else. Prior art for the pattern: Pokemon
Stadium announces its next layout before transforming; Brawl's Skyworld
regenerates destroyed platforms; the platformer-design rule is "display the
randomization before the player encounters it."

## Placement (where ghosts appear)

Seeded-random pick from a candidate lattice (deterministic under stepped
E2E), filtered every time by:

- **Above the acid, with margin** — candidate must clear the CURRENT loop's
  rise ceiling (AcidConfig.RiseCeilingFor / StormCeilingY) by a clearance
  band, so a fresh platform is never eaten by the rise it spawned into. As
  the loops escalate, the viable band climbs on its own — the match
  naturally becomes a climb.
- **No overlap** with living platforms or active ghosts.
- **Move the fight**: candidates too close to the dead platform's spot are
  excluded, so the replacement always asks players to travel.
- **Reachability**: lattice spacing stays within jump range of the banks or
  a living platform.

During the STORM the filter simply keeps working: only the top band is
viable, so the endgame is a cramped scramble over the last spawns while
crests break over them. Respawns never stop — the chase IS the ending.

## Verification finding: the boards were secretly load-bearing

First full-arc probe (2026-07-11): one starting platform died DURING the
loop-1 contest — a beat the eight-tier world never lost. Cause: the old
diving boards sat exactly where the corner cascades pour over the pit lip
and soaked up their impact spray; with them gone, more froth pools on the
pair's tops (held wet through the dwell filter all match) and the loop-1
crest washes finished the job seconds early. Fix: the platform erosion rate
is retuned for the two-platform world (3.0 → 1.8 passes/s) so the contest
margin is structural, not luck — the loop-2 consume beat stretches from
~4 s to a still-snappy ~7 s of visible dissolution.

Same probe, second find: with the tier ladder gone, IDLE players respawn
onto the banks (the lowest dry rung), die to the next rise, and burn three
stocks by t≈30 debug-fast — the match ends before the storm. Real players
climb; automation now keeps its probes alive by parking them on the newest
footing (exactly what the E2E's chase already does).

## Map v3 (gen_sump_map.py)

- Keep: banks, pit, walls, inlets, spawns.
- Tiers: ONE starting tier row (replacing boards/lows/mids/tops). The row's
  platforms are ordinary DissolvingPlatforms and become the first links in
  the respawn cycle when eaten.
- AcidConfig.RespawnPoints (player respawn) reworked: banks + a high
  fallback; the flood-aware picker additionally prefers a LIVING platform
  when the banks are wet.
- Rise-ceiling schedule {528, 432, 392} + storm 272 stays as the pacing
  clock — it no longer maps to named tier ranks, it just drives how high
  the viable spawn band sits per loop.

## What this deletes / changes

- E2E tier-rank assertions (boards die loop 1, lows loop 2, mids in storm)
  are replaced by cycle assertions: ghost appears with >= 2 s lead; platform
  materializes exactly at its ghost; population is conserved; every spawn
  is above the live surface; storm spawns sit in the top band.
- New oracles: platformsAlive, ghostActive, ghostX/ghostY, lastSpawnX/Y.
- Unit: lattice geometry (candidates on-arena, reachable spacing, per-loop
  band above the ceiling), ghost timing constants in the 2-5 s band.

## Decisions locked (user's words)

- Rockfall REMOVED wholesale ("The rocks arent working. Remove them.") —
  done, committed before this proposal.
- Ghost telegraph before every spawn, 2-5 s ("flash for maybe 2-5 seconds").
- Map goes to a single starting tier ("instead of a three tier platform, we
  have just the one tier").
- The cycle is death-triggered ("as a platform is removed, we flash a
  ghost").

## Resolved calls (user, 2026-07-11)

1. **Starting tier shape**: a PAIR — one platform per side, mirroring the
   arena; both players get contestable first footing.
2. **Ghost duration**: 3.0 s default (middle of the requested band),
   live-tunable.
3. **Storm behavior**: respawns CONTINUE, confined to the top band — the
   endgame is a cramped chase over the last spawns while crests break over
   them.
