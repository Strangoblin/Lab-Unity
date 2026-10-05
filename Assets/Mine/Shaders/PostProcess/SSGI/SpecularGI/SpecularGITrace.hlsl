// ═══════════════════════════════════════════════════════════════
//  SpecularGI Trace — geometry-only screen-space intersection contract.
// ═══════════════════════════════════════════════════════════════
#ifndef SPECULAR_GI_TRACE_INCLUDED
#define SPECULAR_GI_TRACE_INCLUDED

struct SpecularTraceResult
{
    float2 uv;
    float distance;
    float confidence;
    bool valid;
};

float SpecularGI_EdgeConfidence(float2 uv)
{
    float2 edge = abs(uv * 2.0 - 1.0);
    return 1.0 - smoothstep(0.80, 1.0, max(edge.x, edge.y));
}

SpecularTraceResult SpecularGI_TraceScreen(
    float3 originWS,
    float3 directionWS,
    float maxDistance,
    float thickness,
    int stepCount)
{
    SpecularTraceResult result = (SpecularTraceResult)0;
    ScreenSpaceTraceHit hit;
    if (!SST_Trace(originWS, directionWS, maxDistance, thickness, stepCount, hit))
        return result;

    float3 hitNormalWS = SampleSceneNormals(hit.uv);
    float facing = saturate(dot(hitNormalWS, -directionWS));
    result.uv = hit.uv;
    result.distance = hit.distance;
    result.confidence = SpecularGI_EdgeConfidence(hit.uv) * smoothstep(0.0, 0.1, facing);
    result.valid = result.confidence > 0.0001;
    return result;
}

#endif
