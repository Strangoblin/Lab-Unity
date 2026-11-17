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
    // 薄壳（_CameraNormalsTexture.a == -1：草、植被、双面卡片）的法线不是几何朝向，
    // 背面判定会把整片叶子判成假命中；这些像素不接受朝向衰减，只保留屏幕边缘衰减。
    float facing = SST_HitSurfaceIsClosed(hit.uv)
        ? saturate(dot(hitNormalWS, -directionWS)) : 1.0;
    result.uv = hit.uv;
    result.distance = hit.distance;
    result.confidence = SpecularGI_EdgeConfidence(hit.uv) * smoothstep(0.0, 0.1, facing);
    result.valid = result.confidence > 0.0001;
    return result;
}

#endif
