// Transparent unlit overlay that maps a coverage texture onto the floor using world-space XZ,
// so it lines up with CoverageGrid regardless of the mesh's UVs.
Shader "CleanerBot/CoverageOverlay"
{
    Properties
    {
        _CoverageTex ("Coverage", 2D) = "black" {}
        _Rect ("Origin XZ (xy), Size XZ (zw)", Vector) = (0, 0, 10, 8)
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Overlay"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CoverageTex);
            SAMPLER(sampler_CoverageTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _CoverageTex_ST;
                float4 _Rect;
                float _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = (i.positionWS.xz - _Rect.xy) / _Rect.zw;
                half4 c = SAMPLE_TEXTURE2D(_CoverageTex, sampler_CoverageTex, uv);
                c.a *= _Opacity;
                return c;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
