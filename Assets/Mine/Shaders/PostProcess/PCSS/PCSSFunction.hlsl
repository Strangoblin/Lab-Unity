// ════════════════════════════════════════════════════════════
//  PCSS Compute Shader — 共享声明与工具函数
//  被 PCSS.compute #include 使用
// ════════════════════════════════════════════════════════════

// 时域累积共享库（跨效果横切，Assets 全路径）
#include "Assets/Mine/Special/HLSL/TemporalFunction.hlsl"

// ── 质量档位（编译期固定采样数，编译器可展开循环）──
// 未定义 = High: 32, PCSS_MEDIUM: 16, PCSS_LOW: 8
// 关键词由 PCSSFeature 按性能档位下发（PerformanceTier.samples）
#if defined(PCSS_LOW)
    #define BLOCKER_SAMPLES 8
    #define PCF_SAMPLES 8
#elif defined(PCSS_MEDIUM)
    #define BLOCKER_SAMPLES 16
    #define PCF_SAMPLES 16
#else
    #define BLOCKER_SAMPLES 32
    #define PCF_SAMPLES 32
#endif

#define NUMTHREAD_X 8
#define NUMTHREAD_Y 8

// ── 输入纹理 ──
Texture2D<float>   _CameraDepthTexture;
Texture2D<float4>  _CameraNormalsTexture;
Texture2D<float>   _PCSS_ShadowCacheTex;

// ── 输出 ──
RWTexture2D<float4> _PCSS_SoftShadow;

// ── 双边模糊 I/O ──
Texture2D<float4>   _PCSS_BlurInput;
RWTexture2D<float4> _PCSS_BlurOutput;
float _BlurScale;

// ── 时域累积 I/O ──
// _PCSS_CurrentTex 是 _PCSS_SoftShadow 的 SRV 视图（同一次 dispatch 内
// 不可既是 SRV 又是 UAV，故当帧信号单独绑定一份只读句柄）
Texture2D<float4>   _PCSS_CurrentTex;
Texture2D<float4>   _PCSS_HistoryInput;
RWTexture2D<float4> _PCSS_TemporalOut;
RWTexture2D<float4> _PCSS_HistoryOutput;
float4x4 _PrevViewProj;
float _FrameIndex;
float _TemporalBlend;
float _TemporalReset;
int   _TemporalDebug;

// ── 时域常量 ──
// 这四项原为可调参数，参数整理后固定为验证过的默认配置：钳制越紧越不易
// 拖影、越松越稳；深度/法线置信度决定几何变化时多快丢弃历史。
// 需要重新暴露为美术参数、或换回邻域盒式钳制时改这里——盒式钳制的
// Temporal_ClampNeighborhood 仍保留在 TemporalFunction.hlsl 中。
#define TEMPORAL_CLAMP_RADIUS 2        // 钳制邻域半径（像素）
#define TEMPORAL_CLAMP_SIGMA  1.5      // 方差包围盒半宽（σ 倍数）
#define TEMPORAL_DEPTH_SCALE  10.0     // 深度置信度衰减（世界单位）
#define TEMPORAL_NORMAL_POWER 8.0      // 法线一致性幂次

SamplerState PointClampSampler;
SamplerState LinearClampSampler;

// ── 屏幕 & 相机 ──
// _ScreenSize.xy = 目标尺寸，.zw = 1/尺寸（texel size）——C# 侧按 Vector4 绑定
float4   _ScreenSize;
float4   _FrustumRay0, _FrustumRay1, _FrustumRay2, _FrustumRay3;
float4   _ZBufferParams;
float3   _WorldSpaceCameraPos;

// ── Atlas 尺寸（2×2 布局）──
// .x = tile 分辨率, .y = 1/tile, .z = atlas 分辨率(2×tile), .w = 1/atlas
//
// 本文件与 PCSS.compute 里所有 texel↔UV 换算原先把 512/1024/2048 写死。
// 那是「参数半接线」：C# 侧 PCSSFeature 用 shadowMapResolution 决定 RT 分配、
// 烘焙 _CascadeLightVP、设 viewport，shader 侧却读不到它——默认 2048 下数值
// 恰好全对，所以缺陷长期不可见；分辨率一旦可调就是必现的画面错乱。
// 现由 PCSSFeature 按性能档位下发，档位表见 PCSS.md。
float4   _PCSS_AtlasParams;

