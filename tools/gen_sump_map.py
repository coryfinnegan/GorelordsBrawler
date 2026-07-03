#!/usr/bin/env python3
"""
Generate Content/maps/arena1.tmx — "The Sump" acid arena (Phase A greybox).

Why a generator instead of hand-editing the TMX:
  The map is 40x25 across three tile layers = 3000 CSV cells. Hand-editing that
  is error-prone, and the geometry has hard constraints the fluid sim depends on
  (a sealed basin, and a clear vertical column for the acid inlet to fall down).
  Encoding the layout as a spec and generating the CSV makes those constraints
  explicit and the file regenerable. The output is still a normal .tmx you can
  open and refine in Tiled afterwards.

Tileset (Content/tilesets/arena.tsx, firstgid=1, 3 tiles):
  gid 1 = background fill   (background layer)
  gid 2 = solid surface     (banks / basin floor / refuge tiers)
  gid 3 = wall              (outer frame)

Geometry (grid coords; row 0 = top, col 0 = left; 32px tiles -> 1280x800):
  - Outer frame: row 0 (ceiling), col 0 / col 39 (side walls).
  - Banks (solid): cols 1-13 (left) and cols 26-38 (right), rows 17-22 down
    to the floor. Bank TOP surface = row 17 (world y = 544) — the fighting floor.
  - Basin: the gap cols 14-25 (world x 448..832, 384px wide), open rows 17-22,
    floored at rows 23-24. This is where the acid pools and where players are
    knocked in. Sealed left/right by the bank inner walls, bottom by the floor.
  - Refuge tiers (solid): stepped platforms above the banks for the flood phase.
    The center columns (19-20) are kept CLEAR at every tier so the acid inlet
    (world x 640 = col 20) has an unobstructed drop straight into the basin.
  - Spawns: 4 points on the bank tops, facing the center basin.

Run:  python tools/gen_sump_map.py
"""

import os

W, H = 40, 25
TILE = 32

# gid 0 = empty
BG, SOLID, WALL = 1, 2, 3

solid = set()   # gid 2 cells
wall = set()    # gid 3 cells


def fill_rect(cells, x0, x1, y0, y1):
    """Inclusive rectangle of (col,row) into `cells`."""
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            cells.add((x, y))


def plat(x0, x1, y):
    """A solid platform run on a single row (inclusive)."""
    for x in range(x0, x1 + 1):
        solid.add((x, y))


# ── Outer frame (walls) ────────────────────────────────────────────────────
for x in range(W):
    wall.add((x, 0))            # ceiling
for y in range(H):
    wall.add((0, y))           # left wall
    wall.add((W - 1, y))       # right wall

# ── Basin floor (full inner width, rows 23-24) ─────────────────────────────
fill_rect(solid, 1, W - 2, 23, 24)

# ── Banks (rows 17-22), leaving the basin gap cols 14-25 open ──────────────
fill_rect(solid, 1, 13, 17, 22)     # left bank
fill_rect(solid, 26, 38, 17, 22)    # right bank

# ── Refuge tiers — DISSOLVABLE entities, not static tiles ───────────────────
# The acid consumes the arena tier by tier as it rises, so tiers cannot live in
# the collision layer (static tiles can't burn). They are emitted as rects on a
# "tiers" OBJECT layer; ArenaScene turns each into an entity with a collider +
# a DissolvingPlatform that burns away when the acid reaches its top edge.
# rank: "low" gates the log spawner (logs only drop once the low pair is eaten).
#
# C.2 layout (docs/sump-layout-redesign-proposal.md) — four bands, placed
# against the C.1 rise schedule (AcidConfig keeps the matching Y constants):
#   shallow (row 15, y 480): DIVING BOARDS overhanging the pit edges — the
#       early risk/reward perch and the first destruction beat (standing-dry
#       under loop 0's 528 ceiling, fully drowned by loop 1's 432).
#   low     (row 13, y 416): contested loop 1, consumed loop 2 — unchanged.
#   mid     (row 9,  y 288): pulled INWARD to overhang the pit (a committed
#       32 px gap-jump from the lows — the old layout was a zero-risk
#       staircase) and narrowed 5→4 tiles. Storm territory.
#   top     (row 5,  y 160): narrowed 6→5 tiles, center gap widened to 128 px
#       so the final perches are a real jump apart, not one choke.
TIERS = [
    # (col0, col1, row, rank)
    (14, 16, 15, "shallow"),   # x 448-544, over the pit's left lip
    (23, 25, 15, "shallow"),   # x 736-832, over the pit's right lip
    (4, 9, 13, "low"),         # x 128-320
    (30, 35, 13, "low"),       # x 960-1152
    (11, 14, 9, "mid"),        # x 352-480 (32 px gap-jump from the low tier)
    (25, 28, 9, "mid"),        # x 800-928
    (13, 17, 5, "top"),        # x 416-576
    (22, 26, 5, "top"),        # x 704-864 (128 px center gap)
]

