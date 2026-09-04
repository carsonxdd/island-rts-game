// Stylized low-poly water for the island (Phase 10 Stage 3, kept simple).
//
//  - depth-based colour: turquoise shallows fading to deep blue, read from a
//    depth map TerrainGrid bakes out of the island's own heightfield
//  - shoreline foam: a noise-broken band where the water is thin
//  - gentle wave displacement in the vertex shader (the water mesh is a real
//    grid, built by TerrainGrid, so there are vertices to move)
//  - flat-shaded facets: the normal is rebuilt per pixel from screen-space
//    derivatives, which gives the low-poly look without duplicating verts
//  - per-facet tone: every grid triangle gets its own slowly drifting
//    lightness jitter, and the brightest few become sun glints. This is what
//    makes the water read as low-poly — it does NOT depend on the lighting
//    geometry, which matters because of the camera (below)
//  - lit by the main light (so the day/night cycle tints it) with a SOFT
//    specular sheen
//
// Depth comes from the heightfield, NOT the camera depth texture (2026-09-03):
//  1. The depth texture holds whatever is behind the water ALONG THE VIEW RAY,
//     which made the sea's colour depend on camera tilt, and held nothing at
//     all past the edge of the terrain, where the seabed geometry simply stops.
//     The ocean was therefore cut by a hard straight line along the map border,
//     lighter over the seabed and flat deep blue beyond it. The island's own
//     heights are the real answer and are already in memory: TerrainGrid bakes
//     them into _HeightMap (one texel per terrain vertex, water column scaled
//     into 0..1 by _MapParams.z) and the shader reads the exact depth under
//     every pixel. Sampling past the map clamps to the border texel, which is
//     open-ocean depth, so there is no boundary left to see. _DepthDistance and
//     _FoamDistance are real vertical metres.
//
// Orthographic camera notes:
//  2. The view direction is the same for every pixel, so a specular highlight
//     has no spot to form: on a flat plane it is either on EVERYWHERE or off
//     everywhere, depending only on camera tilt and heading. A hard-stepped
//     highlight therefore flips the whole ocean light at some angles, and any
//     facet the wave tilts out of the step renders as a dark tile (2026-09-01).
//     The sheen here is a low-power, low-strength pow() with no cutoff, so the
//     worst case is a mild, smooth brightening — sparkle comes from the
//     per-facet glints instead, which are angle-independent.
//  3. Wavelengths must stay ≥ ~6× the water grid step (TerrainGrid builds the
//     grid at _GridStep). Shorter waves alias against the grid into slow
//     diagonal beat stripes across the whole sea.
Shader "Island RTS/Stylized Water"
{
    Properties
    {
        _HeightMap ("Water depth map (set by TerrainGrid)", 2D) = "white" {}
        _MapParams ("Depth map mapping (set by TerrainGrid)", Vector) = (1, 0, 4, 0)
        _ShallowColor ("Shallow colour", Color) = (0.24, 0.78, 0.80, 0.55)
        _DeepColor ("Deep colour", Color) = (0.04, 0.27, 0.56, 1.0)
        _DepthDistance ("Depth fade distance (m of water column)", Float) = 1.3
        _FoamColor ("Foam colour", Color) = (0.94, 0.98, 1.0, 1.0)
        _FoamDistance ("Foam distance (m of water column)", Float) = 0.4
        _FoamScale ("Foam noise scale", Float) = 0.45
        _FoamSpeed ("Foam speed", Float) = 0.35
        _WaveAmplitude ("Wave amplitude", Float) = 0.09
        _WaveLength ("Wave length (m, longest ~1.25x)", Float) = 16.0
        _WaveSpeed ("Wave speed", Float) = 0.6
        _GridStep ("Water grid step (m, set by TerrainGrid)", Float) = 1.5
        _FacetTint ("Facet tone jitter", Range(0, 0.3)) = 0.06
        _FacetSpeed ("Facet drift speed", Float) = 0.25
        _GlintStrength ("Glint strength", Range(0, 2)) = 0.45
        _GlintThreshold ("Glint threshold", Range(0.8, 1)) = 0.96
        _SpecStrength ("Sheen strength", Range(0, 1)) = 0.22
        _SpecPower ("Sheen power", Float) = 48
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Water column per terrain vertex, baked by TerrainGrid. White = the
            // full encode range (deep). Its default is "white", so a scene with no
            // TerrainGrid renders open ocean rather than a world of foam.
            TEXTURE2D(_HeightMap);
            SAMPLER(sampler_HeightMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MapParams;      // xy: worldXZ -> uv (scale, offset), z: metres per unit of texel
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _FoamColor;
                float _DepthDistance;
                float _FoamDistance;
                float _FoamScale;
                float _FoamSpeed;
                float _WaveAmplitude;
                float _WaveLength;
                float _WaveSpeed;
                float _GridStep;
                float _FacetTint;
                float _FacetSpeed;
                float _GlintStrength;
                float _GlintThreshold;
                float _SpecStrength;
                float _SpecPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;

            };

            // Three sine trains at 1.0x / 0.69x / 1.25x the base wavelength,
            // in three directions. All of them are ≥ 7 grid steps long at the
            // defaults (16 m base, 1.5 m grid).
            float WaveHeight(float2 p, float t)
            {
                float k = 6.28318 / max(_WaveLength, 0.01);
                float h = sin(p.x * k + t) * 0.50
                        + sin((p.x * 0.6 + p.y * 0.8) * k * 1.45 - t * 1.3) * 0.30
                        + sin((p.y - p.x) * 0.707 * k * 0.8 + t * 0.7) * 0.20;
                return h * _WaveAmplitude;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                ws.y += WaveHeight(ws.xz, _Time.y * _WaveSpeed);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);

                return o;
            }

            // Cheap value noise for the foam break-up
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Which grid triangle this pixel is on. The grid's vertices sit on
            // whole multiples of _GridStep (TerrainGrid guarantees an even
            // quad count so the centred mesh lands on the step), the wave
            // only moves vertices vertically, and every quad is split along
            // the same diagonal (i, i+n, i+1 is the lower-left triangle), so
            // the triangle is fully determined by the XZ position. Returns a
            // slowly drifting 0..1 value per triangle: neighbours are
            // uncorrelated (crisp facets) and each eases between two random
            // values, so glints fade in and out instead of popping.
            float FacetValue(float2 xz)
            {
                float2 g = xz / max(_GridStep, 0.01);
                float2 cell = floor(g);
                float2 f = g - cell;
                float upper = step(1.0, f.x + f.y);
                float2 id = cell + float2(upper * 0.5, upper * 0.25);

                float tt = _Time.y * _FacetSpeed + Hash(id) * 4.0;   // desync the phase per facet
                float t0 = floor(tt);
                float tf = tt - t0;
                tf = tf * tf * (3.0 - 2.0 * tf);
                float a = Hash(id + t0 * 17.3);
                float b = Hash(id + (t0 + 1.0) * 17.3);
                return lerp(a, b, tf);
            }

            half4 frag(Varyings i) : SV_Target
            {

                // Metres of water column, read from the island's own heightfield
                // (TerrainGrid bakes it into _HeightMap). Sampling past the map
                // clamps to the border texel, which is open-ocean depth.
                float2 huv = i.positionWS.xz * _MapParams.x + _MapParams.y;
                float depthDiff = SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, saturate(huv)).r * _MapParams.z;

                // Colour by water thickness
                float depthT = saturate(depthDiff / max(_DepthDistance, 0.01));
                depthT = depthT * depthT * (3.0 - 2.0 * depthT);
                float4 water = lerp(_ShallowColor, _DeepColor, depthT);

                // Foam: strongest right at the shore, broken up by drifting noise
                float shore = saturate(1.0 - depthDiff / max(_FoamDistance, 0.01));
                float n = Noise(i.positionWS.xz * _FoamScale + _Time.y * _FoamSpeed);
                float n2 = Noise(i.positionWS.xz * _FoamScale * 2.3 - _Time.y * _FoamSpeed * 0.7);
                float foamMask = step(0.62 - shore * 0.45, n * 0.6 + n2 * 0.4) * shore;

                // Flat-shaded facet normal from derivatives (sign-safe)
                float3 nrm = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (nrm.y < 0.0) nrm = -nrm;

                // Per-facet tone, and the brightest few facets become sun glints
                float facet = FacetValue(i.positionWS.xz);
                float tint = 1.0 + (facet - 0.5) * 2.0 * _FacetTint;
                float glint = smoothstep(_GlintThreshold, 1.0, facet);

                Light light = GetMainLight();
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndl = saturate(dot(nrm, light.direction)) * 0.5 + 0.5;
                float3 halfDir = normalize(light.direction + viewDir);
                // Soft sheen — no cutoff, so it can never flip the whole sea (see header)
                float sheen = pow(saturate(dot(nrm, halfDir)), _SpecPower) * _SpecStrength;
                // Glints are angle-independent; they fade as the sun drops below the horizon
                float sunUp = saturate(light.direction.y * 2.0);
                float glintAmount = glint * _GlintStrength * sunUp;

                float3 ambient = SampleSH(nrm);
                float3 rgb = water.rgb * tint * (light.color * ndl + ambient)
                           + light.color * (sheen + glintAmount);
                rgb = lerp(rgb, _FoamColor.rgb * (light.color * 0.6 + ambient), foamMask);

                float alpha = lerp(water.a, 1.0, foamMask);
                alpha = saturate(alpha + glintAmount * 0.5);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
