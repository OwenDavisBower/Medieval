Shader "Universal Render Pipeline/Stylized Toon Lit (Animatron)"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        _ShadowColor("Shadow Tint", Color) = (0.45, 0.55, 0.75, 1)

        [IntRange] _StepCount("Cel Step Count", Range(1, 16)) = 4
        _StepSmoothness("Step Edge Softness", Range(0.001, 0.5)) = 0.08

        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0

        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
        [HideInInspector] _AddPrecomputedVelocity("_AddPrecomputedVelocity", Float) = 0.0
        [HideInInspector] _XRMotionVectorsPass("_XRMotionVectorsPass", Float) = 1.0
        _QueueOffset("Queue offset", Float) = 0.0

        // Animatron (per-instance). Must be set by Animatron skinning renderer.
        // Note: the instanced value is accessed in HLSL via DOTS instancing macros.
        _SkinMatrixIndex("_SkinMatrixIndex", Float) = 0

        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }

    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.projectdawn.animatron/Shaders/AnimatronLinearBlendSkinning.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadowColor;
            half _StepCount;
            half _StepSmoothness;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex StylizedToonPassVertex
            #pragma fragment StylizedToonPassFragment

            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            // URP/Entities versions differ on where DOTS instancing macros live.
            // On some Metal builds the DOTS include doesn't define UNITY_DOTS_* macros, so we provide a compatibility shim.
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            // Unity 6 / URP ships DOTS instancing helpers in URP's ShaderLibrary.
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #ifndef UNITY_DOTS_INSTANCING_START
                #define UNITY_DOTS_INSTANCING_START(name) UNITY_INSTANCING_BUFFER_START(name)
                #define UNITY_DOTS_INSTANCING_END(name)   UNITY_INSTANCING_BUFFER_END(name)
                #define UNITY_DOTS_INSTANCED_PROP(type, var) UNITY_DEFINE_INSTANCED_PROP(type, var)
                #define UNITY_ACCESS_DOTS_INSTANCED_PROP(type, var) UNITY_ACCESS_INSTANCED_PROP(MaterialPropertyMetadata, var)
            #endif

            // Animatron per-instance property (required)
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float, _SkinMatrixIndex)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            struct StylizedAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;

                // Animatron skinning inputs
                uint4 skinIndices : BLENDINDICES0;
                float4 skinWeights : BLENDWEIGHTS0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct StylizedVaryings
            {
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD3;
            #endif
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                half4 fogFactorAndVertexLight : TEXCOORD4;
            #else
                half fogFactor : TEXCOORD4;
            #endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 5);
            #if defined(DYNAMICLIGHTMAP_ON)
                float2 dynamicLightmapUV : TEXCOORD6;
            #endif
            #if (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)) && defined(USE_APV_PROBE_OCCLUSION)
                float4 probeOcclusion : TEXCOORD7;
            #endif
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half StylizedToonDiffuseTerm(half ndotl, half stepsFloat, half edgeSoft)
            {
                half steps = max(stepsFloat, 1.0h);
                half scaled = saturate(ndotl) * steps;
                half f = frac(scaled);
                half idx = floor(scaled);
                half low = idx / steps;
                half high = (idx + 1.0h) / steps;
                half ramp = smoothstep(1.0h - edgeSoft, 1.0h, f);
                return lerp(low, high, ramp);
            }

            half3 SampleStylizedGI(StylizedVaryings input, half3 normalWS, half3 viewDirWS, float4 shadowMask)
            {
            #if defined(_SCREEN_SPACE_IRRADIANCE)
                return SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy);
            #elif defined(DYNAMICLIGHTMAP_ON)
                return SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, normalWS);
            #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
            #if defined(USE_APV_PROBE_OCCLUSION)
                return SAMPLE_GI(input.vertexSH,
                    GetAbsolutePositionWS(input.positionWS),
                    normalWS,
                    viewDirWS,
                    input.positionCS.xy,
                    input.probeOcclusion,
                    shadowMask);
            #else
                return SAMPLE_GI(input.vertexSH,
                    GetAbsolutePositionWS(input.positionWS),
                    normalWS,
                    viewDirWS,
                    input.positionCS.xy,
                    half4(1, 1, 1, 1),
                    shadowMask);
            #endif
            #elif defined(LIGHTMAP_ON)
                return SAMPLE_GI(input.staticLightmapUV, input.vertexSH, normalWS);
            #else
                return SampleSHPixel(input.vertexSH, normalWS);
            #endif
            }

            StylizedVaryings StylizedToonPassVertex(StylizedAttributes input)
            {
                StylizedVaryings output = (StylizedVaryings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Animatron skinning (linear blend, 4 joints)
                uint skinMatrixIndex = (uint)asint(UNITY_ACCESS_DOTS_INSTANCED_PROP(float, _SkinMatrixIndex));
                float3 skinnedPosOS;
                float3 skinnedNormalOS;
                float3 skinnedTangentOS;
                Animatron_LinearBlendSkinning_float(
                    skinMatrixIndex,
                    input.skinIndices,
                    input.skinWeights,
                    input.positionOS.xyz,
                    input.normalOS,
                    input.tangentOS.xyz,
                    skinnedPosOS,
                    skinnedNormalOS,
                    skinnedTangentOS);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(skinnedPosOS);
                VertexNormalInputs normalInput = GetVertexNormalInputs(skinnedNormalOS);

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionWS = vertexInput.positionWS;
                output.positionCS = vertexInput.positionCS;
                output.normalWS = NormalizeNormalPerVertex(normalInput.normalWS);

            #if defined(_FOG_FRAGMENT)
                half fogFactor = 0;
            #else
                half fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif

                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
            #if defined(DYNAMICLIGHTMAP_ON)
                output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
            #endif
            #if !defined(LIGHTMAP_ON)
            #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
            #if defined(USE_APV_PROBE_OCCLUSION)
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, viewDirWS, output.vertexSH, output.probeOcclusion);
            #else
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, viewDirWS, output.vertexSH, float4(0, 0, 0, 0));
            #endif
            #else
                OUTPUT_SH(output.normalWS.xyz, output.vertexSH);
            #endif
            #endif

            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
                output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
            #else
                output.fogFactor = fogFactor;
            #endif

            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(vertexInput);
            #endif

                return output;
            }

            void StylizedToonPassFragment(
                StylizedVaryings input,
                out half4 outColor : SV_Target0)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(input.positionCS);
            #endif

                half4 albedoA = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 albedo = albedoA.rgb;
                half alpha = albedoA.a;

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #else
                float4 shadowCoord = float4(0, 0, 0, 0);
            #endif

            #if defined(LIGHTMAP_ON)
                half4 shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            #else
                half4 shadowMask = SAMPLE_SHADOWMASK(0);
            #endif

                half3 bakedGI = SampleStylizedGI(input, normalWS, viewDirWS, shadowMask);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.positionCS = input.positionCS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                Light mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);

            #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
                AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
                if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_AMBIENT_OCCLUSION))
                {
                    mainLight.color *= aoFactor.directAmbientOcclusion;
                }
            #endif

                half ndotlMain = saturate(dot(normalWS, mainLight.direction));
                half steps = max(_StepCount, 1.0h);
                half edge = max(_StepSmoothness, 1.0e-4h);
                half toonTerm = StylizedToonDiffuseTerm(ndotlMain, steps, edge);

            #if defined(_RECEIVE_SHADOWS_OFF)
                half mainAtten = half(mainLight.distanceAttenuation);
            #else
                half mainAtten = mainLight.shadowAttenuation * half(mainLight.distanceAttenuation);
            #endif

                half3 diffuseMain = lerp(albedo * _ShadowColor.rgb, albedo, toonTerm) * mainLight.color * mainAtten;

            #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();

            #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

                    Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);

                #if defined(_LIGHT_LAYERS)
                    if (!IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer()))
                        continue;
                #endif
                    half ndotlAdd = saturate(dot(normalWS, light.direction));
                    half toonAdd = StylizedToonDiffuseTerm(ndotlAdd, steps, edge);
                    half atten = half(light.distanceAttenuation) * light.shadowAttenuation;
                    diffuseMain += lerp(albedo * _ShadowColor.rgb, albedo, toonAdd) * light.color * atten;
                }
            #endif

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);

                #if defined(_LIGHT_LAYERS)
                    if (IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer()))
                #endif
                    {
                        half ndotlAdd = saturate(dot(normalWS, light.direction));
                        half toonAdd = StylizedToonDiffuseTerm(ndotlAdd, steps, edge);
                        half atten = half(light.distanceAttenuation) * light.shadowAttenuation;
                        diffuseMain += lerp(albedo * _ShadowColor.rgb, albedo, toonAdd) * light.color * atten;
                    }
                LIGHT_LOOP_END
            #endif

            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                half3 vertexLighting = input.fogFactorAndVertexLight.yzw;
            #else
                half3 vertexLighting = half3(0, 0, 0);
            #endif

                half3 finalColor = bakedGI * albedo + diffuseMain + vertexLighting * albedo;

            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                half fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
            #else
                half fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
            #endif
                finalColor = MixFog(finalColor, fogCoord);

                outColor = half4(finalColor, alpha);
            }

            ENDHLSL
        }

        // NOTE: These passes remain URP stock. If you see incorrect shadows/depth for skinned meshes,
        // we can add skinned variants of these passes too.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            ZTest LEqual
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ZTest LEqual
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

