// ════════════════════════════════════════════════════════════
//  WindField — shared 2D + height sampling, world-space velocity in m/s
//  Self-declared global resources; usable from material and Compute shaders.
// ════════════════════════════════════════════════════════════
#ifndef WINDFIELD_HLSL_INCLUDED
#define WINDFIELD_HLSL_INCLUDED

Texture2D<float4> _WindFieldTexture;
SamplerState sampler_WindFieldTexture;
float4 _WindFieldWorldToUV;
float4 _WindFieldHeight;
float4 _WindFieldBaseVelocity;
float _WindFieldValid;
float _WindFieldTime;
float _WindFieldVersion;

// ════════════════════════════════════════════════════════════
//  WindFieldHeightFactor — reference height, transition range, lower/upper gains
// ════════════════════════════════════════════════════════════
float WindFieldHeightFactor(float heightWS)
{
    float height = saturate((heightWS - _WindFieldHeight.x) / max(_WindFieldHeight.y, 0.001));
    height = height * height * (3.0 - 2.0 * height);
    return lerp(_WindFieldHeight.z, _WindFieldHeight.w, height);
}

// ════════════════════════════════════════════════════════════
//  SampleWindVelocityWS — outside the finite field, fall back to base wind
// ════════════════════════════════════════════════════════════
float3 SampleWindVelocityWS(float3 positionWS)
{
    float2 uv = (positionWS.xz - _WindFieldWorldToUV.xy) * _WindFieldWorldToUV.zw;
    float edge = min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y));
    float coverage = smoothstep(0.0, 0.05, edge);
    float3 localVelocity = _WindFieldTexture.SampleLevel(sampler_WindFieldTexture, saturate(uv), 0).xyz;
    float3 velocity = lerp(_WindFieldBaseVelocity.xyz, localVelocity, coverage);
    return velocity * WindFieldHeightFactor(positionWS.y) * saturate(_WindFieldValid);
}
#endif
