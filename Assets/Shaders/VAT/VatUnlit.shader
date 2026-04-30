Shader "Medieval/VAT/Unlit"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)

        _VatPosTex("VAT Position Texture", 2D) = "black" {}
        _VatNrmTex("VAT Normal Texture (optional)", 2D) = "black" {}

        _VatVertexCount("VAT Vertex Count", Float) = 0
        _VatTotalFrames("VAT Total Frames", Float) = 0
        _VatFrame("VAT Frame (per-instance)", Float) = 0
        _VatUseNormals("VAT Use Normals", Float) = 0
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

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityDOTSInstancing.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_VatPosTex);
            SAMPLER(sampler_VatPosTex);
            TEXTURE2D(_VatNrmTex);
            SAMPLER(sampler_VatNrmTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;

                float _VatVertexCount;
                float _VatTotalFrames;
                float _VatUseNormals;
                #ifndef UNITY_DOTS_INSTANCING_ENABLED
                    float _VatFrame; // non-DOTS fallback (material/global)
                #endif
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
                UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                    UNITY_DOTS_INSTANCED_PROP(float, _VatFrame)
                UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #endif

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                uint vertexID     : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float2 VatUV(uint vertexID, float frame, float vertexCount, float totalFrames)
            {
                // Textures are (width = vertexCount, height = totalFrames).
                // Sample at texel centers; require Point filtering and Clamp.
                float x = (vertexID + 0.5) / max(1.0, vertexCount);
                float y = (frame + 0.5) / max(1.0, totalFrames);
                return float2(x, y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                #ifdef UNITY_DOTS_INSTANCING_ENABLED
                    float frame = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(float, _VatFrame, 0.0);
                #else
                    float frame = _VatFrame;
                #endif
                float2 vatUV = VatUV(input.vertexID, frame, _VatVertexCount, _VatTotalFrames);

                // Vertex stage needs explicit LOD on some platforms (e.g. Metal).
                float3 vatPos = SAMPLE_TEXTURE2D_LOD(_VatPosTex, sampler_VatPosTex, vatUV, 0).xyz;
                float3 posOS = vatPos;

                output.positionHCS = TransformObjectToHClip(posOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                return half4(albedo.rgb, albedo.a);
            }
            ENDHLSL
        }
    }
}

