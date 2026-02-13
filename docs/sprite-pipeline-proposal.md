# Digitized Sprite Pipeline: From Toy to Game

## Context

GorelordsBrawler characters are currently rendered as colored rectangles using `PrototypeSpriteRenderer`. The game is based on M.U.S.C.L.E.-style toys — static, non-articulated poses. Rather than fight the static poses, we embrace them — rendering the 3D sculpts from multiple angles to create sprite sheets, then using Nez's built-in `SpriteAtlas` + `SpriteAnimator` pipeline to render and animate them in-game.

We have **17 print-ready OBJ/STL models** in `reference/wetransfer_death-grid-playset_2026-02-04_2223/Gorelords/`, plus the Death Grid playset pieces. This means we can skip photography entirely and render sprites directly from the 3D models in Blender.

### Available Models

| Model File | Likely Character | Role (from treatment) |
|---|---|---|
| `Future AXE.obj` | Future Axe | Main character, melee brawler |
| `Tormentorr.obj` | The Suffer | Reigning champion, heavy fighter |
| `Astrarot_PrintReady.OBJ` | Ichor | Two-headed ogre, grappler |
| `Bloodozer_REV1.obj` | Bloodozer | Vehicle/charge type |
| `Treadkill_PrintReady.OBJ` | Treadkill | Vehicle/charge type |
| `Phaserbeast_PrintReady.OBJ` | Phaserbeast | Ranged/beam fighter |
| `MaceFace_PrintReady.OBJ` | MaceFace | Weapon specialist |
| `Skab_PrintReady.OBJ` | Skab | Fighter |
| `Pistain_PrintReady.OBJ` | Pistain | Fighter |
| `TotalMaster_Printready.OBJ` | Total Master | Boss/villain (non-playable?) |
| `RightHandMan_Printready.OBJ` | Right Hand Man | Brute enforcer |
| `MutantCop_Printready02.OBJ` | Mutant Cop | Guard |
| `MadMan_PrintReady02.OBJ` | Madman | Death Grid surgeon |
| `Death Guard 1.obj` | Death Guard | Cyborg guards (TV heads) |
| `DeathGuard_2_PrintReady.OBJ` | Death Guard 2 | Cyborg guard variant |
| `Bobee_PrintReady.OBJ` | Bobee | TBD |
| `lil flex.obj` | Lil Flex | TBD |

**Note:** "Trollborg" and "Doc Marauder" (current in-game characters) don't have exact model matches yet. We'll either map them to existing models, create new ones, or keep them as colored rectangles until models are available.

---

## Phase 1: Blender Setup (One-Time)

### 1a. Install Blender

1. Go to blender.org and download **Blender 4.x** (free, works on Windows/Mac/Linux)
2. Run the installer with default settings
3. Launch Blender — you'll see a splash screen with a default cube scene

### 1b. First Launch — Getting Oriented

When Blender opens you'll see:

- **3D Viewport** — the big center area showing a cube, a camera, and a light
- **Outliner** (top-right) — lists everything in your scene like a file explorer
- **Properties panel** (bottom-right) — settings for the selected object
- **Timeline** (bottom) — for animation, we'll use this later

**Essential controls (memorize these):**
| Action | Control |
|---|---|
| Orbit (rotate view) | Middle-mouse-button drag |
| Pan (slide view) | Shift + middle-mouse-button drag |
| Zoom | Scroll wheel |
| Select object | Left-click |
| Delete selected | X key, then confirm |
| Undo | Ctrl+Z |
| Numpad 0 | Look through camera |
| Numpad 1 | Front view |
| Numpad 3 | Right side view |
| Numpad 7 | Top view |

If you don't have a numpad, go to `Edit > Preferences > Input` and check **Emulate Numpad** — this lets you use the number row instead.

If you don't have a middle mouse button, check **Emulate 3 Button Mouse** — this lets you Alt+left-click to orbit.

### 1c. Clean the Default Scene

Before importing anything:

1. Press `A` to select all (the cube, camera, and light will all highlight)
2. Press `X` then click **Delete** to remove everything
3. You now have an empty scene

---

## Phase 2: Import and Prepare a Model

### 2a. Import the OBJ

1. `File > Import > Wavefront (.obj)`
2. Navigate to `reference/wetransfer_death-grid-playset_2026-02-04_2223/Gorelords/`
3. Select the `.OBJ` file (e.g. `Future AXE.obj`)
4. **Before clicking Import**, check the import settings panel on the right side:
   - **Forward Axis:** `-Z Forward` (or try `Y Forward` — depends on how the model was exported)
   - **Up Axis:** `Y Up` (or `Z Up` — try both if the model imports sideways)
5. Click **Import**

The model will appear in the viewport. It might be huge, tiny, or oriented wrong — that's normal.

### 2b. Fix Orientation and Scale

