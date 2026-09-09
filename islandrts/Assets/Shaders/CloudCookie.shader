// Renders the cloud layer's shade into the sun's light cookie.
//
// URP projects a directional cookie in LIGHT space: cookie uv = light-local xy
// / size + 0.5 (LightCookieManager, s_DirLightProj * uvTransform * worldToLight).
// So each texel here is a point on the plane through the light's transform,
// perpendicular to its direction. We push that point along the light direction
// until it hits the cloud altitude and evaluate the coverage there. A cloud and
// the ground beneath it (along the light) share light-space xy, which is why the
// shade lands offset by the sun's angle for free and follows the sun all day.
//
// Blitted by CloudSystem into a small R8 RenderTexture every frame the sky has
// anything in it. 1 = full sun, lower = shade; never darker than 1 - density.
Shader "Hidden/IslandRTS/CloudCookie"
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
            #include "CloudCommon.hlsl"

            #define MAX_CLOUDS 16

            float4x4 _LightToWorld;
            float    _CookieSize;
            float    _CloudHeight;
            float    _ShadowDensity;
            int      _CloudCount;
            float4   _Clouds[MAX_CLOUDS];       // x,z centre, radius, opacity
            float4   _CloudSeeds[MAX_CLOUDS];   // x: seed

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
                // Texel -> light-local plane point -> world -> cloud altitude.
                float2 local = (i.uv - 0.5) * _CookieSize;
                float3 p = mul(_LightToWorld, float4(local, 0.0, 1.0)).xyz;
                float3 dir = normalize(mul((float3x3)_LightToWorld, float3(0.0, 0.0, 1.0)));
                if (abs(dir.y) < 0.05) return half4(1, 1, 1, 1);
                float t = (_CloudHeight - p.y) / dir.y;
                float2 xz = (p + dir * t).xz;

                float clear = 1.0 - CloudOvercastDensity(xz);
                for (int c = 0; c < MAX_CLOUDS; c++)
                {
                    if (c >= _CloudCount) break;
                    float4 cl = _Clouds[c];
                    if (cl.w <= 0.001) continue;
                    clear *= 1.0 - CloudPuffDensity(xz, cl.xy, cl.z, _CloudSeeds[c].x) * cl.w;
                }
                float coverage = 1.0 - clear;

                // Fade to full sun at the cookie border: the texture clamps, and a
                // clamped shaded edge would paint a stripe to the horizon.
                float2 e = abs(i.uv - 0.5);
                float edge = 1.0 - smoothstep(0.40, 0.49, max(e.x, e.y));
                float v = 1.0 - coverage * _ShadowDensity * edge;
                return half4(v, v, v, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
