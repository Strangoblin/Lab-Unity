// ════════════════════════════════════════════════════════════
//  SSO — 屏幕空间轮廓（Screen Space Outline）
//
//  Unity 6 URP 17+ 全屏后处理，两个 Pass：
//    Pass 0  深度 / 法线差分检测轮廓，叠加投影条纹阴影，输出 4 通道中间结果
//    Pass 1  用中间结果与场景色合成轮廓与条纹
//  差分方式（DDXY / Basic / Sobel）与阴影方式（None / Hard / Soft）由关键字切换。
//  源色统一取 Blit.hlsl 的 _BlitTexture；轮廓 / 条纹中间纹理 _SSOTex 由
//  SSOFeature 用 cmd.SetGlobalTexture 发布。
// ════════════════════════════════════════════════════════════
Shader "PostProcess/SSO"
{
    Properties
    {
        // 参数由 SSOFeature 传入，无需 Inspector 暴露
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Assets/Mine/Special/HLSL/DeclareCustomTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/HashFunction.hlsl"

    // ── 参数 ──
    CBUFFER_START(UnityPerMaterial)
        float  _DepthThickness;
        float  _DepthIntensity;
        float2 _DepthThreshold;

        float  _NormalThickness;
        float  _NormalIntensity;
        float2 _NormalThreshold;

        float  _ShadowIntensity;
        float  _ShadowSharpness;
        float  _ShadowThickness;
        float  _ShadowDensity;

        float  _Jitter;
        float4 _OutlineColor;
    CBUFFER_END

    // ── 中间纹理（Feature 端 cmd.SetGlobalTexture 发布） ──
    TEXTURE2D_X(_SSOTex);

    // ── 常量：差分采样偏移 ──
    static const float2 UVoffsetsDDXY[2] =
    {
        float2(-1, 0), float2(0, -1)
    };

    static const float2 UVoffsetsBasic[4] =
    {
        float2(-1, 0), float2(1, 0),
        float2(0, -1), float2(0, 1)
    };

    static const float2 UVoffsetsSobel[8] =
    {
        float2(-1, -1), float2(0, -1), float2(1, -1),
        float2(-1,  0),                float2(1,  0),
        float2(-1,  1), float2(0,  1), float2(1,  1)
    };

    // ════════════════════════════════════════════════════════════
    //  采样封装 — 深度取线性眼空间，法线归一化
    // ════════════════════════════════════════════════════════════
    float SampleDepth(float2 uv)
    {
        float rawDepth = SampleSceneDepth(uv);
        return LinearEyeDepth(rawDepth, _ZBufferParams);
    }

    float3 SampleNormal(float2 uv)
    {
        float3 normal = SampleSceneNormals(uv);
        return normalize(normal);
    }

    // ════════════════════════════════════════════════════════════
    //  Hash21 — 2D hash 映射到 [-1, 1]，用于采样偏移抖动
    // ════════════════════════════════════════════════════════════
    float2 Hash21(float2 p)
    {
        return Hash_Frac22(p) * 2.0 - 1.0;
    }

    // ════════════════════════════════════════════════════════════
    //  OffsetUV — 差分采样偏移，按 _Jitter 在偏移上叠加随机扰动
    // ════════════════════════════════════════════════════════════
    float2 OffsetUV(float2 uv, float2 uvoffsets, float thickness)
    {
        float2 jitter = Hash21(uv + uvoffsets);
        float2 offset = uvoffsets * thickness;
        return uv + offset + jitter * offset * _Jitter;
    }

    // ════════════════════════════════════════════════════════════
    //  RotateUV — 绕 UV 原点旋转，用于条纹 45 度取向
    // ════════════════════════════════════════════════════════════
    float2 RotateUV(float2 uv, float angle)
    {
        float s = sin(angle);
        float c = cos(angle);
        return float2(
            uv.x * c - uv.y * s,
            uv.x * s + uv.y * c
        );
    }

    // ════════════════════════════════════════════════════════════
    //  ShadowLine — 45 度密度条纹，_ShadowDensity 控制疏密
    // ════════════════════════════════════════════════════════════
    float ShadowLine(float2 uv, float density, float thickness)
    {
        float2 jitter = Hash21(uv);
        uv += jitter * thickness * 0.1;
        uv = RotateUV(uv, 45.0 * PI / 180.0);
        float shadowline = abs(frac(uv.x * 1 / density) - 0.5);
        return smoothstep(thickness, 0.0, shadowline);
    }

    // ProjectionFunction.hlsl 的三平面投影用 SAMPLE_CUSTOM 取纹素；
    // 本效果复用该投影路径生成条纹（纹素参数被覆盖，不再读取贴图），
    // 故必须在 include 之前覆盖宏定义。
    #ifndef SAMPLE_CUSTOM
    #define SAMPLE_CUSTOM(tex, sam, uv) \
        ShadowLine(uv, _ShadowDensity, _ShadowThickness * 0.1)
    #endif
    #include "Assets/Mine/Special/HLSL/ProjectionFunction.hlsl"

    // ════════════════════════════════════════════════════════════
    //  DepthDiffDDXY / Basic / Sobel — 三档深度差分
    // ════════════════════════════════════════════════════════════
    float DepthDiffDDXY(float depth, float2 uv, float2 thickness)
    {
        float depthDiff = 0.0;

        [unroll]
        for (int i = 0; i < UVoffsetsDDXY.Length; i++)
        {
            float2 offsetUV = OffsetUV(uv, UVoffsetsDDXY[i], thickness);
            float  offsetDepth = SampleDepth(offsetUV);
            depthDiff += abs(depth - offsetDepth);
        }
        return depthDiff;
    }

    float DepthDiffBasic(float depth, float2 uv, float2 thickness)
    {
        float depthDiff = 0.0;

        [unroll]
        for (int i = 0; i < UVoffsetsBasic.Length; i++)
        {
            float2 offsetUV = OffsetUV(uv, UVoffsetsBasic[i], thickness);
            float  offsetDepth = SampleDepth(offsetUV);
            depthDiff += abs(depth - offsetDepth);
        }
        return depthDiff;
    }

    float DepthDiffSobel(float depth, float2 uv, float2 thickness)
    {
        float depthDiff = 0.0;
        float depthDiffs[8];

        [unroll]
        for (int i = 0; i < UVoffsetsSobel.Length; i++)
        {
            float2 offsetUV = OffsetUV(uv, UVoffsetsSobel[i], thickness);
            float  offsetDepth = SampleDepth(offsetUV);
            depthDiffs[i] = abs(depth - offsetDepth);
        }

        float gradx = depthDiffs[0] + 2.0 * depthDiffs[3] + depthDiffs[5]
                    - depthDiffs[2] - 2.0 * depthDiffs[4] - depthDiffs[7];
        float grady = depthDiffs[0] + 2.0 * depthDiffs[1] + depthDiffs[2]
                    - depthDiffs[5] - 2.0 * depthDiffs[6] - depthDiffs[7];
        float2 grad = float2(gradx, grady);
        depthDiff = length(grad);
        return depthDiff;
    }

    // ════════════════════════════════════════════════════════════
    //  NormalDiffDDXY / Basic — 两档法线差分（Sobel 复用 Basic）
    // ════════════════════════════════════════════════════════════
    float NormalDiffDDXY(float3 normal, float2 uv, float2 thickness)
    {
        float normalDiff = 0.0;

        [unroll]
        for (int i = 0; i < UVoffsetsDDXY.Length; i++)
        {
            float2 offsetUV = OffsetUV(uv, UVoffsetsDDXY[i], thickness);
            float3 offsetNormal = SampleNormal(offsetUV);
            normalDiff += 1 - dot(normal, offsetNormal);
        }
        return normalDiff;
    }

    float NormalDiffBasic(float3 normal, float2 uv, float2 thickness)
    {
        float normalDiff = 0.0;

        [unroll]
        for (int i = 0; i < UVoffsetsBasic.Length; i++)
        {
            float2 offsetUV = OffsetUV(uv, UVoffsetsBasic[i], thickness);
            float3 offsetNormal = SampleNormal(offsetUV);
            normalDiff += 1 - dot(normal, offsetNormal);
        }
        return normalDiff;
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 0: 轮廓检测 — 深度 / 法线差分 + 投影条纹阴影
    //
    //  输出：R = 轮廓强度, G = 受光因子, B = 条纹阴影, A = 1
    // ════════════════════════════════════════════════════════════
    half4 Frag_SSO(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = 1.0 / _ScreenParams.xy;

        float rawDepth = SampleSceneDepth(uv);
        float  depth  = SampleDepth(uv);
        float3 normal = SampleNormal(uv);

        Light mainLight = GetMainLight();
        float3 litDir = mainLight.direction;

        float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
        float3 viewDir = normalize(positionWS - _WorldSpaceCameraPos);
        float depthDiff;
        float normalDiff;

        #ifdef SSO_Basic
            depthDiff = DepthDiffBasic(depth, uv, _DepthThickness * texelSize);
            normalDiff = NormalDiffBasic(normal, uv, _NormalThickness * texelSize);
        #elif defined(SSO_Sobel)
            depthDiff = DepthDiffSobel(depth, uv, _DepthThickness * texelSize);
            normalDiff = NormalDiffBasic(normal, uv, _NormalThickness * texelSize);
        #elif defined(SSO_DDXY)
            depthDiff = DepthDiffDDXY(depth, uv, _DepthThickness * texelSize);
            normalDiff = NormalDiffDDXY(normal, uv, _NormalThickness * texelSize);
        #else
            depthDiff = 0.0;
            normalDiff = 0.0;
        #endif

        float depthMask = pow(dot(normal, -viewDir), 2);
        depthDiff = smoothstep(_DepthThreshold.x, _DepthThreshold.y, depthDiff * depthMask) * _DepthIntensity;
        normalDiff = smoothstep(_NormalThreshold.x, _NormalThreshold.y, normalDiff) * _NormalIntensity;

        float DiffShadow;
        #ifdef SSO_SHADOW_NONE
            DiffShadow = 1.0;
        #else
            float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
            float  shadowMask  = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_MainLightShadowmapTexture, shadowCoord);
            #ifdef SSO_SHADOW_HARD
            DiffShadow = LowCostTriplanarProjectionTex(_BlitTexture, sampler_LinearClamp, normal, positionWS, _ShadowSharpness * 100, 1.0).r;
            DiffShadow *= (1 - shadowMask) * _ShadowIntensity;
            #elif defined(SSO_SHADOW_SOFT)
            DiffShadow = HighCostTriplanarProjectionTex(_BlitTexture, sampler_LinearClamp, normal, positionWS, _ShadowSharpness * 100, 1.0).r;
            DiffShadow *= (1 - shadowMask) * _ShadowIntensity;
            #endif
        #endif

        float DiffTotal  = saturate(depthDiff + normalDiff);
        float DiffFactor = saturate(dot(normal, litDir) * 0.5 + 0.5);
        float4 Diff = float4(DiffTotal, DiffFactor, DiffShadow, 1);
        return Diff;
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 1: 合成 — 用轮廓强度混合描边色，条纹按受光因子调制
    // ════════════════════════════════════════════════════════════
    half4 Frag_Composite(Varyings input) : SV_Target
    {
        half4 ssoColor = SampleCustomTexture(_SSOTex, sampler_PointClamp, input.texcoord);
        half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
        half3 outlineColor = lerp(sceneColor.rgb, _OutlineColor.rgb, _OutlineColor.a);
        return half4(lerp(sceneColor.rgb, outlineColor * (ssoColor.g * 1.5 + 0.25), saturate(ssoColor.r + ssoColor.b)), sceneColor.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            Name "SSO"

            HLSLPROGRAM
            #pragma multi_compile _ SSO_Basic SSO_Sobel SSO_DDXY
            #pragma multi_compile _ SSO_SHADOW_NONE SSO_SHADOW_HARD SSO_SHADOW_SOFT
            #pragma vertex Vert
            #pragma fragment Frag_SSO
            ENDHLSL
        }

        Pass
        {
            Name "SSO_Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_Composite
            ENDHLSL
        }
    }
}
