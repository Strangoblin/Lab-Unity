// ════════════════════════════════════════════════════════════
//  SSL — 屏幕空间光（Screen Space Light）
//
//  Unity 6 URP 17+ 全屏后处理，单一 HG 体积光模式：
//    沿视线 3D 步进，累积阴影光照 × 光空间透明透射图
//    Pass 0 生成 → Pass 1/2 多级模糊链（BlurFunction.hlsl）→ Pass 3 加法合成
//  参数全部由 SSLFeature 经 PropertyToID 传入，面板不暴露。
// ════════════════════════════════════════════════════════════
Shader "PostProcess/SSL"
{
    Properties
    {
        // 参数由 SSLFeature 传入，无需 Inspector 暴露
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
    #include "Assets/Mine/Special/HLSL/HashFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/BlurFunction.hlsl"
    #include "Assets/Mine/Special/Renderer/TransparentShadow/TransparentShadowFunction.hlsl"

    // ── 参数 ──
    CBUFFER_START(UnityPerMaterial)
        int   _MaxSteps;
        float _MaxDistance;
        float _Intensity;
        float _SSLScale;
        float _BlurScale;
        float _JitterScale;
    CBUFFER_END

    // ── 中间纹理（Feature 端 cmd.SetGlobalTexture 发布） ──
    TEXTURE2D_X(_SSLTex);

    // ════════════════════════════════════════════════════════════
    //  ReconstructWorldPos — 由深度重建世界坐标
    // ════════════════════════════════════════════════════════════
    float3 ReconstructWorldPos(float2 uv)
    {
        float rawDepth = SampleSceneDepth(uv);
        return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
    }

    // ════════════════════════════════════════════════════════════
    //  SampleShadow — 世界坐标采样主光阴影图
    // ════════════════════════════════════════════════════════════
    float SampleShadow(float3 positionWS)
    {
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
        return SAMPLE_TEXTURE2D_SHADOW(
            _MainLightShadowmapTexture,
            sampler_MainLightShadowmapTexture,
            shadowCoord);
    }

    // 质量归一化参考步数（旧 Medium 预算）：换档只改采样精度，不改亮度
    static const float SSL_REFERENCE_STEPS = 32.0;

    // ════════════════════════════════════════════════════════════
    //  SSL — HG 体积光步进
    //
    //  沿视线从相机步进至深度重建点，累积阴影光照 × 彩色透射；
    //  相位按像素计算一次，阴影为 0 的步跳过透射采样
    // ════════════════════════════════════════════════════════════
    float3 SSL_VolumeLight(
        float3 cameraPos,
        float3 lightDir,
        float3 worldPosFromDepth,
        int    maxSteps,
        float  maxDistance,
        float2 uv
    )
    {
        float3 rayVec  = worldPosFromDepth - cameraPos;
        float3 rayDir  = normalize(rayVec);
        float  rayLen  = clamp(length(rayVec), 0.0, maxDistance);

        float  stepSize   = rayLen / maxSteps;
        float3 jitter     = Hash_Sine21(uv);
        float3 currentPos = cameraPos + rayDir * (stepSize * jitter.x * _JitterScale);
        float3 density    = 0.0;

        // 与 MaskRBR 一致：Core HG 自带 1/(4π)，用峰值归一化消去该常量；
        // g 由 SSL 自己钳制，避免 g=1 的奇点。
        float g = clamp(_SSLScale, 0.0, 0.999);
        float phase = HenyeyGreensteinPhaseFunction(g, dot(rayDir, lightDir))
                    / HenyeyGreensteinPhaseFunction(g, 1.0);
        phase = saturate(phase);

        [loop]
        for (int i = 0; i < maxSteps; i++)
        {
            if (length(currentPos - cameraPos) > maxDistance)
            {
                break;
            }

            float3 samplePos = currentPos;
            currentPos += rayDir * stepSize;

            float shadow = SampleShadow(samplePos);
            if (shadow <= 0.0)
            {
                continue;                 // 阴影区跳过透射采样
            }

            density += stepSize * shadow * TransparentShadow_Transmission(samplePos);
        }

        return density * (phase / SSL_REFERENCE_STEPS);
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 体积光生成入口，密度转主光颜色
    // ════════════════════════════════════════════════════════════
    half4 Frag(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        Light mainLight = GetMainLight();
        float3 lightDirWS  = mainLight.direction;
        float3 cameraPosWS = GetCameraPositionWS();
        float3 positionWS  = ReconstructWorldPos(uv);

        float3 density = SSL_VolumeLight(
            cameraPosWS,
            lightDirWS,
            positionWS,
            _MaxSteps,
            _MaxDistance,
            uv
        );

        float3 sslColor = density * _Intensity * mainLight.color;
        return float4(sslColor, 1.0);
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_BlurHorizontal / Frag_BlurVertical — 调用 BlurFunction.hlsl 双向高斯
    // ════════════════════════════════════════════════════════════
    half4 Frag_BlurHorizontal(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
        return BlurHorizontal(uv, texelSize, _BlurScale, _BlitTexture, sampler_LinearClamp);
    }

    half4 Frag_BlurVertical(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
        return BlurVertical(uv, texelSize, _BlurScale, _BlitTexture, sampler_LinearClamp);
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_Mix — 合成：场景 + 加法体积光
    // ════════════════════════════════════════════════════════════
    half4 Frag_Mix(Varyings input) : SV_Target
    {
        half4 mainColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
        half4 sslColor  = SAMPLE_TEXTURE2D_X(_SSLTex, sampler_LinearClamp, input.texcoord);
        return mainColor + sslColor;
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
            Name "SSL_Raymarch"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "SSL_BlurHorizontal"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_BlurHorizontal
            ENDHLSL
        }

        Pass
        {
            Name "SSL_BlurVertical"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_BlurVertical
            ENDHLSL
        }

        Pass
        {
            Name "SSL_Mix"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Mix
            ENDHLSL
        }
    }

    FallBack Off
}
