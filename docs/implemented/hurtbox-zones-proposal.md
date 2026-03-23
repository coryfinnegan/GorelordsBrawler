# Per-Frame Multi-Zone Hurtbox System

## Context

The current hurtbox is a single 28x44 BoxCollider centered at the entity position (physics body center, 24 units above the floor). The character sprite is 256px at 0.8 scale = ~205 world units tall. **The hurtbox only covers the character's feet — the torso and head are unhittable.**

The user wants per-frame multi-zone hurtboxes (head, body, legs) — the industry standard for party brawlers (Smash Bros, Rivals of Aether). The architecture must also leave a door open for future **limb removal** (legs/hands blown off).

The design reuses the existing weapon socket sidecar pattern (baker → `.sockets.json` → `WeaponSocketData` → `MeleeAttack.ComputeHitboxCenter()`) as a proven template for the hurtbox zone system.

---

## 1. Baker Extension (`tools/sprite_sheet_baker.py`)

### New Blender Empties

Add a "Setup Hurtbox Zones" operator that creates three Empties parented to bones:

| Empty Name | Parent Bone | Purpose |
|---|---|---|
| `HurtboxZone_Head` | Head/neck bone | Tracks head center |
| `HurtboxZone_Body` | Spine/chest bone | Tracks torso center |
| `HurtboxZone_Legs` | Hips/pelvis bone | Tracks legs center |

Each Empty gets custom properties for zone dimensions (width/height in pixels at render resolution). These are set once per character in Blender and baked into the sidecar.

### Per-Frame Zone Tracking

In `_render_action()`, after the existing weapon socket tracking block (line ~875), add a second tracking block that iterates over all `HurtboxZone_*` Empties in the scene. For each frame:
- Use the same `world_to_camera_view()` NDC → pixel conversion
- Record `[x, y]` or `null` (behind camera)
- Same FaceLeft handling as sockets: `track_socket=False` for face-left passes (game mirrors face-right data)

### New Sidecar: `.hurtboxes.json`

Written by a new `_write_hurtboxes_sidecar()` method (parallel to `_write_sockets_sidecar()`). Format:

```json
{
  "frame_width": 256,
  "frame_height": 256,
  "zones": {
    "Head": { "width": 24, "height": 24 },
    "Body": { "width": 30, "height": 40 },
    "Legs": { "width": 28, "height": 32 }
  },
  "animations": {
    "FutureAxe_AttackIdleRightHand": {
      "Head": [[130, 45], [131, 44], null, ...],
      "Body": [[128, 100], [129, 99], ...],
      "Legs": [[128, 170], [128, 171], ...]
    }
  }
}
```

- `zones` — zone metadata (pixel dimensions at render scale). Set from Empty custom properties.
- `animations.{anim}.{zone}` — per-frame center positions in rendered pixels (same coordinate space as weapon sockets). `null` = zone not visible that frame.

### Baker UI Changes

- New panel section: "Hurtbox Zones" with PointerProperty fields for head/body/legs Empties
- New operator: "Setup Hurtbox Zones" — creates Empties, parents to bones, sets default dimensions
- Hurtbox zones are tracked for ALL animation types (not just attacks), since hurtboxes need to exist during idle, run, jump too
- Per-action toggle: "Track Hurtboxes" (default: True) — allows skipping for animations like Select screen

---

## 2. New Component: `HurtboxZoneData` (`GorelordsBrawler/Components/HurtboxZoneData.cs`)

Mirrors `WeaponSocketData` but stores multi-zone data. Loaded by `CharacterFactory` from `.hurtboxes.json` sidecars.

