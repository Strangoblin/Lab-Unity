// ═══════════════════════════════════════════════════════════════
//  InteractionField.hlsl — 交互 RT 的共享采样契约
//
//  由 UniversalInteractionManager 发布的全局量：
//    _InteractionOrthoV / _P  正交相机的世界 → 裁剪矩阵
//    _InteractionAreaPos      交互域中心（世界坐标）
//    _InteractionAreaSize     交互域边长（米）
//    _InteractionResolution   RT 分辨率（像素）
//
//  各交互子集（Water / Grass / Snow / Sand）的处理器另外发布自己的输出纹理，
//  但它们共用这里的「世界 → UV」映射与域外遮罩，保证所有表面落在同一块交互域上。
//
//  世界 → RT 的映射与 InteractionDebug.shader 完全一致：
//  同一张高度场既可以直接画热力图，也可以当作位移/法线的高度场。
// ═══════════════════════════════════════════════════════════════

#ifndef INTERACTIONFIELD_HLSL_INCLUDED
#define INTERACTIONFIELD_HLSL_INCLUDED

// ── 水面（兼容旧引用）────────────────────────────────────────
TEXTURE2D(_InteractionWaterTex);
SAMPLER(sampler_InteractionWaterTex);

float4x4 _InteractionOrthoV;
float4x4 _InteractionOrthoP;
float3   _InteractionAreaPos;
float    _InteractionAreaSize;
float    _InteractionResolution;

// ── 世界坐标 → 交互 RT 的 UV ────────────────────────────────
float2 InteractionWorldToUV(float3 positionWS)
{
    float4 clipPos = mul(_InteractionOrthoP, mul(_InteractionOrthoV, float4(positionWS, 1.0)));
    return clipPos.xy / clipPos.w * 0.5 + 0.5;
}

// ── 域边界（软淡出在数据里，这里只做域外拒绝）──────────────
// 边界淡出由各子集的 compute 在数据里完成，统一约定见 InteractionDomainEdge.hlsl：
// 输入先乘窗口、已存的痕迹再按位移吸收，靠近边缘的值本来就收到 0。
// 所以渲染侧这里只需要一个硬的内外判断，避免 RT 是 Clamp 采样、域外把边界那一圈值无限拖出去。
// 不要再在这里叠一层 smoothstep —— 会和数据里的淡出相乘，把淡出带压窄一半。
float InteractionDomainMask(float2 uv)
{
    float2 inside = step(abs(uv - 0.5), 0.5);
    return inside.x * inside.y;
}

// ── 一个 RT 纹素的世界尺寸（米）────────────────────────────
float InteractionTexelWorldSize()
{
    return max(_InteractionAreaSize, 1e-4) / max(_InteractionResolution, 1.0);
}

// ── 遮罩采样（域外为 0）────────────────────────────────────
float InteractionSampleMasked(Texture2D tex, SamplerState smp, float3 positionWS)
{
    float2 uv = InteractionWorldToUV(positionWS);
    float value = SAMPLE_TEXTURE2D_LOD(tex, smp, uv, 0).r;
    return value * InteractionDomainMask(uv);
}

// ── 高度场梯度（世界 XZ 方向的 dH/dx, dH/dz）──────────────
// 返回原始场单位；调用方按自己的位移缩放再乘 heightScale。
float2 InteractionSlopeXZ(Texture2D tex, SamplerState smp, float3 positionWS)
{
    float e = InteractionTexelWorldSize();

    float hL = InteractionSampleMasked(tex, smp, positionWS - float3(e, 0.0, 0.0));
    float hR = InteractionSampleMasked(tex, smp, positionWS + float3(e, 0.0, 0.0));
    float hD = InteractionSampleMasked(tex, smp, positionWS - float3(0.0, 0.0, e));
    float hU = InteractionSampleMasked(tex, smp, positionWS + float3(0.0, 0.0, e));

    return float2(hR - hL, hU - hD) / (2.0 * e);
}

// ── 水面兼容包装 ───────────────────────────────────────────
// heightScale 必须和位移用的缩放一致，法线才和看到的起伏对得上。
float SampleInteractionHeight(float3 positionWS)
{
    return InteractionSampleMasked(_InteractionWaterTex, sampler_InteractionWaterTex, positionWS);
}

float2 InteractionSlopeXZ(float3 positionWS, float heightScale)
{
    return InteractionSlopeXZ(_InteractionWaterTex, sampler_InteractionWaterTex, positionWS) * heightScale;
}

// ── 把坡度叠加到已有法线上 ──────────────────────────────────
// 水面基本水平，法线可以无损还原成坡度再加，避免直接 lerp 两个法线丢长度。
float3 ApplyInteractionSlope(float3 normalWS, float2 slopeXZ, float strength)
{
    float  invY  = 1.0 / max(abs(normalWS.y), 1e-4);
    float2 slope = -normalWS.xz * invY + slopeXZ * strength;
    return normalize(float3(-slope.x, 1.0, -slope.y));
}

#endif // INTERACTIONFIELD_HLSL_INCLUDED
