// ═══════════════════════════════════════════════════════════════
//  InteractionParallax.hlsl — 用视差映射（POM）把交互高度场"看出来"
//
//  为什么这里适合做视差映射：
//    · 表面是**平整的水平面**，TBN 就是世界的 XZ，不需要切线属性；
//    · 高度场本来就是每帧算出来的有符号高度，不用额外烘焙；
//    · 域 12 m / 256² = 4.69 cm 一个纹素，而起伏只有 0.22–0.32 m，
//      **纵向只有 5–7 个纹素**——这是 POM 最省的工况，步进几步就收敛，
//      也不会出现常见的大位移台阶伪影。
//
//  它的边界（不是 bug）：
//    · 只表达**平面以下**的深度。沙的坑取 saturate(-h)，雪的压痕取 saturate(h)；
//      沙的边脊在平面上方，POM 表达不了，仍然交给法线。
//    · 只改采样位置，不改顶点、也不改深度：**轮廓不动**，所以它治的是
//      "贴纸感"（内部细节不会跟着视角滑），不是几何。
//
//  做法：沿视线在 uv 上步进，比较"射线相对平面的深度"与"高度场的深度"，
//  第一次穿过去后二分一次；采样一律复用 InteractionWorldToUV 的同一套映射。
// ═══════════════════════════════════════════════════════════════

#ifndef INTERACTIONPARALLAX_HLSL_INCLUDED
#define INTERACTIONPARALLAX_HLSL_INCLUDED

#include "Assets/Mine/Scripts/InteractionManager/Shaders/InteractionField.hlsl"

// ── 世界位移 → uv 位移（正交：w 恒为 1，所以是纯线性）────────
float2 InteractionWorldToUVLinear(float3 dWorld)
{
    float4 clip = mul(_InteractionOrthoP, mul(_InteractionOrthoV, float4(dWorld, 0.0)));
    return clip.xy * 0.5;
}

// ── 视差偏移：返回世界空间的 XZ 偏移（y 恒为 0）───────────────
//  sign  = -1：沙（负值 = 坑）；+1：雪（正值 = 压痕）
//  steps = 0 关闭
//  返回值直接加在 positionWS 上再做高度场采样，调用方的其余代码不用改。
float3 InteractionParallaxOffsetWS(Texture2D tex, SamplerState smp, float3 positionWS,
                                   float3 viewWS, float depthScale, float sign,
                                   int steps, float valid)
{
    if (steps <= 0 || depthScale <= 1e-4 || valid <= 0.0)
        return float3(0.0, 0.0, 0.0);

    float2 uv0 = InteractionWorldToUV(positionWS);
    float  d0  = saturate(SAMPLE_TEXTURE2D_LOD(tex, smp, uv0, 0).r * sign);
    // 平坦处一次采样就退出：绝大多数像素都不该为这个功能付循环的钱。
    if (d0 < 0.02)
        return float3(0.0, 0.0, 0.0);

    float3 V  = normalize(viewWS);
    float  vY = max(abs(V.y), 0.20);              // 掠射时钳住，否则 tMax 发散
    float  tMax = depthScale / vY;

    float prevT = 0.0;
    float tHit  = 0.0;
    bool  hit   = false;

    [loop]
    for (int i = 1; i <= steps; i++)
    {
        float t = tMax * (float)i / (float)steps;

        float3 probe = positionWS + float3(-V.x, 0.0, -V.z) * t;
        float2 uv    = InteractionWorldToUV(probe);
        float  d     = saturate(SAMPLE_TEXTURE2D_LOD(tex, smp, uv, 0).r * sign)
                       * InteractionDomainMask(uv) * depthScale;
        float  ray   = t * vY;                    // 射线此刻离平面多深

        if (ray >= d)
        {
            // 二分一次，把台阶感压掉一半（多余一次采样，值得）。
            float tMid = 0.5 * (prevT + t);
            float3 mid = positionWS + float3(-V.x, 0.0, -V.z) * tMid;
            float2 uvM = InteractionWorldToUV(mid);
            float  dM  = saturate(SAMPLE_TEXTURE2D_LOD(tex, smp, uvM, 0).r * sign)
                         * InteractionDomainMask(uvM) * depthScale;
            tHit = (tMid * vY >= dM) ? tMid : t;
            hit  = true;
            break;
        }
        prevT = t;
    }

    if (!hit)
        return float3(0.0, 0.0, 0.0);

    return float3(-V.x, 0.0, -V.z) * tHit;
}

#endif // INTERACTIONPARALLAX_HLSL_INCLUDED
