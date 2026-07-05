#!/usr/bin/env python3
"""
Generate the Sump's Phase-E art: the vessel tileset (arena.png / arena.tsx)
plus the destructible-platform textures (tier_metal.png, log_wood.png).

Why a generator instead of hand-drawn art:
  Same philosophy as gen_sump_map.py — the art is a SPEC (palette, tile
  inventory, technique parameters) so it can be regenerated, retuned, and
  diffed. The look is "industrial waste-processing vessel": corroded riveted
  steel, machined top plates, acid staining near the basin. Deterministic
  (fixed seed) so re-running produces byte-identical PNGs.

Tile inventory (gid = 1-based index, referenced by gen_sump_map.py):
  1 bg panel      — near-black plate with faint seams; LOW contrast on purpose
                    (the stage must never fight the characters for the eye)
  2 fill A        — corroded steel body
  3 wall          — heavy frame plate with corner rivets
  4 top A         — bank surface: machined top edge highlight + wear
  5 fill B        — patina variant of 2
  6 top B         — variant of 4
  7 top stained   — top plate with acid staining (cells flanking the basin)
  8 fill stained  — body plate with acid staining (basin-facing walls/floor)

Run:  python tools/gen_arena_tileset.py   (requires Pillow)
"""

import os
import random

from PIL import Image, ImageDraw, ImageFilter

TILE = 32
SEED = 0xAC1D

