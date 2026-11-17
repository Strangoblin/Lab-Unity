// ═══════════════════════════════════════════════════════════════
//  WaterFunction.hlsl — Water 专属波浪、BRDF 界面与单步体积散射
//
//  依赖调用方定义：
//    · Core / Lighting / DeclareOpaqueTexture / DeclareDepthTexture
//    · DepthDiffFunction / PBRFunction / ENVFunction
//    · FFT displacement/normal 纹理、patch size 与 Water 材质参数
//  使用方式：由 Water.shader 在上述声明完成后 include。
// ═══════════════════════════════════════════════════════════════

#ifndef WATERFUNCTION_HLSL_INCLUDED
#define WATERFUNCTION_HLSL_INCLUDED

// ════════════════════════════════════════════════════════════
//  ComputeFFTWave — 3 级 cascade 波浪混合
// ════════════════════════════════════════════════════════════
float3 ComputeFFTWave(float3 positionWS)
{
    return SampleWaveDisplacementWS(positionWS) * _HeightIntensity;
}

// ════════════════════════════════════════════════════════════
//  ComputeFFTNormal — 3 级 cascade 法线混合
// ════════════════════════════════════════════════════════════
float3 ComputeFFTNormal(float3 positionWS, float3 defaultNormalWS)
{
    float3 normal = SampleWaveNormalBlendWS(positionWS);
    return normalize(lerp(defaultNormalWS, normal, _NormalIntensity));
}

// ════════════════════════════════════════════════════════════
//  ComputeFFTFoam — 3 级 displacement alpha 泡沫混合
// ════════════════════════════════════════════════════════════
float3 ComputeFFTFoam(float3 positionWS)
{
    return SampleWaveFoam(positionWS);
}

// ════════════════════════════════════════════════════════════
//  ComputeInteractionWave — 交互高度场 → 垂直位移
// ════════════════════════════════════════════════════════════
float3 ComputeInteractionWave(float3 positionWS)
{
    float height = SampleInteractionHeight(positionWS);
    return float3(0.0, height * _InteractionHeightScale, 0.0);
}

// ════════════════════════════════════════════════════════════
//  ComputeInteractionNormal — 交互高度场梯度叠加到 FFT 法线
// ════════════════════════════════════════════════════════════
float3 ComputeInteractionNormal(float3 positionWS, float3 normalWS)
{
    float2 slope = InteractionSlopeXZ(positionWS, _InteractionHeightScale);
    return ApplyInteractionSlope(normalWS, slope, _InteractionNormalStrength);
}

// ════════════════════════════════════════════════════════════
//  Water_F0 — 界面反射率 F0 = ((n − 1) / (n + 1))²
// ════════════════════════════════════════════════════════════
float3 Water_F0()
{
    float f0 = (_IOR - 1.0) / (_IOR + 1.0);
    float f0sq = f0 * f0;
    return float3(f0sq, f0sq, f0sq);
}

// ════════════════════════════════════════════════════════════
//  Water_InterfaceFresnel — 界面 Fresnel，含全反射判断
// ════════════════════════════════════════════════════════════
float Water_InterfaceFresnel(float NdotV)
{
    return BSDF_Fresnel(NdotV, 1.0, _IOR);
}

// ════════════════════════════════════════════════════════════
//  Water_EnvReflection — 环境反射辐亮度，Fresnel 由调用方乘
// ════════════════════════════════════════════════════════════
float3 Water_EnvReflection(float3 normalWS, float3 viewDirWS, float roughness)
{
    float3 reflectDir = reflect(-viewDirWS, normalWS);
    const float kHorizonClamp = 0.05;
    reflectDir.y = max(reflectDir.y, kHorizonClamp);
    reflectDir   = normalize(reflectDir);

    float  mip = PerceptualRoughnessToMipmapLevel(saturate(roughness));
    return DecodeHDREnvironment(
        unity_SpecCube0.SampleLevel(samplerunity_SpecCube0, reflectDir, mip),
        unity_SpecCube0_HDR);
}

