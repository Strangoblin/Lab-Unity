Shader "PostProcess/SSSM"
{
    Properties
    {
        _StepSize ("Step Size", Range(0.1, 2.0)) = 0.5
        _MaxDistance ("Max Distance", Range(1, 200)) = 50.0
        _StepCount ("Step Count", Range(4, 128)) = 32
        _Thickness ("Thickness", Range(0.001, 0.5)) = 0.05
        _BlurScale ("Blur Scale", Range(0.0, 5.0)) = 1.0
    }

    HLSLINCLUDE
    // Unity 6 管线 — 全屏后处理必须使用 Blit.hlsl
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Assets/Mine/Special/HLSL/BlurFunction.hlsl"

    // ── 参数 ──
    CBUFFER_START(UnityPerMaterial)
        float _StepSize;
        float _MaxDistance;
        int   _StepCount;
        float _Thickness;
        float _BlurScale;
    CBUFFER_END
    // DDA 使用 Unity 内置矩阵（与 ComputeWorldSpacePosition / SampleSceneDepth 一致）
    // UNITY_MATRIX_V, UNITY_MATRIX_P — 自动匹配当前相机和图形 API

    // ════════════════════════════════════════════════════════════
    //  Pass 0: DDA 2D 雷步进 — 屏幕空间阴影追踪
    //
    //  核心思路（与 SSR 对称）：
    //     SSR:   沿反射方向步进 → 找第一个表面命中点
    //     SSSM:  沿光源方向步进 → 检查是否有遮挡物
    //
    //  输出：
    //    R: shadow factor（0=完全阴影, 1=完全照亮）
    //    G: 遮挡物平均深度（Eye Space, 保留给未来 PCSS 使用）
    // ════════════════════════════════════════════════════════════
    half4 Frag_SSSM_DDA(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;

        // ── 1. 重建世界坐标 ──
        float rawDepth = SampleSceneDepth(uv);
        float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

        // ── 2. 获取主光源方向（URP 约定：表面→光源） ──
        float3 lightDirWS = GetMainLight().direction;
        float3 viewDirWS  = normalize(_WorldSpaceCameraPos - positionWS);

        // ── 3. DDA Ray 设置（投影到屏幕空间） ──
        float3 startWS = positionWS;
        float3 endWS   = positionWS + lightDirWS * _MaxDistance;

        float3 startVS = mul(UNITY_MATRIX_V, float4(startWS, 1)).xyz;
        float3 endVS   = mul(UNITY_MATRIX_V, float4(endWS, 1)).xyz;

        float  factor = min(1.0, -startVS.z / _MaxDistance);
        endWS = lerp(startWS, endWS, factor);
        endVS = lerp(startVS, endVS, factor);

        float4 startCS = mul(UNITY_MATRIX_P, float4(startVS, 1));
        float4 endCS   = mul(UNITY_MATRIX_P, float4(endVS, 1));

        // 透视校正插值系数: K = 1/w
        float  startK = 1.0 / startCS.w;
        float  endK   = 1.0 / endCS.w;

        // 屏幕空间起始/终点 UV
        float2 startS = (float2(startCS.x, startCS.y * _ProjectionParams.x) * startK) * 0.5 + 0.5;
        float2 endS   = (float2(endCS.x, endCS.y * _ProjectionParams.x) * endK) * 0.5 + 0.5;

        // 透视校正插值: V = viewSpacePos * K
        float3 startV = startVS * startK;
        float3 endV   = endVS * endK;

        // 每步增量
        float  dk = (endK - startK) / _StepCount * _StepSize;
        float2 ds = (endS - startS) / _StepCount * _StepSize;
        float3 dv = (endV - startV) / _StepCount * _StepSize;

        // ── 4. Jitter ──
        float jitter = frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);

        float  K = startK + jitter * dk;
        float2 S = startS + jitter * ds;
        float3 V = startV + jitter * dv;

        // ── 5. 步进与遮挡测试 ──
        float shadow = 1.0;
        float occluderDepthSum = 0.0;
        int   blockerCount = 0;
        float prevDepthDiff = 0.0;

        [loop]
        for (int i = 0; i < _StepCount; i++)
        {
            K += dk;
            S += ds;
            V += dv;

            if (K <= 0.0)
                break;

            if (S.x < 0 || S.x > 1 || S.y < 0 || S.y > 1)
                break;

            float sceneRawDepth = SampleSceneDepth(S);
            float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
            float linearRayDepth =  - V.z / K;
            float depthDiff = linearRayDepth - sceneEyeDepth;

            bool hitCond1 = (depthDiff > 0.0 && depthDiff < _Thickness);
            bool hitCond2 = (prevDepthDiff < 0.0 && depthDiff > 0.0);
            if (hitCond1)
            {
                shadow = 0.0;
                occluderDepthSum += depthDiff;
                blockerCount++;
                break;
            }
            prevDepthDiff = depthDiff;
        }

        half4 result;
        result.r = shadow;
        result.g = blockerCount > 0 ? occluderDepthSum / blockerCount : 0.0;
        result.ba = half2(0, 1);
        return shadow;
        return result;
    }

    // ════════════════════════════════════════════════════════════
    //  双边保边模糊 — 调用 BlurFunction.hlsl，关键字控制强度与法线
    // ════════════════════════════════════════════════════════════

    half4 Frag_BlurH(Varyings input) : SV_Target
    {
        float2 texelSize = 1.0 / _ScreenParams.xy;
        return BilateralBlurHorizontal(input.texcoord, texelSize, _BlurScale, _BlitTexture, sampler_LinearClamp);
    }

    half4 Frag_BlurV(Varyings input) : SV_Target
    {
        float2 texelSize = 1.0 / _ScreenParams.xy;
        return BilateralBlurVertical(input.texcoord, texelSize, _BlurScale, _BlitTexture, sampler_LinearClamp);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "SSSM_RayMarch"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_SSSM_DDA
            ENDHLSL
        }
        Pass
        {
            Name "SSSM_BlurHorizontal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_BlurH
            #pragma shader_feature _ BLUR_BILATERAL_LOW BLUR_BILATERAL_MEDIUM BLUR_BILATERAL_HIGH
            #pragma shader_feature _ BLUR_BILATERAL_NORMAL
            ENDHLSL
        }
        Pass
        {
            Name "SSSM_BlurVertical"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_BlurV
            #pragma shader_feature _ BLUR_BILATERAL_LOW BLUR_BILATERAL_MEDIUM BLUR_BILATERAL_HIGH
            #pragma shader_feature _ BLUR_BILATERAL_NORMAL
            ENDHLSL
        }
    }
}