```csharp
public class HurtboxZoneData : Component
{
    public int FrameWidth;
    public int FrameHeight;

    // Zone name → pixel dimensions (from sidecar "zones" block)
    public Dictionary<string, Vector2> ZoneSizes = new();

    // Animation name → zone name → per-frame center positions
    public Dictionary<string, Dictionary<string, Vector2?[]>> Animations = new();

    public Vector2? GetZoneCenter(string animName, string zone, int frame)
    {
        // Same FaceLeft redirect as WeaponSocketData.GetSocket()
        // Strip "FaceLeft" suffix → look up base animation
    }

    public Vector2 GetZoneSize(string zone)
    {
        // Returns pixel dimensions for the zone
    }
}
```

---

## 3. New Component: `HurtboxZoneTracker` (`GorelordsBrawler/Components/HurtboxZoneTracker.cs`)

`IUpdatable` component that repositions and resizes zone colliders each frame based on `HurtboxZoneData` + current animation frame. Runs at a late UpdateOrder (after `LocomotionAnimator`).

```csharp
public class HurtboxZoneTracker : Component, IUpdatable
{
    private HurtboxZoneData _zoneData;
    private SpriteAnimator _animator;
    private SpriteData _spriteData;
    private CharacterStats _stats;
    private PhysicsBody _body;

    // Zone name → collider (created in OnAddedToEntity)
    private Dictionary<string, BoxCollider> _zoneColliders = new();

    // Zone name → enabled state (for future limb removal)
    private Dictionary<string, bool> _zoneEnabled = new();
```

**Key behavior:**
- Creates one `BoxCollider` per zone in `OnAddedToEntity()` (Hurtbox layer, trigger, `ShouldColliderScaleAndRotateWithTransform = false`)
- Each frame: reads current animation name + frame index from `SpriteAnimator`, looks up zone centers in `HurtboxZoneData`, sets `collider.LocalOffset` to the computed world offset
- Coordinate transform is the same as `MeleeAttack.ComputeHitboxCenter()`: `dx = (px - fw/2) * scale * facing`, `dy = bodyHalfH + (py - fh) * scale`
- **But uses `LocalOffset` instead of entity position** — zone colliders are children of the same entity, so offset is relative to entity center
- Zones with `null` position for current frame: disable the collider (`collider.Enabled = false`) until the next frame with data
- FaceLeft handling: identical to weapon sockets (strip suffix, use face-right data, multiply X by FacingDirection)

**Limb removal future-proofing:**
- `DisableZone(string zoneName)` / `EnableZone(string zoneName)` — sets `_zoneEnabled[zone] = false`, disables the collider. Future limb removal calls this.
- `GetZoneCollider(string zoneName)` — returns the specific collider for a zone (for future per-zone effects)

---

## 4. Update `Hurtbox.cs` — Zone Identification

The `local` parameter in `OnTriggerEnter(Collider other, Collider local)` identifies which specific collider was hit. Add zone identification:

```csharp
public void OnTriggerEnter(Collider other, Collider local)
{
    // ... existing checks ...

    // Identify which zone was hit (for future damage multipliers / limb removal)
    string hitZone = null;
    var zoneTracker = Entity.GetComponent<HurtboxZoneTracker>();
    if (zoneTracker != null)
    {
        hitZone = zoneTracker.GetZoneName(local);
    }

    // For now: all zones deal the same damage. hitZone is available
    // for future headshot multipliers, limb removal triggers, etc.

    _health.TakeDamage(attackData.Damage);
    // ... rest unchanged ...
}
```

---

## 5. Update `CharacterFactory.cs`

### Hurtbox creation changes

Replace the current single-hurtbox block (lines 64-70) with:

