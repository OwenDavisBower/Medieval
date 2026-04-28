#ifndef MEDIEVAL_PIXEL_PERFECT_OUTLINE_INCLUDED
#define MEDIEVAL_PIXEL_PERFECT_OUTLINE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D_X(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);

TEXTURE2D_X(_SceneNormalsTexture);
SAMPLER(sampler_SceneNormalsTexture);
TEXTURE2D_X(_MaskTex);
SAMPLER(sampler_MaskTex);

float4 _PixelGrid;
float _DepthThreshold;
float _NormalThreshold;
float _FillNeighbor;
float4 _OutlineSilColor;
float4 _OutlineCreaseColor;

float3 SampleNormalsLoRes(float2 uv)
{
    float3 n = SAMPLE_TEXTURE2D_X(_SceneNormalsTexture, sampler_SceneNormalsTexture, uv).rgb * 2.0 - 1.0;
    return length(n) > 1e-4 ? normalize(n) : float3(0, 0, 1);
}

float LinearEyeFromDepth(float2 uv)
{
    float raw = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
    return LinearEyeDepth(raw, _ZBufferParams);
}

float4 SampleMaskLoRes(float2 cellUv)
{
    return SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, cellUv);
}

float MaskMaxNeighborhoodR(float2 cuv, float2 du, float2 dv)
{
    float m = SampleMaskLoRes(cuv).r;
    m = max(m, SampleMaskLoRes(cuv - du).r);
    m = max(m, SampleMaskLoRes(cuv + du).r);
    m = max(m, SampleMaskLoRes(cuv - dv).r);
    m = max(m, SampleMaskLoRes(cuv + dv).r);
    return m;
}

bool SilhouetteFromMaskEdge(float2 cuv)
{
    float2 g = max(_PixelGrid.xy, float2(1, 1));
    float2 du = float2(1.0 / g.x, 0);
    float2 dv = float2(0, 1.0 / g.y);

    // Draw silhouette on pixels just outside the masked object.
    float center = SampleMaskLoRes(cuv).r;
    float neighMax = center;
    if (_FillNeighbor > 0.5)
        neighMax = MaskMaxNeighborhoodR(cuv, du, dv);

    bool outside = center <= 0.001;
    bool nearObject = neighMax > 0.001;
    return outside && nearObject;
}

void EvaluateEdgesAtCell(float2 cuv, out bool rawSil, out bool rawCre)
{
    float2 g = max(_PixelGrid.xy, float2(1, 1));
    float2 du = float2(1.0 / g.x, 0);
    float2 dv = float2(0, 1.0 / g.y);

    float d0 = LinearEyeFromDepth(cuv);
    float dL = LinearEyeFromDepth(cuv - du);
    float dR = LinearEyeFromDepth(cuv + du);
    float dU = LinearEyeFromDepth(cuv + dv);
    float dD = LinearEyeFromDepth(cuv - dv);

    rawSil = (abs(d0 - dL) > _DepthThreshold) || (abs(d0 - dR) > _DepthThreshold)
        || (abs(d0 - dU) > _DepthThreshold) || (abs(d0 - dD) > _DepthThreshold);

    float3 n0 = SampleNormalsLoRes(cuv);
    float3 nL = SampleNormalsLoRes(cuv - du);
    float3 nR = SampleNormalsLoRes(cuv + du);
    float3 nU = SampleNormalsLoRes(cuv + dv);
    float3 nD = SampleNormalsLoRes(cuv - dv);
    float th = _NormalThreshold;
    rawCre = (abs(dot(n0, nL)) < th) || (abs(dot(n0, nR)) < th)
        || (abs(dot(n0, nU)) < th) || (abs(dot(n0, nD)) < th);
}

bool DilatedSilhouette(float2 cuv)
{
    float2 g = max(_PixelGrid.xy, float2(1, 1));
    float2 du = float2(1.0 / g.x, 0);
    float2 dv = float2(0, 1.0 / g.y);
    bool silCenter, creTmp;
    EvaluateEdgesAtCell(cuv, silCenter, creTmp);
    bool a = silCenter;
    if (_FillNeighbor > 0.5)
    {
        EvaluateEdgesAtCell(cuv - du, silCenter, creTmp); a = a || silCenter;
        EvaluateEdgesAtCell(cuv + du, silCenter, creTmp); a = a || silCenter;
        EvaluateEdgesAtCell(cuv - dv, silCenter, creTmp); a = a || silCenter;
        EvaluateEdgesAtCell(cuv + dv, silCenter, creTmp); a = a || silCenter;
    }
    return a;
}

bool DilatedCreases(float2 cuv)
{
    float2 g = max(_PixelGrid.xy, float2(1, 1));
    float2 du = float2(1.0 / g.x, 0);
    float2 dv = float2(0, 1.0 / g.y);
    bool silTmp, creCenter;
    EvaluateEdgesAtCell(cuv, silTmp, creCenter);
    bool a = creCenter;
    if (_FillNeighbor > 0.5)
    {
        EvaluateEdgesAtCell(cuv - du, silTmp, creCenter); a = a || creCenter;
        EvaluateEdgesAtCell(cuv + du, silTmp, creCenter); a = a || creCenter;
        EvaluateEdgesAtCell(cuv - dv, silTmp, creCenter); a = a || creCenter;
        EvaluateEdgesAtCell(cuv + dv, silTmp, creCenter); a = a || creCenter;
    }
    return a;
}

float4 CompositeOutline(float2 uv, float3 sceneColor)
{
    float2 g = max(_PixelGrid.xy, float2(1, 1));
    float2 cell = floor(uv * g);
    float2 cuv = (cell + 0.5) / g;

    float2 du = float2(1.0 / g.x, 0);
    float2 dv = float2(0, 1.0 / g.y);
    float4 mask = SampleMaskLoRes(cuv);

    // Note: silhouette is drawn outside the object, so "allow" must consider neighbors.
    float silAllow = (_FillNeighbor > 0.5) ? MaskMaxNeighborhoodR(cuv, du, dv) : mask.r;
    bool allowSil = silAllow > 0.001;
    bool allowCre = mask.g > 0.001;

    bool sil = SilhouetteFromMaskEdge(cuv) && allowSil;
    bool cre = false;
    if (!sil && allowCre)
        cre = DilatedCreases(cuv);

    float4 col = float4(sceneColor, 1);
    if (sil)
        col = float4(lerp(sceneColor, _OutlineSilColor.rgb, _OutlineSilColor.a), 1);
    else if (cre)
        col = float4(lerp(sceneColor, _OutlineCreaseColor.rgb, _OutlineCreaseColor.a), 1);
    return col;
}

#endif