# The diving boards must genuinely OVERHANG the basin mouth (cols 14-25) —
# that's their whole job — while every other band stays out of the two center
# columns (19-20) the original design keeps clear.
for (c0, c1, row, rank) in TIERS:
    if rank == "shallow":
        assert c0 <= 25 and c1 >= 14, \
            f"shallow board ({c0}-{c1}) does not overhang the basin mouth"
    else:
        assert not (c0 <= 20 and c1 >= 19), \
            f"tier ({c0}-{c1} row {row}) blocks the center columns 19-20"

# ── Sanity: the corner inlet columns must be clear from ceiling to the banks ─
# Acid pours from the TOP CORNERS of the map (cols 2 / 37, world x 72/1208),
# falls onto the banks, and cascades over the lip into the pit. Nothing may
# obstruct those columns above the bank tops (row 17).
for col in (2, 37):
    for y in range(1, 17):
        assert (col, y) not in solid and (col, y) not in wall, \
            f"corner inlet column blocked at ({col},{y}) — the corner stream could not reach the bank"
    for (c0, c1, row, _rank) in TIERS:
        assert not (c0 <= col <= c1), \
            f"tier ({c0}-{c1} row {row}) sits under the corner inlet column {col}"

# ── Spawns: on bank tops, CLEAR of the low refuge tiers ─────────────────────
# The low tiers occupy x 128-320 (left) and 960-1152 (right); the old outer
# spawns at x=200/1080 sat directly under them ("spawning under the platforms").
# Player 1 & 2 (shipping 2-player mode use slots 0/1) go to the FAR OUTER bank
# corners; the inner pair sits between the lip and the tier, also clear.
SPAWN_Y = 520
spawns = [
    (96,   SPAWN_Y),   # 0: P1 — far left bank corner (left of the low-left tier)
    (1184, SPAWN_Y),   # 1: P2 — far right bank corner
    (384,  SPAWN_Y),   # 2: inner left  (between tier-end x320 and lip x448)
    (896,  SPAWN_Y),   # 3: inner right (between lip x832 and tier-start x960)
]
for i, (sx, _sy) in enumerate(spawns):
    col = sx // TILE
    bank_ok = (1 <= col <= 13) or (26 <= col <= 38)
    assert bank_ok, f"spawn {i} at x={sx} (col {col}) is not over a bank"


# ── Phase-E visual gids (tools/gen_arena_tileset.py's tile inventory) ──────
# The COLLISION layer keeps the plain semantic gids (2 solid / 3 wall) — the
# physics never cares about looks. The PLATFORMS (visual) layer picks from the
# rendered tileset: machined TOP plates where a surface is walkable, corroded
# FILL below, acid-STAINED variants on basin-facing cells, and a deterministic
# A/B hash so big plate runs don't tile visibly.
FILL_A, TOP_A, FILL_B, TOP_B, TOP_STAIN, FILL_STAIN = 2, 4, 5, 6, 7, 8


