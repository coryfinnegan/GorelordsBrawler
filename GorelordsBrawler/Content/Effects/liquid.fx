// Liquid metaball post-process for the acid hazard.
// Pipeline (driven by LiquidPostProcessor + LiquidFieldRenderer):
//
//   1. LiquidFieldRenderer renders every liquid particle as a soft-alpha disc
//      to its own RenderTexture, using BlendState.Additive. Overlapping
//      particles accumulate alpha into a "potential field" — pixels covered
//      by many close particles have high alpha; fringe pixels low alpha.
//
//   2. The scene's other renderers draw normally (TiledMap, players,
//      effects on HitboxRenderLayer, etc.) into the main scene render target.
//
//   3. This shader runs as a post-process: SpriteBatch.Draw(_scene, ..., effect)
//      feeds the scene as RenderTargetTexture; we sample the field RT via the
//      explicitly-bound FieldTexture parameter; smoothstep on the field alpha
//      to mask the liquid, lerp scene → liquid_color where masked.
//
// Cited: XorDev/2DFluids (GMS), MonoGame Example04 SimpleLightShader,
//        GameDev.net "Fluid Rendering with Box2D".

#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// ── Scene (bound implicitly by SpriteBatch.Draw) ──────────────────────────
Texture2D RenderTargetTexture;
sampler2D SceneSampler = sampler_state
{
    Texture   = <RenderTargetTexture>;
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

// ── Particle field (bound explicitly from C# every frame) ─────────────────
Texture2D FieldTexture;
sampler2D FieldSampler = sampler_state
{
    Texture   = <FieldTexture>;
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

// ── Tunables (set from C# every frame) ────────────────────────────────────
float  ThresholdMin;    // bottom of the soft edge; e.g. 0.40
float  ThresholdMax;    // top of the soft edge (fully solid above); e.g. 0.55
float4 LiquidColor;     // body color, e.g. acid green (0.18, 0.70, 0.16, 1)
float4 EdgeColor;       // brighter edge highlight, e.g. (0.55, 1.0, 0.4, 1)
float  EdgeBandWidth;   // half-width of the bright edge band; e.g. 0.04
// Pulse: a 0..1 value the C# side animates with sin(Time) to drive a
// surface-highlight breathing effect. 0 = full body brightness only, 1 =
// edge highlight maxed. We modulate the highlight's intensity (not its
// position) so the geometry stays stable while the surface "charges up"
// and "dims" rhythmically — reads as "alive / corrosive / dangerous".
float  Pulse;
float  PulseStrength;   // 0..1 — how much of the edge highlight is pulsed away at Pulse=0

float4 LiquidPS(float2 uv : TEXCOORD0) : COLOR
{
    float4 scene  = tex2D(SceneSampler, uv);
    float  fieldA = tex2D(FieldSampler, uv).a;

    // Body mask: 0 outside the liquid, 1 inside, soft band between.
    float bodyMask = smoothstep(ThresholdMin, ThresholdMax, fieldA);

    // Edge highlight: narrow ridge centred on the threshold midpoint.
    // Computed as (rise) - (fall) of two smoothsteps around the midpoint.
    float midPoint  = (ThresholdMin + ThresholdMax) * 0.5;
    float edgeStart = midPoint - EdgeBandWidth;
    float edgeEnd   = midPoint + EdgeBandWidth;
    float rise      = smoothstep(edgeStart, midPoint, fieldA);
    float fall      = smoothstep(midPoint,  edgeEnd,  fieldA);
    float edgeBand  = saturate(rise - fall);

    // Pulse modulates the edge highlight intensity between
    // (1 - PulseStrength) and 1.0. Body color is unaffected so the
    // shape stays still while the "charge" breathes.
    float edgeIntensity = lerp(1.0 - PulseStrength, 1.0, Pulse);
    edgeBand = edgeBand * edgeIntensity;

    float3 liquidRgb = lerp(LiquidColor.rgb, EdgeColor.rgb, edgeBand);

    // Composite over scene by the body mask, respecting LiquidColor.a as a
    // global opacity scale (lets us partially see-through the liquid if we
    // want, by lowering LiquidColor.a).
    float3 outRgb = lerp(scene.rgb, liquidRgb, bodyMask * LiquidColor.a);
    return float4(outRgb, 1.0);
}

technique Liquid
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL LiquidPS();
    }
};
