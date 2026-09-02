// Stylized low-poly water for the island (Phase 10 Stage 3, kept simple).
//
//  - depth-based colour: turquoise shallows fading to deep blue, read from
//    the camera depth texture (URP "Depth Texture" must be on — PC_RPAsset is)
//  - shoreline foam: a noise-broken band where the water is thin
//  - gentle wave displacement in the vertex shader (the water mesh is a real
//    grid, built by TerrainGrid, so there are vertices to move)
//  - flat-shaded facets: the normal is rebuilt per pixel from screen-space
//    derivatives, which gives the low-poly look without duplicating verts
//  - lit by the main light (so the day/night cycle tints it) with a hard,
//    stepped specular highlight
//
// Orthographic camera note: the gameplay camera is ortho with a NEGATIVE near
// clip. Eye depth is reconstructed linearly from the raw depth for ortho, and
// only the DIFFERENCE between scene depth and water depth is used, so the
// negative-near quirk that breaks SSAO does not affect this shader.
Shader "Island RTS/Stylized Water"
{
    Properties
    {
        _ShallowColor ("Shallow colour", Color) = (0.24, 0.78, 0.80, 0.55)
        _DeepColor ("Deep colour", Color) = (0.04, 0.27, 0.56, 0.94)
        _DepthDistance ("Depth fade distance", Float) = 2.6
        _FoamColor ("Foam colour", Color) = (0.94, 0.98, 1.0, 1.0)
        _FoamDistance ("Foam distance", Float) = 0.75
        _FoamScale ("Foam noise scale", Float) = 0.45
        _FoamSpeed ("Foam speed", Float) = 0.35
        _WaveAmplitude ("Wave amplitude", Float) = 0.07
        _WaveLength ("Wave length", Float) = 7.0
        _WaveSpeed ("Wave speed", Float) = 0.8
        _SpecStrength ("Specular strength", Range(0, 2)) = 0.9
        _SpecPower ("Specular power", Float) = 64
        _SpecCutoff ("Specular cutoff", Range(0, 1)) = 0.45
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
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
                float _SpecStrength;
                float _SpecPower;
                float _SpecCutoff;
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
                float4 screenPos : TEXCOORD1;
            };

            float WaveHeight(float2 p, float t)
            {
                float k = 6.28318 / max(_WaveLength, 0.01);
                float h = sin(p.x * k + t) * 0.55
                        + sin((p.x * 0.6 + p.y * 0.8) * k * 1.7 - t * 1.3) * 0.30
                        + sin(p.y * k * 0.8 + t * 0.7) * 0.15;
                return h * _WaveAmplitude;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                ws.y += WaveHeight(ws.xz, _Time.y * _WaveSpeed);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.screenPos = ComputeScreenPos(o.positionCS);
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

            // Linear eye depth of whatever is under this pixel, for both
            // projection types. Ortho depth is linear in the buffer already.
            float SceneEyeDepth(float2 screenUV)
            {
                float raw = SampleSceneDepth(screenUV);
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                        float lin01 = 1.0 - raw;
                    #else
                        float lin01 = raw;
                    #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, lin01);
                }
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 screenUV = i.screenPos.xy / i.screenPos.w;

                float sceneEye = SceneEyeDepth(screenUV);
                float waterEye = -TransformWorldToView(i.positionWS).z;
                float depthDiff = max(0.0, sceneEye - waterEye);

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

                Light light = GetMainLight();
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndl = saturate(dot(nrm, light.direction)) * 0.5 + 0.5;
                float3 halfDir = normalize(light.direction + viewDir);
                float spec = pow(saturate(dot(nrm, halfDir)), _SpecPower);
                spec = smoothstep(_SpecCutoff, _SpecCutoff + 0.12, spec) * _SpecStrength;

                float3 ambient = SampleSH(nrm);
                float3 rgb = water.rgb * (light.color * ndl + ambient) + light.color * spec;
                rgb = lerp(rgb, _FoamColor.rgb * (light.color * 0.6 + ambient), foamMask);

                float alpha = lerp(water.a, 1.0, foamMask);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
