// ═══════════════════════════════════════════════════════════════
//  WaterFunction.hlsl — Water 专属波浪、光照、泡沫与焦散函数库
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
    return SampleWaveDisplacementWS(positionWS) * _DisplacementScale;
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
//  ComputeOpaque — PBR 光照
// ════════════════════════════════════════════════════════════
float3 ComputeOpaque(float3 baseColor, float3 positionWS, float3 normalWS, float3 viewDirWS, float3 mainLitDir, float3 mainLitColor, float mainLitDistanceAtten, float mainLitShadowAtten)
{
    float  roughness = 0.2;
    float  metallic = 0.0;
    float3 lightDirWS = normalize(mainLitDir);

    float3 halfVec = normalize(lightDirWS + viewDirWS);
    float  NdotL = max(0.0, dot(normalWS, lightDirWS));
    float  NdotV = max(0.0, dot(normalWS, viewDirWS));
    float  NdotH = max(0.0, dot(normalWS, halfVec));
    float  VdotH = max(0.0, dot(viewDirWS, halfVec));
    float  LdotH = max(0.0, dot(lightDirWS, halfVec));
    float  ndotl = dot(normalWS, lightDirWS) * 0.25 + 0.75;
    float  ndoth = dot(normalWS, halfVec) * 0.5 + 0.5;
    float  ndotv = dot(normalWS, viewDirWS) * 0.5 + 0.5;
    ndoth = smoothstep(0.9, 1.0, ndoth);

    float  shadowArea = mainLitShadowAtten * 0.5 + 0.5;
    float3 radiance = mainLitColor * mainLitDistanceAtten * shadowArea;
    float3 F0 = lerp(0.04, baseColor, metallic);
    float3 F  = F_Fast(F0, VdotH);

    float3 diffuse  = Diff_Lambert(baseColor) * PI * radiance * (1.0 - metallic) * (1.0 - F);
    float3 specular = Spec_Unity(ndoth, LdotH, VdotH, 0.0, 0.0, roughness, 0.0) * PI * radiance * ndotl * F;
    float3 ambient  = BRDF_Env(baseColor, ndotv, normalWS, viewDirWS, roughness, metallic, unity_SpecCube0, samplerunity_SpecCube0) * mainLitColor;
    return diffuse + specular + ambient;
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
    float3 correctDir = refract(-mainLitDir, surfaceNor, eta);
    float3 correctRay = correctDir * (positionWS.y - scenePosWS.y) / correctDir.y;
    float3 correctHit = scenePosWS + correctRay;
    float3 correctNor = ComputeFFTNormal(correctHit, float3(0, 1, 0));

    float eps = _CausticsScale * (1.0 + sceneDepDf);

    float3 surfaceResidualVector = correctHit - surfaceHit;
    float  surfaceResidualRadius = eps * 2.0;
    float  confidence = rcp(1.0 + dot(surfaceResidualVector, surfaceResidualVector) / (surfaceResidualRadius * surfaceResidualRadius));
    float  intensity = _CausticsIntensity * confidence;

    float sceneNorDf = dot(mainLitDir, sceneNorWS) * 0.5 + 0.5;
    intensity *= sceneNorDf * sceneDepDf;
    return intensity;
}

// ════════════════════════════════════════════════════════════
//  ComputePositionSS / ComparePositionSS — 折射屏幕坐标与穿透回退
// ════════════════════════════════════════════════════════════
float4 ComputePositionSS(float3 positionWS, float3 normalWS, float distortion)
{
    positionWS -= normalWS * distortion;
    float4 positionCS = TransformWorldToHClip(positionWS);
    float4 positionSS = ComputeScreenPos(positionCS);
    return positionSS;
}

float4 ComparePositionSS(float3 positionWS, float4 positionSSDetail, float4 positionSSBasic)
{
    float  depth = ComputeRelDepthDiff(positionWS, positionSSDetail);
    float  branch = step(depth, 0.0);
    return lerp(positionSSDetail, positionSSBasic, branch);
}

#endif // WATERFUNCTION_HLSL_INCLUDED
