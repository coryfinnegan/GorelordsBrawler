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

# ── Refuge tiers (solid). Center cols 19-20 stay clear for the inlet drop. ──
plat(4, 9, 13)      # low-left
plat(30, 35, 13)    # low-right
plat(9, 13, 9)      # mid-left  (sits at the basin's left lip, no overhang)
plat(26, 30, 9)     # mid-right (sits at the basin's right lip, no overhang)
plat(13, 18, 5)     # top-left
plat(21, 26, 5)     # top-right   -> gap at cols 19-20 between the two top tiers

# ── Sanity: the inlet column (col 20) must be clear from ceiling to basin ───
for y in range(1, 17):
    assert (20, y) not in solid and (20, y) not in wall, \
        f"inlet column blocked at (20,{y}) — acid could not fall into the basin"

# ── Spawns: on bank tops (world y just above row-17 surface at y=544) ───────
SPAWN_Y = 520
spawns = [
    (200, SPAWN_Y),    # 0: left bank, outer
    (1080, SPAWN_Y),   # 1: right bank, outer
    (360, SPAWN_Y),    # 2: left bank, inner
    (920, SPAWN_Y),    # 3: right bank, inner
]
for i, (sx, _sy) in enumerate(spawns):
    col = sx // TILE
    bank_ok = (1 <= col <= 13) or (26 <= col <= 38)
    assert bank_ok, f"spawn {i} at x={sx} (col {col}) is not over a bank"


def gid_at(x, y, layer):
    if layer == "background":
        return BG
    # platforms + collision are identical: walls and solids, matching visuals.
    if (x, y) in wall:
        return WALL
    if (x, y) in solid:
        return SOLID
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
