//ProjectionFunction.hlsl
#ifndef PROJECTIONFUNCTION_HLSL_INCLUDED
#define PROJECTIONFUNCTION_HLSL_INCLUDED

//direction weight function
float3 DirectionWeight(float3 normalWS, float sharpness)
{
    //sharpness is recommonded to be about 100
    float3 a = pow(abs(normalWS), sharpness);
    float  b = dot(a, float3(1.0, 1.0, 1.0));
    float3 weight = round(a / b);
    return weight;
}

float2 LowCostTriplanarProjection(float3 normalWS, float3 positionWS, float sharpness, float scale)
{
    float3 weight = DirectionWeight(normalWS, sharpness);
    float3 positionUV = positionWS * scale;
    float2 uvX = positionUV.yz * weight.x;
    float2 uvY = positionUV.zx * weight.y;
    float2 uvZ = positionUV.xy * weight.z;
    float2 uv = uvX + uvY + uvZ;
    return uv;
}

#endif