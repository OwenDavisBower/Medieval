Shader "Medieval/Rendering/PixelArtComposite"
{
    Properties
    {
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    float2 _PixelGrid; // (width, height) of the low-res buffer
    float _Posterize; // 0 = off, else per-channel steps (>= 2)
    float _PosterizeDitherStrength; // 0 = off; scales Bayer offset before quantize

    static const uint k_Bayer4x4[16] =
    {
        0u, 8u, 2u, 10u,
        12u, 4u, 14u, 6u,
        3u, 11u, 1u, 9u,
        15u, 7u, 13u, 5u
    };

    float Bayer4x4(uint2 p)
    {
        uint i = (p.x & 3u) + ((p.y & 3u) << 2u);
        uint v = k_Bayer4x4[i];
        return (v + 0.5) / 16.0 - 0.5;
    }

    float4 FragDownsamplePoint(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 c = floor(input.texcoord.xy * _PixelGrid) + 0.5;
        float2 uv = c / _PixelGrid;
        return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
    }

    float4 FragUpscalePosterize(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 w = 1.0 / max(_BlitTexture_TexelSize.xy, 1.0e-4);
        float2 snUv = (floor(input.texcoord.xy * w) + 0.5) / w;
        float4 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, snUv, 0);

        if (_Posterize > 0.001)
        {
            float levels = max(_Posterize, 2.0);
            float3 c = color.rgb;
            if (_PosterizeDitherStrength > 0.001)
            {
                uint2 pix = (uint2)(input.texcoord.xy * _ScreenParams.xy);
                float b = Bayer4x4(pix);
                c += b * _PosterizeDitherStrength / levels;
            }
            color.rgb = float3(floor(c * levels) / (levels - 1.0));
        }

        return color;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "PixelArtDownsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownsamplePoint
            #pragma target 3.0
            ENDHLSL
        }
        Pass
        {
            Name "PixelArtPosterizeUpscale"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUpscalePosterize
            #pragma target 3.0
            ENDHLSL
        }
    }
    Fallback Off
}
