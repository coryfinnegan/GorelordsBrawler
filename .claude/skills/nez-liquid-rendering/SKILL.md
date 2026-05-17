---
name: nez-liquid-rendering
description: Render N particles as a continuous-looking 2D liquid (water, acid, blood, slime, lava) using the splat-and-threshold metaball technique. Use whenever you have many small particles whose individual circles should NOT be visible — instead they should merge into a smooth, organic, fluid-looking body with a clean edge. Specifically applies to acid pools, water surfaces, blood puddles, oil slicks, slime, lava, "anything that flows". Triggers on "make the liquid look smooth", "metaball rendering", "fluid rendering", "particles look chunky", "make particles merge into a blob", "soft fluid edges".
allowed-tools: Bash, PowerShell, Read, Write, Edit, Grep
---

# Render particles as liquid (metaball splat-and-threshold)

## When to use this

You have N moving particles (typically 100–10,000) and rendering each as its own visible circle looks "chunky" or "like a bunch of dots". You want them to look like ONE flowing body of liquid with smooth organic edges.

Every working implementation of this in production 2D games (Where's My Water, Worms, Hollow Knight's acid pools, GMS XorDev fluids, the Box2D LiquidFun demos) uses the same pipeline. Don't reinvent — use this:

## The pipeline (three passes, two render targets)

```
Pass 1 — FIELD PASS
  Render each particle as a soft-alpha circle into a dedicated RenderTarget
  with BlendState.Additive.
  → Overlapping particles' alphas sum up. The RT now holds a "potential
    field": pixels covered by many close particles have high alpha; pixels
    just on the fringe have low alpha; everywhere else is 0.

Pass 2 — SCENE PASS
  Render the rest of the world (tilemap, characters, etc.) into the main
  scene RenderTarget normally with BlendState.AlphaBlend.

Pass 3 — THRESHOLD POST-PROCESS
  Fullscreen quad with a shader that samples BOTH the field RT and the
  scene RT. For each pixel:
    field_alpha = sample(FieldRT, uv).a
    liquid_mask = smoothstep(threshold - softness, threshold + softness, field_alpha)
    output      = lerp(scene, liquid_color, liquid_mask)
  → Anywhere the additive field is above the threshold becomes solid
    liquid colour. The smoothstep gives a soft edge of ~2–6 px width.
```

That's it. The pool body is one continuous shape. Edges are smooth. Individual particles vanish into the body.

## Cited references (read these if you change anything fundamental)

- **MonoGame Example04 "Simple 2D Lighting"** — `github.com/manbeardgames/monogame-hlsl-examples`. Identical render-target ping-pong as our Pass 1 + 3, just for lighting masks instead of liquid. The cleanest XNA/MonoGame implementation of the pattern.
- **GameDev.net "Fluid Rendering with Box2D"** — explains the *potential field* concept and why each particle must be drawn LARGER than its physics radius for the field to overlap.
- **XorDev/2DFluids** — `github.com/XorDev/2DFluids`. Production GLSL shader. Uses `smoothstep(0.7, 0.8, pow(field.a, 2.0))` for the threshold; we can port directly.
- **Daniel Ilett "2D Metaballs in URP"** — Unity tutorial. Shows the alternate per-pixel-over-N-metaballs technique (computes distance to every metaball in the shader). Don't use this for >100 particles — it's O(pixels × N). Splat-and-threshold is O(pixels + N).
- **John Wigg "A simple method for creating 2D Metaballs"** — `john-wigg.dev/2DMetaballs`. Uses a color-ramp gradient texture indexed by field strength — neat trick for stylized "depth tint" (deep liquid darker than shallow).

## Why the alternatives don't work for our case

| Approach | Why it fails for 4000+ particles |
|---|---|
| One soft-disc sprite per particle (alpha blend) | Each disc visible as a circle, "chunky" look |
| Per-particle PrimitiveBatch quads | Same as above, just sharper edges |
| Per-column polygon fill from particle data | Stair-steps; can't distinguish stream from pool reliably |
| Per-pixel-over-N-metaballs shader (Ilett style) | O(pixels × N) — 4000 metaballs blows out fragment shader |
| Marching squares | Too expensive at high particle counts in realtime |
| Reflection shaders (Gabriel Di Giorgio MonoGame water) | Designed for static water bodies, not particle fluids |

The splat-and-threshold technique is the only one that scales to thousands of particles AND looks smooth.

## Implementation in Nez

Nez gives us all the building blocks:

- **Custom `Renderer`** — subclass `Nez.Renderer` and override `Render(Scene scene)`. You can call `BeginRender(cam)` with your own RenderTexture target. Pick renderables by RenderLayer.
- **`PostProcessor`** — subclass `Nez.PostProcessor`. The `Process(RenderTarget2D source, RenderTarget2D destination)` method gets the scene fully rendered as `source` and you write the composited result into `destination`.
- **`RenderTexture`** — Nez wrapper around `RenderTarget2D`. Create in `OnSceneBackBufferSizeChanged` so resolution-changes are handled automatically.

