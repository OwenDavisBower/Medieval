#ifndef ANIMATRON_LINEAR_BLEND_SKINNING_INCLUDE
#define ANIMATRON_LINEAR_BLEND_SKINNING_INCLUDE

uniform ByteAddressBuffer _AnimatronSkinMatrices;

float3x4 LoadSkinMatrix(uint index)
{
    uint offset = index * 48;
    // Read in 4 columns of float3 data each.
    // Done in 3 load4 and then repacking into final 3x4 matrix
    // _AnimatronSkinMatrices consists of float32
    float4 p1 = asfloat(_AnimatronSkinMatrices.Load4(offset + 0 * 16));
    float4 p2 = asfloat(_AnimatronSkinMatrices.Load4(offset + 1 * 16));
    float4 p3 = asfloat(_AnimatronSkinMatrices.Load4(offset + 2 * 16));
    return float3x4(p1.x, p1.w, p2.z, p3.y, p1.y, p2.x, p2.w, p3.z, p1.z, p2.y, p3.x, p3.w);
}

void Animatron_LinearBlendSkinning_float(uint skinMatrixIndex, uint4 indices, float4 weights, float3 positionIn, float3 normalIn, float3 tangentIn, out float3 positionOut, out float3 normalOut, out float3 tangentOut)
{
    positionOut = 0;
    normalOut = 0;
    tangentOut = 0;
    for (int i = 0; i < 4; ++i)
    {
        uint skinMatrixIndex = indices[i] + skinMatrixIndex;
        float3x4 skinMatrix = LoadSkinMatrix(skinMatrixIndex);
        float3 vtransformed = mul(skinMatrix, float4(positionIn, 1));
        float3 ntransformed = mul(skinMatrix, float4(normalIn, 0));
        float3 ttransformed = mul(skinMatrix, float4(tangentIn, 0));
        
        positionOut += vtransformed * weights[i];
        normalOut += ntransformed * weights[i];
        tangentOut += ttransformed * weights[i];
    }
}

void Animatron_LinearBlendSkinning_dynamic_float(uint skinMatrixIndex, int blendWeightCount, uint4 indices, float4 weights, float3 positionIn, float3 normalIn, float3 tangentIn, out float3 positionOut, out float3 normalOut, out float3 tangentOut)
{
    positionOut = 0;
    normalOut = 0;
    tangentOut = 0;
    for (int i = 0; i < blendWeightCount; ++i)
    {
        uint skinMatrixIndex = indices[i] + skinMatrixIndex;
        float3x4 skinMatrix = LoadSkinMatrix(skinMatrixIndex);
        float3 vtransformed = mul(skinMatrix, float4(positionIn, 1));
        float3 ntransformed = mul(skinMatrix, float4(normalIn, 0));
        float3 ttransformed = mul(skinMatrix, float4(tangentIn, 0));
        
        positionOut += vtransformed * weights[i];
        normalOut += ntransformed * weights[i];
        tangentOut += ttransformed * weights[i];
    }
}

#endif // ANIMATRON_LINEAR_BLEND_SKINNING_INCLUDE