// ── 级联 ──
float4x4 _CascadeLightVP[4];
float4   _CascadeAtlasOffset[4];
int      _CascadeCount;
float4   _CascadeSplits;
float4   _CascadeHalfWidth;
float4   _CascadeZDistance;

// ── PCSS 参数 ──
// _PCSS_Softness 是半影尺度（物理上即光源尺寸）：0 = 硬边，越大阴影越软。
// 注意与旧命名的区别——旧的 _PCSS_Softness 是 PCF 深度偏移的乘数，
// 该乘数现已固定为 1.0（见 PCSS.compute 的 pcfBias 计算）。
float  _PCSS_Softness;
float3 _LightDirection;

// ════════════════════════════════════════════════════════════
//  Vogel disk（32 samples）
// ════════════════════════════════════════════════════════════
static const float2 VogelDisk[32] = {
    float2( 0.0284,  0.1087), float2( 0.1766,  0.0676), float2(-0.1744,  0.1780), float2( 0.0210, -0.2479),
    float2(-0.2168, -0.1762), float2(-0.0368,  0.3060), float2( 0.1458, -0.2791), float2( 0.2927,  0.0888),
    float2(-0.3105,  0.0397), float2( 0.1648,  0.3230), float2(-0.0347, -0.4025), float2(-0.2360, -0.3356),
    float2(-0.4092, -0.0566), float2(-0.2305,  0.3875), float2( 0.1669, -0.4405), float2( 0.3942, -0.2468),
    float2(-0.3678, -0.2440), float2( 0.3264,  0.2472), float2(-0.3033,  0.3505), float2( 0.0359, -0.5256),
    float2( 0.4130, -0.4186), float2(-0.3752, -0.3988), float2( 0.1697,  0.4868), float2( 0.3826,  0.3261),
    float2(-0.4990, -0.1348), float2( 0.4773, -0.1076), float2(-0.2348, -0.5315), float2(-0.3548,  0.4740),
    float2( 0.4941, -0.2563), float2(-0.4674, -0.3919), float2(-0.0510,  0.5771), float2( 0.2900, -0.5407)
};

// ── 双边模糊常量 ──
static const float Gauss5[5] = { 0.1216, 0.2332, 0.2910, 0.2332, 0.1216 };
static const int   BlurOff[5] = { -2, -1, 0, 1, 2 };
static const float BlurDepthSens = 10.0;

// ════════════════════════════════════════════════════════════
//  通用工具
// ════════════════════════════════════════════════════════════

float Random1D(float2 seed) { return frac(sin(dot(seed, float2(12.9898, 78.233))) * 43758.5453); }
float2 RotateVector(float2 v, float angle) { float s = sin(angle), c = cos(angle); return float2(v.x * c - v.y * s, v.x * s + v.y * c); }

bool IsSkybox(float rawDepth)
{
#if UNITY_REVERSED_Z
    return rawDepth <= 0.0001;
#else
    return rawDepth >= 0.9999;
#endif
}

float LinearEyeDepthCS(float rawDepth)
{
    return 1.0 / (_ZBufferParams.x * rawDepth + _ZBufferParams.y);
}

// ════════════════════════════════════════════════════════════
//  BilinearSampleAtlas — 手动双线性采样 + tile 边界 clamp
// ════════════════════════════════════════════════════════════
float BilinearSampleAtlas(float2 atlasUV, float2 tileMin, float2 tileMax)
{
    float texelSize = _PCSS_AtlasParams.w;
    float halfTexel = 0.5 * texelSize;
    float2 uv = clamp(atlasUV, tileMin + halfTexel, tileMax - halfTexel);
    float2 coord = uv / texelSize - 0.5;
    float2 f = frac(coord);
    int2 base = int2(coord);
    float s00 = _PCSS_ShadowCacheTex.Load(int3(base.x,     base.y,     0));
    float s10 = _PCSS_ShadowCacheTex.Load(int3(base.x + 1, base.y,     0));
    float s01 = _PCSS_ShadowCacheTex.Load(int3(base.x,     base.y + 1, 0));
    float s11 = _PCSS_ShadowCacheTex.Load(int3(base.x + 1, base.y + 1, 0));
    return lerp(lerp(s00, s10, f.x), lerp(s01, s11, f.x), f.y);
}

