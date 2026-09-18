// 渲染方程：Lo(x, wo) = Le(x, wo) + integral_Omega+ fr(x, wi, wo) * Li(x, wi) * max(dot(N, wi), 0) dwi
// 出射光 = 自发光 + 入射半球上所有方向的（BRDF × 入射光 × 投影余弦）积分。

// ════════════════════════════════════════════════════════════
//  PBRFunction.hlsl — BRDF、BTDF、BSDF 与毛发高光共享函数库
//  用法：调用方使用 Assets/Mine/Special/HLSL/PBRFunction.hlsl 全路径 include。
//  依赖：调用方先 include Core.hlsl，提供 real 类型与 PI；本库零 CBUFFER、零兄弟 include。
//  输出：材质散射响应，光照、阴影与余弦项由调用方组合。
// ════════════════════════════════════════════════════════════

#ifndef PBRFUNCTION_HLSL_INCLUDED
#define PBRFUNCTION_HLSL_INCLUDED

// ════════════════════════════════════════════════════════════
//  共享项 — 微表面分布、遮蔽与菲涅耳
// ════════════════════════════════════════════════════════════

/// <summary>各向同性 GGX 微表面法线分布。</summary>
real Spec_D_GGX(real NdotH, real alpha)
{
    real alpha2   = alpha * alpha;
    real NdotH2   = NdotH * NdotH;
    real denomD   = (NdotH2 * (alpha2 - 1.0) + 1.0);
    return alpha2 / (PI * denomD * denomD + 1e-5);
}

/// <summary>各向异性 GGX 微表面法线分布。</summary>
real Spec_D_GGX_Aniso(real TdotH, real BdotH, real NdotH, real alpha, real aniso, real anisotropy)
{
    real alpha2   = alpha * alpha;
    real aspect   = sqrt(1.0 - aniso * 0.9);
    real axisX    = aspect / alpha;
    real axisY    = aspect * alpha;

    if (anisotropy < 0.0)
    {
        axisX = aspect * alpha;
        axisY = aspect / alpha;
    }

    real TdotH2   = TdotH * TdotH;
    real BdotH2   = BdotH * BdotH;
    real NdotH2   = NdotH * NdotH;
    real denomD   = (TdotH2 / axisX / axisX + BdotH2 / axisY / axisY + NdotH2);
    return 1 / (PI * axisX * axisY * denomD * denomD + 1e-5);
}

/// <summary>Schlick-GGX 遮蔽与阴影近似。</summary>
real Spec_G_Smith(real NdotV, real NdotL, real alpha)
{
    real k = (alpha + 1.0) * (alpha + 1.0) / 8.0;
    real GV = NdotV / (NdotV * (1.0 - k) + k + 1e-5);
    real GL = NdotL / (NdotL * (1.0 - k) + k + 1e-5);
    return GV * GL;
}

/// <summary>基于 LdotH 的几何遮蔽近似。</summary>
real Spec_G_SKSmith(real NdotV, real NdotL, real LdotH, real alpha)
{
    real k = alpha;
    return (NdotL * NdotV) / ((1 - k) * pow(LdotH, 2.0) + k + 1e-5);
}

/// <summary>高光可见性近似。</summary>
real Spec_V(real LdotH, real alpha)
{
    return 1 / (max(pow(LdotH, 2.0), 0.1) * (alpha + 0.5));
}

/// <summary>Schlick 五次幂菲涅耳近似。</summary>
real3 F_P5(real3 F0, real VdotH)
{
    return F0 + (1.0 - F0) * pow(1.0 - VdotH, 5.0);
}

/// <summary>快速多项式菲涅耳近似。</summary>
real3 F_Fast(real3 F0, real VdotH)
{
    return F0 + (1.0 - F0) * pow(1.0 - VdotH, 2.0) * (1.0 - 2.0 * VdotH);
}

/// <summary>完整介电菲涅耳，包含全反射判断；保留参考，当前不调用。</summary>
real F_Dielectric(real VdotH, real etaV, real etaL)
{
    if (etaV <= 0.0 || etaL <= 0.0)
        return 1.0;
    if (etaV == etaL)
        return 0.0;

    real cosV = saturate(abs(VdotH));
    real eta = etaV / etaL;
    real sinL2 = eta * eta * max(1.0 - cosV * cosV, 0.0);
    if (sinL2 >= 1.0)
        return 1.0;

    real cosL = sqrt(1.0 - sinL2);
    real Rs = (etaV * cosV - etaL * cosL) / max(etaV * cosV + etaL * cosL, 1e-7);
    real Rp = (etaL * cosV - etaV * cosL) / max(etaL * cosV + etaV * cosL, 1e-7);
    return saturate(0.5 * (Rs * Rs + Rp * Rp));
}

