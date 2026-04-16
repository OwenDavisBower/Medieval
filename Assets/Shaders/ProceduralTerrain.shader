// Procedural terrain splat blending for URP 17.x (Unity 6.4).
// Fragment: 5 texture samples (splat + 4 albedos). Rock uses one dominant-axis triplanar UV with a single albedo sample.
Shader "Universal Render Pipeline/ProceduralTerrain"
{
    Properties
    {
        [Header(Splat)][NoScaleOffset] _Splatmap("Splatmap", 2D) = "white" {}
        [Header(Albedo)][NoScaleOffset] _GrassTex("Grass", 2D) = "white" {}
        [NoScaleOffset] _RockTex("Rock", 2D) = "white" {}
        [NoScaleOffset] _SandTex("Sand", 2D) = "white" {}
        [NoScaleOffset] _DirtTex("Dirt", 2D) = "white" {}
        _GrassTiling("Grass Tiling", Float) = 4
        _RockTiling("Rock Tiling", Float) = 4
        _SandTiling("Sand Tiling", Float) = 4
        _DirtTiling("Dirt Tiling", Float) = 4
        _WorldSize("World Size", Float) = 1024
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Splatmap);
            SAMPLER(sampler_Splatmap);
            TEXTURE2D(_GrassTex);
            SAMPLER(sampler_GrassTex);
            TEXTURE2D(_RockTex);
            SAMPLER(sampler_RockTex);
            TEXTURE2D(_SandTex);
            SAMPLER(sampler_SandTex);
            TEXTURE2D(_DirtTex);
            SAMPLER(sampler_DirtTex);

            CBUFFER_START(UnityPerMaterial)
                half _GrassTiling;
                half _RockTiling;
                half _SandTiling;
                half _DirtTiling;
                half _WorldSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half2 splatUV : TEXCOORD2;
                half2 heightSlope : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                const VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                const VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = half3(normalInput.normalWS);
                output.splatUV = half2(input.uv0);
                output.heightSlope = half2(input.uv1);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                const half4 splat = SAMPLE_TEXTURE2D(_Splatmap, sampler_Splatmap, input.splatUV);
                const half wGrass = splat.r;
                const half wRock = splat.g;
                const half wSand = splat.b;
                const half wDirt = splat.a;

                const half3 wp = half3(input.positionWS);
                const half2 uvGrass = wp.xz * _GrassTiling;
                const half2 uvSand = wp.xz * _SandTiling;
                const half2 uvDirt = wp.xz * _DirtTiling;

                const half3 n = normalize(input.normalWS);
                const half3 an = abs(n);
                half2 uvRock;
                if (an.x >= an.y && an.x >= an.z)
                    uvRock = wp.zy * _RockTiling;
                else if (an.y >= an.z)
                    uvRock = wp.xz * _RockTiling;
                else
                    uvRock = wp.xy * _RockTiling;

                const half3 grass = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, uvGrass).rgb;
                const half3 rock = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, uvRock).rgb;
                const half3 sand = SAMPLE_TEXTURE2D(_SandTex, sampler_SandTex, uvSand).rgb;
                const half3 dirt = SAMPLE_TEXTURE2D(_DirtTex, sampler_DirtTex, uvDirt).rgb;

                const half3 albedo = grass * wGrass + rock * wRock + sand * wSand + dirt * wDirt;

                const float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                const Light mainLight = GetMainLight(shadowCoord);
                const half slope = input.heightSlope.y;
                const half ao = saturate(1.0h - slope * 0.06h);
                const half3 lit = albedo * LightingLambert(mainLight.color, mainLight.direction, n);
                const half3 ambient = albedo * SampleSH(n);
                return half4((lit + ambient) * ao, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