// ════════════════════════════════════════════════════════════
//  ReconstructWorldPosition — Frustum Corner Ray 世界坐标重建
// ════════════════════════════════════════════════════════════
float3 ReconstructWorldPosition(float2 screenUV, float rawDepth)
{
    float linear01 = 1.0 / (_ZBufferParams.x * rawDepth + _ZBufferParams.y);
#if UNITY_UV_STARTS_AT_TOP
    float vpY = 1.0 - screenUV.y;
#else
    float vpY = screenUV.y;
#endif
    float3 rayBL = _FrustumRay0.xyz, rayBR = _FrustumRay1.xyz;
    float3 rayTL = _FrustumRay2.xyz, rayTR = _FrustumRay3.xyz;
    float3 ray = lerp(lerp(rayBL, rayBR, screenUV.x), lerp(rayTL, rayTR, screenUV.x), vpY);
    return _WorldSpaceCameraPos + ray * linear01;
}

// ════════════════════════════════════════════════════════════
//  SelectCascade — 按世界空间距离选择级联
// ════════════════════════════════════════════════════════════
int SelectCascade(float3 positionWS)
{
    float d = distance(positionWS, _WorldSpaceCameraPos);
    int ci = 0;
    if (d > _CascadeSplits.x) ci = 1;
    if (d > _CascadeSplits.y) ci = 2;
    if (d > _CascadeSplits.z) ci = 3;
    return min(ci, _CascadeCount - 1);
}

// ════════════════════════════════════════════════════════════
//  ProjectToShadow — 投影到级联 shadow map，返回 false = 越界
// ════════════════════════════════════════════════════════════
bool ProjectToShadow(float3 positionWS, int ci,
    out float2 shadowUV, out float receiverDepth,
    out float2 atlasUV, out float2 tileMin, out float2 tileMax)
{
    shadowUV = 0; receiverDepth = 0; atlasUV = 0; tileMin = 0; tileMax = 0;

    float4 shadowCoord = mul(_CascadeLightVP[ci], float4(positionWS, 1.0));
    float2 suv = shadowCoord.xy / shadowCoord.w;
    float  rd  = shadowCoord.z / shadowCoord.w;

    shadowUV = suv;
    receiverDepth = rd;

    if (suv.x < 0.0 || suv.x > 1.0 || suv.y < 0.0 || suv.y > 1.0)
        return false;

    float4 atlas = _CascadeAtlasOffset[ci];
    atlasUV = suv * atlas.z + atlas.xy;
    tileMin = atlas.xy;
    tileMax = atlas.xy + atlas.zz;
    return true;
}

// ════════════════════════════════════════════════════════════
//  GetCascadeParams — 获取级联相关参数
// ════════════════════════════════════════════════════════════
void GetCascadeParams(int ci, out float halfWci, out float zDistCi, out float halfW0)
{
    halfWci = ci == 0 ? _CascadeHalfWidth.x : (ci == 1 ? _CascadeHalfWidth.y : (ci == 2 ? _CascadeHalfWidth.z : _CascadeHalfWidth.w));
    zDistCi = ci == 0 ? _CascadeZDistance.x : (ci == 1 ? _CascadeZDistance.y : (ci == 2 ? _CascadeZDistance.z : _CascadeZDistance.w));
    halfW0  = _CascadeHalfWidth.x;
}

// ════════════════════════════════════════════════════════════
//  DepthCmpLit — 统一深度比较（遮挡物在接收面后方 = lit）
// ════════════════════════════════════════════════════════════
bool DepthCmpLit(float occluderDepth, float receiverDepth, float bias)
{
#if UNITY_REVERSED_Z
    return occluderDepth > receiverDepth - bias;
#else
    return occluderDepth < receiverDepth + bias;
#endif
}

