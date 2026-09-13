// Fog of war shading (2026-09-09). Included by every surface that draws the island:
// OccluderCutout.hlsl (terrain chunks, scatter decor, nodes, buildings, walls, pickups)
// and StylizedWater.shader. The mask is a GLOBAL texture written by FogOfWar.cs, for
// the same reason the unit hole mask is: a material property would shadow it and every
// instance would need the texture pushed by hand.
//
// Red is "ever explored", green is "in someone's sight right now", both smoothed on the
// CPU so a reveal grows rather than pops. Unexplored ground is one flat fog colour; explored
// ground nobody is looking at sits in a dim, desaturated shroud; seen ground is drawn
// as-is. _FogParams.w is 0 wherever no FogOfWar exists (the main menu, the showcase
// scene, a freshly unloaded game), and an unset float global reads 0, so those draw
// unfogged with no setup.
#ifndef ISLAND_FOG_OF_WAR_INCLUDED
#define ISLAND_FOG_OF_WAR_INCLUDED

TEXTURE2D(_FogMask);
SAMPLER(sampler_FogMask);
float4 _FogParams;   // x: uv per world metre, y: uv offset (uv = worldXZ * x + y, on texel CENTRES), z: unused, w: 1 = fog active
float4 _FogLook;     // x: unused, y: shroud brightness, z: shroud desaturation, w: unused
float4 _FogColor;    // rgb: unexplored colour, LINEAR, independent of the lit colour

// explored (r) and visible (g), each 0..1, at a world position.
half2 SampleFogOfWar(float3 positionWS)
{
    float2 uv = positionWS.xz * _FogParams.x + _FogParams.y;
    half2 fog = SAMPLE_TEXTURE2D(_FogMask, sampler_FogMask, saturate(uv)).rg;
    // The water plane runs far past the island. Clamp sampling would stretch the mask's
    // last row/column over that whole ocean (the same edge-texel smear the water depth
    // map had), so a reveal touching the map edge painted a band to the horizon. Beyond
    // the map is never explored; FogOfWar.cs also fades the mask out over its outer
    // cells so the ramp to black lands inside the map rather than as a seam on its edge.
    fog *= (all(uv >= 0.0) && all(uv <= 1.0)) ? (half)1 : (half)0;
    // Bilinear across 2 m cells is a wide ramp; tighten it so the edge reads as an edge
    // while the CPU-side smoothing still animates it.
    return smoothstep(half2(0.12, 0.12), half2(0.88, 0.88), fog);
}

half3 ApplyFogOfWar(half3 rgb, float3 positionWS)
{
    if (_FogParams.w < 0.5) return rgb;
    half2 fog = SampleFogOfWar(positionWS);

    half lum = dot(rgb, half3(0.299, 0.587, 0.114));
    half3 shroud = lerp(rgb, lum.xxx, _FogLook.z) * _FogLook.y;
    // Unexplored REPLACES the lit colour rather than scaling it (2026-09-12): a multiply
    // left 4% of noon sunlight on the ground, and the sun/shade contrast of every slope
    // read as the island's shape through the fog, with the hidden trees missing on top.
    half3 dark = _FogColor.rgb;

    half3 c = lerp(dark, shroud, fog.r);
    return lerp(c, rgb, fog.g);
}

#endif
