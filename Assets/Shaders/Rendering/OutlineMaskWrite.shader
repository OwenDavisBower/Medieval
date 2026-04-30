Shader "Hidden/Medieval/OutlineMaskWrite"
{
    Properties
    {
        [HideInInspector] _SilhouetteWeight ("Silhouette (R)", Float) = 1
        [HideInInspector] _CreaseWeight ("Crease (G)", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "OutlineMask"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest LEqual
            Cull Back
            ColorMask RGB

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityDOTSInstancing.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float _SilhouetteWeight;
                float _CreaseWeight;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
                // Optional per-entity overrides (falls back to per-material values).
                UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                    UNITY_DOTS_INSTANCED_PROP(float, _SilhouetteWeight_Instanced)
                    UNITY_DOTS_INSTANCED_PROP(float, _CreaseWeight_Instanced)
                UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #endif

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                #ifdef UNITY_DOTS_INSTANCING_ENABLED
                    float silhouette = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(float, _SilhouetteWeight_Instanced, _SilhouetteWeight);
                    float crease = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_CUSTOM_DEFAULT(float, _CreaseWeight_Instanced, _CreaseWeight);
                #else
                    float silhouette = _SilhouetteWeight;
                    float crease = _CreaseWeight;
                #endif

                return float4(saturate(silhouette), saturate(crease), 0, 1);
            }
            ENDHLSL
        }
    }
}
