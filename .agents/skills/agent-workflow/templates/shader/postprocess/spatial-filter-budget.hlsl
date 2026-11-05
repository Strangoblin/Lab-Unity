// ═══════════════════════════════════════════════════════════════
//  Spatial Filter Budget — quality fixes cost; radius scales coverage.
//
//  Copy beside the effect shader. Define exactly one quality keyword in the
//  owning shader and use FILTER_SAMPLE_COUNT as the fixed loop bound.
// ═══════════════════════════════════════════════════════════════
#ifndef SPATIAL_FILTER_BUDGET_HLSL_INCLUDED
#define SPATIAL_FILTER_BUDGET_HLSL_INCLUDED

#if defined(FILTER_QUALITY_LOW)
    #define FILTER_GRID_WIDTH 3
    #define FILTER_SAMPLE_COUNT 9
#elif defined(FILTER_QUALITY_HIGH)
    #define FILTER_GRID_WIDTH 5
    #define FILTER_SAMPLE_COUNT 25
#else
    #define FILTER_GRID_WIDTH 4
    #define FILTER_SAMPLE_COUNT 16
#endif

float2 SpatialFilter_GetOffset(int sampleIndex, float radius, float2 sourceTexelSize)
{
    int sampleX = sampleIndex % FILTER_GRID_WIDTH;
    int sampleY = sampleIndex / FILTER_GRID_WIDTH;
    float denominator = max((float)(FILTER_GRID_WIDTH - 1), 1.0);
    float2 normalizedOffset = float2(sampleX, sampleY) / denominator * 2.0 - 1.0;
    return normalizedOffset * radius * sourceTexelSize;
}

#endif // SPATIAL_FILTER_BUDGET_HLSL_INCLUDED
