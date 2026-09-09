// One drifting cloud puff (or, with _IsSheet, the whole-island overcast sheet).
// A flat quad at cloud altitude; unlit, transparent, never solid: alpha IS the
// coverage field from CloudCommon.hlsl, so the same shape shades the ground via
// CloudCookie.shader. Tinted by the sun colour so night clouds are not white.
Shader "IslandRTS/CloudPuff"
{
    Properties
    {
        _Cloud ("Centre XZ, radius, opacity", Vector) = (0, 0, 10, 1)
        _Seed ("Noise seed", Float) = 0
        _IsSheet ("Overcast sheet", Float) = 0
        _Alpha ("Peak alpha", Range(0, 1)) = 0.8
        _Shade ("Dense-part tint", Color) = (0.72, 0.75, 0.82, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
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
            #include "CloudCommon.hlsl"

            float4 _CloudSunTint;   // global, set by CloudSystem from the sun colour

            CBUFFER_START(UnityPerMaterial)
                float4 _Cloud;
                float _Seed;
                float _IsSheet;
                float _Alpha;
                float4 _Shade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = _IsSheet > 0.5
                    ? CloudOvercastDensity(i.positionWS.xz)
                    : CloudPuffDensity(i.positionWS.xz, _Cloud.xy, _Cloud.z, _Seed) * _Cloud.w;
                // Thick parts go a touch grey-blue so the puff reads as a body,
                // not a white smear; thin parts stay bright and see-through.
                float3 col = lerp(float3(1, 1, 1), _Shade.rgb, saturate(d * 0.7)) * _CloudSunTint.rgb;
                return half4(col, d * _Alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
