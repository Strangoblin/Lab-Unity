// ═══════════════════════════════════════════════════════════════
//  SnowInteraction.hlsl — 雪地子集的材质侧采样
//
//  SnowInteractionProcessor 发布：
//    _InteractionSnowTex    压痕深度（0..1，R）
//    _InteractionSnowValid  0 = 没有雪地子集
// ═══════════════════════════════════════════════════════════════

#ifndef SNOWINTERACTION_HLSL_INCLUDED
#define SNOWINTERACTION_HLSL_INCLUDED

#include "Assets/Mine/Scripts/InteractionManager/Shaders/InteractionField.hlsl"

TEXTURE2D(_InteractionSnowTex);
SAMPLER(sampler_InteractionSnowTex);
float _InteractionSnowValid;

float SampleSnowImprintWS(float3 positionWS)
{
    return saturate(InteractionSampleMasked(_InteractionSnowTex, sampler_InteractionSnowTex, positionWS)
                    * _InteractionSnowValid);
}

float2 SnowImprintSlopeXZ(float3 positionWS)
{
    return InteractionSlopeXZ(_InteractionSnowTex, sampler_InteractionSnowTex, positionWS)
           * _InteractionSnowValid;
}

#endif // SNOWINTERACTION_HLSL_INCLUDED
