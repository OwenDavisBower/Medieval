Shader "Hidden/URP/PixelateScreen"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "Pixelate"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_CameraOpaqueTexture);
            float4 _CameraOpaqueTexture_TexelSize; // (1/w, 1/h, w, h) — set by runtime when texture is bound
            // sampler_PointClamp comes from Blit.hlsl / GlobalSamplers (do not redefine here).

            float4 _PixelGrid; // xy = horizontal/vertical cell counts, zw unused
            float _Posterize; // 0 = off, else per-channel steps (>= 2)

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 res = max(_PixelGrid.xy, 1.0);
                float2 uv = saturate(input.texcoord);

                // Macro grid: same as original floor(uv * res) / res (logical blocks stay locked to UV).
                float2 cellUV = floor(uv * res) / res;

                // Point sampling at macro corners lands on native texel edges and crawls on any camera motion.
                // Bias into the center of the nearest opaque-texture texel (standard RT post-process fix).
                float2 halfTexel = 0.5 * _CameraOpaqueTexture_TexelSize.xy;
                float2 maxUV = 1.0 - halfTexel;
                float2 snappedUV = min(cellUV + halfTexel, maxUV);

                half4 color = SAMPLE_TEXTURE2D_X(_CameraOpaqueTexture, sampler_PointClamp, snappedUV);

                if (_Posterize > 0.001h)
                {
                    float levels = max(_Posterize, 2.0);
                    color.rgb = floor(color.rgb * levels) / (levels - 1.0);
                }

                return color;
            }
            ENDHLSL
        }
    }
}
