#!/usr/bin/env python3
"""
Sprite Atlas Builder for GorelordsBrawler

Reads a sprites.json manifest from a character's sprite directory, splits
sprite sheets into individual frames, downscales them, and runs the Nez
SpriteAtlasPacker to produce a packed atlas.

Usage:
    python tools/build_atlas.py Content/Sprites/FutureAxe
    python tools/build_atlas.py Content/Sprites/FutureAxe --skip-pack
    python tools/build_atlas.py --all

Manifest format (sprites.json):
    {
        "cellSize": 512,          // source cell size in the sprite sheet
        "targetSize": 128,        // downscale target (omit to keep original)
        "originX": 0.5,           // atlas origin X (0.5 = center)
        "originY": 1.0,           // atlas origin Y (1.0 = bottom)
        "padding": 2,             // pixels between packed sprites
        "fps": 8,                 // default animation framerate
        "animations": {
            "idle": { "sheet": "idle.png", "frames": 58 },
            "run":  { "sheet": "run.png",  "frames": 42 }
        }
    }
"""

import argparse
import json
import math
import os
import shutil
import subprocess
import sys

from PIL import Image


def find_project_root():
    """Walk up from script location to find the repo root (contains .git)."""
    path = os.path.dirname(os.path.abspath(__file__))
    while path != os.path.dirname(path):
        if os.path.isdir(os.path.join(path, ".git")):
            return path
        path = os.path.dirname(path)
    return None


def load_manifest(sprite_dir):
    """Load and validate sprites.json from the given directory."""
    manifest_path = os.path.join(sprite_dir, "sprites.json")
    if not os.path.exists(manifest_path):
        print(f"Error: No sprites.json found in {sprite_dir}")
        sys.exit(1)

    with open(manifest_path, "r") as f:
        manifest = json.load(f)

    required = ["cellSize", "animations"]
    for key in required:
        if key not in manifest:
            print(f"Error: sprites.json missing required key '{key}'")
            sys.exit(1)

    return manifest


def split_sheet(sprite_dir, anim_name, anim_config, cell_size, target_size, frames_dir):
    """Split a sprite sheet into individual frame PNGs."""
    sheet_path = os.path.join(sprite_dir, anim_config["sheet"])
    if not os.path.exists(sheet_path):
        print(f"  Error: Sheet not found: {sheet_path}")
        return False

    img = Image.open(sheet_path)
    cols = img.width // cell_size
    frame_count = anim_config["frames"]

    anim_dir = os.path.join(frames_dir, anim_name)
    os.makedirs(anim_dir, exist_ok=True)

    resize = target_size is not None and target_size != cell_size

    frame = 0
    for row in range(math.ceil(frame_count / cols)):
        for col in range(cols):
            if frame >= frame_count:
                break

            x = col * cell_size
            y = row * cell_size
            crop = img.crop((x, y, x + cell_size, y + cell_size))

            if resize:
                crop = crop.resize((target_size, target_size), Image.LANCZOS)

            crop.save(os.path.join(anim_dir, f"{anim_name}_{frame:04d}.png"))
            frame += 1

    print(f"  {anim_name}: {frame} frames" + (f" (downscaled to {target_size}x{target_size})" if resize else ""))
    return True


