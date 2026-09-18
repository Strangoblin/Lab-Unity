// ═══════════════════════════════════════════════════════════════
//  Water.shader — FFT 位移、透明折射、泡沫与单点反推焦散
//  私有功能库：Assets/Mine/Shaders/Render/Water/WaterFunction.hlsl
// ═══════════════════════════════════════════════════════════════

Shader "Render/Water"
{
    Properties
    {
        [Header(Opaque)]
        _baseColorA("Base Color A", Color) = (1, 1, 1, 1)
        _baseColorB("Base Color B", Color) = (1, 1, 1, 1)

        [Header(FFT Wave)]
        _DisplacementScale("Displacement Scale", Float) = 1.0
        _NormalIntensity("Normal Intensity", Range(0, 2)) = 1.0
        _TessellationFactor("Tessellation Factor", Range(1, 32)) = 8
        _FoamIntensity("Foam Intensity", Range(0, 1)) = 0.5

        [Header(Caustics)]
        _CausticsScale("Caustics Scale", Range(0, 1)) = 0.0
        _CausticsIntensity("Caustics Intensity", Range(0, 2)) = 1.0

        [Header(Transparency)]
        _Distortion("Distortion", Range(0, 1)) = 0.1
        _Alpha("Alpha", Range(0, 1)) = 1.0
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

    // ── FFT Wave 全局纹理（由 FFTWaveOrchestrator 注入）──
    TEXTURE2D(_WaveDisplacement0); SAMPLER(sampler_WaveDisplacement0);
    TEXTURE2D(_WaveDisplacement1); SAMPLER(sampler_WaveDisplacement1);
    TEXTURE2D(_WaveDisplacement2); SAMPLER(sampler_WaveDisplacement2);
    TEXTURE2D(_WaveNormal0);       SAMPLER(sampler_WaveNormal0);
    TEXTURE2D(_WaveNormal1);       SAMPLER(sampler_WaveNormal1);
    TEXTURE2D(_WaveNormal2);       SAMPLER(sampler_WaveNormal2);
    float _WavePatchSize0, _WavePatchSize1, _WavePatchSize2;

    CBUFFER_START(UnityPerMaterial)
        float4 _baseColorA;
        float4 _baseColorB;

        float _DisplacementScale;
        float _NormalIntensity;
        float _TessellationFactor;
        float _FoamIntensity;

        float _CausticsScale;
        float _CausticsIntensity;

        float _Distortion;
        float _Alpha;
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
        f.edge[0] = _TessellationFactor;
        f.edge[1] = _TessellationFactor;
        f.edge[2] = _TessellationFactor;
        f.inside  = _TessellationFactor;
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
        positionWS += ComputeFFTWave(positionWS);

        output.positionCS = TransformWorldToHClip(positionWS);
        output.positionWS = positionWS;
        output.normalWS   = TransformObjectToWorldNormal(normalOS);
        output.tangentWS  = TransformObjectToWorldDir(tangentOS.xyz);
        output.bitanentWS = cross(output.normalWS, output.tangentWS) * tangentOS.w * unity_WorldTransformParams.w;
        output.uv = uv;
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 片元着色器
    // ════════════════════════════════════════════════════════════
    half4 Frag(WaterVaryings input) : SV_Target
    {
        float3 camPos      = GetCameraPositionWS();
        float3 positionWS  = input.positionWS;
        float3 defaultNormalWS = normalize(input.normalWS);
        float3 normalWS    = ComputeFFTNormal(positionWS, defaultNormalWS);
        float3 viewDirWS   = normalize(camPos - positionWS);

        float3 mainLitDir;
        float3 mainLitColor;
        float  mainLitDistanceAtten;
        float  mainLitShadowAtten;
        MainLight(positionWS, mainLitDir, mainLitColor, mainLitDistanceAtten, mainLitShadowAtten);

        float4 positionSS1 = ComputePositionSS(positionWS, normalWS, _Distortion * 0.1);
        float4 positionSS2 = ComputePositionSS(positionWS, normalWS, 0);
        float4 positionSS  = ComparePositionSS(positionWS, positionSS1, positionSS2);

        float2 screenUV   = positionSS.xy / positionSS.w;
        float  sceneDepth = SampleSceneDepth(screenUV);
        float3 sceneNorWS = SampleSceneNormals(screenUV);
        float3 scenePosWS = ComputeWorldSpacePosition(screenUV, sceneDepth, UNITY_MATRIX_I_VP);

        float normalDiff = dot(normalWS, defaultNormalWS);
        float sceneDepDf = positionWS.y - scenePosWS.y;
        float3 baseColor = lerp(_baseColorA.rgb, _baseColorB.rgb, saturate(sceneDepDf));

        float3 opaque      = ComputeOpaque(baseColor, positionWS, normalWS, viewDirWS, mainLitDir, mainLitColor, mainLitDistanceAtten, mainLitShadowAtten);
        float3 transparent = SampleSceneColor(screenUV);
        float  caustics    = ComputeCaustics(positionWS, mainLitDir, scenePosWS, sceneDepDf, sceneNorWS);
        float  edge        = ComputeEdgeFoam(sceneDepDf, positionWS, normalDiff);
        float  wave        = ComputeWaveFoam(positionWS);
        float  foam        = saturate(edge + wave);

        float3 color = lerp(transparent, opaque, _Alpha);
        color += (foam + caustics) * mainLitColor;

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
            Blend SrcAlpha OneMinusSrcAlpha

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
    }
}
