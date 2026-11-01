// ════════════════════════════════════════════════════════════
//  FabricFunction.hlsl — 织物双流（KM）与 SGGX 微片共享函数库
//  用法：调用方使用 Assets/Mine/Special/HLSL/FabricFunction.hlsl 全路径 include。
//  依赖：调用方先 include Core.hlsl，提供 real 类型与 PI；本库零 CBUFFER、零兄弟 include。
//  输出：织物薄层的反射率 / 透射率、LUT 采样坐标与纤维微片的角向分布；光照与阴影由调用方组合。
//  合成：Fabric_ShadeLayer 以 TEXTURE2D_PARAM 接收 LUT，要求 SM4.5+ 独立采样器（桌面端，URP 17 已无 GLES2）。
// ════════════════════════════════════════════════════════════

#ifndef FABRICFUNCTION_HLSL_INCLUDED
#define FABRICFUNCTION_HLSL_INCLUDED

// ════════════════════════════════════════════════════════════
//  常量 — 织物层固定参数；调用方可在 include 前重定义
// ════════════════════════════════════════════════════════════

#ifndef FABRIC_FIBER_WIDTH
#define FABRIC_FIBER_WIDTH   0.55     // SGGX 纤维截面半宽；<0.4 会在球面上出竖向加厚带
#endif

#ifndef FABRIC_MAX_OPTICAL
#define FABRIC_MAX_OPTICAL   0.06     // 覆盖度 1 对应的基础光学厚度 S·d
#endif

#ifndef FABRIC_MAX_KS
#define FABRIC_MAX_KS        16.0     // LUT 的 K/S 上限，必须与 FabricLutBaker 一致
#endif

#ifndef FABRIC_LUT_MAX_SD
#define FABRIC_LUT_MAX_SD    8.0      // LUT 的 S·d 上限，必须与 FabricLutBaker 一致
#endif

#ifndef FABRIC_SHEEN_COLOR
#define FABRIC_SHEEN_COLOR   float3(0.85, 0.88, 0.95)   // 绒面光泽的冷白色偏
#endif

#ifndef FABRIC_SHEEN_POWER
#define FABRIC_SHEEN_POWER   3.0      // 绒面 Fresnel 指数：掠射增强的陡度
#endif

#ifndef FABRIC_SHEEN_BASE
#define FABRIC_SHEEN_BASE    0.15     // 绒面在正面的保留量，避免背光侧绒面彻底消失
#endif

#ifndef FABRIC_TINT_GAIN
#define FABRIC_TINT_GAIN     3.0      // 透射偏色：标量 K/S 无光谱，用织物色近似吸收
#endif

#ifndef FABRIC_TINT_BIAS
#define FABRIC_TINT_BIAS     0.35     // 透射偏色的保底亮度
#endif

// ════════════════════════════════════════════════════════════
//  KM — Kubelka-Munk 双流薄层解析解
//  K 为吸收系数、S 为散射系数，均为单位厚度量；sd = S * 厚度 为光学厚度。
// ════════════════════════════════════════════════════════════

/// <summary>双流常数 A = 1 + K/S。</summary>
real KM_Alpha(real K, real S)
{
    return 1.0 + K / max(S, 1e-6);
}

/// <summary>双流常数 B = sqrt(A² - 1)，即特征值系数。</summary>
real KM_Beta(real alpha)
{
    return sqrt(max(alpha * alpha - 1.0, 0.0));
}

/// <summary>无限厚反射率 R∞ = A - B，即织物自身的极限反射。</summary>
real KM_ReflectanceInfinite(real K, real S)
{
    real alpha = KM_Alpha(K, S);
    return alpha - KM_Beta(alpha);
}

/// <summary>黑背景薄层解：R 为反射率、T 为透射率；sd 为 S 乘厚度。</summary>
void KM_Slab(real K, real S, real sd, out real R, out real T)
{
    real alpha = KM_Alpha(K, S);
    real beta  = KM_Beta(alpha);
    real tau   = beta * max(sd, 0.0);

    real e2     = exp(-2.0 * tau);
    real oneMinusE2 = max(1.0 - e2, 1e-6);
    real cothTau    = (1.0 + e2) / oneMinusE2;
    R = 1.0 / (alpha + beta * cothTau);

    real e1 = exp(-tau);
    T = (2.0 * beta * e1) / max(alpha * oneMinusE2 + beta * (1.0 + e2), 1e-6);
}

