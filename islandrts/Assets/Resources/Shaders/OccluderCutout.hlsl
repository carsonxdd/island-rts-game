// The cutout half of OccluderCutout.shader (2026-09-08). Included by its forward pass
// AFTER LitForwardPass.hlsl, so it wraps the stock URP Lit fragment: read the unit hole
// mask at this pixel, and if this fragment is nearer the camera than the unit under it,
// clip it away. Everything else - lighting, shadows received, the light cookie, fog,
// emission (the hover glow writes _EmissionColor on these same materials) - is the
// unmodified Lit code.
#ifndef ISLAND_OCCLUDER_CUTOUT_INCLUDED
#define ISLAND_OCCLUDER_CUTOUT_INCLUDED

// Global, set by UnitHoleMask.cs. NOT a material property on purpose: a material
// property would shadow the global and every instance would need the texture pushed
// to it by hand.
TEXTURE2D(_UnitHoleMask);
SAMPLER(sampler_UnitHoleMask);
float _UnitHoleDepthMargin;   // how far in FRONT of the unit a fragment must be before it cuts
float _UnitHoleDepthSoft;     // depth band over which the cut ramps in, so a trunk level with a unit does not flicker

// Ordered 4x4 Bayer threshold for the pixel: the soft rim of the hole is screen-door
// dithered rather than alpha blended, which is what keeps the material opaque
// (ZWrite on, shadow-casting, sorted with everything else).
float OccluderDither(float2 pixel)
{
    const float4x4 bayer = float4x4(
         0.0,  8.0,  2.0, 10.0,
        12.0,  4.0, 14.0,  6.0,
         3.0, 11.0,  1.0,  9.0,
        15.0,  7.0, 13.0,  5.0);
    uint2 p = uint2(pixel) & 3;
    return (bayer[p.y][p.x] + 0.5) / 16.0;
}

// View depth of this fragment from its rasterised depth, for either projection. The
// game camera is orthographic (and has a NEGATIVE near clip, which this handles: the
// mapping is linear in near..far), but the showcase scene is not.
float OccluderEyeDepth(float4 positionCS)
{
    return unity_OrthoParams.w == 0
        ? LinearEyeDepth(positionCS.z, _ZBufferParams)
        : LinearDepthToEyeDepth(positionCS.z);
}

void ApplyOccluderCutout(float4 positionCS)
{
    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
    half2 hole = SAMPLE_TEXTURE2D(_UnitHoleMask, sampler_UnitHoleMask, uv).rg;
    if (hole.r <= 0.001) return;

    float eye = OccluderEyeDepth(positionCS);
    float inFront = (hole.g - eye) - _UnitHoleDepthMargin;      // > 0: this surface is between the camera and the unit
    float cut = hole.r * saturate(inFront / max(_UnitHoleDepthSoft, 1e-3));
    clip(OccluderDither(positionCS.xy) - cut);
}

// Fog of war (2026-09-09): the same fragment darkens whatever the colony has not seen.
// The terrain chunks and the scatter decor are on this shader too, with _UNIT_CUTOUT
// off (FogMaterials), so the ground reads the fog without cutting holes.
#include "FogOfWar.hlsl"

void OccluderCutoutFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
#if defined(_UNIT_CUTOUT)
    ApplyOccluderCutout(input.positionCS);
#endif
    LitPassFragment(input, outColor
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
    outColor.rgb = ApplyFogOfWar(outColor.rgb, input.positionWS);
}

#endif
