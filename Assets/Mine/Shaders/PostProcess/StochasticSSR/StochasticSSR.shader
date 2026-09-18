Shader "PostProcess/StochasticSSR"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

    float _StepSize, _MaxDistance, _Thickness, _Roughness, _TemporalBlend, _FrameIndex;
    float _SkyFallback;
    int _StepCount, _ResolveRadius, _ResolveQuality;
    float4 _ResolveTexelSize;   // resolve RT 的 texel size（低分辨率 gather 步长）

    float4x4 _CameraViewMatrix, _CameraProjectionMatrix;

    // ════════════════════════════════════════════════════════════
    //  GGX VNDF importance sampling (Intel fast, no TBN)
    // ════════════════════════════════════════════════════════════

    float3 SampleGGX_VNDF(float2 xi, float a, float3 N, float3 Ve)
    {
        float3 Vh = normalize(float3(a * Ve.x, a * Ve.y, Ve.z));
        float lensq = Vh.x * Vh.x + Vh.y * Vh.y;
        float3 T1 = lensq > 1e-4 ? float3(-Vh.y, Vh.x, 0) * rsqrt(lensq) : float3(1, 0, 0);
        float3 T2 = cross(Vh, T1);
        float r = sqrt(xi.x), phi = 6.2831853 * xi.y;
        float t1 = r * cos(phi), t2 = r * sin(phi);
        float s = 0.5 * (1.0 + Vh.z);
        t2 = (1.0 - s) * sqrt(1.0 - t1 * t1) + s * t2;
        float3 Nh = t1 * T1 + t2 * T2 + sqrt(max(0, 1 - t1 * t1 - t2 * t2)) * Vh;
        return normalize(float3(a * Nh.x, a * Nh.y, max(0, Nh.z)));
    }

    float GGX_PDF(float3 N, float3 H, float3 Ve, float a)
    {
        float a2 = a * a, NoH = saturate(dot(N, H)), VoH = saturate(dot(Ve, H));
        float D = a2 / (3.14159265 * pow(NoH * NoH * (a2 - 1) + 1, 2));
        return D * NoH / (4.0 * max(VoH, 1e-3));
    }

    // ════════════════════════════════════════════════════════════
    //  HitProcess
    // ════════════════════════════════════════════════════════════

    float4 HitProcess(float4 color, float3 rayDir, float2 hitUV, float hitDist)
    {
        float3 N = SampleSceneNormals(hitUV);
        if (dot(N, -rayDir) <= 0.0) return color;
        float3 c = SampleSceneColor(hitUV);
        float fade = 1.0 - pow(max(abs(hitUV.x * 2 - 1), abs(hitUV.y * 2 - 1)), 4);
        return float4(c, fade / (1.0 + hitDist));
    }

    // ════════════════════════════════════════════════════════════
    //  Trace — GGX 1-ray + Ray3D march → traceRT (low-res)
    // ════════════════════════════════════════════════════════════

    half4 Frag_Trace(Varyings input) : SV_Target
    {
        float4 c = half4(0, 0, 0, 0);
        float2 uv = input.texcoord;
        float d = SampleSceneDepth(uv);
        if (d >= 0.9999) return c;

        float3 P = ComputeWorldSpacePosition(uv, d, UNITY_MATRIX_I_VP);
        float3 Ve = normalize(P - GetCameraPositionWS());
        float3 N = SampleSceneNormals(uv);
        if (dot(N, -Ve) <= 0.0) return c;

        // GGX importance sampling — 切线空间采样 → 世界空间
        float a = max(0.05, _Roughness);
        float2 xi = float2(frac(sin(dot(uv + _FrameIndex * 0.618, float2(12.9898, 78.233))) * 43758.5453),
                           frac(sin(dot(uv + _FrameIndex * 0.618, float2(78.233, 12.9898))) * 43758.5453));
        // Build TBN: N=Z, T=X, B=Y
        float3 T = abs(N.y) < 0.999 ? normalize(cross(float3(0,1,0), N)) : float3(1,0,0);
        float3 B = cross(N, T);
        float3 VeLocal = float3(dot(-Ve, T), dot(-Ve, B), dot(-Ve, N));
        float3 Hlocal = SampleGGX_VNDF(xi, a, N, VeLocal); // N unused inside, VeLocal correct
        float3 H = T * Hlocal.x + B * Hlocal.y + N * Hlocal.z;
        float3 R = reflect(Ve, H);

        // Ray3D world-space march
        float3 dw = R * (_MaxDistance / _StepCount * _StepSize);
        float4x4 VP = mul(_CameraProjectionMatrix, _CameraViewMatrix);
        float3 W = P + dw * xi.x * 0.5;

        [loop] for (int i = 0; i < _StepCount; i++)
        {
            W += dw;
            float4 clip = mul(VP, float4(W, 1));
            if (clip.w <= 0.0) break;
            float2 S = (float2(clip.x, clip.y * _ProjectionParams.x) * rcp(clip.w)) * 0.5 + 0.5;
            if (S.x < 0 || S.x > 1 || S.y < 0 || S.y > 1) break;
            float sD = LinearEyeDepth(SampleSceneDepth(S), _ZBufferParams);
            float rD = clip.w;
            if (rD - sD > 0.0 && rD - sD < _Thickness) {
                float4 h = HitProcess(c, R, S, rD);
                if (h.a > 0.0) { c = half4(h.rgb, h.a); break; }
            }
        }
        if (c.a < 1e-4) c = float4(SampleSH(R), _SkyFallback);
        return c;
    }

    // ════════════════════════════════════════════════════════════
    //  Resolve — spatial gather + BRDF cone-tracing
    //
    //  5×5 邻域搜索 + 深度/法线几何权重 + 粗糙度锥角 mip fade。
    //  本质是几何感知的 bilateral blur，替代了独立 blur pass。
    //  低分辨率执行（_ResolveTexelSize 步长），gather 窗口在低
    //  分辨率 texel 尺度上覆盖更大屏幕范围，temporal 上采样抹痕。
    // ════════════════════════════════════════════════════════════

    TEXTURE2D_X(_TraceTex);

    static const float2 ResolveOffsets[25] = {
        float2(-1,-1), float2(-0.5,-1), float2(0,-1), float2(0.5,-1), float2(1,-1),
        float2(-1,-0.5), float2(-0.5,-0.5), float2(0,-0.5), float2(0.5,-0.5), float2(1,-0.5),
        float2(-1,0), float2(-0.5,0), float2(0,0), float2(0.5,0), float2(1,0),
        float2(-1,0.5), float2(-0.5,0.5), float2(0,0.5), float2(0.5,0.5), float2(1,0.5),
        float2(-1,1), float2(-0.5,1), float2(0,1), float2(0.5,1), float2(1,1)
    };
    int ResolveSampleCount() { return _ResolveQuality <= 0 ? 9 : (_ResolveQuality == 1 ? 16 : 25); }
    float2 ResolveOffset(int i)
    {
        if (_ResolveQuality <= 0)
        {
            static const float2 low[9] = {
                float2(-1,-1), float2(0,-1), float2(1,-1), float2(-1,0), float2(0,0),
                float2(1,0), float2(-1,1), float2(0,1), float2(1,1)
            };
            return low[i];
        }
        if (_ResolveQuality == 1)
        {
            static const float2 medium[16] = {
                float2(-1,-1), float2(-0.3333,-1), float2(0.3333,-1), float2(1,-1),
                float2(-1,-0.3333), float2(-0.3333,-0.3333), float2(0.3333,-0.3333), float2(1,-0.3333),
                float2(-1,0.3333), float2(-0.3333,0.3333), float2(0.3333,0.3333), float2(1,0.3333),
                float2(-1,1), float2(-0.3333,1), float2(0.3333,1), float2(1,1)
            };
            return medium[i];
        }
        return ResolveOffsets[i];
    }

    half4 Frag_Resolve(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 ts = _ResolveTexelSize.xy;
        float d = SampleSceneDepth(uv);
        if (d >= 0.9999) return half4(0, 0, 0, 0);

        float3 P = ComputeWorldSpacePosition(uv, d, UNITY_MATRIX_I_VP);
        float3 Ve = normalize(P - GetCameraPositionWS());
        float3 N = SampleSceneNormals(uv);
        float eye = LinearEyeDepth(d, _ZBufferParams);

        float4 gathered = 0; float totalW = 0;
        int r = _ResolveRadius;
        int sampleCount = ResolveSampleCount();
        [loop] for (int i = 0; i < 25; i++)
        {
            if (i >= sampleCount) continue;
            float2 suv = uv + ResolveOffset(i) * r * ts;
            if (suv.x < 0 || suv.x > 1 || suv.y < 0 || suv.y > 1) continue;
            float4 hit = SAMPLE_TEXTURE2D_X(_TraceTex, sampler_LinearClamp, suv);
            if (hit.a < 1e-4) continue;

            float nd = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
            float dw = exp(-abs(eye - nd) * 5.0);
            float nw = pow(saturate(dot(N, SampleSceneNormals(suv))), 32.0);
            float w = dw * nw;
            gathered += hit * w; totalW += w;
        }

        float4 result = totalW > 1e-3 ? gathered / totalW : half4(0, 0, 0, 0);

        // Cone-tracing fade: rough surfaces sample wider → dimmer
        // pixelFootprint ≈ roughness × screenHeight (FOV-independent approx)
        float mipF = log2(1.0 + _Roughness * _ScreenParams.y);
        result.rgb *= 1.0 - saturate(mipF / 12.0);

        return result;
    }

    // ════════════════════════════════════════════════════════════
    //  Temporal — motion reprojection + history blend
    // ════════════════════════════════════════════════════════════

    TEXTURE2D_X(_HistoryTex);
    TEXTURE2D_X(_MotionVecTex);

    half4 Frag_Temporal(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;

        // Motion vector reprojection
        float2 mv = SAMPLE_TEXTURE2D_X(_MotionVecTex, sampler_LinearClamp, uv).xy;
        float2 histUV = uv - mv;

        float4 current = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float4 history = SAMPLE_TEXTURE2D_X(_HistoryTex, sampler_LinearClamp, histUV);

        // Disocclusion: reject history if depth changed
        float curD = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
        float histD = LinearEyeDepth(SampleSceneDepth(histUV), _ZBufferParams);
        float confidence = exp(-abs(curD - histD) * 10.0);

        float blend = lerp(0.0, _TemporalBlend, saturate(confidence));
        return lerp(current, history, blend);
    }

    // ════════════════════════════════════════════════════════════
    //  Debug
    // ════════════════════════════════════════════════════════════

    half4 Frag_Debug(Varyings input) : SV_Target
    {
        #if defined(SSSR_DEBUG_TRACE)
            return SAMPLE_TEXTURE2D_X(_TraceTex, sampler_LinearClamp, input.texcoord);
        #else
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
        #endif
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always Blend One Zero

        Pass { Name "SSSR_Trace"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Trace
            ENDHLSL
        }
        Pass { Name "SSSR_Resolve"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Resolve
            ENDHLSL
        }
        Pass { Name "SSSR_Temporal"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Temporal
            ENDHLSL
        }
        Pass { Name "SSSR_Debug"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma multi_compile _ SSSR_DEBUG_TRACE
            #pragma vertex Vert
            #pragma fragment Frag_Debug
            ENDHLSL
        }
    }
}