/// <summary>薄层叠在反射率 Rg 的背景上：输出总反射率与总透射率。</summary>
void KM_LayerOverBackground(real R, real T, real Rg, out real reflected, out real transmitted)
{
    real denom = max(1.0 - R * Rg, 1e-4);
    reflected   = R + (T * T * Rg) / denom;
    transmitted = (T * (1.0 - Rg)) / denom;
}

// ════════════════════════════════════════════════════════════
//  SGGX — 纤维微片的椭球分布
//  形状在织物的 T/B/N 基上对角化，sigma 为三个轴向的半轴长度。
// ════════════════════════════════════════════════════════════

/// <summary>沿方向 w 的投影面积 sqrt(wᵀΣw)，决定该方向的遮蔽比例。</summary>
real SGGX_ProjectedArea(real3 w, real3 T, real3 B, real3 N, real3 sigma)
{
    real wt = dot(w, T) * sigma.x;
    real wb = dot(w, B) * sigma.y;
    real wn = dot(w, N) * sigma.z;
    return sqrt(max(wt * wt + wb * wb + wn * wn, 1e-8));
}

/// <summary>SGGX 微片分布 D(m) = 1 / (π σtσbσn (mᵀΣ⁻¹m)²)。</summary>
real SGGX_Density(real3 m, real3 T, real3 B, real3 N, real3 sigma)
{
    real mt = dot(m, T) / max(sigma.x, 1e-4);
    real mb = dot(m, B) / max(sigma.y, 1e-4);
    real mn = dot(m, N) / max(sigma.z, 1e-4);
    real q  = max(mt * mt + mb * mb + mn * mn, 1e-6);
    real det = max(sigma.x * sigma.y * sigma.z, 1e-6);
    return 1.0 / (PI * det * q * q);
}

/// <summary>纤维族的形状参数：沿 T 拉长、B/N 截面收窄。</summary>
real3 SGGX_FiberSigma(real fiberWidth)
{
    real width = clamp(fiberWidth, 0.02, 1.0);
    return real3(1.0, width, width);
}

// ════════════════════════════════════════════════════════════
//  Fabric — 以 SGGX 投影面积修正双流光学厚度的薄层求值
// ════════════════════════════════════════════════════════════

/// <summary>沿 dir 的有效光学厚度：SGGX 投影面积相对法向的比值放大基准厚度。</summary>
real Fabric_OpticalDepth(real3 dir, real3 T, real3 B, real3 N, real3 sigma, real baseSd)
{
    real sigmaDir = SGGX_ProjectedArea(dir, T, B, N, sigma);
    real sigmaN   = SGGX_ProjectedArea(N, T, B, N, sigma);
    return baseSd * (sigmaDir / max(sigmaN, 1e-6));
}

/// <summary>织物薄层：返回含背景的总反射 / 透射率，R∞ 供调试或自定义合成。</summary>
void Fabric_TwoFlux(real K, real S, real3 dir, real3 T, real3 B, real3 N,
    real3 sigma, real baseSd, real Rg,
    out real reflected, out real transmitted, out real infiniteReflectance)
{
    real sd  = Fabric_OpticalDepth(dir, T, B, N, sigma, baseSd);
    real R, Tval;
    KM_Slab(K, S, sd, R, Tval);
    KM_LayerOverBackground(R, Tval, saturate(Rg), reflected, transmitted);
    infiniteReflectance = KM_ReflectanceInfinite(K, S);
}

/// <summary>纤维绒面高光瓣：SGGX 分布乘 Ashikhmin 可见性。</summary>
real Fabric_Sheen(real3 H, real NdotV, real NdotL, real3 T, real3 B, real3 N, real3 sigma)
{
    real D = SGGX_Density(H, T, B, N, sigma);
    real visibility = 1.0 / max(NdotV + NdotL - NdotV * NdotL, 1e-4);
    return D * 0.25 * visibility;
}

// ════════════════════════════════════════════════════════════
//  Fabric — 预积分 LUT 查表与薄层叠加
// ════════════════════════════════════════════════════════════

/// <summary>LUT 采样坐标：域 (K/S, S·d) 各自 sqrt 映射，内缩半个纹素避免跨边界。</summary>
float2 Fabric_LutUV(real absorptionRatio, real opticalDepth, float2 texelSize)
{
    float2 domain = saturate(float2(absorptionRatio / FABRIC_MAX_KS, opticalDepth / FABRIC_LUT_MAX_SD));
    return sqrt(domain) * (1.0 - texelSize) + 0.5 * texelSize;
}

