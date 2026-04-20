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
            // sampler_PointClamp comes from Blit.hlsl / GlobalSamplers (do not redefine here).

            float4 _PixelGrid; // xy = horizontal/vertical cell counts, zw unused
            float _Posterize; // 0 = off, else per-channel steps (>= 2)

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 res = max(_PixelGrid.xy, 1.0);
                float2 uv = input.texcoord;

                // Pixelation: snap UVs to a regular grid (quantize in normalized space).
                // floor(uv * res) / res matches the requested quantization; sampling uses point clamp on the source.
                float2 snappedUV = floor(uv * res) / res;

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
