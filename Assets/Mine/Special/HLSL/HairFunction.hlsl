// ════════════════════════════════════════════════════════════
//  HairFunction.hlsl — 毛发 R 与 TRT 双高光瓣共享函数库
//  用法：调用方使用 Assets/Mine/Special/HLSL/HairFunction.hlsl 全路径 include。
//  依赖：调用方先 include Core.hlsl，提供 real 类型；本库零 CBUFFER、零兄弟 include。
//  输出：切线偏移后的高光瓣响应；漫射、光照与阴影由调用方组合。
// ════════════════════════════════════════════════════════════

#ifndef HAIRFUNCTION_HLSL_INCLUDED
#define HAIRFUNCTION_HLSL_INCLUDED

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

#endif // HAIRFUNCTION_HLSL_INCLUDED