# ── Palette (industrial steel under sodium work-lights) ─────────────────────
BG_BASE     = (18, 21, 27)
BG_SEAM     = (24, 28, 35)
BG_RIVET    = (30, 35, 43)
STEEL_HI    = (124, 136, 150)
STEEL_MID   = (58, 64, 72)
STEEL_LO    = (42, 46, 53)
STEEL_DARK  = (30, 33, 39)
WALL_MID    = (46, 50, 58)
WALL_FRAME  = (23, 26, 31)
RIVET_HI    = (150, 160, 172)
RIVET_LO    = (20, 23, 28)
ACID_STAIN  = (63, 174, 63)
ROCK_MID    = (96, 92, 88)
ROCK_HI     = (146, 142, 134)
ROCK_DARK   = (52, 50, 47)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def patina(img, rng, strength=0.5, scale=3, shades=4):
    """Blotchy corrosion: coarse noise, blurred, posterized to a few shades —
    the classic cheap 'value noise → quantize' pixel-art texture pass."""
    w, h = img.size
    noise = Image.new("L", (max(1, w // scale), max(1, h // scale)))
    noise.putdata([rng.randint(0, 255) for _ in range(noise.width * noise.height)])
    noise = noise.resize((w, h), Image.BILINEAR).filter(ImageFilter.BoxBlur(1))
    px, npx = img.load(), noise.load()
    for y in range(h):
        for x in range(w):
            n = (npx[x, y] / 255.0 - 0.5) * 2.0            # -1..1
            n = round(n * shades) / shades                  # posterize
            t = max(-1.0, min(1.0, n * strength))
            base = px[x, y][:3]
            px[x, y] = lerp(base, STEEL_DARK if t < 0 else STEEL_HI, abs(t) * 0.35)
    return img


def edge_darken(img, depth=3, amount=0.45):
    """Darken toward the tile border — sells 'separate plate', hides seams."""
    w, h = img.size
    px = img.load()
    for y in range(h):
        for x in range(w):
            d = min(x, y, w - 1 - x, h - 1 - y)
            if d < depth:
                t = (depth - d) / depth * amount
                px[x, y] = lerp(px[x, y][:3], (0, 0, 0), t)
    return img


def rivet(draw, x, y, hi=RIVET_HI, lo=RIVET_LO):
    draw.point((x + 1, y + 1), fill=lo)
    draw.rectangle([x, y, x + 1, y + 1], fill=lo)
    draw.point((x, y), fill=hi)


def stain(img, rng, anchor_top, blobs=4, strength=0.5):
    """Acid staining: green multiply blobs creeping from the anchored edge."""
    w, h = img.size
    px = img.load()
    for _ in range(blobs):
        cx = rng.randint(2, w - 3)
        cy = rng.randint(0, h // 3) if anchor_top else rng.randint(h // 3, h - 1)
        r = rng.randint(3, 8)
        for y in range(max(0, cy - r), min(h, cy + r)):
            for x in range(max(0, cx - r), min(w, cx + r)):
                d2 = (x - cx) ** 2 + (y - cy) ** 2
                if d2 <= r * r:
                    t = (1.0 - d2 / (r * r)) * strength
                    base = px[x, y][:3]
                    tinted = tuple(int(base[i] * (1 - t) + base[i] * ACID_STAIN[i] / 255 * t * 2) for i in range(3))
                    px[x, y] = tuple(min(255, c) for c in tinted)
    return img


def base_plate(color, rng, noise=0.35):
    img = Image.new("RGB", (TILE, TILE), color)
    return patina(img, rng, strength=noise)


def tile_bg(rng):
    img = Image.new("RGB", (TILE, TILE), BG_BASE)
    d = ImageDraw.Draw(img)
    # faint plate seams (cross at 1/2) + dim rivets — barely-there depth
    d.line([(0, 15), (31, 15)], fill=BG_SEAM)
    d.line([(15, 0), (15, 31)], fill=BG_SEAM)
    for (x, y) in [(3, 3), (26, 3), (3, 26), (26, 26)]:
        rivet(d, x, y, hi=BG_RIVET, lo=BG_BASE)
    return patina(img, rng, strength=0.18, shades=3)


def tile_fill(rng, stained=False):
    img = base_plate(STEEL_LO, rng, noise=0.4)
    if stained:
        img = stain(img, rng, anchor_top=False, blobs=5, strength=0.45)
    return edge_darken(img, depth=2, amount=0.35)


def tile_wall(rng):
    img = base_plate(WALL_MID, rng, noise=0.3)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 31, 31], outline=WALL_FRAME, width=3)
    for (x, y) in [(4, 4), (26, 4), (4, 26), (26, 26)]:
        rivet(d, x, y)
    return img


def tile_top(rng, stained=False):
    img = base_plate(STEEL_MID, rng, noise=0.35)
    d = ImageDraw.Draw(img)
    # machined walking surface: bright lip, shadow line under it
    d.line([(0, 0), (31, 0)], fill=STEEL_HI)
    d.line([(0, 1), (31, 1)], fill=lerp(STEEL_HI, STEEL_MID, 0.4))
    d.line([(0, 2), (31, 2)], fill=STEEL_DARK)
    # wear scratches
    for _ in range(4):
        x = rng.randint(2, 29)
        d.line([(x, 4), (x, 4 + rng.randint(2, 6))], fill=STEEL_DARK)
    if stained:
        img = stain(img, rng, anchor_top=True, blobs=4, strength=0.5)
    return edge_darken(img, depth=2, amount=0.3)


def build_tileset(out_dir):
    rng = random.Random(SEED)
    tiles = [
        tile_bg(rng),                    # 1
        tile_fill(rng),                  # 2
        tile_wall(rng),                  # 3
        tile_top(rng),                   # 4
        tile_fill(rng),                  # 5 (variant B — different rng draw)
        tile_top(rng),                   # 6 (variant B)
        tile_top(rng, stained=True),     # 7
        tile_fill(rng, stained=True),    # 8
    ]
    sheet = Image.new("RGB", (TILE * len(tiles), TILE))
    for i, t in enumerate(tiles):
        sheet.paste(t, (i * TILE, 0))
    png = os.path.join(out_dir, "arena.png")
    sheet.save(png)

    tsx = os.path.join(out_dir, "arena.tsx")
    with open(tsx, "w", encoding="utf-8", newline="\n") as f:
        f.write(
            f'<?xml version="1.0" encoding="UTF-8"?>\n'
            f'<tileset version="1.10" tiledversion="1.11.2" name="arena" '
            f'tilewidth="{TILE}" tileheight="{TILE}" tilecount="{len(tiles)}" columns="{len(tiles)}">\n'
            f' <image source="arena.png" width="{TILE * len(tiles)}" height="{TILE}"/>\n'
            f'</tileset>\n')
    return png, tsx


def build_tier_texture(out_path):
    """192×32 grimy steel slab. ErodibleRenderer maps this 1:1 proportionally
    onto each tier's full area, so erosion reveals holes through a STABLE
    image (the pattern doesn't swim as cells vanish)."""
    rng = random.Random(SEED + 1)
    img = Image.new("RGB", (192, 32), STEEL_MID)
    img = patina(img, rng, strength=0.45, scale=4)
    d = ImageDraw.Draw(img)
    d.line([(0, 0), (191, 0)], fill=STEEL_HI)
    d.line([(0, 1), (191, 1)], fill=lerp(STEEL_HI, STEEL_MID, 0.5))
    d.line([(0, 31), (191, 31)], fill=STEEL_DARK)
    # panel joints + rivets every 32px — reads as bolted segments
    for x in range(32, 192, 32):
        d.line([(x, 2), (x, 31)], fill=STEEL_DARK)
        rivet(d, x - 4, 5)
        rivet(d, x + 2, 5)
    img = edge_darken(img, depth=2, amount=0.3)
    img.save(out_path)


def build_rock_texture(out_path, w, h, seed_offset):
    """Boulder for the rockfall (docs/rockfall-proposal.md): weathered granite
    — coarse patina, jagged cracks, a lit top face — so a resting cairn reads
    as stone the acid is gnawing, not a gray box."""
    rng = random.Random(SEED + seed_offset)
    img = Image.new("RGB", (w, h), ROCK_MID)
    img = patina(img, rng, strength=0.55, scale=5, shades=5)
    d = ImageDraw.Draw(img)
    # jagged cracks: random-walk polylines from near the top
    for _ in range(3):
        x, y = rng.randint(8, w - 8), rng.randint(2, h // 3)
        for _ in range(rng.randint(4, 8)):
            nx = max(2, min(w - 3, x + rng.randint(-6, 6)))
            ny = min(h - 3, y + rng.randint(3, 9))
            d.line([(x, y), (nx, ny)], fill=ROCK_DARK)
            x, y = nx, ny
    # lit top face + settled shadow at the base
    d.line([(0, 0), (w - 1, 0)], fill=ROCK_HI)
    d.line([(0, 1), (w - 1, 1)], fill=lerp(ROCK_HI, ROCK_MID, 0.5))
    d.line([(0, h - 1), (w - 1, h - 1)], fill=ROCK_DARK)
    img = edge_darken(img, depth=3, amount=0.45)
    img.save(out_path)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    tileset_dir = os.path.normpath(os.path.join(here, "..", "GorelordsBrawler", "Content", "tilesets"))
    hazards_dir = os.path.normpath(os.path.join(here, "..", "GorelordsBrawler", "Content", "Sprites", "hazards"))
    os.makedirs(hazards_dir, exist_ok=True)

    png, tsx = build_tileset(tileset_dir)
    build_tier_texture(os.path.join(hazards_dir, "tier_metal.png"))
    build_rock_texture(os.path.join(hazards_dir, "rock_96.png"), 96, 96, seed_offset=2)
    build_rock_texture(os.path.join(hazards_dir, "rock_128.png"), 96, 128, seed_offset=3)
    print(f"Wrote {png}")
    print(f"Wrote {tsx}")
    print(f"Wrote {os.path.join(hazards_dir, 'tier_metal.png')}")
    print(f"Wrote {os.path.join(hazards_dir, 'rock_96.png')}")
    print(f"Wrote {os.path.join(hazards_dir, 'rock_128.png')}")