// ════════════════════════════════════════════════════════════
//  BRDF — 漫反射、高光反射与组合
// ════════════════════════════════════════════════════════════

/// <summary>Lambert 漫射项，返回 albedo / PI。</summary>
real3 Diff_Lambert(real3 albedo)
{
    return albedo / PI;
}

/// <summary>Burley 漫射项，包含 Lambert 基础与角度、粗糙度修正。</summary>
real3 Diff_Burley(real3 albedo, real NdotL, real NdotV, real LdotH, real roughness)
{
    real FL = pow(1.0 - NdotL, 5.0);
    real FV = pow(1.0 - NdotV, 5.0);
    real Fd90 = 0.5 + 2.0 * LdotH * LdotH * roughness;
    return Diff_Lambert(albedo) * (1.0 + (Fd90 - 1.0) * FL) * (1.0 + (Fd90 - 1.0) * FV);
}

/// <summary>Cook-Torrance 高光项，菲涅耳权重由调用方乘入。</summary>
real Spec_CK(real NdotH, real NdotV, real NdotL, real VdotH, real roughness)
{
    real alpha = roughness * roughness;
    real D = Spec_D_GGX(NdotH, alpha);
    real G = Spec_G_Smith(NdotV, NdotL, alpha);

    return (D * G) / (4.0 * NdotV * NdotL + 1e-5);
}

/// <summary>各向异性混合高光近似，菲涅耳权重由调用方乘入。</summary>
real3 Spec_Unity(real NdotH, real LdotH, real VdotH, real TdotH, real BdotH, real roughness, real anisotropy)
{
    real alpha = roughness * roughness;
    real aniso = saturate(abs(anisotropy));

    real D1 = Spec_D_GGX(NdotH, alpha);
    real D2 = Spec_D_GGX_Aniso(TdotH, BdotH, NdotH, alpha, aniso, anisotropy);
    real D  = lerp(D1, D2, aniso);
    real V  = Spec_V(LdotH, alpha);

    return (D * V) / ((4.0 + 1e-5) * (1.0 + 0.5 * alpha));
}

/// <summary>组合 Lambert 漫反射与 Cook-Torrance 高光反射。</summary>
real3 BRDF_Classic(real3 baseColor, real NdotL, real NdotV, real NdotH, real VdotH, real LdotH, real roughness, real metallic)
{
    real3 F0 = lerp(0.04, baseColor, metallic);
    real3 F  = F_P5(F0, VdotH);

    real3 specular = Spec_CK(NdotH, NdotV, NdotL, VdotH, roughness) * F;
    real3 diffuse  = Diff_Lambert(baseColor) * (1.0 - metallic) * (1.0 - F);
    return specular + diffuse;
}

/// <summary>组合 Lambert 漫反射与各向异性高光近似。</summary>
real3 BRDF_Unity(real3 baseColor, real NdotL, real NdotV, real NdotH,
                real VdotH, real LdotH, real TdotH, real BdotH,
                real roughness, real metallic, real anisotropy)
{
    real3 F0 = lerp(0.04, baseColor, metallic);
    real3 F  = F_Fast(F0, VdotH);

    real3 specular = Spec_Unity(NdotH, LdotH, VdotH, TdotH, BdotH, roughness, anisotropy) * F;
    real3 diffuse  = Diff_Lambert(baseColor) * (1.0 - metallic) * (1.0 - F);
    return specular + diffuse;
}

/// <summary>组合 Burley 漫反射与 Cook-Torrance 高光反射。</summary>
real3 BRDF_Burley(real3 baseColor, real NdotL, real NdotV, real NdotH, real VdotH, real LdotH, real roughness, real metallic)
{
    real3 F0 = lerp(0.04, baseColor, metallic);
    real3 F  = F_P5(F0, VdotH);

    real3 specular = Spec_CK(NdotH, NdotV, NdotL, VdotH, roughness) * F;
    real3 diffuse  = Diff_Burley(baseColor, NdotL, NdotV, LdotH, roughness) * (1.0 - metallic) * (1.0 - F);
    return specular + diffuse;
}

// ════════════════════════════════════════════════════════════
//  BTDF — 漫透射、粗糙透射与组合
//  N/L/V 均为单位向量，L/V 从表面向外；etaV/etaL 分别为 V/L 侧折射率。
//  采用 radiance 约定，调用方另乘 Li * abs(NdotL)；alpha 最小为 0.001。
//  等折射率的理想直通需单独采样；不包含厚度吸收或内部散射。
// ════════════════════════════════════════════════════════════