**If the model is sideways or upside down:**
1. Select the model (left-click it)
2. Press `R` to rotate, then `X`, `Y`, or `Z` to constrain to an axis, type the degrees (e.g. `90`), press Enter
3. Keep rotating until the character is standing upright, facing toward you

**If the model is too big or too small:**
1. Select the model
2. Press `S` to scale, then move the mouse to resize. Click to confirm
3. Or press `S` then type a number like `0.1` (shrink to 10%) or `10` (grow 10x)

**Center the model at the origin:**
1. Select the model
2. Press `Alt+G` to snap it to the world origin (center of the scene)
3. If the model's feet aren't at ground level, press `G` then `Z` to move it up/down along the vertical axis. Click to confirm

**Apply the transforms** (important — locks in your changes):
1. With the model selected, press `Ctrl+A`
2. Click **All Transforms**

### 2c. Understanding the Model — It's Just Gray

The OBJ files from your reference are **print-ready sculpts** — they have geometry (shape) but no color information. This is totally fine. You have two options:

**Option 1: Flat color per character (Recommended to start)**
This gives each character a solid color, matching the M.U.S.C.L.E. toy aesthetic (single-color plastic figures). This is the fastest path to sprites.

**Option 2: Painted/textured (Later)**
Full color painting on the model. More work but better looking. Covered in Phase 2e below.

### 2d. Apply a Flat Material Color

