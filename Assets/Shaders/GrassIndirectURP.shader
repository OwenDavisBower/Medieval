Shader "Medieval/GrassIndirectURP"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.25, 0.55, 0.18, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"

            struct GrassInstance
            {
                float3 position;
                float rotationY;
                float3 scale;
                float pad;
            };

            StructuredBuffer<GrassInstance> _GrassInstances;
            StructuredBuffer<uint> _VisibleIndices;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            float3x3 RotationY(float rad)
            {
                float c = cos(rad);
                float s = sin(rad);
                return float3x3(
                    c, 0, s,
                    0, 1, 0,
                    -s, 0, c);
            }

            Varyings Vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings o;
                uint src = _VisibleIndices[instanceID];
                GrassInstance g = _GrassInstances[src];

                float3x3 rot = RotationY(g.rotationY);
                float3 posOS = input.positionOS * g.scale;
                posOS = mul(rot, posOS);
                float3 nrm = normalize(mul(rot, input.normalOS));

                float3 positionWS = posOS + g.position;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionWS = positionWS;
                o.normalWS = nrm;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 n = normalize(i.normalWS);
                half ndl = saturate(dot(n, mainLight.direction));
                half3 gi = SampleSH(n);
                half3 color = _BaseColor.rgb * (mainLight.color * ndl + gi);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