### Recommended layout

```
LiquidFieldRenderer  : Renderer        — its own RenderTexture; draws only renderables on a dedicated FluidRenderLayer with Additive blend.
LiquidParticleSprite : RenderableComponent  — one component per particle simulation; draws each particle as a soft-disc sprite at PHYSICS-RADIUS × ~3.
LiquidPostProcessor  : PostProcessor   — samples both the scene RT and the field RT; shader does threshold + composite.
liquid.fx            : HLSL effect file — fullscreen PS that does the threshold.
```

The fluid simulation (`FluidSimulation`) stays unchanged — physics already works. Only the rendering pipeline changes.

## HLSL shader template (port of XorDev's GLSL, adapted)

```hlsl
#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// The Batcher-supplied texture (scene rendered without liquid) is bound
// to RenderTargetTexture by SpriteBatch.Draw().
Texture2D RenderTargetTexture;
sampler2D SceneSampler = sampler_state { Texture = <RenderTargetTexture>; };

// We bind our additive-blended particle field RT to this slot from C# via
//   effect.Parameters["FieldTexture"].SetValue(_fieldRT);
Texture2D FieldTexture;
sampler2D FieldSampler = sampler_state
{
    Texture   = <FieldTexture>;
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

// Threshold knobs.  Set from C# every frame so we can tune live.
float   ThresholdMin;     // e.g. 0.45 — start of the soft edge
float   ThresholdMax;     // e.g. 0.55 — end of the soft edge (fully solid above)
float4  LiquidColor;      // body colour (e.g. acid green)
float4  EdgeColor;        // brighter edge highlight colour
float   EdgeBandStart;    // e.g. 0.45 — narrow band for the edge highlight
float   EdgeBandEnd;      // e.g. 0.52

float4 LiquidPS(float2 uv : TEXCOORD0) : COLOR
{
    float4 scene = tex2D(SceneSampler, uv);
    float  fieldA = tex2D(FieldSampler, uv).a;

    // Mask = 0 outside the liquid, 1 inside, soft transition through the band.
    float mask = smoothstep(ThresholdMin, ThresholdMax, fieldA);

    // Bright edge band — narrow smoothstep PEAK centred on the threshold.
    float edge = smoothstep(EdgeBandStart, (EdgeBandStart + EdgeBandEnd) * 0.5, fieldA)
               - smoothstep((EdgeBandStart + EdgeBandEnd) * 0.5, EdgeBandEnd, fieldA);

    float3 liquid = lerp(LiquidColor.rgb, EdgeColor.rgb, edge);

    // Composite over scene by mask.
    return float4(lerp(scene.rgb, liquid, mask * LiquidColor.a), 1.0);
}

technique Liquid
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL LiquidPS();
    }
};
```

## C# render pipeline template

```csharp
// 1. The custom Renderer — owns a RenderTexture, draws only liquid particles
//    on a dedicated layer with Additive blend.
public class LiquidFieldRenderer : Renderer
{
    public RenderTexture FieldTexture { get; private set; }
    private readonly int _liquidLayer;

    public LiquidFieldRenderer(int renderOrder, int liquidLayer) : base(renderOrder, null)
    {
        _liquidLayer    = liquidLayer;
        WantsToRenderToSceneRenderTarget = false;
    }

    public override void OnAddedToScene(Scene scene)
    {
        base.OnAddedToScene(scene);
        FieldTexture = new RenderTexture();
    }

    public override void OnSceneBackBufferSizeChanged(int newWidth, int newHeight)
    {
        // Half-resolution is plenty for the field — the threshold gives
        // smooth edges regardless of source resolution.
        FieldTexture.OnSceneBackBufferSizeChanged(newWidth / 2, newHeight / 2);
    }

    public override void Render(Scene scene)
    {
        var cam = Camera ?? scene.Camera;
        Core.GraphicsDevice.SetRenderTarget(FieldTexture);
        Core.GraphicsDevice.Clear(Color.Transparent);

        Graphics.Instance.Batcher.Begin(BlendState.Additive, cam.TransformMatrix);
        var renderables = scene.RenderableComponents.ComponentsWithRenderLayer(_liquidLayer);
        for (int j = 0; j < renderables.Length; j++)
        {
            var r = renderables.Buffer[j];
            if (r.Enabled && r.IsVisibleFromCamera(cam))
                r.Render(Graphics.Instance.Batcher, cam);
        }
        Graphics.Instance.Batcher.End();
    }
}

// 2. The PostProcessor — samples both the scene + the field, runs the shader.
public class LiquidPostProcessor : PostProcessor
{
    private readonly LiquidFieldRenderer _fieldRenderer;
    public LiquidPostProcessor(int order, Effect liquidEffect, LiquidFieldRenderer fieldRenderer)
        : base(order, liquidEffect) { _fieldRenderer = fieldRenderer; }

    public override void Process(RenderTarget2D source, RenderTarget2D destination)
    {
        Effect.Parameters["FieldTexture"].SetValue(_fieldRenderer.FieldTexture.RenderTarget);
        // ... set other knobs (Threshold, LiquidColor, etc.) here every frame ...
        DrawFullscreenQuad(source, destination, Effect);
    }
}

// 3. The per-particle "splat" RenderableComponent. RenderLayer = the liquid
//    layer (so the LiquidFieldRenderer picks it up exclusively).  Draws each
//    particle as a soft-disc sprite at ~3× physics radius using Batcher.Draw —
//    Batcher inherits the LiquidFieldRenderer's BlendState.Additive.

// 4. Scene setup:
var liquidRenderer = new LiquidFieldRenderer(renderOrder: -10, liquidLayer: 99);
scene.AddRenderer(liquidRenderer);
scene.AddPostProcessor(new LiquidPostProcessor(0, liquidEffect, liquidRenderer));
```