// ════════════════════════════════════════════════════════════
//  ComputeSlopeBias — 斜面深度偏移（随坡度和距离自适应）
// ════════════════════════════════════════════════════════════
float ComputeSlopeBias(float2 screenUV, float halfW0, float zDistCi)
{
    float3 normalWS = _CameraNormalsTexture.SampleLevel(PointClampSampler, screenUV, 0).xyz * 2.0 - 1.0;
    float NdotL = abs(dot(normalWS, _LightDirection));
    float gradient = sqrt(1.0 - NdotL * NdotL) / NdotL;
    return saturate((20.0 * halfW0 / _PCSS_AtlasParams.x) * gradient / zDistCi);
}

// ════════════════════════════════════════════════════════════
//  EvaluatePenumbraMask — 自适应步长 + 预扫描早期退出
// ════════════════════════════════════════════════════════════
float EvaluatePenumbraMask(float2 atlasUV, float2 tileMin, float2 tileMax,
    float receiverDepth, float halfWci, float zDistCi, float epsilon)
{
    // preRadius / maskPixels 是 tile 内的像素数，_PCSS_AtlasParams.x 即 tile 分辨率
    float preRadiusWS = 0.15 * (_PCSS_AtlasParams.x * 0.5) / max(halfWci, 0.001);
#if UNITY_REVERSED_Z
    float biased = receiverDepth - epsilon;
#else
    float biased = receiverDepth + epsilon;
#endif
    float preBlockerSum = 0.0; float preBlockerCnt = 0.001;
    float preLit = 0.0;
    float2 preDirs[4] = {
        float2( 0.7071,  0.7071), float2(-0.7071,  0.7071),
        float2(-0.7071, -0.7071), float2( 0.7071, -0.7071)
    };
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float sd = BilinearSampleAtlas(
            atlasUV + preDirs[k] * preRadiusWS * _PCSS_AtlasParams.w, tileMin, tileMax);
        if (DepthCmpLit(sd, biased, 0.0))
            preLit += 1.0;
        else
            { preBlockerSum += sd; preBlockerCnt += 1.0; }
    }

    if (preLit < 0.5) return 0.0;
    if (preLit > 3.5) return 9.0;

    float preBlocker = preBlockerSum / preBlockerCnt;
    float preDepthDiff = abs(preBlocker - receiverDepth);
    float roughPenumbra = preDepthDiff * zDistCi / max(halfWci, 0.001)
                        * _PCSS_Softness * (_PCSS_AtlasParams.x * 0.5) / max(halfWci, 0.001);

    float maskPixels = clamp(roughPenumbra * 0.67, 2.0, max(2.0, preRadiusWS));
    float maskUV = maskPixels * _PCSS_AtlasParams.w;
    float maskBias = epsilon * maskPixels;

    float lit = 0.0;
    for (int mi = -1; mi <= 1; mi++)
    for (int mj = -1; mj <= 1; mj++)
    {
        float2 mUV = atlasUV + float2(mi, mj) * maskUV;
        float nsd = BilinearSampleAtlas(mUV, tileMin, tileMax);
        lit += DepthCmpLit(nsd, receiverDepth, maskBias) ? 1.0 : 0.0;
    }
    return lit;
}

// ════════════════════════════════════════════════════════════
//  BlockerSearch — Vogel disk 搜索平均遮挡深度
// ════════════════════════════════════════════════════════════
void BlockerSearch(float2 atlasUV, float2 tileMin, float2 tileMax,
    float receiverDepth, float searchRadiusUV, float epsilon, float randomAngle, int n,
    out float avgBlocker, out float blockerRatio)
{
#if UNITY_REVERSED_Z
    float biased = receiverDepth - epsilon;
#else
    float biased = receiverDepth + epsilon;
#endif
    float depthSum = 0.0;
    float count = 0.001;
    [unroll]
    for (int i = 0; i < n; i++)
    {
        float2 offset = RotateVector(VogelDisk[i], randomAngle) * searchRadiusUV;
        float sd = BilinearSampleAtlas(atlasUV + offset, tileMin, tileMax);
        if (!DepthCmpLit(sd, biased, 0.0)) { depthSum += sd; count += 1.0; }
    }
    avgBlocker = depthSum / count;
    blockerRatio = count / (float)n;
}

