// The build grid, masked by the fog of war (2026-09-19).
//
// The overlay's mesh is every BUILDABLE cell boundary on the island, built once when
// the grid is toggled on. Drawn with a plain transparent shader that was the whole
// island's buildable area at a glance — including ground nobody has ever walked, which
// leaked the shape of the map through the fog.
//
// So the line alpha is multiplied by the fog mask's explored channel: the grid exists
// only where the colony has been. It rides FogOfWar.hlsl rather than a CPU cull so the
// mesh never rebuilds — the lines fade in with the same easing the terrain does as a
// scout reveals ground, and the grid costs nothing while it is up.
//
// Explored is the test, not visible: placement already allows explored-but-unwatched
// ground (GhostPlacer refuses only unexplored cells), so the grid shows exactly where a
// building may go. _FogParams.w is 0 where no fog exists (menu, showcase), and the
// include returns the colour untouched there.
//
// Lives in Resources so a player build keeps it — GridOverlay.cs loads it by PATH.
Shader "FoundingTide/GridOverlay"
{
    Properties
    {
        _Color ("Line Color", Color) = (1,1,1,0.22)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "GridLines"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FogOfWar.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = _Color;
                if (_FogParams.w >= 0.5)
                    c.a *= SampleFogOfWar(i.positionWS).r;
                return c;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
