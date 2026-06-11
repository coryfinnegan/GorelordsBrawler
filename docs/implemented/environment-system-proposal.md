# Environment System Proposal — Walled Arenas + Tiled Maps

## Context

The arena is currently hardcoded: four colored rectangles, open pits on both sides, no visual detail. The game needs a proper environment system that supports:

- **Now**: Walled arena (no pits), bigger play area, multi-level platforms
- **Future**: Environmental hazards (acid floors, saws, crushers), breakable/falling platforms, environmental kills, varied terrain

The right foundation is **Tiled map support** — Nez has a full Tiled integration built in. This lets you build maps visually in the Tiled editor instead of hardcoding positions in C#.

---

## Phase 1: Tiled Map Integration (This Task)

### What Changes

Replace the hardcoded platform entities in `ArenaScene` with a Tiled `.tmx` map loaded at runtime.

### Tiled Map Structure

Create a map in the [Tiled editor](https://www.mapeditor.org/) with these layers:

| Layer | Type | Purpose |
|---|---|---|
| `background` | Tile layer | Visual background (non-interactive) |
| `platforms` | Tile layer | Visual platform/wall tiles |
| `collision` | Tile layer | Physics collision geometry (auto-generates BoxColliders) |
| `spawns` | Object layer | Player spawn points (Point objects with custom `index` property) |
| `hazards` | Object layer | Future: hazard trigger zones |

The `collision` layer is special — `TiledMapRenderer` automatically converts contiguous tiles into `BoxCollider` arrays. No manual collider creation needed.

### Loading in ArenaScene

```csharp
// Replace all CreatePlatform() calls with:
var tiledMap = Content.LoadTiledMap("Content/maps/arena1.tmx");

var mapEntity = CreateEntity("tiled-map");
var renderer = mapEntity.AddComponent(new TiledMapRenderer(tiledMap, "collision"));
renderer.SetLayersToRender("background", "platforms");
renderer.RenderLayer = GameConstants.Rendering.DefaultRenderLayer;

// Spawn players from object layer
var spawnGroup = tiledMap.GetObjectGroup("spawns");
```

### Walls

In Tiled, simply paint solid tiles along the left, right, and top edges of the collision layer. Characters bounce off walls via existing `Mover.Move()` collision — no code change needed. The walls are just collision tiles.

### Arena Size

The Tiled map dimensions define the play area. Start with something like **1200x800 pixels** (50% wider than current 800x600) to give more room for platforming.

### Camera

`BrawlerCamera` already follows players — it just needs to know the map bounds so it doesn't scroll past the edges. `TiledMapRenderer` exposes the map bounds for this.

### Tileset

For now, can use a simple 16x16 or 32x32 tileset with a few tiles (solid platform, wall, background fill). Placeholder art is fine — the system works the same with fancy tiles later.

---

## Phase 2: Future Environment Features (Not This Task)

These become straightforward once Tiled maps are working:

### Environmental Hazards
- Define hazard zones as objects in the `hazards` layer (rectangles with `type` = "acid", "saw", etc.)
- `HazardManager` SceneComponent reads the object layer, creates trigger colliders
- `HazardZone` component on each: deals damage on contact, applies knockback, optionally instant-kills

### Falling/Breakable Platforms
- Mark platform objects with `type` = "breakable" and properties like `break_delay`, `respawn_time`
- `BreakablePlatform` component: starts crumbling when stepped on, falls after delay, respawns

### Environmental Kills
- Specific hazard types that bypass HP and instant-kill (acid, creature, saw)
- Triggers death animation + respawn, just like HP=0

### Varied Terrain
- Nez's `TiledMapMover` supports tile properties: `nez:isOneWayPlatform` (jump through from below), `nez:isSlope` (angled surfaces)
- Custom tile properties for surface type: `surface=ice` (reduce friction), `surface=sticky` (increase friction)
- `WalkAbility` reads surface type from the tile the character is standing on

### Multiple Maps
- Each `.tmx` file is a different arena
- Map selection in character select or random rotation
- All use the same loading code — just swap the file path

---

## Files Modified (Phase 1)

| File | Change |
|---|---|
| `GorelordsBrawler/Scenes/ArenaScene.cs` | Replace hardcoded platforms with TiledMap loading |
| `GorelordsBrawler/Constants/GameConstants.cs` | Update Arena constants (map path, remove hardcoded positions) |
| `Content/maps/arena1.tmx` | **New** — first Tiled map file |
| `Content/tilesets/arena.tsx` | **New** — tileset definition |
| `Content/tilesets/arena.png` | **New** — tileset image (placeholder) |

## Content Pipeline Note

`.tmx` files are XML and loaded via `Content.LoadTiledMap()` (Nez's content loader). They don't go through MonoGame's MGCB pipeline — they're loaded at runtime directly. The tileset PNG does need to be accessible as a content file.

## Verification

1. Install [Tiled Map Editor](https://www.mapeditor.org/)
2. Create a simple arena map with ground, walls, and 3-4 platforms
3. Add spawn point objects in a `spawns` layer
4. `dotnet build` and run — should render the Tiled map with collision
5. Characters should collide with walls (can't fall off edges)
6. Camera should respect map bounds