// ════════════════════════════════════════════════════════════
//  Water_SunSpecular — GGX 太阳高光，菲涅耳取 VdotH
// ════════════════════════════════════════════════════════════
float3 Water_SunSpecular(float3 normalWS, float3 viewDirWS, float3 lightDirWS,
                         float3 lightColor, float shadowAtten, float roughness)
{
    float3 halfVec = lightDirWS + viewDirWS;
    float  halfLenSq = dot(halfVec, halfVec);
    halfVec = halfLenSq > 1e-8 ? halfVec * rsqrt(halfLenSq) : normalWS;

    float NdotV = saturate(dot(normalWS, viewDirWS));
    float NdotL = saturate(dot(normalWS, lightDirWS));
    float NdotH = saturate(dot(normalWS, halfVec));
    float VdotH = saturate(dot(viewDirWS, halfVec));
    float r     = max(roughness, 0.02);

    float3 F = F_P5(Water_F0(), VdotH);
    float  D = Spec_CK(NdotH, NdotV, NdotL, VdotH, r);
    return D * F * lightColor * shadowAtten * NdotL;
}

// ════════════════════════════════════════════════════════════
//  Water_RefractionDirection — 水面折射方向（空气 → 水）
// ════════════════════════════════════════════════════════════
float3 Water_RefractionDirection(float3 normalWS, float3 viewDirWS)
{
    float3 refrDir = refract(-viewDirWS, normalWS, 1.0 / _IOR);
    return dot(refrDir, refrDir) > 1e-4 ? refrDir : -viewDirWS;
}

// ════════════════════════════════════════════════════════════
//  Water_LightDirectionUnderwater — 主光进入水体后的传播方向
// ════════════════════════════════════════════════════════════
float3 Water_LightDirectionUnderwater(float3 lightDirWS)
{
    float3 lightDirWater = refract(-lightDirWS, float3(0, 1, 0), 1.0 / _IOR);
    if (dot(lightDirWater, lightDirWater) < 1e-4)
    {
        lightDirWater = normalize(float3(lightDirWS.x, -max(lightDirWS.y, 0.1), lightDirWS.z));
    }
    return lightDirWater;
}

// ════════════════════════════════════════════════════════════
//  Water_RefractionOffsetWS — 折射的水平世界偏移，随水柱厚度缩放
// ════════════════════════════════════════════════════════════
float3 Water_RefractionOffsetWS(float3 refrDir, float verticalDepth)
{
    float scale = max(verticalDepth, 0.0) * _RefractionIntensity * 0.05;
    return float3(refrDir.x, 0.0, refrDir.z) * scale;
}

// ════════════════════════════════════════════════════════════
//  Water_PathLength — 沿折射光线的水柱路径长度
// ════════════════════════════════════════════════════════════
float Water_PathLength(float verticalDepth, float3 refrDir)
{
    float pathLen = max(verticalDepth, 0.0) / max(abs(refrDir.y), 0.1);
    return pathLen;
}

// ════════════════════════════════════════════════════════════
//  Water_PhaseHG — 峰值归一化的 Henyey-Greenstein 相函数
// ════════════════════════════════════════════════════════════
float Water_PhaseHG(float cosTheta, float g)
{
    g = clamp(g, -0.99, 0.99);
    float g2 = g * g;
    float denom = max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4);
    float denomForward = max(1.0 + g2 - 2.0 * g, 1e-4);
    return pow(denomForward / denom, 1.5);
}

// ════════════════════════════════════════════════════════════
//  Water_BodyCoverage — 水体覆盖率 (0..1)：薄水也要起色
// ════════════════════════════════════════════════════════════
float Water_BodyCoverage(float pathLen)
{
    return 1.0 - exp(-max(pathLen, 0.0) * _ScatterDensity);
}

// ════════════════════════════════════════════════════════════
//  Water_ScatterColor — 浅水/深水散射色，由路径长度插值
// ════════════════════════════════════════════════════════════
float3 Water_ScatterColor(float pathLen)
{
    float t = saturate(pathLen / max(_ScatterDepth, 0.001));
    return lerp(_baseColorA.rgb, _baseColorB.rgb, t);
}