// ════════════════════════════════════════════════════════════
//  EstimatePenumbra — 遮挡深度差 → 半影像素数
// ════════════════════════════════════════════════════════════
float EstimatePenumbra(float avgBlocker, float receiverDepth, float zDistCi, float halfWci)
{
    float depthDiff = abs(avgBlocker - receiverDepth);
    float penumbraWS = depthDiff * zDistCi / max(halfWci, 0.001) * _PCSS_Softness;
    float penumbraPixels = penumbraWS * (_PCSS_AtlasParams.x * 0.5) / max(halfWci, 0.001);
    return min(penumbraPixels, 100 / max(halfWci, 0.001));
}

// ════════════════════════════════════════════════════════════
//  VariablePCF — 变核 PCF
// ════════════════════════════════════════════════════════════
float VariablePCF(float2 atlasUV, float2 tileMin, float2 tileMax,
    float receiverDepth, float penumbraPixels, float pcfBias, float epsilon, int n, float pcfAngle)
{
    float pcfRadiusUV = max(penumbraPixels, 1.0) * _PCSS_AtlasParams.w;
    float shadow = 0.0;
    [unroll]
    for (int i = 0; i < n; i++)
    {
        float2 offset = RotateVector(VogelDisk[i], pcfAngle) * pcfRadiusUV;
        float sd = BilinearSampleAtlas(atlasUV + offset, tileMin, tileMax);
        shadow += DepthCmpLit(sd, receiverDepth, epsilon + pcfBias) ? 1.0 : 0.0;
    }
    return shadow / (float)n;
}

// ════════════════════════════════════════════════════════════
//  双边保边模糊
// ════════════════════════════════════════════════════════════
float4 BilateralBlurH(int2 id)
{
    float4 center = _PCSS_BlurInput.Load(int3(id.x, id.y, 0));
    if (center.a < 0.5) return center;

    float2 screenUV = (id + 0.5) / _ScreenSize.xy;
    float centerEye = LinearEyeDepthCS(_CameraDepthTexture.SampleLevel(PointClampSampler, screenUV, 0));

    float4 sum = 0; float totalW = 0;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        int2 sid = clamp(id + int2((int)(BlurOff[i] * _BlurScale), 0),
            int2(0, 0), int2((int)_ScreenSize.x - 1, (int)_ScreenSize.y - 1));
        float4 s = _PCSS_BlurInput.Load(int3(sid.x, sid.y, 0));
        float sdEye = LinearEyeDepthCS(
            _CameraDepthTexture.SampleLevel(PointClampSampler, (sid + 0.5) / _ScreenSize.xy, 0));
        float w = Gauss5[i] * exp(-abs(centerEye - sdEye) * BlurDepthSens);
        sum += s * w; totalW += w;
    }
    return float4((sum / max(totalW, 0.0001)).rgb, center.a);
}

float4 BilateralBlurV(int2 id)
{
    float4 center = _PCSS_BlurInput.Load(int3(id.x, id.y, 0));
    if (center.a < 0.5) return center;

    float2 screenUV = (id + 0.5) / _ScreenSize.xy;
    float centerEye = LinearEyeDepthCS(_CameraDepthTexture.SampleLevel(PointClampSampler, screenUV, 0));

    float4 sum = 0; float totalW = 0;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        int2 sid = clamp(id + int2(0, (int)(BlurOff[i] * _BlurScale)),
            int2(0, 0), int2((int)_ScreenSize.x - 1, (int)_ScreenSize.y - 1));
        float4 s = _PCSS_BlurInput.Load(int3(sid.x, sid.y, 0));
        float sdEye = LinearEyeDepthCS(
            _CameraDepthTexture.SampleLevel(PointClampSampler, (sid + 0.5) / _ScreenSize.xy, 0));
        float w = Gauss5[i] * exp(-abs(centerEye - sdEye) * BlurDepthSens);
        sum += s * w; totalW += w;
    }
    return float4((sum / max(totalW, 0.0001)).rgb, center.a);
}
