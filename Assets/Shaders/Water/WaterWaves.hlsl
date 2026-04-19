#ifndef WATER_WAVES_INCLUDED
#define WATER_WAVES_INCLUDED

// Animated height field in world XZ; displacement along world +Y. Requires WaterLitInput CBUFFER (_Wave* uniforms).

void WaterEvaluateWaves(float2 xz, float waveTime, out float height, out float dhdx, out float dhdz)
{
    // Two crossed directional waves for less repetitive motion than a single sine.
    const float2 dir1 = normalize(float2(1.0, 0.35));
    const float2 dir2 = normalize(float2(-0.52, 0.85));
    const float k2Scale = 1.65;
    const float phase2 = 1.08;

    float k1 = _WaveFrequency;
    float k2 = _WaveFrequency * k2Scale;

    float w1 = dot(xz, dir1) * k1 + waveTime;
    float w2 = dot(xz, dir2) * k2 + waveTime * phase2;

    height = _WaveAmplitude * (sin(w1) + _WaveSecondaryAmp * sin(w2));

    dhdx = _WaveAmplitude * (k1 * dir1.x * cos(w1) + _WaveSecondaryAmp * k2 * dir2.x * cos(w2));
    dhdz = _WaveAmplitude * (k1 * dir1.y * cos(w1) + _WaveSecondaryAmp * k2 * dir2.y * cos(w2));
}

void ApplyWaterWavesWorldSpaceAtTime(inout float3 positionWS, out float3 waveNormalWS, float timeSeconds)
{
    float waveTime = timeSeconds * _WaveSpeed;
    float h, dhdx, dhdz;
    WaterEvaluateWaves(positionWS.xz, waveTime, h, dhdx, dhdz);
    positionWS.y += h;
    waveNormalWS = normalize(float3(-dhdx, 1.0, -dhdz));
}

void ApplyWaterWavesWorldSpace(inout float3 positionWS, out float3 waveNormalWS)
{
    ApplyWaterWavesWorldSpaceAtTime(positionWS, waveNormalWS, _Time.y);
}

float3 ApplyWaterWavesObjectSpace(float3 positionOS)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 waveNormalWS;
    ApplyWaterWavesWorldSpace(positionWS, waveNormalWS);
    return TransformWorldToObject(positionWS);
}

#endif // WATER_WAVES_INCLUDED
