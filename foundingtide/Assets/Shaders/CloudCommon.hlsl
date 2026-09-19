// Shared cloud coverage maths (2026-09-08). Included by CloudPuff.shader (the
// visible puffs and the overcast sheet) and CloudCookie.shader (the shade they
// cast, rendered as the sun's light cookie), so what you see overhead and what
// darkens the ground are the SAME field evaluated at the same world XZ.
//
// Everything is evaluated in world metres so a puff's shape travels with its
// centre. _CloudTime is the game's own clock (CloudSystem sets it), not _Time,
// so a pause freezes the sky along with everything else.
#ifndef ISLAND_CLOUD_COMMON_INCLUDED
#define ISLAND_CLOUD_COMMON_INCLUDED

float  _CloudTime;
float2 _CloudDrift;       // accumulated wind, metres — the overcast sheet scrolls by this
float  _CloudOvercast;    // 0..1 blended overcast amount

float CloudHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float CloudValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = CloudHash(i);
    float b = CloudHash(i + float2(1.0, 0.0));
    float c = CloudHash(i + float2(0.0, 1.0));
    float d = CloudHash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Three octaves, normalised to 0..1.
float CloudFbm(float2 p)
{
    float v = 0.0;
    float amp = 0.5;
    for (int o = 0; o < 3; o++)
    {
        v += amp * CloudValueNoise(p);
        p = p * 2.03 + 17.1;
        amp *= 0.5;
    }
    return v / 0.875;
}

// One puff: a soft radial body broken up by noise so the core varies between
// dense and thin and the skirt dissolves into wisps. 0 outside the radius.
//   c = centre (world xz), r = radius (m), seed = per-cloud noise offset.
float CloudPuffDensity(float2 xz, float2 c, float r, float seed)
{
    float2 local = xz - c;
    float dist = length(local) / max(r, 0.01);
    if (dist >= 1.0) return 0.0;

    float radial = 1.0 - smoothstep(0.3, 1.0, dist);
    float n  = CloudFbm(local * 0.09 + seed + _CloudTime * 0.025);          // big lobes
    float n2 = CloudFbm(local * 0.24 - seed * 1.7 - _CloudTime * 0.015);    // fine breakup
    float body = radial * (0.35 + 0.95 * n) - 0.28 - 0.18 * (1.0 - n2);
    return saturate(body * 1.9);
}

// The overcast layer: a low-frequency field over the whole island, never fully
// closed so light still gets through in places even on a cloudy day.
float CloudOvercastDensity(float2 xz)
{
    if (_CloudOvercast <= 0.001) return 0.0;
    float2 p = xz + _CloudDrift;
    float n  = CloudFbm(p * 0.018 + _CloudTime * 0.01);
    float n2 = CloudFbm(p * 0.05 - _CloudTime * 0.006 + 31.7);
    float field = saturate(0.25 + 0.6 * n + 0.3 * (n2 - 0.5));
    return _CloudOvercast * field;
}

#endif