```csharp
// Try to load per-frame hurtbox zone data from sidecars
var hurtboxZoneData = new HurtboxZoneData();
bool hasZoneData = false;

// Load from main atlas sidecar
if (data.Sprite != null)
{
    hasZoneData |= TryLoadHurtboxSidecar(scene, data.Sprite.AtlasPath, hurtboxZoneData);
    if (data.Sprite.ExtraAtlasPaths != null)
    {
        foreach (var extraPath in data.Sprite.ExtraAtlasPaths)
        {
            hasZoneData |= TryLoadHurtboxSidecar(scene, extraPath, hurtboxZoneData);
        }
    }
}

if (hasZoneData)
{
    entity.AddComponent(hurtboxZoneData);
    entity.AddComponent(new HurtboxZoneTracker());
    // Zone colliders are created by HurtboxZoneTracker.OnAddedToEntity()
}
else
{
    // Fallback: single static hurtbox (legacy path / characters without zone data)
    float hurtW = data.HurtboxWidth  > 0 ? data.HurtboxWidth  : data.BodyWidth;
    float hurtH = data.HurtboxHeight > 0 ? data.HurtboxHeight : data.BodyHeight;
    var hurtboxCollider = entity.AddComponent(new BoxCollider(hurtW, hurtH));
    hurtboxCollider.PhysicsLayer = PhysicsLayers.Hurtbox;
    hurtboxCollider.CollidesWithLayers = PhysicsLayers.Hitbox;
    hurtboxCollider.IsTrigger = true;
    hurtboxCollider.ShouldColliderScaleAndRotateWithTransform = false;
}
```

### New sidecar loader

`TryLoadHurtboxSidecar()` — parallel to `TryLoadSocketSidecar()`. Discovers sidecars by replacing `.atlas` → `.hurtboxes.json`. Parses zone sizes and per-animation per-zone frame positions.

---

## 6. CharacterData Changes

Remove `HurtboxWidth` and `HurtboxHeight` fields (they become the fallback path only, keep them for backward compatibility with characters that don't have zone data yet — no change needed).

---

## Files Modified

| File | Change |
|---|---|
| `tools/sprite_sheet_baker.py` | Add hurtbox zone Empties, per-frame tracking, `.hurtboxes.json` sidecar writer, UI panel |
| `GorelordsBrawler/Components/HurtboxZoneData.cs` | **NEW** — Zone sidecar data storage component |
| `GorelordsBrawler/Components/HurtboxZoneTracker.cs` | **NEW** — Per-frame zone collider positioning component |
| `GorelordsBrawler/Components/Hurtbox.cs` | Add zone identification via `local` collider parameter |
| `GorelordsBrawler/Data/CharacterFactory.cs` | Zone sidecar loading, conditional zone vs. fallback hurtbox creation |

## What stays the same

- Physics collider (BodyWidth x BodyHeight) — unchanged, platform collision works as before
- `WeaponSocketData` / hitbox system — unchanged, operates independently
- `Health`, `Hitstun`, `HitFlash`, `RespawnHandler` — unchanged
- `CharacterData.HurtboxWidth/Height` — kept as fallback for characters without zone data

## Limb Removal Future Path

The architecture supports limb removal with minimal additional work:
1. `HurtboxZoneTracker.DisableZone("Legs")` — disables the legs collider, character is no longer hittable there
2. Zone name from `Hurtbox.OnTriggerEnter` → conditional logic (e.g., "if hitZone == 'Head' → instant kill")
3. Visual: swap sprite animation to limbless variant (separate atlas)
4. Gameplay: disable associated abilities (e.g., no jump if legs removed)

Each zone collider is independently controllable — no refactoring needed to add limb removal later.

## Verification

1. `dotnet build GorelordsBrawler/GorelordsBrawler.csproj`
2. In Blender: run "Setup Hurtbox Zones", parent Empties to correct bones, bake all animations
3. Verify `.hurtboxes.json` sidecars are generated alongside `.atlas` files
4. Run game, open Nez debug console (tilde), enable collider rendering
5. Verify:
   - Three zone colliders visible (head, body, legs) tracking the character's visual body
   - Zone colliders move with animation frames (not static)
   - Physics collider still at feet, walking/jumping works normally
   - Attacks connect when weapon visually hits any zone
   - Face-left animations mirror zones correctly (no double-flip)
   - Characters without hurtbox sidecars fall back to single static hurtbox
