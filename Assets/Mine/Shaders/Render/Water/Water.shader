// ═══════════════════════════════════════════════════════════════
//  Water.shader — FFT 位移、BRDF 界面、单步体积散射、泡沫与单点反推焦散
//  私有功能库：Assets/Mine/Shaders/Render/Water/WaterFunction.hlsl
// ═══════════════════════════════════════════════════════════════

Shader "Render/Water"
{
    Properties
    {
        [Header(BaseColor)]
        _baseColorA("Shallow Scatter Color", Color) = (0.02, 0.12, 0.18, 1)
        _baseColorB("Deep Scatter Color", Color) = (0.1, 0.42, 0.48, 1)
        _Extinction("Extinction RGB (per meter)", Vector) = (0.35, 0.09, 0.045, 0)

        [Header(Scattering)]
        _ScatterIntensity("Scatter Intensity", Range(0, 2)) = 1.0
        _ScatterAnisotropy("Scatter Anisotropy", Range(-0.9, 0.9)) = 0.7
        _ScatterDepth("Scatter Depth", Range(0.1, 10)) = 5.0
        _ScatterDensity("Scatter Density", Range(0.1, 10)) = 2.0

        [Header(Interface)]
        _IOR("Index of Refraction", Range(1, 2)) = 1.333
        _Roughness("Surface Roughness", Range(0, 1)) = 0.08

        [Header(Geometry)]
        _HeightIntensity("Height Intensity", Range(0, 2)) = 0.1
        _NormalIntensity("Normal Intensity", Range(0, 2)) = 1.0
        _Tessellation("Tessellation", Range(1, 32)) = 8

        [Header(Custom)]
        _FoamIntensity("Foam Intensity", Range(0, 2)) = 1.0
        _CausticsIntensity("Caustics Intensity", Range(0, 2)) = 1.0
        _RefractionIntensity("Refraction Intensity", Range(0, 2)) = 1.0

        [Header(Interaction)]
        _InteractionHeightScale("Interaction Height Scale", Range(0, 4)) = 1.0
        _InteractionNormalStrength("Interaction Normal Strength", Range(0, 4)) = 1.0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/DepthDiffFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/LightFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/NPRFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/BlendFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/PBRFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/ENVFunction.hlsl"

    #include "Assets/Mine/Scripts/FieldManager/Wave/WaveField.hlsl"
    #include "Assets/Mine/Scripts/InteractionManager/Shaders/InteractionField.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _baseColorA;
        float4 _baseColorB;
        float4 _Extinction;

        float _ScatterIntensity;
        float _ScatterAnisotropy;
        float _ScatterDepth;
        float _ScatterDensity;

        float _IOR;
        float _Roughness;

        float _HeightIntensity;
        float _NormalIntensity;
        float _Tessellation;

        float _FoamIntensity;
        float _CausticsIntensity;
        float _RefractionIntensity;

        float _InteractionHeightScale;
        float _InteractionNormalStrength;
    CBUFFER_END

    #include "Assets/Mine/Shaders/Render/Water/WaterFunction.hlsl"

    // ── 结构体 ──────────────────────────────────────────────

    struct WaterAttributes
    {
        float3 positionOS   : POSITION;
        float3 normalOS     : NORMAL;
        float4 tangentOS    : TANGENT;
        float2 uv           : TEXCOORD0;
    };

    // 控制点输出: Vert → Hull
    struct HullControlPoint
    {
        float3 positionOS : INTERNALTESSPOS;
        float3 normalOS   : NORMAL;
        float4 tangentOS  : TANGENT;
        float2 uv         : TEXCOORD0;
    };

    // Domain → Frag
    struct WaterVaryings
    {
        float4 positionCS   : SV_POSITION;
        float3 positionWS   : TEXCOORD0;
        float3 normalWS     : TEXCOORD1;
        float3 tangentWS    : TEXCOORD2;
        float3 bitanentWS   : TEXCOORD3;
        float2 uv           : TEXCOORD4;
    };

    // ════════════════════════════════════════════════════════════
    //  Vert — 控制点 pass-through（不做位移，位移在 Domain）
    // ════════════════════════════════════════════════════════════
    HullControlPoint Vert(WaterAttributes input)
    {
        HullControlPoint output;
        output.positionOS = input.positionOS;
        output.normalOS   = input.normalOS;
        output.tangentOS  = input.tangentOS;
        output.uv         = input.uv;
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Tessellation — Hull + Domain
    // ════════════════════════════════════════════════════════════
    struct TessellationFactors
    {
        float edge[3]  : SV_TessFactor;
        float inside   : SV_InsideTessFactor;
    };

    TessellationFactors HullConst(InputPatch<HullControlPoint, 3> patch)
    {
        TessellationFactors f;
        f.edge[0] = _Tessellation;
        f.edge[1] = _Tessellation;
        f.edge[2] = _Tessellation;
        f.inside  = _Tessellation;
        return f;
    }

    [domain("tri")]
    [partitioning("integer")]
    [outputtopology("triangle_cw")]
    [outputcontrolpoints(3)]
    [patchconstantfunc("HullConst")]
    HullControlPoint Hull(InputPatch<HullControlPoint, 3> patch, uint id : SV_OutputControlPointID)
    {
        return patch[id];
    }

    // ════════════════════════════════════════════════════════════
    //  Domain — 细分顶点 + FFT 位移
    // ════════════════════════════════════════════════════════════
    [domain("tri")]
    WaterVaryings Domain(
        TessellationFactors factors,
        OutputPatch<HullControlPoint, 3> patch,
        float3 bary : SV_DomainLocation)
    {
        WaterVaryings output;

        // 重心坐标插值
        float3 positionOS = patch[0].positionOS * bary.x
                          + patch[1].positionOS * bary.y
                          + patch[2].positionOS * bary.z;
        float3 normalOS   = patch[0].normalOS   * bary.x
                          + patch[1].normalOS   * bary.y
                          + patch[2].normalOS   * bary.z;
        float4 tangentOS  = patch[0].tangentOS  * bary.x
                          + patch[1].tangentOS  * bary.y
                          + patch[2].tangentOS  * bary.z;
        float2 uv         = patch[0].uv         * bary.x
                          + patch[1].uv         * bary.y
                          + patch[2].uv         * bary.z;

        float3 positionWS = TransformObjectToWorld(positionOS);                      // 位移前
        positionWS += ComputeFFTWave(positionWS);                                    // FFT（FieldManager 发布）
        positionWS += ComputeInteractionWave(positionWS);                            // 交互凹陷（正交交互 RT）

        output.positionCS = TransformWorldToHClip(positionWS);
        output.positionWS = positionWS;
        output.normalWS   = TransformObjectToWorldNormal(normalOS);
        output.tangentWS  = TransformObjectToWorldDir(tangentOS.xyz);
        output.bitanentWS = cross(output.normalWS, output.tangentWS) * tangentOS.w * unity_WorldTransformParams.w;
        output.uv = uv;
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 界面 BRDF + 单步体积散射合成
    // ════════════════════════════════════════════════════════════
    half4 Frag(WaterVaryings input) : SV_Target
    {
        float3 camPos      = GetCameraPositionWS();
        float3 positionWS  = input.positionWS;
        float3 defaultNormalWS = normalize(input.normalWS);
        float3 normalWS    = ComputeFFTNormal(positionWS, defaultNormalWS);
        normalWS           = ComputeInteractionNormal(positionWS, normalWS);
        float3 viewDirWS   = normalize(camPos - positionWS);

        float3 mainLitDir;
        float3 mainLitColor;
        float  mainLitDistanceAtten;
        float  mainLitShadowAtten;
        MainLight(positionWS, mainLitDir, mainLitColor, mainLitDistanceAtten, mainLitShadowAtten);

        // ── 折射路径：基础屏幕坐标估计水柱厚度 ──
        float4 positionSSBasic = ComputeScreenPos(TransformWorldToHClip(positionWS));
        float2 basicUV         = positionSSBasic.xy / positionSSBasic.w;
        float  basicRawDepth   = SampleSceneDepth(basicUV);
        float3 basicScenePosWS = ComputeWorldSpacePosition(basicUV, basicRawDepth, UNITY_MATRIX_I_VP);
        float  basicWaterDepth = max(positionWS.y - basicScenePosWS.y, 0.0);

        float3 refrDir       = Water_RefractionDirection(normalWS, viewDirWS);
        float3 offsetWS      = Water_RefractionOffsetWS(refrDir, basicWaterDepth);
        float4 positionSS    = ComparePositionSS(
            positionWS,
            Water_RefractionSS(positionWS, offsetWS),
            positionSSBasic);

        float2 screenUV   = positionSS.xy / positionSS.w;
        float  sceneDepth = SampleSceneDepth(screenUV);
        float3 scenePosWS = ComputeWorldSpacePosition(screenUV, sceneDepth, UNITY_MATRIX_I_VP);
        float3 sceneNorWS = SampleSceneNormals(screenUV);
        float  sceneDepDf = max(positionWS.y - scenePosWS.y, 0.0);
        float  pathLen    = Water_PathLength(sceneDepDf, refrDir);

        // ── 界面：Fresnel 分配反射与透射 ──
        float  NdotV       = saturate(dot(normalWS, viewDirWS));
        float  F           = Water_InterfaceFresnel(NdotV);
        float3 envRadiance = Water_EnvReflection(normalWS, viewDirWS, _Roughness);
        float3 sunSpecular = Water_SunSpecular(normalWS, viewDirWS, mainLitDir,
                                               mainLitColor * mainLitDistanceAtten,
                                               mainLitShadowAtten, _Roughness);
        float3 reflection  = envRadiance * F + sunSpecular;

        // ── 体积：吸收 + 单步 in-scattering ──
        //  覆盖率 C 与背景做混合而非相加：薄水（0.2 m）加性散射只有几个百分点，
        //  亮水底上完全看不出水色；混合后薄水即有明显水体色，深水 C→1 退化为纯水体色。
        float3 lightDirWater = Water_LightDirectionUnderwater(mainLitDir);
        float3 transmittance = exp(-_Extinction.xyz * pathLen);
        float  coverage      = Water_BodyCoverage(pathLen);
        float3 bodyRadiance  = Water_BodyRadiance(pathLen, refrDir, lightDirWater,
                                                  mainLitColor * mainLitDistanceAtten,
                                                  mainLitShadowAtten);

        float normalDiff = dot(normalWS, defaultNormalWS);
        float caustics   = 0.0;
        if (sceneDepDf > 0.0 && sceneDepDf < _ScatterDepth)
        {
            caustics = ComputeCaustics(positionWS, mainLitDir, scenePosWS, sceneDepDf, sceneNorWS);
        }

        float3 background = SampleSceneColor(screenUV);
        background += caustics.xxx * mainLitColor;
        float3 waterColor = background * transmittance * (1.0 - coverage) + bodyRadiance * coverage;

        // ── 合成 ──
        float3 color = reflection + (1.0 - F) * waterColor;

        // ── 泡沫覆盖层 ──
        float edge = ComputeEdgeFoam(sceneDepDf, positionWS, normalDiff);
        float foam = saturate(edge);
        float3 foamLit = mainLitColor * mainLitShadowAtten * saturate(dot(normalWS, mainLitDir)) + SampleSH(normalWS);
        color = lerp(color, foamLit, foam);

        return half4(color, 1);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        LOD 200

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex Vert
            #pragma hull Hull
            #pragma domain Domain
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS_VERTEX
            ENDHLSL
        }

        // ════════════════════════════════════════════════════════════
        //  Pass — 深度参照（只被 TransparentDepthMergeFeature 用到）
        //
        //  水面是透明队列，正常情况下不写深度；交互相机要让「水面本身」
        //  当穿透深度的参照面，就靠 TransparentDepthMergeFeature 用这个
        //  LightMode 把透明队列再画一遍（强制 ZWrite）并进 _CameraDepthTexture。
        //
        //  这里刻意**不细分、不位移**：
        //  - 不位移是必须的。把 ComputeInteractionWave 算进参照面，就会变成
        //    「水面被压下去 → 参照面更深 → 穿透更大 → 压得更深」的正反馈。
        //  - 不细分是因为位移前就是一块平板，顶点直接变换即可，省一遍曲面细分。
        //    想让参照面跟着 FFT 大浪起伏，就得把 hull/domain 一起搬过来。
        // ════════════════════════════════════════════════════════════
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthOnlyVert
            #pragma fragment DepthOnlyFrag

            float4 DepthOnlyVert(WaterAttributes input) : SV_POSITION
            {
                return TransformObjectToHClip(input.positionOS);
            }

            half4 DepthOnlyFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
