// 渲染方程：Lo(x, wo) = Le(x, wo) + integral_Omega+ fr(x, wi, wo) * Li(x, wi) * max(dot(N, wi), 0) dwi
// 出射光 = 自发光 + 入射半球上所有方向的（BRDF × 入射光 × 投影余弦）积分。

// ════════════════════════════════════════════════════════════
//  PBRFunction.hlsl — PBR 家族入口：BRDF、BTDF、BSDF、毛发与织物共享模型
//  用法：调用方使用 Assets/Mine/Special/HLSL/PBRFunction.hlsl 全路径 include。
//  依赖：调用方先 include Core.hlsl，提供 real 类型与 PI；本库零 CBUFFER。
//  聚合：本库聚合同族子库 HairFunction / FabricFunction，只引本库即可用全部 PBR 模型。
//  输出：材质散射响应，光照、阴影与余弦项由调用方组合。
// ════════════════════════════════════════════════════════════

#ifndef PBRFUNCTION_HLSL_INCLUDED
#define PBRFUNCTION_HLSL_INCLUDED

// ════════════════════════════════════════════════════════════
//  家族聚合 — 毛发与织物子库，靠各自 guard 避免重复展开
// ════════════════════════════════════════════════════════════

#include "Assets/Mine/Special/HLSL/HairFunction.hlsl"
#include "Assets/Mine/Special/HLSL/FabricFunction.hlsl"

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

/// <summary>归一化 Blinn-Phong 法线分布；exponent 为高光指数，可由 GGX 的 α 按 2/α² - 2 换算。</summary>
real Spec_D_BlinnPhong(real NdotH, real exponent)
{
    return max(exponent + 2.0, 0.0) / (2.0 * PI) * pow(max(NdotH, 0.0), exponent);
}

/// <summary>Beckmann 高斯坡面法线分布，长尾比 GGX 短，适合车漆与拉丝金属。</summary>
real Spec_D_Beckmann(real NdotH, real alpha)
{
    real alpha2 = alpha * alpha;
    real NdotH2 = max(NdotH * NdotH, 1e-6);
    real tan2   = (1.0 - NdotH2) / NdotH2;
    return exp(-tan2 / alpha2) / (PI * alpha2 * NdotH2 * NdotH2 + 1e-5);
}

/// <summary>GTR1 广义 Trowbridge-Reitz；α 趋近 1 时退化为均匀分布，常与 GGX 混合出宽瓣。</summary>
real Spec_D_GTR1(real NdotH, real alpha)
{
    real alpha2 = alpha * alpha;
    if (abs(alpha2 - 1.0) < 1e-4)
        return 1.0 / PI;

    real NdotH2 = NdotH * NdotH;
    return (alpha2 - 1.0) / (PI * log(alpha2) * (1.0 + (alpha2 - 1.0) * NdotH2) + 1e-6);
}

/// <summary>Charlie 布料光泽分布；roughness 为感知粗糙度，与原式一致，不再平方。</summary>
real Spec_D_Charlie(real NdotH, real roughness)
{
    real invAlpha = 1.0 / max(roughness, 1e-4);
    real sinTH2   = max(1.0 - NdotH * NdotH, 0.0078125);
    return (2.0 + invAlpha) * pow(sinTH2, invAlpha * 0.5) / (2.0 * PI);
}

/// <summary>Schlick-GGX 遮蔽与阴影近似；roughness 为感知粗糙度，k = (roughness + 1)² / 8 与其定义一致。</summary>
real Spec_G_Smith(real NdotV, real NdotL, real roughness)
{
    real k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    real GV = NdotV / (NdotV * (1.0 - k) + k + 1e-5);
    real GL = NdotL / (NdotL * (1.0 - k) + k + 1e-5);
    return GV * GL;
}

/// <summary>Karis 移动端遮蔽近似，k 取 alpha 而非 Schlick 形式；名称沿用历史，非 Smith-GGX 精确解。</summary>
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

/// <summary>Smith 高度相关可见性（Heitz 2014），已含 1/(4 NdotV NdotL)，与 Spec_CK 同量纲可直接互换。</summary>
real Spec_V_SmithCorrelated(real NdotV, real NdotL, real alpha)
{
    real alpha2 = alpha * alpha;
    real gV     = NdotL * sqrt(NdotV * NdotV * (1.0 - alpha2) + alpha2);
    real gL     = NdotV * sqrt(NdotL * NdotL * (1.0 - alpha2) + alpha2);
    return 0.5 / max(gV + gL, 1e-5);
}

/// <summary>Kelemen 可见性近似 1/(4 LdotH²)，常与 Blinn-Phong 类分布配对。</summary>
real Spec_V_Kelemen(real LdotH)
{
    return 1.0 / max(4.0 * LdotH * LdotH, 1e-5);
}

