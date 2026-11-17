// ═══════════════════════════════════════════════════════════════
//  GrassInteraction.hlsl — 草地子集的材质侧采样
//
//  GrassInteractionProcessor 发布：
//    _InteractionGrassTex    压弯量（0..1，R）
//    _InteractionGrassValid  0 = 没有草地子集，绝不产生弯曲
//
//  弯曲方向不存纹理：用压弯量的世界梯度求「远离压点」的方向。
// ═══════════════════════════════════════════════════════════════

#ifndef GRASSINTERACTION_HLSL_INCLUDED
#define GRASSINTERACTION_HLSL_INCLUDED

#include "Assets/Mine/Scripts/InteractionManager/Shaders/InteractionField.hlsl"

TEXTURE2D(_InteractionGrassTex);
SAMPLER(sampler_InteractionGrassTex);
float _InteractionGrassValid;

float SampleGrassCrushWS(float3 positionWS)
{
    float2 uv = InteractionWorldToUV(positionWS);
    float crush = SAMPLE_TEXTURE2D_LOD(_InteractionGrassTex, sampler_InteractionGrassTex, uv, 0).r;
    return saturate(crush * InteractionDomainMask(uv) * _InteractionGrassValid);
}

// 压弯量梯度指向压点中心，取反就是草叶应该倒向的一侧。
float3 GrassBendOffsetWS(float3 positionWS, float heightWeight, float bendStrength, float pressStrength)
{
    float crush = SampleGrassCrushWS(positionWS);
    if (crush <= 0.0)
        return 0.0;

    float2 outward = -InteractionSlopeXZ(_InteractionGrassTex, sampler_InteractionGrassTex, positionWS)
                     * _InteractionGrassValid;
    float lengthSq = dot(outward, outward);
    float3 lean = lengthSq > 1e-10 ? float3(outward.x, 0.0, outward.y) * rsqrt(lengthSq) : float3(0, 0, 0);

    // 叶尖位移最大，根部固定（heightWeight = uv.y）。
    float weight = crush * saturate(heightWeight);
    return (lean * bendStrength + float3(0.0, -1.0, 0.0) * pressStrength) * weight;
}

#endif // GRASSINTERACTION_HLSL_INCLUDED
