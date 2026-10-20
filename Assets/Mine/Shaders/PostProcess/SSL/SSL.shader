// ════════════════════════════════════════════════════════════
//  SSL — 屏幕空间光（Screen Space Light）
//
//  Unity 6 URP 17+ 全屏后处理，两种模式由关键字分发：
//    RAY3D  沿视线 3D 步进，累积阴影光照（SSL_LIGHT）或深度密度（SSL_FOG）
//    RBR2D  全屏径向模糊，模糊中心为主平行光方向的屏幕投影焦点
//    Pass 0 生成 → Pass 1/2 多级模糊链（BlurFunction.hlsl）→ Pass 3 合成
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

    // ════════════════════════════════════════════════════════════
    //  SSL — RAY3D 屏幕空间光体积步进
    //
    //  沿视线从相机步进至深度重建点，SSL_LIGHT 累积阴影光照、SSL_FOG 累积深度密度
    // ════════════════════════════════════════════════════════════
    float SSL(
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
        float  density = 0.0;

        #if defined(SSL_LIGHT)
        [loop]
        for (int i = 0; i < maxSteps; i++)
        {
            if (length(currentPos - cameraPos) > maxDistance)
            {
                break;
            }
            float lighting = SampleShadow(currentPos);
            // 与 MaskRBR 一致：Core HG 自带 1/(4π)，用峰值归一化消去该常量；
            // g 由 SSL 自己钳制，避免 g=1 的奇点。
            float g = clamp(_SSLScale, 0.0, 0.999);
            float phase = HenyeyGreensteinPhaseFunction(g, dot(rayDir, lightDir))
                        / HenyeyGreensteinPhaseFunction(g, 1.0);
            lighting *= saturate(phase);
            currentPos += rayDir * stepSize;
            density += stepSize * lighting;
        }
        #elif defined(SSL_FOG)
        [loop]
        for (int i = 0; i < maxSteps; i++)
        {
            if (length(currentPos - cameraPos) > maxDistance)
            {
                break;
            }
            currentPos += rayDir * stepSize;
            density += stepSize;
        }
        #else
        density = 0.0;
        #endif

        density /= maxSteps;
        return density;
    }

    // ════════════════════════════════════════════════════════════
    //  GetLightFocusUV — 主平行光方向在屏幕上的投影焦点
    //
    //  沿光方向取远点投影为 UV，取代样本的「屏幕中心 + 假光偏移」
    // ════════════════════════════════════════════════════════════
    float2 GetLightFocusUV()
    {
        Light mainLight = GetMainLight();
        float3 lightPosFar = GetCameraPositionWS() + mainLight.direction * _MaxDistance;
        float4 clip = mul(UNITY_MATRIX_VP, float4(lightPosFar, 1.0));
        if (clip.w <= 0.0) return float2(0.5, 0.5);   // 光在相机背后 → 回退屏幕中心
        float2 focus = clip.xy / clip.w * 0.5 + 0.5;
        focus.y = 1.0 - focus.y;
        return clamp(focus, -0.5, 1.5);               // 允许离屏焦点，限制极端值
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_RAY3D — RAY3D 入口，密度转主光颜色
    // ════════════════════════════════════════════════════════════
    half4 Frag_RAY3D(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        Light mainLight = GetMainLight();
        float3 lightDirWS  = mainLight.direction;
        float3 cameraPosWS = GetCameraPositionWS();
        float3 positionWS  = ReconstructWorldPos(uv);

        float density = SSL(
            cameraPosWS,
            lightDirWS,
            positionWS,
            _MaxSteps,
            _MaxDistance,
            uv
        );

        float sslLight = density * _Intensity;
        float3 sslColor = mainLight.color * sslLight;
        return float4(sslColor, 1.0);
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_RBR2D — RBR2D 全屏径向模糊
    //
    //  沿 (uv → 光焦点) 方向递减加权累积 24 次采样，焦点处聚光收尾
    // ════════════════════════════════════════════════════════════
    half4 Frag_RBR2D(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 focusUV = GetLightFocusUV();

        float  decay   = 0.97;    // 向外辐射的权重衰减
        float  density = 0.5;     // 采样密度（控制 blur 扩散范围）
        float  weight  = 0.1;     // 采样权重
        const float SAMPLES = 24.0;

        // blur 方向向量：像素 → 光焦点（即平行光方向）
        float2 tuv  = uv - focusUV;
        float2 dTuv = tuv * density / SAMPLES;

        half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * 0.25;

        // 抖动去 banding
        uv += dTuv * (Hash_Sine21(uv + frac(_Time.y)) * 2.0 - 1.0);

        [loop]
        for (float i = 0.0; i < SAMPLES; i++)
        {
            uv -= dTuv;
            col += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * weight;
            weight *= decay;
        }

        // 焦点聚光收尾
        col *= 1.0 - dot(tuv, tuv) * 0.75;
        return col;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — Pass 0 入口，关键字分发到 RAY3D / RBR2D
    // ════════════════════════════════════════════════════════════
    half4 Frag(Varyings input) : SV_Target
    {
        #if defined(SSL_RBR2D)
            return Frag_RBR2D(input);
        #else
            return Frag_RAY3D(input);
        #endif
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
    //  Frag_Mix — 合成：RBR2D 按强度 lerp 模糊场景 / RAY3D 加法体积光
    // ════════════════════════════════════════════════════════════
    half4 Frag_Mix(Varyings input) : SV_Target
    {
        half4 mainColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
        half4 sslColor  = SAMPLE_TEXTURE2D_X(_SSLTex, sampler_LinearClamp, input.texcoord);
        #if defined(SSL_RBR2D)
            return lerp(mainColor, sslColor, _Intensity);
        #else
            return mainColor + sslColor;
        #endif
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
            #pragma multi_compile _ SSL_RBR2D SSL_RAY3D
            #pragma multi_compile _ SSL_FOG SSL_LIGHT
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
            #pragma multi_compile _ SSL_RBR2D SSL_RAY3D
            #pragma vertex Vert
            #pragma fragment Frag_Mix
            ENDHLSL
        }
    }

    FallBack Off
}
