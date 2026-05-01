#ifndef STYLIZED_TOON_LIT_ANIMATRON_META_PASSES_INCLUDED
#define STYLIZED_TOON_LIT_ANIMATRON_META_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#if defined(LOD_FADE_CROSSFADE)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

float3 _LightDirection;
float3 _LightPosition;

struct AnimatronMetaAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    uint4 skinIndices : BLENDINDICES0;
    float4 skinWeights : BLENDWEIGHTS0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct MetaShadowVaryings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float4 AnimatronGetShadowPositionHClip(AnimatronMetaAttributes input)
{
    uint skinMatrixIndex = (uint)asint(UNITY_ACCESS_DOTS_INSTANCED_PROP(float, _SkinMatrixIndex));
    float3 skinnedPosOS, skinnedNormalOS, skinnedTangentOS;
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

    float3 positionWS = TransformObjectToWorld(skinnedPosOS);
    float3 normalWS = TransformObjectToWorldNormal(skinnedNormalOS);

#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    positionCS = ApplyShadowClamping(positionCS);
    return positionCS;
}

MetaShadowVaryings ShadowPassVertex(AnimatronMetaAttributes input)
{
    MetaShadowVaryings output = (MetaShadowVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    output.positionCS = AnimatronGetShadowPositionHClip(input);
    return output;
}

half4 ShadowPassFragment(MetaShadowVaryings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif
    return 0;
}

struct MetaDepthNormalsVaryings
{
    float4 positionCS : SV_POSITION;
    half3 normalWS : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

MetaDepthNormalsVaryings DepthNormalsVertex(AnimatronMetaAttributes input)
{
    MetaDepthNormalsVaryings output = (MetaDepthNormalsVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    uint skinMatrixIndex = (uint)asint(UNITY_ACCESS_DOTS_INSTANCED_PROP(float, _SkinMatrixIndex));
    float3 skinnedPosOS, skinnedNormalOS, skinnedTangentOS;
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
    output.positionCS = vertexInput.positionCS;
    output.normalWS = NormalizeNormalPerVertex(normalInput.normalWS);
    return output;
}

void DepthNormalsFragment(
    MetaDepthNormalsVaryings input,
    out half4 outNormalWS : SV_Target0)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif

    outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
}

struct MetaDepthOnlyVaryings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

MetaDepthOnlyVaryings DepthOnlyVertex(AnimatronMetaAttributes input)
{
    MetaDepthOnlyVaryings output = (MetaDepthOnlyVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    uint skinMatrixIndex = (uint)asint(UNITY_ACCESS_DOTS_INSTANCED_PROP(float, _SkinMatrixIndex));
    float3 skinnedPosOS, skinnedNormalOS, skinnedTangentOS;
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

    output.positionCS = TransformObjectToHClip(skinnedPosOS);
    return output;
}

half DepthOnlyFragment(MetaDepthOnlyVaryings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif

    return input.positionCS.z;
}

#endif