/// <summary>折射方向与投影面积换算，包含 radiance 折射率修正；eta = etaL / etaV。</summary>
real Trans_J_Refract(real NdotV, real NdotL, real VdotH, real LdotH, real eta)
{
    real denom = VdotH + eta * LdotH;
    real projected = abs(NdotV * NdotL);
    return abs(VdotH * LdotH) / max(projected * denom * denom, 1e-12);
}

/// <summary>异侧 Lambert 漫透射，transColor 为含界面损失的有效透射率。</summary>
real3 Trans_Lambert(real3 transColor, real NdotV, real NdotL)
{
    if (NdotV * NdotL >= 0.0)
        return real3(0.0, 0.0, 0.0);
    return Diff_Lambert(saturate(transColor));
}

/// <summary>粗糙透射 D * G * (1-F) * J，复用共享近似并保留全反射判断。</summary>
real3 Trans_GGX(real3 transColor, real3 N, real3 L, real3 V,
                real roughness, real etaV, real etaL)
{
    real NdotV = dot(N, V);
    real NdotL = dot(N, L);
    if (NdotV * NdotL >= 0.0 || etaV <= 0.0 || etaL <= 0.0)
        return real3(0.0, 0.0, 0.0);

    real eta = etaL / etaV;
    if (abs(eta - 1.0) < 1e-4)
        return real3(0.0, 0.0, 0.0);

    real3 halfVec = V + eta * L;
    real halfLen2 = dot(halfVec, halfVec);
    if (halfLen2 < 1e-12)
        return real3(0.0, 0.0, 0.0);

    real3 H = halfVec * rsqrt(halfLen2);
    H = dot(N, H) < 0.0 ? -H : H;
    real VdotH = dot(V, H);
    real LdotH = dot(L, H);
    if (VdotH * NdotV <= 0.0 || LdotH * NdotL <= 0.0)
        return real3(0.0, 0.0, 0.0);

    real cosV = saturate(abs(VdotH));
    real sinL2 = max(1.0 - cosV * cosV, 0.0) / (eta * eta);
    if (sinL2 >= 1.0)
        return real3(0.0, 0.0, 0.0);

    real alpha = max(saturate(roughness) * saturate(roughness), 0.001);
    real D = Spec_D_GGX(saturate(dot(N, H)), alpha);
    real G = Spec_G_Smith(saturate(abs(NdotV)), saturate(abs(NdotL)), alpha);
    real F0 = (eta - 1.0) / (eta + 1.0);
    F0 *= F0;
    real cosF = min(cosV, saturate(abs(LdotH)));
    real F = F_P5(real3(F0, F0, F0), cosF).r;
    real J = Trans_J_Refract(NdotV, NdotL, VdotH, LdotH, eta);
    return saturate(transColor) * (D * G * (1.0 - F) * J);
}

/// <summary>视角 F 驱动的风格化分配：粗糙透射乘 F、漫透射乘 (1-F)，非严格介电能量分配。</summary>
real3 BTDF_Classic(real3 transColor, real3 N, real3 L, real3 V,
                    real roughness, real etaV, real etaL)
{
    if (etaV <= 0.0 || etaL <= 0.0)
        return real3(0.0, 0.0, 0.0);

    real NdotV = dot(N, V);
    real NdotL = dot(N, L);
    real F0 = (etaL - etaV) / (etaL + etaV);
    F0 *= F0;
    real F = F_P5(real3(F0, F0, F0), saturate(abs(NdotV))).r;

    real3 specular = Trans_GGX(transColor, N, L, V, roughness, etaV, etaL) * F;
    real3 diffuse = Trans_Lambert(transColor, NdotV, NdotL) * (1.0 - F);
    return specular + diffuse;
}

// ════════════════════════════════════════════════════════════
//  BSDF — 统一界面 F，分配镜面反射、漫反射与粗糙折射
//  输出不含 Li、阴影与 abs(NdotL)，由调用方组合；不包含厚度吸收或 BSSRDF。
//  单次微表面与经验漫射近似；菲涅耳只分配一次，不调用已有 BRDF/BTDF 组合。
// ════════════════════════════════════════════════════════════

/// <summary>介电 Schlick 近似与全反射判断；etaV、etaOther 为正折射率。</summary>
real BSDF_Fresnel(real cosV, real etaV, real etaOther)
{
    cosV = saturate(abs(cosV));
    real eta = etaV / etaOther;
    real sinOther2 = eta * eta * max(1.0 - cosV * cosV, 0.0);
    if (sinOther2 >= 1.0)
        return 1.0;

    real cosOther = sqrt(max(1.0 - sinOther2, 0.0));
    real F0 = (etaV - etaOther) / (etaV + etaOther);
    F0 *= F0;
    return F_P5(F0.xxx, min(cosV, cosOther)).r;
}

