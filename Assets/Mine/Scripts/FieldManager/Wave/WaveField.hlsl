// ════════════════════════════════════════════════════════════
//  WaveField — shared periodic FFT displacement (metres), normal and foam sampling
//  Legacy texture names are retained; material and Compute shaders use the same contract.
// ════════════════════════════════════════════════════════════
#ifndef WAVEFIELD_HLSL_INCLUDED
#define WAVEFIELD_HLSL_INCLUDED

Texture2D<float4> _WaveDisplacement0;
Texture2D<float4> _WaveDisplacement1;
Texture2D<float4> _WaveDisplacement2;
Texture2D<float4> _WaveNormal0;
Texture2D<float4> _WaveNormal1;
Texture2D<float4> _WaveNormal2;
SamplerState sampler_WaveDisplacement0;
SamplerState sampler_WaveDisplacement1;
SamplerState sampler_WaveDisplacement2;
SamplerState sampler_WaveNormal0;
SamplerState sampler_WaveNormal1;
SamplerState sampler_WaveNormal2;
float _WavePatchSize0, _WavePatchSize1, _WavePatchSize2;
float _WaveFieldValid, _WaveFieldTime, _WaveFieldVersion;

// ════════════════════════════════════════════════════════════
//  SampleWaveDisplacementWS — three periodic cascades; RGB is world-space displacement
// ════════════════════════════════════════════════════════════
float3 SampleWaveDisplacementWS(float3 positionWS)
{
    float3 displacement = _WaveDisplacement0.SampleLevel(sampler_WaveDisplacement0, positionWS.xz / max(_WavePatchSize0, 0.001), 0).rgb;
    displacement += _WaveDisplacement1.SampleLevel(sampler_WaveDisplacement1, positionWS.xz / max(_WavePatchSize1, 0.001), 0).rgb;
    displacement += _WaveDisplacement2.SampleLevel(sampler_WaveDisplacement2, positionWS.xz / max(_WavePatchSize2, 0.001), 0).rgb;
    return displacement * saturate(_WaveFieldValid);
}

// ════════════════════════════════════════════════════════════
//  SampleWaveNormalBlendWS — preserve the original unnormalized cascade blend for Water
// ════════════════════════════════════════════════════════════
float3 SampleWaveNormalBlendWS(float3 positionWS)
{
    float3 n0 = _WaveNormal0.SampleLevel(sampler_WaveNormal0, positionWS.xz / max(_WavePatchSize0, 0.001), 0).rgb * 2.0 - 1.0;
    float3 n1 = _WaveNormal1.SampleLevel(sampler_WaveNormal1, positionWS.xz / max(_WavePatchSize1, 0.001), 0).rgb * 2.0 - 1.0;
    float3 n2 = _WaveNormal2.SampleLevel(sampler_WaveNormal2, positionWS.xz / max(_WavePatchSize2, 0.001), 0).rgb * 2.0 - 1.0;
    return lerp(float3(0, 1, 0), n0 * 0.6 + n1 * 0.3 + n2 * 0.1, saturate(_WaveFieldValid));
}

// ════════════════════════════════════════════════════════════
//  SampleWaveNormalWS — normalized normal with a flat-up fallback
// ════════════════════════════════════════════════════════════
float3 SampleWaveNormalWS(float3 positionWS)
{
    float3 normal = SampleWaveNormalBlendWS(positionWS);
    float lengthSq = dot(normal, normal);
    return lengthSq > 1e-8 ? normal * rsqrt(max(lengthSq, 1e-8)) : float3(0, 1, 0);
}

// ════════════════════════════════════════════════════════════
//  SampleWaveFoam — preserve the original 0.5/0.3/0.2 weights
// ════════════════════════════════════════════════════════════
float SampleWaveFoam(float3 positionWS)
{
    float f0 = _WaveDisplacement0.SampleLevel(sampler_WaveDisplacement0, positionWS.xz / max(_WavePatchSize0, 0.001), 0).a;
    float f1 = _WaveDisplacement1.SampleLevel(sampler_WaveDisplacement1, positionWS.xz / max(_WavePatchSize1, 0.001), 0).a;
    float f2 = _WaveDisplacement2.SampleLevel(sampler_WaveDisplacement2, positionWS.xz / max(_WavePatchSize2, 0.001), 0).a;
    return (f0 * 0.5 + f1 * 0.3 + f2 * 0.2) * saturate(_WaveFieldValid);
}
#endif