def run_packer(project_root, sprite_dir, manifest):
    """Run the Nez SpriteAtlasPacker on the frames directory."""
    packer_proj = os.path.join(
        project_root,
        "Nez", "Nez.SpriteAtlasPacker", "SpriteAtlasPacker.Console",
        "SpriteAtlasPacker.Console.csproj"
    )

    if not os.path.exists(packer_proj):
        print(f"Error: Packer project not found at {packer_proj}")
        return False

    frames_dir = os.path.join(sprite_dir, "frames")
    atlas_png = os.path.join(sprite_dir, "atlas.png")
    atlas_file = os.path.join(sprite_dir, "atlas.atlas")

    origin_x = manifest.get("originX", 0.5)
    origin_y = manifest.get("originY", 0.5)
    padding = manifest.get("padding", 2)
    fps = manifest.get("fps", 8)

    max_width = manifest.get("maxWidth", 8192)
    max_height = manifest.get("maxHeight", 8192)

    cmd = [
        "dotnet", "run", "--project", packer_proj, "--",
        f"-image:{atlas_png}",
        f"-map:{atlas_file}",
        f"-fps:{fps}",
        f"-pad:{padding}",
        f"-originX:{origin_x}",
        f"-originY:{origin_y}",
        f"-mw:{max_width}",
        f"-mh:{max_height}",
        frames_dir
    ]

    print(f"\n  Packing atlas...")
    result = subprocess.run(cmd, capture_output=True, text=True)

    if result.returncode != 0:
        print(f"  Packer failed (exit {result.returncode}): {result.stderr.strip()}")
        return False

    # Report output size
    if os.path.exists(atlas_png):
        img = Image.open(atlas_png)
        size_kb = os.path.getsize(atlas_png) / 1024
        print(f"  Atlas: {img.width}x{img.height}, {size_kb:.0f} KB")

    return True


def build_character(sprite_dir, project_root, skip_pack=False):
    """Full pipeline for one character: split -> downscale -> pack."""
    char_name = os.path.basename(sprite_dir)
    print(f"\n=== {char_name} ===")

    manifest = load_manifest(sprite_dir)
    cell_size = manifest["cellSize"]
    target_size = manifest.get("targetSize")
    animations = manifest["animations"]

    # Clean and recreate frames directory
    frames_dir = os.path.join(sprite_dir, "frames")
    if os.path.exists(frames_dir):
        shutil.rmtree(frames_dir)
    os.makedirs(frames_dir)

    # Split each animation sheet
    print("  Splitting sheets...")
    for anim_name, anim_config in animations.items():
        if not split_sheet(sprite_dir, anim_name, anim_config, cell_size, target_size, frames_dir):
            return False

    # Run packer
    if not skip_pack:
        if not run_packer(project_root, sprite_dir, manifest):
            return False
    else:
        print("\n  Skipping atlas packing (--skip-pack)")

    print(f"\n  Done!")
    return True


def main():
    parser = argparse.ArgumentParser(description="Build sprite atlases for GorelordsBrawler")
    parser.add_argument(
        "sprite_dir",
        nargs="?",
        help="Path to character sprite directory (e.g., Content/Sprites/FutureAxe)"
    )
    parser.add_argument(
        "--all",
        action="store_true",
        help="Build all characters that have a sprites.json"
    )
    parser.add_argument(
        "--skip-pack",
        action="store_true",
        help="Only split frames, don't run the atlas packer"
    )

    args = parser.parse_args()

    project_root = find_project_root()
    if not project_root:
        print("Error: Could not find project root (no .git directory found)")
        sys.exit(1)

    content_sprites = os.path.join(project_root, "GorelordsBrawler", "Content", "Sprites")

    if args.all:
        # Find all directories with sprites.json
        if not os.path.exists(content_sprites):
            print(f"Error: Sprites directory not found: {content_sprites}")
            sys.exit(1)

        dirs = []
        for name in sorted(os.listdir(content_sprites)):
            candidate = os.path.join(content_sprites, name)
            if os.path.isdir(candidate) and os.path.exists(os.path.join(candidate, "sprites.json")):
                dirs.append(candidate)

        if not dirs:
            print("No characters with sprites.json found")
            sys.exit(1)

        print(f"Building {len(dirs)} character(s)...")
        for d in dirs:
            build_character(d, project_root, args.skip_pack)
    elif args.sprite_dir:
        # Resolve relative to project root if needed
        sprite_dir = args.sprite_dir
        if not os.path.isabs(sprite_dir):
            # Try relative to project root first, then CWD
            candidate = os.path.join(project_root, "GorelordsBrawler", sprite_dir)
            if os.path.exists(candidate):
                sprite_dir = candidate
            else:
                sprite_dir = os.path.abspath(sprite_dir)

        if not os.path.isdir(sprite_dir):
            print(f"Error: Directory not found: {sprite_dir}")
            sys.exit(1)

        build_character(sprite_dir, project_root, args.skip_pack)
    else:
        parser.print_help()
        sys.exit(1)


if __name__ == "__main__":
    main()