## Tuning the look

| Knob | Effect | Sensible default |
|---|---|---|
| Per-particle soft-disc radius | Smaller = thinner liquid; larger = fatter blobs | 3× physics radius |
| Disc alpha-falloff curve | Sharp center vs soft edge | quadratic: `pow(1-d², 1.5)` |
| `ThresholdMin` / `ThresholdMax` | Lower = MORE coverage (looser blobs); narrower band = harder edge | `(0.4, 0.5)` |
| Field RT resolution | Half-res is invisible at game scale; quarter-res starts to lose edge | 0.5× backbuffer |
| `EdgeColor` brightness | Surface highlight ("sheen") intensity | Body color + ~80 per channel |

## Where this technique breaks down

- **Very sparse particles**: if only 1–2 particles cover an area, the field never crosses threshold → nothing renders there. Symptom: holes in the pool body. Fix: bigger per-particle splat radius OR lower threshold.
- **Mixing colors per particle**: each particle contributes one tint to the additive RT. With tints mixing additively you don't get faithful per-particle colors. Fix: bake everything to one tint OR use multiple field RTs (one per color).
- **Edge-of-screen clipping**: the field RT is screen-space. Particles entirely off-screen don't contribute, so the body edge at screen boundary will look truncated.
- **Build pipeline**: HLSL `.fx` files need MonoGame Content Builder (`.mgcb`) to compile to `.xnb`. If the project doesn't already build content (warns "No Content References Found"), enabling that is non-trivial. Workaround: precompile the `.fx` once via `mgfxc` and ship the `.xnb` as raw content.

## Compiling the shader for MonoGame DesktopGL

```bash
# One-time precompile, then ship the .mgfxo in Content/
mgfxc liquid.fx liquid.mgfxo /Profile:OpenGL
# Load at runtime:
var bytes = File.ReadAllBytes("Content/liquid.mgfxo");
var effect = new Effect(graphicsDevice, bytes);
```

Or wire `liquid.fx` into the `.mgcb` content pipeline and `Content.Load<Effect>("liquid")`.

## Verify it works

If after wiring it up the liquid looks WORSE than per-particle discs, the most common causes (in order of likelihood):

1. **You're rendering the splat sprites with `AlphaBlend` instead of `Additive`** — the field never builds up; check the Renderer's `Batcher.Begin(BlendState.Additive, ...)` call.
2. **The threshold is too high** for the splat radius — lower `ThresholdMin/Max`. You can spot this: the body of the pool is empty even though particles are clearly there. (Visualize the field RT directly first — comment out the shader and `Batcher.Draw(_fieldRT, ...)` it to see what the field looks like.)
3. **The field RT isn't being CLEARED to transparent each frame** — Pass 1 must `Clear(Color.Transparent)` (NOT Color.Black) or alpha accumulates forever.
4. **Player sprites disappear** after wiring up the post-process — the PostProcessor's `Process` must `DrawFullscreenQuad(source, ...)`, NOT skip source. Otherwise the scene is dropped.
5. **Shader didn't compile** — MonoGame silently substitutes a default if the .fx fails to load. Confirm `effect != null` after Content.Load.

## Smoke-test workflow

ALWAYS run the smoke-test skill after any rendering change and inspect the captured frame BEFORE pushing. That's exactly how the previous PRs broke unnoticed:

```bash
pwsh .claude/skills/smoke-test/smoke_test.ps1 -Feature acid
# Then read .smoke-test-screenshot.acid.png — does it look like fluid?
```

The smoke-test acid feature also includes a player-render regression check that catches the class of bug where adding renderables on the wrong layer disables player sprites.
