//OutlineFunction.hlsl
#ifndef OUTLINEFUNCTION_HLSL_INCLUDED
#define OUTLINEFUNCTION_HLSL_INCLUDED

float3 ComputeOutlineWS(float3 normalWS, float outlineScaleWS)
{
    return normalWS * outlineScaleWS;
}

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
    float2 ndcDir = normalize(ndc2 - ndc);
    float2 dNDC = ndcDir * (2.0 * pixels / screenSize);

    posCS.xy += dNDC * posCS.w;
    return posCS;
}

#endif