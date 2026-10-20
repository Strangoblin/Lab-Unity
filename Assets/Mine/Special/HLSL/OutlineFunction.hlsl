// ════════════════════════════════════════════════════════════
//  OutlineFunction — 世界空间 / 屏幕空间法线外扩描边
//  调用方需先 include URP Core.hlsl（TransformWorldToHClip、_ScreenParams）。
//  零 CBUFFER、零兄弟 include；屏幕空间版按像素宽度保持描边粗细。
//  迁入来源：CelToon/Assets/Mine/Special/HLSL/OutlineFunction.hlsl。
// ════════════════════════════════════════════════════════════
#ifndef OUTLINEFUNCTION_HLSL_INCLUDED
#define OUTLINEFUNCTION_HLSL_INCLUDED

// ════════════════════════════════════════════════════════════
//  ComputeOutlineWS — 世界空间法线外扩，宽度随相机距离缩放
// ════════════════════════════════════════════════════════════
float3 ComputeOutlineWS(float3 normalWS, float outlineScaleWS)
{
    return normalWS * outlineScaleWS;
}

// ════════════════════════════════════════════════════════════
//  ComputeOutlineCS — 屏幕空间法线外扩，按屏幕像素宽度保持描边
// ════════════════════════════════════════════════════════════
float4 ComputeOutlineCS(float3 posWS, float4 posCS, float3 normalWS, float outlineScaleCS)
{
    float2 screenSize = float2(_ScreenParams.x, _ScreenParams.y);
    float uvRange = outlineScaleCS;
    float pixelsX = abs(uvRange) * screenSize.x;
    float pixelsY = abs(uvRange) * screenSize.y;
    float pixels = min(pixelsX, pixelsY);

    float3 posWS2 = posWS + normalWS * 0.01;
    float4 posCS2 = TransformWorldToHClip(posWS2);

    float2 ndc = posCS.xy / posCS.w;
    float2 ndc2 = posCS2.xy / posCS2.w;
    float2 ndcDelta = ndc2 - ndc;
    float ndcLength = length(ndcDelta);
    if (ndcLength < 1e-6)
    {
        return posCS;
    }

    float2 ndcDir = ndcDelta / ndcLength;
    float2 dNDC = ndcDir * (2.0 * pixels / screenSize);

    posCS.xy += dNDC * posCS.w;
    return posCS;
}

#endif
