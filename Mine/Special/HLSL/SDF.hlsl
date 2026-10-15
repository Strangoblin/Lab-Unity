//SDF.hlsl
#ifndef SDF_HLSL_INCLUDED
#define SDF_HLSL_INCLUDED

float SphereSDF(float3 position, float radius)
{
    return length(position) - radius;
}

float BoxSDF(float3 position, float3 bounds)
{
    float3 d = abs(position) - bounds;
    return length(max(d,0.0)) + min(max(d.x,max(d.y,d.z)),0.0);
}

float opSmoothUnion(float d1, float d2, float k)
{
    float h = max(k - abs(d1 - d2), 0.0);
    return min(d1, d2) - h * h * 0.25 / k;
}

float opSmoothSubtraction(float d1, float d2, float k)
{
    float h = max(k - abs(-d1 - d2), 0.0);
    return max(-d1, d2) + h * h * 0.25 / k;
}

float opSmoothIntersection(float d1, float d2, float k)
{
    float h = max(k - abs(d1 - d2), 0.0);
    return max(d1, d2) + h * h * 0.25 / k;
}
#endif