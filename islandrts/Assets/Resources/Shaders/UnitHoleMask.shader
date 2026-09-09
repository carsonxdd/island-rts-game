// Renders the screen-space "hole mask" that OccluderCutout.shader samples (2026-09-08).
//
// One soft disc per friendly-or-hostile unit, at the unit's screen position. Red is
// how much of the pixel a unit covers (1 at the centre, fading to 0 at the disc edge);
// green is the view depth of the FARTHEST unit covering the pixel, so an occluder
// fragment nearer than that depth is standing between the camera and someone, and
// cuts itself away. Farthest, not nearest: a palm standing between two colonists must
// open for the one behind it, and a palm in front of both is in front of the far one
// too.
//
// Blitted by UnitHoleMask (the MonoBehaviour) into a quarter-resolution RGHalf render
// texture every frame, then bound as the global _UnitHoleMask. Lives in Resources so a
// player build keeps it.
Shader "Hidden/IslandRTS/UnitHoleMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_UNITS 128

            int    _UnitCount;
            float  _Aspect;                 // screen width / height: discs are round on screen
            float4 _Units[MAX_UNITS];       // x,y: normalized screen uv; z: view depth; w: radius in uv-height units

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float coverage = 0.0;
                float depth = -1e6;
                for (int u = 0; u < MAX_UNITS; u++)
                {
                    if (u >= _UnitCount) break;
                    float4 unit = _Units[u];
                    float2 d = (i.uv - unit.xy) * float2(_Aspect, 1.0);
                    float r = length(d) / max(unit.w, 1e-4);
                    // Solid to 55% of the radius, then a soft rim to the edge.
                    float c = 1.0 - smoothstep(0.55, 1.0, r);
                    if (c <= 0.0) continue;
                    coverage = max(coverage, c);
                    depth = max(depth, unit.z);
                }
                return half4(coverage, depth, 0, 1);
            }
            ENDHLSL
        }
    }
}