/// <summary>Neubelt 布料可见性，分母形式天然抑制掠射角的高光过曝。</summary>
real Spec_V_Neubelt(real NdotL, real NdotV)
{
    return 1.0 / max(4.0 * (NdotL + NdotV - NdotL * NdotV), 1e-5);
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

/// <summary>带 F90 的 Schlick 近似；F90 可调，用于涂层、布料等掠射反射不饱和的材质。</summary>
real3 F_SchlickF90(real3 F0, real3 F90, real VdotH)
{
    return F0 + (F90 - F0) * pow(saturate(1.0 - VdotH), 5.0);
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

/// <summary>Oren-Nayar 粗糙漫反射；sigma 为坡角标准差（弧度，0..PI/2），常用 roughness * PI/2 映射。</summary>
real3 Diff_OrenNayar(real3 albedo, real NdotL, real NdotV, real VdotL, real sigma)
{
    real sigma2 = sigma * sigma;
    real A      = 1.0 - 0.5 * sigma2 / (sigma2 + 0.33);
    real B      = 0.45 * sigma2 / (sigma2 + 0.09);
    real s      = max(VdotL - NdotL * NdotV, 0.0);
    real t      = max(max(NdotL, NdotV), 1e-4);
    return Diff_Lambert(albedo) * max(A + B * s / t, 0.0);
}

/// <summary>Minnaert 边缘变暗漫反射；k = 1 退化为 Lambert，k 越小边缘越暗。</summary>
real3 Diff_Minnaert(real3 albedo, real NdotL, real NdotV, real k)
{
    return Diff_Lambert(albedo)
         * pow(max(NdotL, 1e-4), k)
         * pow(max(NdotV, 1e-4), k - 1.0);
}

/// <summary>Ashikhmin-Shirley 各向异性漫反射，掠射角变亮，适合织物与磨砂金属底色。</summary>
real3 Diff_AshikhminShirley(real3 albedo, real NdotL, real NdotV)
{
    real fL = 1.0 - pow(1.0 - 0.5 * saturate(NdotL), 5.0);
    real fV = 1.0 - pow(1.0 - 0.5 * saturate(NdotV), 5.0);
    return albedo * (28.0 / (23.0 * PI)) * fL * fV;
}

/// <summary>Wrap 漫反射：把明暗交界推向背光面，配合 NdotL 裁剪可做廉价次表面。</summary>
real3 Diff_Wrap(real3 albedo, real NdotL, real wrap)
{
    real w = saturate(wrap);
    return Diff_Lambert(albedo) * saturate((NdotL + w) / (1.0 + w));
}

/// <summary>Cook-Torrance 高光项，菲涅耳权重由调用方乘入；D 用 alpha、G 用感知粗糙度。</summary>
real Spec_CK(real NdotH, real NdotV, real NdotL, real VdotH, real roughness)
{
    real alpha = roughness * roughness;
    real D = Spec_D_GGX(NdotH, alpha);
    real G = Spec_G_Smith(NdotV, NdotL, roughness);

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

/// <summary>组合 Oren-Nayar 漫反射与 Cook-Torrance 高光反射。</summary>
real3 BRDF_OrenNayar(real3 baseColor, real NdotL, real NdotV, real NdotH, real VdotH,
                     real VdotL, real roughness, real sigma, real metallic)
{
    real3 F0 = lerp(0.04, baseColor, metallic);
    real3 F  = F_P5(F0, VdotH);

    real3 specular = Spec_CK(NdotH, NdotV, NdotL, VdotH, roughness) * F;
    real3 diffuse  = Diff_OrenNayar(baseColor, NdotL, NdotV, VdotL, sigma) * (1.0 - metallic) * (1.0 - F);
    return specular + diffuse;
}

/// <summary>Charlie 布料光泽瓣：分布 × 可见性 × 绒面色，漫射由调用方另行提供。</summary>
real3 BRDF_Sheen(real3 sheenColor, real NdotL, real NdotV, real NdotH, real LdotH, real roughness)
{
    real3 F = sheenColor + (1.0 - sheenColor) * pow(saturate(1.0 - LdotH), 5.0);
    return F * (Spec_D_Charlie(NdotH, roughness) * Spec_V_Neubelt(NdotL, NdotV));
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
    real perceptual = sqrt(alpha);
    real D = Spec_D_GGX(saturate(dot(N, H)), alpha);
    real G = Spec_G_Smith(saturate(abs(NdotV)), saturate(abs(NdotL)), perceptual);
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
        real G = Spec_G_Smith(abs(NdotV), abs(NdotL), roughness);
        real J = Trans_J_Refract(NdotV, NdotL, VdotH, LdotH, eta);
        real3 specTransmit = saturate(transColor) * (D * G * J);
        transmission = specTransmit * (1.0 - F) * weight;
    }
}

#endif // PBRFUNCTION_HLSL_INCLUDED
