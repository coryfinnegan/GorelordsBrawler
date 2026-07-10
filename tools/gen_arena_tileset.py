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
    """Pixel-art BOULDER for the rockfall (docs/implemented/rockfall-proposal.md).

    Authored at QUARTER scale and upscaled x4 nearest-neighbor, so one art pixel
    is exactly one 4px erosion cell — the acid eats the boulder pixel-by-pixel.
    The ALPHA CHANNEL is load-bearing: ErodibleSurface initializes its cell mask
    from it, so render, collision, and erosion all follow this exact shape.

    Technique per the pixel-art rock idiom (Lospec / SLYNYRD Pixelblog 13,
    hue-shifting tutorials): big FLAT facet clusters instead of per-pixel
    shading, a 5-shade hue-shifted ramp (highlights lean yellow, shadows lean
    purple), a hard dark outline, and crease lines on the shadow side of facet
    boundaries. Light is screen-fixed top-left — that constancy is what makes
    the tumble read while the boulder spins."""
    import math

    rng = random.Random(SEED + seed_offset)
    lw, lh = w // 4, h // 4

    # Warm oxidized-stone ramp (matches the toy-line reference boulders).
    RAMP = [
        (246, 210, 172),  # 0 highlight
        (224, 164, 130),  # 1 light
        (188, 122, 102),  # 2 mid
        (138, 82, 78),    # 3 dark
        (82, 48, 52),     # 4 outline / crease (deep plum)
    ]

    # ── Silhouette: an irregular convex POLYGON, not a smoothed blob ────────
    # Straight edge runs and visible corners are what make it read as chipped
    # stone; radial wobble reads as a ball. 7–9 vertices, jittered angles,
    # radius 82–100% of the ellipse.
    cx, cy = (lw - 1) / 2.0, (lh - 1) / 2.0
    rx, ry = lw * 0.47, lh * 0.47
    nv = 7 + rng.randrange(3)
    verts = []
    for i in range(nv):
        a = (i + rng.random() * 0.55) * 2 * math.pi / nv
        rr = 0.82 + rng.random() * 0.18
        verts.append((cx + math.cos(a) * rx * rr, cy + math.sin(a) * ry * rr))
    mask_img = Image.new("L", (lw, lh), 0)
    ImageDraw.Draw(mask_img).polygon(verts, fill=255)
    m = mask_img.load()
    solid = [[m[x, y] > 0 for x in range(lw)] for y in range(lh)]

    # Despeckle: drop 1px nubs, fill 1px holes — clean chunky pixels only.
    for y in range(lh):
        for x in range(lw):
            n = sum(1 for ax, ay in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1))
                    if 0 <= ax < lw and 0 <= ay < lh and solid[ay][ax])
            if solid[y][x] and n <= 1:
                solid[y][x] = False
            elif not solid[y][x] and n == 4:
                solid[y][x] = True

    # ── Facets: straight CHORD cuts, not Voronoi — chiseled planes that meet
    # at dead-straight creases (Voronoi boundaries arc, which reads soft).
    # Four cuts at spread base angles, each offset from center along its
    # normal, so the stone splits into comparable planes with no monster
    # facet. Each pixel's facet is its side-of-line signature.
    r_avg = (rx + ry) / 2.0
    cuts = []
    for base_a in (0.0, math.pi / 4, math.pi / 2, 3 * math.pi / 4):
        a = base_a + (rng.random() - 0.5) * 0.5
        off = (0.05 + rng.random() * 0.30) * (1 if rng.random() < 0.5 else -1)
        qx = cx - math.sin(a) * off * r_avg
        qy = cy + math.cos(a) * off * r_avg
        cuts.append((qx, qy, math.cos(a), math.sin(a)))

    def facet_at(x, y):
        sig = 0
        for i, (qx, qy, ux, uy) in enumerate(cuts):
            if (x - qx) * -uy + (y - qy) * ux > 0:
                sig |= 1 << i
        return sig

    # Shade bands by AREA QUANTILE down the light direction: facets sorted by
    # their centroid's dot with the top-left light, then the brightest ~20% of
    # AREA gets the highlight band, ~32% light, ~32% mid, ~16% dark. Area
    # quantiles (not direction thresholds) are what keep the value balance of
    # the reference on every roll — direction alone let one lucky plane flood
    # the whole stone with highlight.
    cells = {}
    for y in range(lh):
        for x in range(lw):
            if solid[y][x]:
                cells.setdefault(facet_at(x, y), []).append((x, y))
    lit_of = {}
    for f, pts in cells.items():
        mx = sum(p[0] for p in pts) / len(pts)
        my = sum(p[1] for p in pts) / len(pts)
        vx, vy = mx - cx, my - cy
        norm = math.hypot(vx, vy) or 1.0
        lit_of[f] = (-0.55 * vx - 0.83 * vy) / norm
    total = sum(len(pts) for pts in cells.values())
    band_of, acc = {}, 0
    for f in sorted(cells, key=lambda f: -lit_of[f]):
        frac = (acc + len(cells[f]) / 2.0) / total  # facet's area midpoint
        band_of[f] = 0 if frac < 0.20 else 1 if frac < 0.52 else 2 if frac < 0.84 else 3
        acc += len(cells[f])

    img = Image.new("RGBA", (lw, lh), (0, 0, 0, 0))
    px = img.load()
    fmap = [[-1] * lw for _ in range(lh)]
    bmap = [[0] * lw for _ in range(lh)]
    for y in range(lh):
        for x in range(lw):
            if not solid[y][x]:
                continue
            f = facet_at(x, y)
            fmap[y][x] = f
            bmap[y][x] = band_of[f]

    # Creases: where the up/left neighbor is a different facet of EQUAL or
    # brighter value, darken this pixel one band — a thin chisel line on the
    # shadow side of every plane boundary (equal-value planes would otherwise
    # merge into one blob). Compared against a frozen copy so creases don't
    # cascade off each other.
    orig = [row[:] for row in bmap]
    for y in range(lh):
        for x in range(lw):
            if fmap[y][x] < 0:
                continue
            for ax, ay in ((x - 1, y), (x, y - 1)):
                if (0 <= ax < lw and 0 <= ay < lh and fmap[ay][ax] >= 0
                        and fmap[ay][ax] != fmap[y][x] and orig[ay][ax] <= orig[y][x]):
                    bmap[y][x] = min(3, orig[y][x] + 1)
                    break

    # Specks: a little surface grain, one band up or down.
    interior = [(x, y) for y in range(1, lh - 1) for x in range(1, lw - 1)
                if solid[y][x] and all(solid[y + dy][x + dx]
                                       for dx in (-1, 0, 1) for dy in (-1, 0, 1))]
    for x, y in rng.sample(interior, min(5, len(interior))):
        bmap[y][x] = max(0, min(3, bmap[y][x] + rng.choice((-1, 1))))

    for y in range(lh):
        for x in range(lw):
            if solid[y][x]:
                c = RAMP[bmap[y][x]]
                px[x, y] = (c[0], c[1], c[2], 255)

    # Hard 1px (art pixel) outline, plus a doubled dark rim along the bottom
    # half — grounds the boulder and pops it off acid and background alike.
    o = RAMP[4]
    for y in range(lh):
        for x in range(lw):
            if not solid[y][x]:
                continue
            at_edge = (x == 0 or y == 0 or x == lw - 1 or y == lh - 1
                       or not solid[y][x - 1] or not solid[y][x + 1]
                       or not solid[y - 1][x] or not solid[y + 1][x])
            if at_edge:
                px[x, y] = (o[0], o[1], o[2], 255)
            elif (y > cy and y + 1 < lh and solid[y + 1][x]
                  and (y + 2 >= lh or not solid[y + 2][x])):
                c = RAMP[3]
                px[x, y] = (c[0], c[1], c[2], 255)

    img.resize((w, h), Image.NEAREST).save(out_path)


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