/// <summary>同侧反射、异侧折射；N/L/V 为单位向量，N 朝外，外部折射率为 1，interiorIOR 大于 1。</summary>
void BSDF_Evaluate(real3 baseColor, real3 transColor, real3 N, real3 L, real3 V,
    real perceptualRoughness, real transmissionWeight, real interiorIOR,
    out real3 reflection, out real3 transmission)
{
    real NdotV = dot(N, V);
    real NdotL = dot(N, L);
    real weight = saturate(transmissionWeight);
    real etaV = NdotV > 0.0 ? 1.0 : interiorIOR;
    real etaOther = NdotV > 0.0 ? interiorIOR : 1.0;
    real roughness = max(saturate(perceptualRoughness), 0.05);
    reflection = 0.0;
    transmission = 0.0;
    if (abs(NdotV) < 1e-4 || abs(NdotL) < 1e-4)
        return;

    if (NdotV * NdotL > 0.0)
    {
        real3 orientedN = NdotV > 0.0 ? N : -N;
        real3 H = SafeNormalize(V + L);
        real VdotH = saturate(dot(V, H));
        real F = BSDF_Fresnel(VdotH, etaV, etaOther);
        real specReflect = Spec_CK(saturate(dot(orientedN, H)),
            abs(NdotV), abs(NdotL), VdotH, roughness);
        real3 diffReflect = Diff_Lambert(saturate(baseColor));
        reflection = specReflect * F
            + diffReflect * (1.0 - F) * (1.0 - weight);
    }
    else
    {
        real eta = etaOther / etaV;
        real3 halfVec = V + eta * L;
        real halfLen2 = dot(halfVec, halfVec);
        if (halfLen2 < 1e-8)
            return;

        real3 H = halfVec * rsqrt(halfLen2);
        H = dot(N, H) < 0.0 ? -H : H;
        real VdotH = dot(V, H);
        real LdotH = dot(L, H);
        if (VdotH * NdotV <= 0.0 || LdotH * NdotL <= 0.0)
            return;

        real F = BSDF_Fresnel(VdotH, etaV, etaOther);
        if (F >= 1.0)
            return;

        real alpha = roughness * roughness;
        real D = Spec_D_GGX(saturate(dot(N, H)), alpha);
        real G = Spec_G_Smith(abs(NdotV), abs(NdotL), alpha);
        real J = Trans_J_Refract(NdotV, NdotL, VdotH, LdotH, eta);
        real3 specTransmit = saturate(transColor) * (D * G * J);
        transmission = specTransmit * (1.0 - F) * weight;
    }
}


// ════════════════════════════════════════════════════════════
//  Hair — R 与 TRT 双高光瓣近似
//  T 为纤维切线；TdotN 在壳层毛发 T=N 时为 1；不包含 TT 透射瓣。
// ════════════════════════════════════════════════════════════

/// <summary>计算偏移纤维切线与半程向量的点积。</summary>
real Hair_ShiftedTdotH(real TdotH, real NdotH, real TdotN, real shift)
{
    real denom = sqrt(max(1.0 + 2.0 * shift * TdotN + shift * shift, 1e-7));
    return (TdotH + shift * NdotH) / denom;
}

/// <summary>Kajiya-Kay 高光瓣，roughness 控制瓣宽。</summary>
real Hair_Specular(real shiftedTdotH, real roughness)
{
    real sinTH2 = max(1.0 - shiftedTdotH * shiftedTdotH, 1e-7);
    real specPower = lerp(256.0, 1.0, roughness * roughness);
    return pow(sinTH2, specPower * 0.5);
}

/// <summary>组合切线偏移、高光瓣、颜色与强度。</summary>
real3 Hair_Lobe(real TdotH, real NdotH, real TdotN, real shift,
                real roughness, real3 color, real strength)
{
    real sTdotH = Hair_ShiftedTdotH(TdotH, NdotH, TdotN, shift);
    real spec = Hair_Specular(sTdotH, roughness);
    return color * strength * spec;
}

/// <summary>组合 R 与 TRT 两个近似高光瓣，漫射由调用方另行提供。</summary>
real3 BRDF_Hair(real TdotH, real NdotH, real TdotN,
                real primaryShift, real primaryRoughness, real3 primaryColor, real primaryStrength,
                real secondaryShift, real secondaryRoughness, real3 secondaryColor, real secondaryStrength)
{
    return Hair_Lobe(TdotH, NdotH, TdotN, primaryShift, primaryRoughness, primaryColor, primaryStrength)
         + Hair_Lobe(TdotH, NdotH, TdotN, secondaryShift, secondaryRoughness, secondaryColor, secondaryStrength);
}

#endif // PBRFUNCTION_HLSL_INCLUDED
