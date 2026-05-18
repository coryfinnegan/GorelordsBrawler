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

// ── Player silhouette mask (bound explicitly from C# every frame) ─────────
// PlayerMaskRenderer renders every active player's current sprite frame
// into its own RT with Color.White tint at full scene resolution. The RT's
// alpha channel is a pixel-perfect silhouette of all players currently on
// screen — sample it and use the alpha as the per-pixel "is player here?"
// signal for reducing bodyMask and applying the underwater tint.
//
// POINT filtering: no interpolation, so the mask edge stays as crisp as
// the source pixel art (otherwise smoothstep would soften a fringe of
// half-mask pixels around every sprite edge).
Texture2D PlayerMaskTexture;
sampler2D PlayerMaskSampler = sampler_state
{
    Texture   = <PlayerMaskTexture>;
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MinFilter = POINT;
    MagFilter = POINT;
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

// ── Player presence tunables ──────────────────────────────────────────────
// PlayerMaskTexture (above) sourced from PlayerMaskRenderer is the pixel-
// perfect silhouette. We just read its alpha and apply an underwater
// color filter to the scene where the player is.
float  PlayerMaskStrength;        // 0..1, how much to reduce bodyMask where players are
float  PlayerDesaturation;        // 0..1, fraction of the way toward luminance-grey in player regions
float  PlayerCast;                // 0..1, strength of multiplicative green color tint in player regions
float  PlayerDarken;              // 0..1 (used as floor), 1=unchanged, lower=more darkening from light absorption

float4 LiquidPS(float2 uv : TEXCOORD0) : COLOR
{
    float4 scene  = tex2D(SceneSampler, uv);
    float  fieldA = tex2D(FieldSampler, uv).a;

    // Body mask: 0 outside the liquid, 1 inside, soft band between.
    float bodyMask = smoothstep(ThresholdMin, ThresholdMax, fieldA);

    // ── Player presence mask (Phase 3) ────────────────────────────────────
    // Pixel-perfect silhouette from PlayerMaskRenderer's RT. The mask RT's
    // alpha channel is non-zero exactly where a player's sprite is drawn,
    // including animation-frame shape (weapon swing, jump pose, etc.).
    float playerMask = tex2D(PlayerMaskSampler, uv).a;

    // Reduce bodyMask where playerMask > 0 → acid becomes partially
    // transparent over the player → scene (player sprite) shows through.
    float bodyMaskAfterPlayer = bodyMask * lerp(1.0, 1.0 - PlayerMaskStrength, playerMask);

    // UNDERWATER color filter applied to the scene where the player is
    // SUBMERGED — gated by `playerMask * bodyMask` so dry players (no acid
    // above them) are not tinted green. Three composable steps modelled on
    // real underwater photography:
    //   1. Desaturate toward luminance grey (water bleeds colors uniform)
    //   2. Multiplicative green CAST (acid's hue dyes everything green)
    //   3. Darken (light is absorbed with depth)
    float submerged = playerMask * bodyMask;
    float lum = dot(scene.rgb, float3(0.299, 0.587, 0.114));
    float3 desat = lerp(scene.rgb, float3(lum, lum, lum), submerged * PlayerDesaturation);
    // Cast: scale toward a green-dominant multiplier (R↓, G↑ slightly, B↓).
    // The 1.10 on green is gentle "lift" so the tint doesn't merely darken;
    // pixels that survive the desat keep some pop.
    float3 castMul = lerp(float3(1.0, 1.0, 1.0), float3(0.35, 1.10, 0.55), submerged * PlayerCast);
    desat *= castMul;
    // Darken: multiplicative brightness floor — closer to PlayerDarken the
    // deeper the perceived "absorption." Only inside the submerged region.
    desat *= lerp(1.0, PlayerDarken, submerged);
    scene.rgb = desat;

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
    // want, by lowering LiquidColor.a). bodyMaskAfterPlayer is the
    // player-region-reduced mask so submerged players show through.
    float3 outRgb = lerp(scene.rgb, liquidRgb, bodyMaskAfterPlayer * LiquidColor.a);
    return float4(outRgb, 1.0);
}

technique Liquid
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL LiquidPS();
    }
};
