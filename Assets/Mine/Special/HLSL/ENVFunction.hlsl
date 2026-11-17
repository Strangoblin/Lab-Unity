#ifndef ENVFUNCTION_HLSL_INCLUDED
#define ENVFUNCTION_HLSL_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"

// ── 全局 FGD LUT ──
// 由场景组件 FGDLutManager 探测自身挂载的 LUT 后自动设置/清除。
TEXTURE2D(_FGDLut);
SAMPLER(sampler_FGDLut);
float   _UseFGDLut;

#if !defined(SHADER_API_GLES)
real4 GenEnvFGDLut(real NdotV, real roughness, uint sampleCount)
{
    return IntegrateGGXAndDisneyDiffuseFGD(NdotV, roughness, sampleCount);
}
#endif

// ════════════════════════════════════════════════════════════════════════════
//  BRDF_Env — 环境光 IBL 计算
// ════════════════════════════════════════════════════════════════════════════
real3 BRDF_Env(real3 baseColor, real NdotV, real3 normalWS, real3 viewDirWS,
               real roughness, real metallic,
               TextureCube spec, SamplerState samSpec, real4 specHDR)
{
    real3 F0  = lerp(0.04, baseColor, metallic);

    real3 R   = reflect(-viewDirWS, normalWS);
    real  mip = PerceptualRoughnessToMipmapLevel(roughness);
    real3 Pre = DecodeHDREnvironment(spec.SampleLevel(samSpec, R, mip), specHDR);
    real3 Env = SampleSH(normalWS);

    // _FGDLut_TexelSize.z = width of bound texture（≥64 for real LUTs, ≤16 for fallback）
    if (_UseFGDLut > 0.5)
    {
        // FGD LUT 路径 — 裂项近似
        real3 Lut = SAMPLE_TEXTURE2D(_FGDLut, sampler_FGDLut, float2(NdotV, roughness)).rgb;
        real3 specFGD = F0 * Lut.g + (1.0 - F0) * Lut.r;
        real  diffFGD = Lut.b + 0.5;
        real3 specular = Pre * specFGD;
        real3 diffuse  = Env * diffFGD * baseColor * (1.0 - metallic) * (1.0 - specFGD);
        return specular + diffuse;
    }
    else
    {
        // 分析近似路径 — Karis 2013
        real  t                = 1.0 - NdotV;
        real  fresnelTerm      = t * t * t * t;
        real3 grazingTerm      = saturate(F0 + (1.0 - roughness));
        real  surfaceReduction = 1.0 / (roughness * roughness + 1.0);
        real3 specularEnv      = surfaceReduction * lerp(F0, grazingTerm, fresnelTerm);
        real3 specular = Pre * specularEnv;
        real3 diffuse  = Env * baseColor * (1.0 - metallic) * (1.0 - specularEnv);
        return specular + diffuse;
    }
}

#endif
