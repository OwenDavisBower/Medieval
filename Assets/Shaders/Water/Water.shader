Shader "Medieval/Water/PlanarDepthWater"
{
    Properties
    {
        [Header(Depth Tint)]
        _ShallowColor("Shallow Color", Color) = (0.25, 0.75, 1.0, 0.85)
        _DeepColor("Deep Color", Color) = (0.02, 0.15, 0.35, 0.85)
        _MaxDepth("Max Depth (world -> view)", Range(0.05, 50.0)) = 6.0
        _DepthSteps("Depth Steps (quantized)", Range(2, 16)) = 5

        [Header(Refraction)]
        _NoiseTex("Noise", 2D) = "white" {}
        _NoiseTiling("Noise Tiling", Float) = 0.08
        _NoiseSpeed("Noise Speed", Float) = 0.15
        _DistortionStrength("Distortion Strength", Range(0.0, 0.05)) = 0.01

        [Header(Waves)]
        _WaveAmplitude("Wave Amplitude", Range(0.0, 0.5)) = 0.08
        _WaveFrequency("Wave Frequency", Float) = 0.6
        _WaveSpeed("Wave Speed", Float) = 1.25

        [Header(Foam)]
        _ShoreFoamWidth("Shore Foam Width (depth)", Range(0.001, 1.0)) = 0.08
        _ShoreFoamColor("Shore Foam Color", Color) = (1,1,1,1)
        _FoamTiling("Foam Tiling", Float) = 0.12
        _FoamSpeed("Foam Speed", Float) = 0.05
        _FoamThreshold("Foam Threshold", Range(0.0, 1.0)) = 0.78
        _FoamColor("Foam Color", Color) = (1,1,1,1)

        [Header(Planar Reflection (optional))]
        [NoScaleOffset]_ReflectionTex("Reflection Texture", 2D) = "black" {}
        _ReflectionStrength("Reflection Strength", Range(0.0, 1.0)) = 0.35
        _FresnelPower("Fresnel Power", Range(0.1, 8.0)) = 3.0

        [Header(Debug)]
        [Enum(Off,0,DepthFade,1,ShoreFoam,2,FoamCaps,3,DepthDiff,4)] _DebugView("Debug View", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            TEXTURE2D(_ReflectionTex);
            SAMPLER(sampler_ReflectionTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float _MaxDepth;
                float _DepthSteps;

                float _NoiseTiling;
                float _NoiseSpeed;
                float _DistortionStrength;

                float _WaveAmplitude;
                float _WaveFrequency;
                float _WaveSpeed;

                float _ShoreFoamWidth;
                float4 _ShoreFoamColor;
                float _FoamTiling;
                float _FoamSpeed;
                float _FoamThreshold;
                float4 _FoamColor;

                float _ReflectionStrength;
                float _FresnelPower;
                float _DebugView;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            float Quantize01(float v, float steps)
            {
                steps = max(2.0, steps);
                float q = floor(saturate(v) * (steps - 1.0) + 0.5) / (steps - 1.0);
                return saturate(q);
            }

            float2 Rot90(float2 v) { return float2(-v.y, v.x); }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                float t = _Time.y * _WaveSpeed;
                float2 p = positionWS.xz;
                float waveA = sin((p.x + t) * _WaveFrequency);
                float waveB = sin((dot(Rot90(p), float2(1, 0)) + t * 0.85) * (_WaveFrequency * 1.07));
                float wave = waveA * waveB;
                positionWS.y += wave * _WaveAmplitude;

                OUT.positionWS = positionWS;
                OUT.uv = IN.uv;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uvSS = IN.screenPos.xy / IN.screenPos.w;

                // Depth fade (0 = surface, 1 = max depth)
                float rawSceneDepth = SampleSceneDepth(uvSS);
                float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
                // Compute water surface depth in view space to avoid clip/NDC depth conversion pitfalls.
                float3 waterViewPos = TransformWorldToView(IN.positionWS);
                float waterEyeDepth = -waterViewPos.z;
                float depthDiff = max(0.0, sceneEyeDepth - waterEyeDepth);
                float depthFade = saturate(depthDiff / max(0.0001, _MaxDepth));
                float depthFadeQ = Quantize01(depthFade, _DepthSteps);

                float4 baseCol = lerp(_ShallowColor, _DeepColor, depthFadeQ);

                // Refraction (screen-space). If opaque texture is disabled, this returns black; we fall back to tint.
                float2 noiseUv = IN.positionWS.xz * _NoiseTiling + (_Time.y * _NoiseSpeed);
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUv).r * 2.0 - 1.0;
                float2 distortion = (noise.xx) * (_DistortionStrength * depthFadeQ);

                float3 sceneCol = SampleSceneColor(uvSS + distortion);

                // Shore foam band: 1 when geometry is close under surface
                float shoreFoam = step(depthDiff, _ShoreFoamWidth);

                // Surface foam caps
                float2 foamUv = IN.positionWS.xz * _FoamTiling + (_Time.y * _FoamSpeed);
                float foamNoise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, foamUv).r;
                float foamCaps = step(_FoamThreshold, foamNoise);

                // Debug views
                if (_DebugView > 0.5 && _DebugView < 1.5) return half4(depthFadeQ.xxx, 1);
                if (_DebugView > 1.5 && _DebugView < 2.5) return half4(shoreFoam.xxx, 1);
                if (_DebugView > 2.5 && _DebugView < 3.5) return half4(foamCaps.xxx, 1);
                if (_DebugView > 3.5 && _DebugView < 4.5) return half4(saturate(depthDiff / max(0.0001, _MaxDepth)).xxx, 1);

                // Planar reflection (optional, driven by script). Fresnel uses flat up normal.
                float3 viewDirWS = SafeNormalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                float3 normalWS = float3(0, 1, 0);
                float fresnel = pow(1.0 - saturate(dot(viewDirWS, normalWS)), _FresnelPower);
                float3 reflCol = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, uvSS).rgb;

                float3 outRgb = baseCol.rgb;
                outRgb = lerp(outRgb, sceneCol * baseCol.rgb, 0.55); // refraction contribution
                outRgb = lerp(outRgb, reflCol, fresnel * _ReflectionStrength);

                // Foam overlays (hard, pixel-art friendly)
                outRgb = lerp(outRgb, _FoamColor.rgb, foamCaps * 0.35);
                outRgb = lerp(outRgb, _ShoreFoamColor.rgb, shoreFoam);

                return half4(outRgb, baseCol.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