1. Select the model
2. In the **Properties panel** (right side), click the **Material** tab (sphere icon — it's a circle that looks like a red/orange ball)
3. If there's no material listed, click **New**
4. You'll see a material called "Material" with settings. Find **Base Color**
5. Click the white rectangle next to **Base Color** — a color picker appears
6. Pick your character's color. For M.U.S.C.L.E. vibes: flesh pink, dark green, purple, red, etc.
7. Below Base Color, find **Roughness** — set it to about `0.8` (makes it look like matte plastic, not shiny)
8. That's it — the model is now a solid color

**To see the color in the viewport** (not just in renders):
- In the top-right of the 3D viewport, find the shading mode buttons (four circles)
- Click the third one (**Material Preview**) — the model will now show its color
- Or click the fourth one (**Rendered**) to see it with full lighting

### 2e. Painting the Model (Optional, More Advanced)

If you want to paint actual detail onto the model (skin tones, armor colors, weapon details), you'll use **Texture Paint** mode. This is more involved but produces much better results.

**Step 1: Create a texture to paint on**

1. Select the model
2. Go to the **Material** tab, click on the yellow dot next to **Base Color**
3. Select **Image Texture** from the dropdown
4. Click **New** — a dialog appears:
   - Name: `FutureAxe_Color` (or whatever character)
   - Width/Height: `2048 x 2048` (good starting resolution)
   - Color: White
   - Check **Alpha** if you want transparency
5. Click **OK**

**Step 2: UV Unwrap the model**

UV unwrapping tells Blender how to flatten the 3D surface onto the 2D texture image. Think of it like peeling an orange and flattening the skin.

1. Select the model
2. Press `Tab` to enter **Edit Mode** (the model turns into a wireframe mesh)
3. Press `A` to select all faces
4. Press `U` to open the UV menu
5. Select **Smart UV Project** — this is the easiest auto-unwrap
   - Angle Limit: `66` (default is fine)
   - Click **OK**
6. Press `Tab` to go back to **Object Mode**

**Step 3: Paint**

1. Switch to **Texture Paint** workspace — click the tab at the very top of the Blender window that says **Texture Paint** (or go to the dropdown next to "Layout" / "Modeling" etc.)
2. You'll see the model on the left and the flattened UV texture on the right
3. Pick a color from the color wheel on the left sidebar
4. Paint directly on the 3D model — left-click and drag to paint
5. Use brush size (F key + drag) and strength to control the brush
6. Paint different regions different colors — armor, skin, weapons, etc.

**Tips for painting:**
- Start with big flat color regions first, detail later
- Use the **Fill** brush (change brush type in the header) to quickly fill large areas
- Press `Ctrl+S` in the **Image Editor** (right panel) to save your texture — Blender does NOT auto-save textures
- You can also paint in an external program: save the UV image (`Image > Save As` in the Image Editor), paint in GIMP/Photoshop/Photopea, then reload it in Blender

### 2f. Simple Posing (No Skeleton Needed)

Since these are static toy sculpts, you probably don't need a full skeleton rig. But you might want small pose tweaks — tilt the character back for a "hit" reaction, raise an arm, etc.

**Method: Proportional Editing (sculpt-like deformation)**

1. Select the model, press `Tab` to enter Edit Mode
2. Turn on **Proportional Editing** — click the circle icon in the header bar (or press `O`)
3. Set the falloff type to **Smooth** (dropdown next to the proportional editing icon)
4. Select a vertex or group of vertices near what you want to move (e.g., the head area for a knockback tilt)
5. Press `G` to grab, then move the mouse — you'll see a circle of influence
6. Scroll the mouse wheel to make the influence radius bigger or smaller
7. This will smoothly deform the area around your selection, like pushing clay

**For a "hit" pose:**
1. Select vertices near the upper body/head
2. Press `G` then `Y` (to move backward), drag slightly to tilt the character back
3. Scroll wheel to adjust how much of the body gets pulled along
4. Press `Tab` to exit Edit Mode

**Save the pose as a separate file** — `File > Save As` with a different name (e.g., `FutureAxe_hit.blend`) so you don't lose the original standing pose.

### 2g. Full Rigging with a Skeleton (Optional, Advanced)

If you want proper poseable characters (multiple attack poses, walk cycles, etc.), you'll need an armature (skeleton). This is significantly more work but gives maximum flexibility.

**Step 1: Add an armature**

1. Make sure you're in Object Mode
2. `Add > Armature > Single Bone` (or press Shift+A, then Armature > Single Bone)
3. A bone appears at the origin. This is the root bone.

**Step 2: Build the skeleton**

1. Select the armature, press `Tab` to enter Edit Mode
2. Select the tip (top) of the bone
3. Press `E` to **Extrude** — this creates a new connected bone. Drag it to position
4. Build a chain: spine > chest > neck > head. Branch out for arms and legs
5. Name each bone in the Properties panel (bone icon) — e.g., "Spine", "UpperArm.L", "UpperArm.R"

A basic biped skeleton needs roughly:
```
Root
 └─ Spine
     └─ Chest
         ├─ Neck > Head
         ├─ UpperArm.L > LowerArm.L > Hand.L
         └─ UpperArm.R > LowerArm.R > Hand.R
     └─ Hips
         ├─ UpperLeg.L > LowerLeg.L > Foot.L
         └─ UpperLeg.R > LowerLeg.R > Foot.R
```

**Step 3: Parent the mesh to the skeleton**

1. Press `Tab` to exit Edit Mode on the armature
2. **First** click the character mesh to select it
3. **Then** Shift+click the armature to add it to the selection (armature should have a brighter outline — it's the "active" object)
4. Press `Ctrl+P` > **With Automatic Weights**
5. Blender will automatically figure out which parts of the mesh should move with which bone

**Step 4: Pose the character**

1. Select the armature
2. Switch to **Pose Mode** (dropdown in the top-left of the viewport, or Ctrl+Tab)
3. Click a bone — it highlights
4. Press `R` to rotate the bone. The mesh deforms with it
5. Pose the character however you want — fighting stance, hit reaction, attack wind-up

**If automatic weights look wrong** (arm moves the leg, etc.):
- Select the mesh, go to Edit Mode
- Select the vertices that are moving wrong
- In the Properties panel, go to the **Object Data** tab (green triangle icon)
- Under **Vertex Groups**, find the bone that's incorrectly pulling those vertices
- Click **Remove** to take them out of that group
- Select the correct bone's vertex group and click **Assign**

This is fiddly work. For a first pass, automatic weights are usually 80% correct and that's good enough for static sprite renders.

---

## Phase 3: Render Setup — The Camera Rig

This is where the magic happens. You'll set up a camera that orbits the character and renders out one frame per angle automatically.

### 3a. Set Up Lighting

1. `Add > Light > Sun` (Shift+A > Light > Sun)
2. The sun light gives even, directional lighting — good for sprites
3. In the **Properties panel** (light bulb icon), set:
   - **Strength:** `3.0` (adjust to taste — brighter = more detail visible)
   - **Color:** Pure white for now
4. Rotate the sun to come from above-front: select it, press `R` then `X`, type `45`, Enter
5. Optionally add a second, dimmer sun from the opposite side for fill light:
   - `Add > Light > Sun`
   - **Strength:** `1.0`
   - Rotate to come from behind/below

### 3b. Set Up the Camera

1. `Add > Camera` (Shift+A > Camera)
2. Select the camera, then in the **Properties panel** (camera icon), set:
   - **Type:** Orthographic (this gives a flat, sprite-like look — no perspective distortion)
   - **Orthographic Scale:** Adjust until the character fills most of the frame (try `1.5` to start, adjust later)
3. Position the camera to look at the character:
   - Select the camera
   - Press `Numpad 0` to look through it
   - Use `G` to move and `R` to rotate until the character is centered and fills the frame
   - Or: select the camera, then in Properties > Object Properties (orange square icon), set:
     - **Location:** `X: 0, Y: -3, Z: 1` (in front of and slightly above the character)
     - **Rotation:** `X: 80, Y: 0, Z: 0` (looking slightly down at the character)

### 3c. Set Up Transparent Background

We need PNG output with a transparent background (no gray/white behind the character).

1. Go to **Render Properties** (camera icon in Properties panel)
2. Set **Render Engine:** `EEVEE` (fast) or `Cycles` (prettier but slower — EEVEE is fine for sprites)
3. Under **Film**, check **Transparent** — this makes the background see-through in the render
4. Go to **Output Properties** (printer icon):
   - **Resolution X:** `256` (or `128` for smaller sprites)
   - **Resolution Y:** `384` (1.5x width for a tall character) — adjust ratio to fit your characters
   - **File Format:** `PNG`
   - **Color:** `RGBA` (the A means alpha/transparency)

### 3d. Test Render

1. Press `F12` to render (or `Render > Render Image`)
2. You should see your character on a transparent (checkerboard) background
3. If the character is too small/big, adjust the camera's **Orthographic Scale**
4. If the character is cut off, move the camera back or adjust the scale
5. Press `Esc` to close the render window

### 3e. Camera Turntable Rig (Automated Multi-Angle Rendering)

Instead of manually positioning the camera 8 times, we'll make it orbit the character automatically.

**Step 1: Create an Empty as the center point**

1. `Add > Empty > Plain Axes` (Shift+A > Empty > Plain Axes)
2. Position it at the center of your character (should already be at origin if you centered earlier)
3. Name it "CameraTarget" in the Outliner (double-click the name)

**Step 2: Parent the camera to the empty**

1. Select the camera first (left-click)
2. Then Shift+click the CameraTarget empty (so both are selected, empty is active/brighter)
3. Press `Ctrl+P` > **Object (Keep Transform)**
4. Now when you rotate the empty, the camera orbits around the character

**Step 3: Test the orbit**

1. Select the CameraTarget empty
2. Press `R` then `Z` (rotate around vertical axis), type `45`, Enter
3. Press `Numpad 0` to look through the camera — you should see the character from a different angle
4. Press `Ctrl+Z` to undo

**Step 4: Animate the rotation for batch rendering**

1. Select the CameraTarget empty
2. Make sure the Timeline is visible at the bottom. Set the frame range:
   - **Start:** `1`
   - **End:** `8` (for 8 angles)
3. Go to **frame 1** (click on frame 1 in the timeline, or type `1` in the frame counter)
4. With the empty selected, press `I` (Insert Keyframe) > **Rotation**
5. Go to **frame 2**: in the empty's Properties > Object Properties > Rotation, set `Z: 45`
6. Press `I` > **Rotation**
7. Repeat for each frame:
   - Frame 3: Z = 90
   - Frame 4: Z = 135
   - Frame 5: Z = 180
   - Frame 6: Z = 225
   - Frame 7: Z = 270
   - Frame 8: Z = 315

**Step 5: Fix the interpolation**

By default Blender smoothly interpolates between keyframes (the camera would slowly sweep). We want it to snap to each angle.

1. Open the **Graph Editor** — at the bottom of the screen, click the editor type dropdown (might say "Timeline") and switch to **Graph Editor**
2. Press `A` to select all keyframes
3. Press `T` to set interpolation type
4. Select **Constant** — now the camera snaps to each angle instead of sliding

**Step 6: Set output path and render all frames**

1. Go to **Output Properties** (printer icon)
2. Set the output path: click the folder icon and navigate to `assets/FutureAxe/idle/`
3. Set the file name to something like `angle_` — Blender will append the frame number automatically
4. `Render > Render Animation` (or press Ctrl+F12)
5. Blender will render 8 images: `angle_0001.png` through `angle_0008.png`

**Step 7: Rename the outputs**

Rename the files to match the game's naming convention:
```
angle_0001.png  →  front.png
angle_0002.png  →  front-right.png
angle_0003.png  →  right.png
angle_0004.png  →  back-right.png
angle_0005.png  →  back.png
angle_0006.png  →  back-left.png
angle_0007.png  →  left.png
angle_0008.png  →  front-left.png
```

Or use a quick batch rename in PowerShell:
```powershell
$names = @("front","front-right","right","back-right","back","back-left","left","front-left")
for ($i = 0; $i -lt 8; $i++) {
    $frame = $i + 1
    $src = "angle_{0:D4}.png" -f $frame
    $dst = "$($names[$i]).png"
    Rename-Item $src $dst
}
```

### 3f. Rendering the "Hit" Pose

1. `File > Save As` — save as `FutureAxe_hit.blend`
2. Modify the pose using proportional editing (Phase 2f) or skeleton posing (Phase 2g) — tilt the character backward
3. Only render 2 angles: front (frame 1) and back (frame 5)
4. Save to `assets/FutureAxe/hit/` as `hit-front.png` and `hit-back.png`

### 3g. Save a Reusable Template

Once you have the camera rig, lighting, and render settings working for one character:

1. Delete the character mesh (select it, press X, Delete)
2. `File > Save As` — save as `SpriteRenderTemplate.blend`
3. For each new character: open the template, import the OBJ, center it, apply material, render

This way you only set up the camera rig once.

---

## Phase 4: Image Processing

Since we're rendering from Blender with a transparent background, most processing is already done. All that's left:

### 4a. Scale to Game Size

Characters in the game are roughly 28-48 pixels wide. Decide on a base resolution:
- **Recommended: 64x96 per frame** — small enough to feel retro, large enough to see detail
- You can render at a higher resolution in Blender (e.g. 256x384) then downscale for better quality

**Batch downscale with ImageMagick** (free CLI tool, install from imagemagick.org):
```powershell
# Run from the character's idle folder
Get-ChildItem *.png | ForEach-Object {
    magick $_.Name -resize 64x96 -gravity center -extent 64x96 $_.Name
}
```

Or do it in GIMP/Photopea: `Image > Scale Image` to 64x96, use **Bilinear** interpolation for smooth look or **Nearest Neighbor** for pixel-art crunch.

### 4b. Verify Transparency

Open any output PNG in an image viewer that supports transparency. The background should be transparent (shown as checkerboard in most editors). If it's white or colored, go back to Blender and make sure **Film > Transparent** is checked and output is set to **RGBA**.

---

## Phase 5: Atlas Creation

### Tool: Nez.SpriteAtlasPacker

Nez ships with a sprite atlas packer at: `Nez/Nez.SpriteAtlasPacker/PrebuiltExecutable/SpriteAtlasPacker.exe`

**Folder structure for the packer:**
```
assets/
  FutureAxe/
    idle/
      front.png
      front-right.png
      right.png
      ...
    hit/
      hit-front.png
      hit-back.png
  Tormentorr/
    idle/
      front.png
      ...
```

The packer treats **subfolders as animations** — images in `idle/` become frames of an animation named `idle`, images in `hit/` become frames of `hit`.

**Run per character:**
```powershell
Nez\Nez.SpriteAtlasPacker\PrebuiltExecutable\SpriteAtlasPacker.exe `
  -image:Content\Atlases\FutureAxe.png `
  -map:Content\Atlases\FutureAxe.atlas `
  -fps:8 `
  -originX:0.5 -originY:0.5 `
  assets\FutureAxe
```

**Output:** `FutureAxe.atlas` + `FutureAxe.png` — a packed texture sheet and a text manifest.

### Atlas File Format (for reference)

The `.atlas` file is plain text:
```
idle-front
    0,0,64,96
    0.5,0.5
idle-front-right
    64,0,64,96
    0.5,0.5
...

idle
    8
    0,1,2,3,4,5,6,7
hit
    8
    8,9
```

The top section defines named sprites (name, source rect, origin). A blank line separates sprites from animations. Animations reference sprite indices and a framerate.

---

## Phase 6: Engine Integration

### 6a. File Layout

```
Content/
  Atlases/
    FutureAxe.atlas
    FutureAxe.png
    Tormentorr.atlas
    Tormentorr.png
  Characters/
    Trollborg.json      (existing)
    DocMarauder.json    (existing)
    FutureAxe.json      (new — or update existing to point to atlas)
```

### 6b. CharacterData Changes

**File:** `GorelordsBrawler/Data/CharacterData.cs`

Add an atlas path field:
```csharp
public string atlasPath;    // e.g. "Content/Atlases/FutureAxe"
```

The color fields (`colorR`, `colorG`, `colorB`) and body dimensions stay — they're still useful as fallbacks and for the character select preview. The `bodyWidth`/`bodyHeight` remain relevant for collision box sizing.

### 6c. CharacterFactory Changes

**File:** `GorelordsBrawler/Data/CharacterFactory.cs`

Replace `PrototypeSpriteRenderer` with `SpriteAnimator` when an atlas is available:

```csharp
if (!string.IsNullOrEmpty(data.atlasPath))
{
    var atlas = scene.Content.LoadSpriteAtlas(data.atlasPath + ".atlas");
    var animator = entity.AddComponent<SpriteAnimator>();
    animator.AddAnimationsFromAtlas(atlas);
    animator.Play("idle", SpriteAnimator.LoopMode.Loop);
}
else
{
    // Fallback to prototype renderer (keeps existing characters working)
    var renderer = entity.AddComponent(new PrototypeSpriteRenderer(stats.bodyWidth, stats.bodyHeight));
    renderer.SetColor(stats.BodyColor);
}
```

Characters without an atlas keep working as colored rectangles — you can add sprites one character at a time.

### 6d. Animation State Management

A new `SpriteController` component will read game state and tell the `SpriteAnimator` what to play:

**File:** `GorelordsBrawler/Components/SpriteController.cs` (new)

Responsibilities:
- Read `PhysicsBody.FacingDirection` to pick left vs right angle sprites
- On damage received (listen to `Health.OnDamaged`), briefly play the `hit` animation then return to `idle`
- Handle `SpriteAnimator.FlipX` based on facing direction (so we only need right-facing sprites, flip for left)
- Idle "animation" = the static front-facing sprite, optionally with a subtle bob (handled by entity transform, not sprite frames)

If we only render from one side (right-facing), we use `FlipX = true` when facing left. This halves the required renders from 8 to 5 (front, front-right, right, back-right, back — the left angles are just mirrored right angles).

### 6e. Constants

**File:** `GorelordsBrawler/Constants/GameConstants.cs`

```csharp
public static class Sprites
{
    public const string IdleAnimation = "idle";
    public const string HitAnimation = "hit";
    public const float HitAnimationDuration = 0.3f;
    public const float IdleBobAmount = 1.5f;       // pixels of vertical bob
    public const float IdleBobSpeed = 2f;           // cycles per second
}
```

### 6f. Projectile and Hitbox Sprites (Future)

Projectiles and melee hitboxes can also get sprite treatment later. For now they stay as `PrototypeSpriteRenderer` — colored rectangles for hitboxes are actually a fine visual style for a fighting game (think debug-mode-as-aesthetic).

---

## Phase 7: Minimum Viable Sprite

To validate the full pipeline end-to-end with one character:

1. Open Blender, import `Future AXE.obj`, center and orient it
2. Apply a flat green/olive material color
3. Set up the camera rig, lighting, and transparent background
4. Render **3 angles minimum**: front, right, back
5. Downscale to 64x96, verify transparent background
6. Run SpriteAtlasPacker to generate `FutureAxe.atlas` + `FutureAxe.png`
7. Add `atlasPath` to character JSON
8. Update `CharacterFactory` with the atlas/fallback branch
9. Create `SpriteController` with facing direction + FlipX logic
10. Run the game — character renders as a real sprite, others stay as rectangles

Once this works, scale to more angles, more characters, hit reactions, and painted textures.

---

## Appendix A: Blender Keyboard Cheat Sheet

| Key | What It Does |
|---|---|
| Middle mouse drag | Orbit view |
| Shift + middle mouse | Pan view |
| Scroll wheel | Zoom |
| Left click | Select |
| A | Select all |
| X | Delete selected |
| G | Grab (move) |
| R | Rotate |
| S | Scale |
| G/R/S then X/Y/Z | Constrain to axis |
| Tab | Toggle Edit Mode |
| Numpad 0 | Camera view |
| Numpad 1/3/7 | Front/Right/Top view |
| F12 | Render image |
| Ctrl+F12 | Render animation |
| Ctrl+Z | Undo |
| Ctrl+S | Save |
| I | Insert keyframe |
| O | Toggle proportional editing |
| E (Edit Mode) | Extrude |
| Alt+G | Clear location (snap to origin) |
| Ctrl+A | Apply transforms |
| Ctrl+P | Parent objects |

## Appendix B: Troubleshooting

**Model imports sideways/upside down:** Try different Forward/Up axis settings in the import dialog. Common combos: `-Z Forward, Y Up` or `Y Forward, Z Up`.

**Model is solid black in render:** You need a light. Add a Sun light (Shift+A > Light > Sun).

**Render background isn't transparent:** Render Properties > Film > check Transparent. Output Properties > set Color to RGBA.

**Colors don't show in viewport:** Switch viewport shading to Material Preview (third circle button in viewport header).

**Proportional editing moves everything:** Scroll the mouse wheel to shrink the influence radius. Or check that you're in the right mode (should show a circle around your cursor).

**Automatic weights fail ("bone heat" error):** The mesh might have issues. Try: select mesh > Edit Mode > Mesh menu > Clean Up > Merge by Distance. Then try parenting again.

**Render is taking forever:** Switch from Cycles to EEVEE (Render Properties > Render Engine). EEVEE is much faster and fine for sprite-quality output.

---

## Tools Summary

| Step | Tool | Cost |
|---|---|---|
| 3D rendering | Blender 4.x | Free |
| Image scaling (optional) | ImageMagick or GIMP | Free |
| Atlas packing | Nez.SpriteAtlasPacker (included) | Free |
| Texture painting (optional) | Blender or GIMP/Photopea | Free |

---

## Verification

After implementing Phase 6 (engine integration):
1. `dotnet build GorelordsBrawler/GorelordsBrawler.csproj` — should compile with no new errors
2. Run the game, select the sprite character — should render with the sprite instead of a colored rectangle
3. Move left/right — sprite should flip via `FlipX`
4. Take damage — should briefly show hit sprite then return to idle
5. Select a character without an atlas — should still render as a colored rectangle (fallback works)

---

## Appendix C: Learning Resources — Tutorials, Devlogs, and Tools

This section collects tutorials, YouTube channels, devlogs, and tools specifically relevant to our pipeline: taking 3D sculpt models, texturing them, rigging/posing them, and rendering them into pre-rendered 2D sprites (DKC / Killer Instinct style).

### The Technique: Pre-Rendered 3D Sprites

The technique we're using was pioneered by Rare in 1994 for Donkey Kong Country and Killer Instinct. They modeled characters on Silicon Graphics workstations, animated them, then rendered each frame into 2D sprites. The result looked "3D" but ran on hardware that couldn't handle real-time 3D. Today we can do the same thing in Blender for free.

**Background reading:**
- [The Making of Donkey Kong Country & Killer Instinct / Pre-rendered Graphics (NeoGAF)](https://www.neogaf.com/threads/the-making-of-donkey-kong-country-killer-instinct-pre-rendered-graphics.1607594/) — Deep dive into Rare's original process with SGI workstations
- [Bits & Beats: 30 Retro Games with DKC-like Pre-Rendered Sprites (NeoGAF)](https://www.neogaf.com/threads/bits-beats-30-retro-games-with-donkey-kong-country-like-pre-rendered-sprites.1638369/) — Catalog of 30 games that used this technique, good for visual reference and inspiration
- [Pre-rendered Graphics: Showing Examples, Discussing Possibilities (NeoGAF)](https://www.neogaf.com/threads/pre-rendered-graphics-showing-examples-discussing-possibilities.1613023/) — Community thread with modern examples and discussion of the technique's revival
- [Why Prerendering is Here to Stay in Game Dev (garagefarm.net)](https://garagefarm.net/blog/why-prerendering-is-here-to-stay-in-game-dev) — Overview of pre-rendering in modern game development
- [3D Rendered Pixel Sprites (cxong.github.io)](https://cxong.github.io/2017/03/3d-rendered-pixel-sprites) — Technical breakdown of rendering 3D models into pixel-art-style sprites

---

### Step 1: Texturing Your Model

The OBJ files are untextured sculpts. You need to add color/material to them before rendering sprites.

#### Grant Abbitt — Texture Painting for PS1 Characters (YouTube, Free)
This is the single best starting point for a complete beginner. Grant walks through the entire process of texture painting a low-poly character in Blender — UV unwrapping, setting up materials, and hand-painting directly on the model. The PS1 aesthetic (low-res, chunky, hand-painted) is very close to what we want for toy-like characters.

- [Texture Painting for PS1 Characters — Beginner Friendly (Class Central listing)](https://www.classcentral.com/course/youtube-blender-tutorial-texture-painting-for-ps1-characters-beginner-friendly-488621)
- [PlayStation 1 Characters in Blender — Low-Poly 3D Modeling (Class Central listing)](https://www.classcentral.com/course/youtube-playstation-1-characters-in-blender-486471)
- [Grant Abbitt's YouTube Channel & Course Library](https://www.gabbitt.co.uk/courses)

#### Blender Texture Painting Complete Guide (generalistprogrammer.com)
A comprehensive written tutorial covering everything from basic texture paint mode to PBR workflows, UV unwrapping, and exporting textures for game engines.

- [Blender Texture Painting Tutorial: Complete Game Asset Texturing Guide 2025](https://generalistprogrammer.com/tutorials/blender-texture-painting-complete-game-asset-tutorial)

#### GameDev.tv — Blender 2D Sprites Course (Paid)
A full course covering shaders, cameras, and Grease Pencil to turn 3D models into game-ready 2D sprites, tilesets, and UI. Covers the complete pipeline from model to sprite sheet.

- [Blender 2D Sprites: Turn 3D Models into Pixel Art for Games (GameDev.tv)](https://gamedev.tv/courses/blender-sprites)

---

### Step 2: Rigging & Animation (Skeletons and Posing)

To create attack poses, hit reactions, and any movement beyond the static sculpt, you'll need to rig the model with an armature (skeleton).

#### CGDive — "Rigging Isn't Scary" (YouTube, Free)
The best free rigging course available. Beginner to intermediate in 3 levels, ~20 hours total. Starts with absolute basics (what is an armature, what is weight painting) and builds to full character rigs. Released completely free on YouTube.

- [Rigging Isn't Scary: Learn Rigging in Blender 2025 (CGDive)](https://cgdive.com/48-hours-early-access-learn-rigging-in-blender-2024-2025/comment-page-1/)
- [Blender Rigging: A Complete Learning Path (CGDive)](https://cgdive.com/cgdive-learn-rigging-in-blender-path/)

#### GameDev Academy — Intro to Rigging Models in Blender
A shorter written + video guide that covers the fundamentals quickly if you don't want to commit to a full course yet.

- [Beginner's Guide to Rigging in Blender (GameDev Academy)](https://gamedevacademy.org/blender-rigging-tutorial/)

#### Skillshare — How to Rig in Blender Step-by-Step
Another beginner-friendly walkthrough with step-by-step instructions.

- [How to Rig in Blender: A Step-by-Step Tutorial (Skillshare)](https://www.skillshare.com/en/blog/how-to-rig-in-blender-a-step-by-step-tutorial-skillshare-blog/)

---

### Step 3: Rendering Sprites from 3D Models

This is the core of the pipeline — setting up cameras, lighting, and rendering out multi-angle sprite sheets.

#### Gravity Ace Devlog — Creating 2D Sprites with Blender + Aseprite
An indie game dev who documents their exact workflow: model in Blender, animate, render frames, then clean up and assemble sprite sheets in Aseprite. Practical, real-world, proven in a shipped game.

- [Creating 2D Sprites with Blender + Aseprite (Gravity Ace devlog)](https://gravityace.com/devlog/3d-to-2d-with-blender/)

#### Gemserk Blog — Building 2D Sprites from 3D Models Using Blender
A classic tutorial that walks through the full process: model setup, camera configuration (64x64 viewport), keyframe animation for rotation, rendering with proper anti-aliasing (Catmull-Rom), and assembling into sprite sheets.

- [Building 2D Sprites from 3D Models Using Blender (Gemserk)](https://blog.gemserk.com/2011/07/20/building-2d-sprites-from-3d-models-using-blender/)

#### ArtStation — Creating 2D Pixel Art Style Isometric Sprites from a 3D Model
Step-by-step written tutorial with images. Covers orthographic camera setup, rendering at low resolution for pixel-art crunch, and post-processing.

- [Tutorial: Creating 2D Pixel Art Style Isometric Sprites from a 3D Model in Blender (ArtStation)](https://www.artstation.com/blogs/jsabbott/YQaAw/tutorial-creating-2d-pixel-art-style-isometric-sprites-from-a-3d-model-in-blender)

#### CoderNunk — How to Make a Sprite Sheet from a 3D Model Using Blender and ImageMagick
Covers the render-to-spritesheet pipeline end to end, including using ImageMagick to stitch rendered frames into a single sheet.

- [How to Make a Sprite Sheet from a 3D Model Using Blender and ImageMagick (CoderNunk)](https://codernunk.com/tutorials/sprite-sheet-from-3d/)

---

### Blender Addons That Automate the Pipeline

These tools can save significant time by automating multi-angle rendering, sprite sheet assembly, or both.

#### BlenderSpriteGenerator (Free, GitHub)
Renders 3D models from multiple angles into 2D/2.5D game sprites automatically. Click "Render Sprites" and it creates a sprite image for each angle in the output directory.

- [BlenderSpriteGenerator (GitHub)](https://github.com/RubielGames/BlenderSpriteGenerator)

#### Sprite 2D Add-on ($12, itch.io)
Generates 2D sprite animations from 3D animated models. Render the same animation from multiple angles (you define how many). Supports side-scroller, top-down, and isometric games. Can auto-generate sprite sheets.

- [Sprite 2D Blender Add-on (itch.io)](https://kameloov.itch.io/sprite-2d)
- [Sprite 2D review (BlenderNation)](https://www.blendernation.com/2021/06/27/generate-sprites-from-3d-models-with-the-sprite-2d-add-on/)

#### Blender Sprite Studio (Free, CC0)
A pre-configured Blender 3.6 project with cameras (orthogonal, isometric, top-down), lighting, and compositor node setup for clean sprite rendering. Removes default anti-aliasing on render edges. Drop your model in, render, done.

- [Blender Sprite Studio (itch.io)](https://croomfolk.itch.io/blender-2d-sprite-studio)

#### Get Sheet Done (Free)
Blender addon for rendering animations from multiple cameras and creating sprite sheets directly inside Blender.

- [Get Sheet Done — Create Sprite Sheet in Blender3D](https://kilbee.github.io/GetSheetDone/docs.html)

#### Game Sprite Creator (Free, GitHub)
Camera presets for top-down, isometric, side view. Automatic rendering of multiple objects including animations. Automatic sprite sheet creation.

- [Game Sprite Creator (GitHub)](https://github.com/johnferley/Game-Sprite-Creator)

#### Sprite Atlas Addon (Free, GitHub)
Automatically renders and tiles animations to a sprite sheet from a user-definable number of angles. Exports animation data to XML and JSON.

- [SpriteAtlasAddon (GitHub)](https://github.com/Mattline1/SpriteAtlasAddon)

---

### Recommended Learning Path

For a complete novice, follow this order:

1. **Watch Grant Abbitt's PS1 character tutorials** — Learn to texture paint a model. This is the fastest skill to pick up and gives immediate visual results.
2. **Download Blender Sprite Studio** (free) — Drop a textured model in and do a test render. See what a sprite looks like before committing to the full pipeline.
3. **Read the Gemserk blog post** — Understand the camera rig and keyframe rotation approach for multi-angle rendering.
4. **Try BlenderSpriteGenerator addon** (free) — Automate multi-angle rendering instead of doing it manually.
5. **Watch CGDive's "Rigging Isn't Scary" Level 1** — Only 5 lessons, gives you enough to pose characters for hit reactions and attack frames.
6. **Follow the full pipeline** from Phase 1 through Phase 7 of this proposal with one character end-to-end.
