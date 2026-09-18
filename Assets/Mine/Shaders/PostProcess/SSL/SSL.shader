Shader "PostProcess/SSL"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" { }
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/BlurFunction.hlsl"

    // ════════════════════════════════════════════════════════════
    //  共享 Uniforms
    // ════════════════════════════════════════════════════════════

    int   _MaxSteps;
    float _MaxDistance;
    float _Intensity;
    float _SSLScale;
    float _BlurScale;
    float _JitterScale;

    TEXTURE2D_X(_SSLTex);

    // ════════════════════════════════════════════════════════════
    //  RAY3D — 屏幕空间光体积步进（原 SSL 方法）
    // ════════════════════════════════════════════════════════════

    // 深度重建世界坐标
    float3 ReconstructWorldPos(float2 uv)
    {
        float rawDepth = SampleSceneDepth(uv);
        return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
    }

    // 世界坐标采样阴影图
    float SampleShadow(float3 positionWS)
    {
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
        return SAMPLE_TEXTURE2D_SHADOW(
            _MainLightShadowmapTexture,
            sampler_MainLightShadowmapTexture,
            shadowCoord);
    }

    float HG(float cosTheta, float g)
    {
        return (1 - g * g) / pow(1 + g * g - 2 * g * cosTheta, 1.5);
    }

    // 屏幕空间光体积步进 — 沿视线累积阴影光照（SSL_LIGHT）/ 深度密度（SSL_FOG）
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
        float3 jitter     = frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
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
            float phase = HG(dot(rayDir, lightDir), _SSLScale);
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
    //  RBR2D — 全屏径向模糊（样本 Full Scene Radial Blur 改造）
    //
    //  blur 中心 = 主平行光方向在屏幕上的投影焦点，取代样本的
    //  屏幕中心 + 假光偏移；模糊方向即平行光方向
    // ════════════════════════════════════════════════════════════

    // 平行光方向 → 屏幕空间焦点 UV（沿光方向取远点投影）
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

    // 2x1 hash — 采样抖动去 banding
    float hash21(float2 p)
    {
        return frac(sin(dot(p, float2(41.0, 289.0))) * 45758.5453);
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 0 入口 — 关键字分发到 RAY3D / RBR2D
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

    // 径向模糊采样累积 — 沿 (uv → 光焦点) 方向递减加权
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
        uv += dTuv * (hash21(uv + frac(_Time.y)) * 2.0 - 1.0);

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

    half4 Frag(Varyings input) : SV_Target
    {
        #if defined(SSL_RBR2D)
            return Frag_RBR2D(input);
        #else
            return Frag_RAY3D(input);
        #endif
    }

    // ════════════════════════════════════════════════════════════
    //  模糊 Pass 入口 — BlurFunction.hlsl 双向高斯
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
    //  合成 Pass — RBR2D: lerp 模糊场景 / RAY3D: 加法体积光
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
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

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
