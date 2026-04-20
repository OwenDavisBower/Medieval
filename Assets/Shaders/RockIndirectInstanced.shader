Shader "Universal Render Pipeline/RockIndirectInstanced"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.35, 0.34, 0.32, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            StructuredBuffer<float4x4> _RockObjectToWorld;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings vert(Attributes v, uint instanceID : SV_InstanceID)
            {
                float4x4 otw = _RockObjectToWorld[instanceID];
                float3 posWS = mul(otw, float4(v.positionOS.xyz, 1.0)).xyz;
                float3x3 rot = (float3x3)otw;
                float3 nWS = normalize(mul(rot, v.normalOS));

                Varyings o;
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = nWS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalize(i.normalWS), mainLight.direction));
                half3 lit = ndotl * mainLight.color + half3(0.06, 0.065, 0.08);
                return half4(lit * _BaseColor.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertShadow
            #pragma fragment fragShadow

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float4x4> _RockObjectToWorld;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vertShadow(Attributes v, uint instanceID : SV_InstanceID)
            {
                float4x4 otw = _RockObjectToWorld[instanceID];
                float3 positionWS = mul(otw, float4(v.positionOS.xyz, 1.0)).xyz;
                Varyings o;
                o.positionCS = TransformWorldToHClip(positionWS);
                return o;
            }

            half4 fragShadow(Varyings i) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