// ════════════════════════════════════════════════════════════
//  Water_BodyRadiance — 深水极限的水体辐亮度（不含覆盖率）
// ════════════════════════════════════════════════════════════
float3 Water_BodyRadiance(float pathLen, float3 refrDir, float3 lightDirWater,
                          float3 lightColor, float shadowAtten)
{
    float  phase    = Water_PhaseHG(dot(refrDir, lightDirWater), _ScatterAnisotropy);
    float3 ambient  = SampleSH(float3(0.0, 1.0, 0.0));

    float  sunPath  = 1.0 + 1.0 / max(abs(lightDirWater.y), 0.1);
    float3 sunTerm  = lightColor * shadowAtten * phase / sunPath;

    return Water_ScatterColor(pathLen) * (sunTerm + ambient) * _ScatterIntensity;
}

// ════════════════════════════════════════════════════════════
//  Water_RefractionSS — 折射屏幕坐标
// ════════════════════════════════════════════════════════════
float4 Water_RefractionSS(float3 positionWS, float3 offsetWS)
{
    float4 positionCS = TransformWorldToHClip(positionWS + offsetWS);
    return ComputeScreenPos(positionCS);
}

// ════════════════════════════════════════════════════════════
//  ComparePositionSS — 折射屏幕坐标的深度回退
// ════════════════════════════════════════════════════════════
float4 ComparePositionSS(float3 positionWS, float4 positionSSDetail, float4 positionSSBasic)
{
    float  depth = ComputeRelDepthDiff(positionWS, positionSSDetail);
    float  branch = step(depth, 0.0);
    return lerp(positionSSDetail, positionSSBasic, branch);
}

// ════════════════════════════════════════════════════════════
//  ComputeEdgeFoam — 深度边缘泡沫
// ════════════════════════════════════════════════════════════
float ComputeEdgeFoam(float edge, float3 positionWS, float normalDiff)
{
    edge = saturate(exp(- edge * _FoamIntensity * 10));
    float offset = normalDiff;
    float3 coord = float3(positionWS.y - offset, 1, (edge + offset) * 2);
    float  foam  = ComputeFFTFoam(coord).x;
    float  edgeFoam = lerp(edge, foam, 0.75) * edge;
    edgeFoam = smoothstep(0.15, 0.16, edgeFoam);
    return edgeFoam;
}

// ════════════════════════════════════════════════════════════
//  ComputeWaveFoam — Jacobian 波峰泡沫
// ════════════════════════════════════════════════════════════
float ComputeWaveFoam(float3 positionWS)
{
    float foam = ComputeFFTFoam(positionWS).y;
    foam = smoothstep(_FoamIntensity, 1.0, foam);
    return foam;
}

// ════════════════════════════════════════════════════════════
//  ComputeCaustics — 水面反推残差生成单点焦散
// ════════════════════════════════════════════════════════════
float ComputeCaustics(float3 positionWS, float3 mainLitDir,
                      float3 scenePosWS, float sceneDepDf, float3 sceneNorWS)
{
    float  eta = 1.0 / 1.33;
    float3 refractDir = refract(-mainLitDir, float3(0, 1, 0), eta);
    float3 surfaceRay = refractDir * (positionWS.y - scenePosWS.y) / refractDir.y;
    float3 surfaceHit = scenePosWS + surfaceRay;
    float3 surfaceNor = ComputeFFTNormal(surfaceHit, float3(0, 1, 0));
    surfaceNor = ComputeInteractionNormal(surfaceHit, surfaceNor);
    float3 correctDir = refract(-mainLitDir, surfaceNor, eta);
    float3 correctRay = correctDir * (positionWS.y - scenePosWS.y) / correctDir.y;
    float3 correctHit = scenePosWS + correctRay;
    float3 correctNor = ComputeFFTNormal(correctHit, float3(0, 1, 0));
    correctNor = ComputeInteractionNormal(correctHit, correctNor);

    float eps = 0.001 * (1.0 + sceneDepDf);

    float3 surfaceResidualVector = correctHit - surfaceHit;
    float  surfaceResidualRadius = eps * 2.0;
    float  confidence = rcp(1.0 + dot(surfaceResidualVector, surfaceResidualVector) / (surfaceResidualRadius * surfaceResidualRadius));
    float  intensity = _CausticsIntensity * confidence;

    float sceneNorDf = dot(mainLitDir, sceneNorWS) * 0.5 + 0.5;
    intensity *= sceneNorDf * sceneDepDf;
    return intensity;
}

#endif // WATERFUNCTION_HLSL_INCLUDED