def visual_solid_gid(x, y):
    top_exposed = (x, y - 1) not in solid and (x, y - 1) not in wall
    # Basin-facing cells wear the acid staining: the pit floor + the two bank
    # lip corners and inner walls the pool laps every loop.
    basin_floor = 14 <= x <= 25
    lip_corner  = y == 17 and x in (12, 13, 26, 27)
    inner_wall  = x in (13, 26) and 17 <= y <= 22
    variant     = (x * 7 + y * 13) % 2
    if top_exposed:
        if basin_floor or lip_corner:
            return TOP_STAIN
        return TOP_A if variant == 0 else TOP_B
    if basin_floor or inner_wall:
        return FILL_STAIN
    return FILL_A if variant == 0 else FILL_B


def gid_at(x, y, layer):
    if layer == "background":
        return BG
    if (x, y) in wall:
        return WALL
    if (x, y) in solid:
        # collision stays semantic; only the visual layer picks variants
        return visual_solid_gid(x, y) if layer == "platforms" else SOLID
    return 0


def csv_layer(layer):
    rows = []
    for y in range(H):
        rows.append(",".join(str(gid_at(x, y, layer)) for x in range(W)))
    return ",\n".join(rows)


def object_xml():
    parts = []
    oid = 1
    for i, (sx, sy) in enumerate(spawns):
        parts.append(
            f'  <object id="{oid}" name="spawn{i}" type="SpawnPoint" '
            f'x="{sx}" y="{sy}" width="0" height="0">\n'
            f'   <point/>\n'
            f'   <properties>\n'
            f'    <property name="index" type="int" value="{i}"/>\n'
            f'   </properties>\n'
            f'  </object>'
        )
        oid += 1
    return "\n".join(parts)


def tier_object_xml():
    parts = []
    oid = 100
    for i, (c0, c1, row, rank) in enumerate(TIERS):
        x, y = c0 * TILE, row * TILE
        w, h = (c1 - c0 + 1) * TILE, TILE
        parts.append(
            f'  <object id="{oid}" name="tier{i}" type="Tier" '
            f'x="{x}" y="{y}" width="{w}" height="{h}">\n'
            f'   <properties>\n'
            f'    <property name="rank" value="{rank}"/>\n'
            f'   </properties>\n'
            f'  </object>'
        )
        oid += 1
    return "\n".join(parts)


def build_tmx():
    return f'''<?xml version="1.0" encoding="UTF-8"?>
<map version="1.10" tiledversion="1.11.2" orientation="orthogonal" renderorder="right-down" width="{W}" height="{H}" tilewidth="{TILE}" tileheight="{TILE}" infinite="0" nextlayerid="5" nextobjectid="{len(spawns) + 1}">
 <tileset firstgid="1" source="../tilesets/arena.tsx"/>
 <layer id="1" name="background" width="{W}" height="{H}">
  <data encoding="csv">
{csv_layer("background")}
</data>
 </layer>
 <layer id="2" name="platforms" width="{W}" height="{H}">
  <data encoding="csv">
{csv_layer("platforms")}
</data>
 </layer>
 <layer id="3" name="collision" width="{W}" height="{H}">
  <data encoding="csv">
{csv_layer("collision")}
</data>
 </layer>
 <objectgroup id="4" name="spawns">
{object_xml()}
 </objectgroup>
 <objectgroup id="5" name="tiers">
{tier_object_xml()}
 </objectgroup>
</map>
'''


def ascii_preview():
    glyph = {0: ".", BG: ".", SOLID: "#", WALL: "@"}
    spawn_cells = {(sx // TILE, sy // TILE): str(i) for i, (sx, sy) in enumerate(spawns)}
    lines = []
    for y in range(H):
        row = []
        for x in range(W):
            if (x, y) in spawn_cells:
                row.append(spawn_cells[(x, y)])
                continue
            g = gid_at(x, y, "collision")
            row.append(glyph[g])
        lines.append(f"{y:2d} " + "".join(row))
    header = "   " + "".join(str(c % 10) for c in range(W))
    return header + "\n" + "\n".join(lines)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(here, "..", "GorelordsBrawler", "Content", "maps", "arena1.tmx")
    out = os.path.normpath(out)
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write(build_tmx())
    print("ASCII preview (@=wall  #=solid  .=open  digits=spawns):\n")
    print(ascii_preview())
    print(f"\nWrote {out}")
