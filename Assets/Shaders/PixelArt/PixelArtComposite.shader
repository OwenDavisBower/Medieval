Shader "Medieval/Rendering/PixelArtComposite"
{
    Properties
    {
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "PixelArtCieLab.hlsl"

    float2 _PixelGrid; // (width, height) of the low-res buffer

    #define MAX_PALETTE 48
    float4 _Palette[MAX_PALETTE];
    int _PaletteCount;
    float3 _PaletteLab[MAX_PALETTE];

    float4 FragDownsamplePoint(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 c = floor(input.texcoord.xy * _PixelGrid) + 0.5;
        float2 uv = c / _PixelGrid;
        return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
    }

    float4 FragLabUpscalePoint(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        // Point-clamp: snap to source texel centers (keeps tixels blocky on upscale)
        float2 w = 1.0 / max(_BlitTexture_TexelSize.xy, 1.0e-4);
        float2 snUv = (floor(input.texcoord.xy * w) + 0.5) / w;
        float3 linearRgb = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, snUv, 0).rgb;
        if (_PaletteCount <= 0)
            return float4(linearRgb, 1);
        float3 plab = LinearRgbToCieLab(linearRgb);
        float bestD = 1e10;
        int bestI = 0;
        [unroll]
        for (int i = 0; i < MAX_PALETTE; i++)
        {
            if (i < _PaletteCount)
            {
                float3 clab = _PaletteLab[i].xyz;
                float d = Cie76Distance(plab, clab);
                if (d < bestD)
                {
                    bestD = d;
                    bestI = i;
                }
            }
        }
        return float4(_Palette[bestI].rgb, 1);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Cull Off

        // Pass 0: full-res → low-res, cell-centre sampling
        Pass
        {
            Name "PixelArtDownsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownsamplePoint
            #pragma target 3.0
            ENDHLSL
        }
        // Pass 1: low-res → full-res, point sample + Lab palette
        Pass
        {
            Name "PixelArtLabQuantize"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragLabUpscalePoint
            #pragma target 3.0
            ENDHLSL
        }
    }
    Fallback Off
}