/// <summary>由法线构造任意正交基，供 SGGX 投影面积使用（纤维方向不跟 UV 走）。</summary>
void Fabric_Basis(float3 normalWS, out float3 tangentWS, out float3 bitangentWS)
{
    float3 helper = abs(normalWS.y) < 0.99 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
    tangentWS = normalize(cross(helper, normalWS));
    bitangentWS = cross(normalWS, tangentWS);
}

// ════════════════════════════════════════════════════════════
//  Fabric — 与风格化本体合成
//  契约：Rg 只能是反射率（albedo·(1−metallic)），不能是已着色的最终色；
//  已按 toon/SSS/AO/补光塑形的本体辐亮度整体走透过率，自发光只走单程 T。
// ════════════════════════════════════════════════════════════

/// <summary>采样织物薄层：织物色定 K/S，视线方向经 SGGX 定光学厚度，返回 LUT 的 R/T。</summary>
void Fabric_EvaluateLayer(TEXTURE2D_PARAM(lutTex, lutSampler), float2 lutTexelSize,
    real3 fabricColor, real coverage,
    real3 dirWS, real3 tangentWS, real3 bitangentWS, real3 normalWS,
    out real reflectance, out real transmittance)
{
    real darkest = max(fabricColor.r, max(fabricColor.g, fabricColor.b));
    real absorptionRatio = FABRIC_MAX_KS * saturate(1.0 - darkest);
    real3 sigma = SGGX_FiberSigma(FABRIC_FIBER_WIDTH);
    real opticalDepth = Fabric_OpticalDepth(dirWS, tangentWS, bitangentWS, normalWS, sigma,
        coverage * FABRIC_MAX_OPTICAL);

    float2 uv = Fabric_LutUV(absorptionRatio, opticalDepth, lutTexelSize);
    float2 lut = SAMPLE_TEXTURE2D(lutTex, lutSampler, uv).rg;
    reflectance = lut.x;
    transmittance = lut.y;
}

/// <summary>薄层合成：本体辐亮度 × T²/(1−R·Rg)，加织物自身反射、绒面与单程透射的自发光。</summary>
float3 Fabric_CompositeLayer(real reflectance, real transmittance,
    real3 fabricColor, real sheenStrength, real coverage,
    real3 bodyReflectance, real3 bodyRadiance, real3 directionalLight, real3 ambientLight,
    real3 emission, real NdotV, real NdotL)
{
    float3 rg = saturate(bodyReflectance);
    float3 through = (transmittance * transmittance) / max(1.0 - reflectance * rg, 1e-3);
    float3 veilTint = lerp(float3(1.0, 1.0, 1.0),
        saturate(fabricColor * FABRIC_TINT_GAIN + FABRIC_TINT_BIAS),
        saturate(1.0 - transmittance));
    float3 fiberReflection = fabricColor * reflectance * (directionalLight + ambientLight);
    float3 sheen = sheenStrength * saturate(coverage) * FABRIC_SHEEN_COLOR * directionalLight
        * pow(saturate(1.0 - NdotV), FABRIC_SHEEN_POWER)
        * (FABRIC_SHEEN_BASE + (1.0 - FABRIC_SHEEN_BASE) * NdotL);

    return fiberReflection + through * veilTint * bodyRadiance + sheen + transmittance * emission;
}

/// <summary>织物层入口：薄层采样与合成为一次调用，调用方只提供法线、视线与本体的分段结果。</summary>
float3 Fabric_ShadeLayer(TEXTURE2D_PARAM(lutTex, lutSampler), float2 lutTexelSize,
    float3 normalWS, float3 viewDirWS,
    float3 fabricColor, real coverage, real sheenStrength,
    float3 bodyReflectance, float3 bodyRadiance, float3 directionalLight, float3 ambientLight,
    float3 emission, real NdotV, real NdotL)
{
    float3 tangentWS;
    float3 bitangentWS;
    Fabric_Basis(normalWS, tangentWS, bitangentWS);

    real reflectance;
    real transmittance;
    Fabric_EvaluateLayer(TEXTURE2D_ARGS(lutTex, lutSampler), lutTexelSize, fabricColor, coverage,
        viewDirWS, tangentWS, bitangentWS, normalWS, reflectance, transmittance);

    return Fabric_CompositeLayer(reflectance, transmittance, fabricColor, sheenStrength, coverage,
        bodyReflectance, bodyRadiance, directionalLight, ambientLight, emission, NdotV, NdotL);
}

#endif // FABRICFUNCTION_HLSL_INCLUDED
