// ═══════════════════════════════════════════════════════════
//  TemporalFunction.hlsl — 时域累积共享函数库
//
//  定位: 跨效果横切复用 → Assets/Mine/Special/HLSL/ 家族
//  档位: A 档（纯函数）—— 零 CBUFFER、零兄弟 include。
//        深度线性化与世界坐标重建留在调用方，库只收已线性化的
//        标量/向量，避免 compute 侧 include Core.hlsl 的顺序地雷。
//
//  调用方 include（Assets 全路径）:
//    #include "Assets/Mine/Special/HLSL/TemporalFunction.hlsl"
//
//  调用序列（历史 RT 的 ping-pong 与有效性标记由调用方负责）:
//    1. Temporal_ReprojectUV(P, prevViewProj, histUV)   → 上一帧 UV
//    2. Temporal_Confidence(...)                        → [0,1] 置信度
//    3. Temporal_ClampNeighborhood / ClampVariance      → 约束历史
//    4. Temporal_Blend(current, history, conf, blend)   → 输出
//
//  注: 本库只做「时间换方差」的收敛，不产生独立信号——调用方必须
//      让当帧采样逐帧去相关（如逐帧旋转采样核），否则累积的是同一
//      份偏差，不收敛。
// ═══════════════════════════════════════════════════════════

#ifndef TEMPORALFUNCTION_HLSL_INCLUDED
#define TEMPORALFUNCTION_HLSL_INCLUDED

// ════════════════════════════════════════════════════════════
//  Temporal_ReprojectUV — 世界位置投影到上一帧 UV
//
//  prevViewProj 需为 Unity/GL 约定的 proj * view（即
//  Camera.projectionMatrix * Camera.worldToCameraMatrix），不要传
//  GL.GetGPUProjectionMatrix 的结果。函数内部按 UNITY_UV_STARTS_AT_TOP
//  做 Y 翻转，与调用方主 pass 的 UV 约定对齐。
//  投影落在相机后方或 UV 越界返回 false。
// ════════════════════════════════════════════════════════════
bool Temporal_ReprojectUV(float3 positionWS, float4x4 prevViewProj, out float2 histUV)
{
    histUV = 0;

    float4 clip = mul(prevViewProj, float4(positionWS, 1.0));
    if (clip.w <= 1e-6) return false;

    float2 ndc = clip.xy / clip.w;
    histUV = ndc * 0.5 + 0.5;

#if UNITY_UV_STARTS_AT_TOP
    histUV.y = 1.0 - histUV.y;
#endif

    return histUV.x >= 0.0 && histUV.x <= 1.0
        && histUV.y >= 0.0 && histUV.y <= 1.0;
}

// ════════════════════════════════════════════════════════════
//  Temporal_Confidence — 深度 + 法线双重衰减 → [0,1] 置信度
//
//  curEyeDepth / histEyeDepth 单位需一致（建议世界单位米）；depthScale
//  越大越严格，= 10 时 0.1m 误差衰减到 0.37。normalPower 为法线点积的
//  幂次，越大越严格；<= 0 视作关闭法线校验，退化为纯深度校验。
//  histNormal 全零（未写入法线的像素）时法线项返回 0，置信度为 0。
// ════════════════════════════════════════════════════════════
float Temporal_Confidence(float curEyeDepth, float histEyeDepth,
                          float3 curNormal, float3 histNormal,
                          float depthScale, float normalPower)
{
    float depthW = exp(-abs(curEyeDepth - histEyeDepth) * depthScale);

    float normalW = 1.0;
    if (normalPower > 0.0)
        normalW = pow(saturate(dot(curNormal, histNormal)), normalPower);

    return saturate(depthW * normalW);
}

// ════════════════════════════════════════════════════════════
//  Temporal_ClampNeighborhood — 当帧邻域盒式钳制
//
//  curTex/curSampler 为当帧信号；texelSize 为 UV 空间单像素步长；
//  radius 为方形邻域半径（1 = 3×3，2 = 5×5）。
//  邻域噪声越去相关，包围盒越宽、钳制越宽松——不会与累积本身对抗。
// ════════════════════════════════════════════════════════════
float3 Temporal_ClampNeighborhood(Texture2D curTex, SamplerState curSampler,
                                  float2 uv, float2 texelSize, float3 history, int radius)
{
    float3 lo = 1e9;
    float3 hi = -1e9;

    for (int y = -radius; y <= radius; y++)
    {
        for (int x = -radius; x <= radius; x++)
        {
            float3 c = curTex.SampleLevel(curSampler, uv + float2(x, y) * texelSize, 0).rgb;
            lo = min(lo, c);
            hi = max(hi, c);
        }
    }

    return clamp(history, lo, hi);
}

// ════════════════════════════════════════════════════════════
//  Temporal_ClampVariance — 邻域一阶/二阶矩构造包围盒钳制
//
//  sigmaScale 为包围盒半宽（单位：邻域标准差），常用 1.0 ~ 2.5。
//  盒式钳制对孤立离群点敏感，方差钳制用矩统计更稳，代价是多一次乘加。
// ════════════════════════════════════════════════════════════
float3 Temporal_ClampVariance(Texture2D curTex, SamplerState curSampler,
                              float2 uv, float2 texelSize, float3 history,
                              int radius, float sigmaScale)
{
    float3 m1 = 0;
    float3 m2 = 0;
    float  n  = 0;

    for (int y = -radius; y <= radius; y++)
    {
        for (int x = -radius; x <= radius; x++)
        {
            float3 c = curTex.SampleLevel(curSampler, uv + float2(x, y) * texelSize, 0).rgb;
            m1 += c;
            m2 += c * c;
            n  += 1.0;
        }
    }

    m1 /= n;
    m2 /= n;

    float3 sigma = sqrt(max(m2 - m1 * m1, 0));
    return clamp(history, m1 - sigmaScale * sigma, m1 + sigmaScale * sigma);
}

// ════════════════════════════════════════════════════════════
//  Temporal_Blend — confidence 与 blend 合成权重后混合
//
//  等价于「指数移动平均」：blend=0.9 时有效样本数约 1/(1-0.9) = 10 帧，
//  提高 blend 的收益很快被延迟与拖影吃回去。
// ════════════════════════════════════════════════════════════
float3 Temporal_Blend(float3 current, float3 history, float confidence, float blend)
{
    return lerp(current, history, saturate(confidence) * saturate(blend));
}

#endif // TEMPORALFUNCTION_HLSL_INCLUDED
