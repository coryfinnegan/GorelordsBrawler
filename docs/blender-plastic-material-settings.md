# Blender Plastic Toy Material Settings

Settings for creating a molded plastic / vinyl toy look using Blender's Principled BSDF shader. Aimed at replicating the look of M.U.S.C.L.E.-style soft vinyl figures and Gorelords toys.

---

## Quick Settings (Flat Plastic Toy)

In the **Shading** workspace, select your model, click **New** material, and set these on the **Principled BSDF** node:

| Setting | Value | Why |
|---|---|---|
| **Base Color** | Your character's color (saturated works best) | The "plastic" color |
| **Roughness** | **0.35 - 0.45** | Matte vinyl feel. Lower (0.1-0.2) = shiny action figure. Higher (0.4-0.5) = M.U.S.C.L.E. soft rubber |
| **Specular / IOR** | IOR **1.46** (plastic's real IOR) | Controls how reflective the surface is |
| **Subsurface Weight** | **0.05 - 0.1** | Very subtle — gives the faint waxy translucency of real vinyl. Set to 0 if you want hard plastic. |
| **Subsurface Radius** | R: 1.0, G: 0.2, B: 0.1 | Warm light scatter like real plastic |
| **Clearcoat** (or Coat in Blender 4) | **0.3 - 0.5** | Adds a faint glossy layer on top of the matte surface, like the sheen on a vinyl toy |
| **Coat Roughness** | **0.15** | Makes the clearcoat layer slightly soft, not mirror-sharp |

Everything else stays at default (Metallic = 0, Transmission = 0, Emission = 0).

---

## Adding Molded Surface Texture (Optional)

To get the subtle bumpy/grainy surface that injection-molded plastic has:

1. Press **Shift+A** in the shader editor, add a **Noise Texture** node
2. Set **Scale: 200-400** (very fine grain), **Detail: 4**, **Roughness: 0.5**
3. Add a **Bump** node (Shift+A > Vector > Bump)
4. Connect Noise Texture **Fac** output → Bump **Height** input
5. Set Bump **Strength: 0.02 - 0.05** (very subtle — you barely want to see it)
6. Connect Bump **Normal** output → Principled BSDF **Normal** input

This gives the faint surface grain you see on real molded figures.

---

## Adding Roughness Variation (Optional)

Real toys have slightly uneven shininess — mold seams are shinier, recessed areas are matte:

1. Add another **Noise Texture** (Scale: **50-80**, larger than the bump texture)
2. Add a **Color Ramp** node between the noise and the Roughness input
3. Set the Color Ramp to go from **0.3** (dark slider) to **0.5** (light slider)
4. Connect Color Ramp output → Principled BSDF **Roughness** input

This replaces the flat roughness value with a slightly varied one.

---

## Node Graph Summary

```
[Noise Texture (Scale: 200-400)] → Fac → [Bump (Strength: 0.02-0.05)] → Normal → [Principled BSDF] → Normal

[Noise Texture (Scale: 50-80)] → Fac → [Color Ramp (0.3 to 0.5)] → Color → [Principled BSDF] → Roughness

[Principled BSDF] → BSDF → [Material Output] → Surface
```

---

## Cheat Sheet: Plastic Types

| Look | Roughness | Subsurface | Coat |
|---|---|---|---|
| M.U.S.C.L.E. (matte rubber) | 0.45 | 0.1 | 0 |
| Vinyl art toy (satin finish) | 0.35 | 0.05 | 0.3 |
| Shiny action figure (glossy) | 0.15 | 0 | 0.5 |
| Soft vinyl (Japanese sofubi) | 0.4 | 0.15 | 0.2 |

For Gorelords, the **vinyl art toy** or **M.U.S.C.L.E.** row is probably what you want.

---

## Sources

- [How to Create Plastic Toy Look-Alikes in Blender (Morphic Studio)](https://www.themorphicstudio.com/how-to-create-plastic-toy-look-alikes-in-blender/)
- [Creating an Advanced Plastic Shader (BlenderNation)](https://www.blendernation.com/2020/02/01/creating-an-advanced-plastic-shader/)
- [Principled BSDF Manual (Blender Docs)](https://docs.blender.org/manual/en/latest/render/shader_nodes/shader/principled.html)
