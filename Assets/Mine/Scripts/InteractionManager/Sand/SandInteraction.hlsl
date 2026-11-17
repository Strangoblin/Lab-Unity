// ═══════════════════════════════════════════════════════════════
//  SandInteraction.hlsl — 沙地子集的材质侧采样
//
//  SandInteractionProcessor 发布：
//    _InteractionSandTex    有符号沙面高度（负 = 坑，R）
//    _InteractionSandValid  0 = 没有沙地子集
// ═══════════════════════════════════════════════════════════════

#ifndef SANDINTERACTION_HLSL_INCLUDED
#define SANDINTERACTION_HLSL_INCLUDED

#include "Assets/Mine/Scripts/InteractionManager/Shaders/InteractionField.hlsl"

TEXTURE2D(_InteractionSandTex);
SAMPLER(sampler_InteractionSandTex);
float _InteractionSandValid;

float SampleSandHeightWS(float3 positionWS)
{
    return InteractionSampleMasked(_InteractionSandTex, sampler_InteractionSandTex, positionWS)
           * _InteractionSandValid;
}

float2 SandHeightSlopeXZ(float3 positionWS)
{
    return InteractionSlopeXZ(_InteractionSandTex, sampler_InteractionSandTex, positionWS)
           * _InteractionSandValid;
}

#endif // SANDINTERACTION_HLSL_INCLUDED
