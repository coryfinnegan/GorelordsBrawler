// Damage feedback post-process for the acid hazard (Phase 4).
//
// Combines two screen-space effects in one fullscreen pass:
//
//   1. Chromatic aberration: per-channel RGB split along a radial
//      direction from the screen centre. CaStrength is driven from
//      C# — spiked on each acid damage tick, decays exponentially
//      between ticks. Radial (not cardinal) so it reads as a lens
//      artefact, not a glitch effect.
//
//   2. Radial vignette: smoothstep darken from the screen edges,
//      coloured by VignetteColor (CPU-side lerp from "calm" black
//      at the engage threshold to "panicked" dark red at 0% HP).
//      Intensity is driven by the worst-alive-player HP%.
//
// Both effects are driven by DamageFeedbackController — this shader
// is pure presentation. Runs at PostProcessor order 10, AFTER the
// LiquidPostProcessor (order 0), so the vignette + CA apply to the
// final composited image including the acid liquid.
//
// Cited: Lettier "3D Game Shaders for Beginners: Chromatic Aberration"
//        (https://lettier.github.io/3d-game-shaders-for-beginners/chromatic-aberration.html),
//        Microsoft HLSL smoothstep docs, GameDev.net "HLSL Vignetting"
//        thread.

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

// ── Tunables (set from C# every frame) ────────────────────────────────────
// CA pulse magnitude in normalised UV. 0 = no offset. Small values
// (~0.004) read as a subtle hit feedback; larger values read as a
// glitch effect. Per Alisavakis / Lettier, keep this very small for
// fast-paced 2D games.
float  CaStrength;

// Vignette: radial distance band that darkens via smoothstep.
// dist is computed as length((uv - 0.5) * 2), so range is 0 at the
// centre and ~sqrt(2) at the corners. Radius is where the darken
// FULLY engages; Softness is the band width below Radius where the
// transition happens.
float  VignetteRadius;
float  VignetteSoftness;

// Vignette intensity: how strongly the darken applies (0 = off, 1 =
// fully replaced by VignetteColor). Controller derives this each
// frame from the worst-alive-player HP% — driven up as HP drops.
float  VignetteIntensity;

// Vignette colour: target colour the scene blends toward in the
// vignette band. Controller lerps this CPU-side from black (calm
// "tunnel of vision" at the engage threshold) to dark red (panic at
// 0% HP). Keeping the lerp on the C# side keeps this shader generic.
float4 VignetteColor;

float4 DamageFeedbackPS(float2 uv : TEXCOORD0) : COLOR
{
    // Radial direction from screen centre. Range: -1..1 on each axis,
    // length 0 at centre to ~sqrt(2) at corners. Used by BOTH the CA
    // offset (so RGB split radiates outward) and the vignette mask.
    float2 dir = (uv - 0.5) * 2.0;

    // ── Chromatic aberration ──────────────────────────────────────────
    // Sample R outward, G at centre, B inward — classic three-channel
    // split. Magnitude scaled by CaStrength so a single uniform can be
    // pulsed and decayed from C#.
    float2 offset = dir * CaStrength;
    float r = tex2D(SceneSampler, uv + offset).r;
    float g = tex2D(SceneSampler, uv).g;
    float b = tex2D(SceneSampler, uv - offset).b;
    float3 rgb = float3(r, g, b);

    // ── Vignette ──────────────────────────────────────────────────────
    // smoothstep band from (Radius - Softness) to Radius. Outside band
    // (toward corners) = 1, inside band (toward centre) = 0. Multiply
    // by VignetteIntensity to scale the whole effect 0..1.
    float dist = length(dir);
    float mask = smoothstep(VignetteRadius - VignetteSoftness, VignetteRadius, dist);
    rgb = lerp(rgb, VignetteColor.rgb, mask * VignetteIntensity);

    return float4(rgb, 1.0);
}

technique DamageFeedback
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL DamageFeedbackPS();
    }
};
