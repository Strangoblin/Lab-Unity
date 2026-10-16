// ════════════════════════════════════════════════════════════
//  Kuwahara — 桑原滤波（Basic / Generalized / Anisotropic 三模式）
//
//  Unity 6 URP 17+ 全屏后处理，六个 Pass 互斥：
//    Pass 0  Basic          4 象限硬选方差最低区（不做加权）
//    Pass 1  Generalized    8 扇形反方差加权混合
//    Pass 2  StructureTensor  ddx/ddy 结构张量（Aniso 链）
//    Pass 3  BlurHorizontal  张量水平模糊（调用 BlurFunction.hlsl）
//    Pass 4  EigenDecomp     张量垂直模糊 + 2x2 特征分解
//    Pass 5  AnisoFilter     椭圆核各向异性滤波
//  采样预算由 _SampleQuality 档位决定（9 / 16 / 25 个固定 offset），
//  _Radius 只缩放 offset，不参与循环边界。Pass 编排见 KuwaharaFeature.cs。
// ════════════════════════════════════════════════════════════
Shader "PostProcess/Kuwahara"
{
    Properties
    {
        [Header(Kernel)]
        _Radius ("Radius", Range(1, 10)) = 5

        [Header(Weight)]
        _Hardness ("Hardness", Range(1.0, 100.0)) = 8.0
        _WeightScale ("Weight Scale", Range(1.0, 2000.0)) = 1000

        [Header(Anisotropic)]
        _Alpha ("Alpha", Range(0.01, 2.0)) = 1.0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Assets/Mine/Special/HLSL/HashFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/BlurFunction.hlsl"

    // ── 参数 ──
    CBUFFER_START(UnityPerMaterial)
        int   _Radius;
        int   _SampleQuality;   // 质量档展开：0 = 9 点, 1 = 16 点, 2 = 25 点
        float _Q;               // 权重陡度；Aniso 用面板 Sharpness，Generalized 由 C# 固定为 2，Basic 不使用
        float _Hardness;        // 方差惩罚（Generalized / Aniso 使用）
        float _WeightScale;     // 方差放大倍率，三种模式都写面板值，仅 Generalized / Aniso 读取
        float _Alpha;           // Aniso 各向异性敏感度
        float _BlurScale;       // 张量模糊半径，由质量档降采样率推导（downsample + 1）
    CBUFFER_END

    // ── 张量纹理（Feature 端 cmd.SetGlobalTexture 发布） ──
    TEXTURE2D_X(_TFM);

    // ── 常量 ──
    #define _N 8
    #define _ZERO_CROSS 0.58

    // 固定 25 点采样核。半径只缩放采样范围，不增加纹理读取次数。
    // 与 BlurFunction 的固定 tap 思路一致，最大偏移为 ±Radius。
    static const float2 KuwaharaSampleOffsets[25] =
    {
        float2(-1.0, -1.0), float2(-0.5, -1.0), float2(0.0, -1.0), float2(0.5, -1.0), float2(1.0, -1.0),
        float2(-1.0, -0.5), float2(-0.5, -0.5), float2(0.0, -0.5), float2(0.5, -0.5), float2(1.0, -0.5),
        float2(-1.0,  0.0), float2(-0.5,  0.0), float2(0.0,  0.0), float2(0.5,  0.0), float2(1.0,  0.0),
        float2(-1.0,  0.5), float2(-0.5,  0.5), float2(0.0,  0.5), float2(0.5,  0.5), float2(1.0,  0.5),
        float2(-1.0,  1.0), float2(-0.5,  1.0), float2(0.0,  1.0), float2(0.5,  1.0), float2(1.0,  1.0)
    };

    // 低 / 中档核的固定 offset 子集，与 25 点核同分布
    static const float2 KuwaharaSampleOffsetsLow[9] =
    {
        float2(-1, -1), float2(0, -1), float2(1, -1),
        float2(-1,  0), float2(0,  0), float2(1,  0),
        float2(-1,  1), float2(0,  1), float2(1,  1)
    };

    static const float2 KuwaharaSampleOffsetsMedium[16] =
    {
        float2(-1, -1), float2(-0.3333, -1), float2(0.3333, -1), float2(1, -1),
        float2(-1, -0.3333), float2(-0.3333, -0.3333), float2(0.3333, -0.3333), float2(1, -0.3333),
        float2(-1, 0.3333), float2(-0.3333, 0.3333), float2(0.3333, 0.3333), float2(1, 0.3333),
        float2(-1, 1), float2(-0.3333, 1), float2(0.3333, 1), float2(1, 1)
    };

    // ════════════════════════════════════════════════════════════
    //  KuwaharaSampleCount — 质量档展开为编译期可裁剪的样本数
    // ════════════════════════════════════════════════════════════
    int KuwaharaSampleCount()
    {
        return _SampleQuality <= 0 ? 9 : (_SampleQuality == 1 ? 16 : 25);
    }

    // ════════════════════════════════════════════════════════════
    //  KuwaharaSampleOffset — 取归一化到 [-1, 1] 的第 index 个采样偏移
    // ════════════════════════════════════════════════════════════
    float2 KuwaharaSampleOffset(int index)
    {
        if (_SampleQuality <= 0)  return KuwaharaSampleOffsetsLow[index];
        if (_SampleQuality == 1)  return KuwaharaSampleOffsetsMedium[index];
        return KuwaharaSampleOffsets[index];
    }

    // ════════════════════════════════════════════════════════════
    //  KuwaharaWeight — 反方差权重 w = 1 / (1 + pow(var * Hardness * WeightScale, Q * 0.5))
    //
    //  Basic 不做加权，不调用本函数；WeightScale 三种模式都写面板值，只有本函数读取
    // ════════════════════════════════════════════════════════════
    float KuwaharaWeight(float varianceSum)
    {
        return 1.0 / (1.0 + pow(max(0, varianceSum) * _Hardness * _WeightScale, _Q * 0.5));
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 0: Basic — 4 象限分区，硬选方差最低区
    // ════════════════════════════════════════════════════════════
    half4 Frag_Basic(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = 1.0 / _ScreenParams.xy;
        float radius = max((float)_Radius, 1.0);

        float3 mean[4];
        float3 meanSq[4];
        int count[4];
        for (int i = 0; i < 4; i++)
        {
            mean[i] = float3(0,0,0);
            meanSq[i] = float3(0,0,0);
            count[i] = 0;
        }

        int sampleCount = KuwaharaSampleCount();
        [loop]
        for (int i = 0; i < 25; i++)
        {
            if (i >= sampleCount) continue;
            float2 p = KuwaharaSampleOffset(i) * radius;
            int region = 0;
            float2 sampleUV = uv + p * texelSize;
            float3 sampleColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV).rgb;

            if (p.x >= 0 && p.y >= 0) region = 0;
            else if (p.x < 0 && p.y > 0) region = 1;
            else if (p.x < 0 && p.y < 0) region = 2;
            else region = 3;

            mean[region] += sampleColor;
            meanSq[region] += sampleColor * sampleColor;
            count[region]++;
        }

        float3 finalColor = float3(0,0,0);
        float minVariance = 1e+10;

        [unroll]
        for (int i = 0; i < 4; i++)
        {
            mean[i] /= count[i];
            meanSq[i] /= count[i];
            float3 variance = meanSq[i] - mean[i] * mean[i];
            float varianceSum = variance.x + variance.y + variance.z;

            if (varianceSum < minVariance)
            {
                minVariance = varianceSum;
                finalColor = mean[i];
            }
        }

        return half4(finalColor, 1.0);
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 1: Generalized — 8 扇形分区，反方差加权混合
    // ════════════════════════════════════════════════════════════
    half4 Frag_Generalized(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float2 texelSize = 1.0 / _ScreenParams.xy;
        float radius = max((float)_Radius, 1.0);

        float3 mean[8];
        float3 meanSq[8];
        int count[8];
        for (int i = 0; i < 8; i++)
        {
            mean[i] = float3(0,0,0);
            meanSq[i] = float3(0,0,0);
            count[i] = 0;
        }

        int sampleCount = KuwaharaSampleCount();
        [loop]
        for (int i = 0; i < 25; i++)
        {
            if (i >= sampleCount) continue;
            float2 p = KuwaharaSampleOffset(i) * radius;
            float2 sampleUV = uv + p * texelSize;
            float3 sampleColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV).rgb;

            float angleUV = atan2(p.y, p.x);
            float angleRegion = 3.14159265 / 4.0;
            int region = (int)floor((angleUV + 3.14159265) / angleRegion) % 8;

            mean[region] += sampleColor;
            meanSq[region] += sampleColor * sampleColor;
            count[region]++;
        }

        float3 finalColor = float3(0,0,0);
        float weightSum = 0.0;

        [unroll]
        for (int i = 0; i < 8; i++)
        {
            mean[i] /= count[i];
            meanSq[i] /= count[i];
            float3 variance = meanSq[i] - mean[i] * mean[i];
            float varianceSum = variance.x + variance.y + variance.z;

            float w = KuwaharaWeight(varianceSum);
            weightSum += w;
            finalColor += mean[i] * w;
        }
        finalColor /= weightSum;

        return half4(finalColor, 1.0);
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 2: StructureTensor — ddx/ddy 结构张量 (Sxx, Syy, Sxy)
    // ════════════════════════════════════════════════════════════
    half4 Frag_StructureTensor(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

        float3 Gx = ddx(color);
        float3 Gy = ddy(color);

        float Sxx = dot(Gx, Gx);
        float Syy = dot(Gy, Gy);
        float Sxy = dot(Gx, Gy);

        return half4(Sxx, Syy, Sxy, 1.0);
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 3: BlurHorizontal — 张量水平模糊，调用 BlurFunction.hlsl
    // ════════════════════════════════════════════════════════════
    half4 Frag_BlurH(Varyings input) : SV_Target
    {
        float2 texelSize = 1.0 / _ScreenParams.xy;
        return BlurHorizontal(input.texcoord, texelSize, _BlurScale, _BlitTexture, sampler_LinearClamp);
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 4: EigenDecomp — 张量垂直模糊 + 2x2 特征分解
    //
    //  输出：RG = 主特征向量方向, B = 角度 phi, A = 各向异性 (λ1-λ2)/(λ1+λ2)
    // ════════════════════════════════════════════════════════════
    half4 Frag_EigenDecomp(Varyings input) : SV_Target
    {
        float2 texelSize = 1.0 / _ScreenParams.xy;

        float4 tensor = BlurVertical(input.texcoord, texelSize, _BlurScale, _BlitTexture, sampler_LinearClamp);

        float Sxx = tensor.x;
        float Syy = tensor.y;
        float Sxy = tensor.z;

        float trace = Sxx + Syy;
        float noiseFloor = 1e-4;

        float sqrtTerm = sqrt(max(0.0, (Syy - Sxx) * (Syy - Sxx) + 4.0 * Sxy * Sxy));
        float lambda1 = 0.5 * (trace + sqrtTerm);
        float lambda2 = max(0.0, 0.5 * (trace - sqrtTerm));

        float2 v = float2(lambda1 - Sxx, -Sxy);
        float2 tDir = (trace > noiseFloor && length(v) > 0.0) ? normalize(v) : float2(1.0, 0.0);
        float phi = -atan2(tDir.y, tDir.x);

        float A = (trace > noiseFloor && lambda1 + lambda2 > 0.0)
            ? (lambda1 - lambda2) / (lambda1 + lambda2)
            : 0.0;

        return half4(tDir.x, tDir.y, phi, A);
    }

    // ════════════════════════════════════════════════════════════
    //  Pass 5: AnisoFilter — 沿张量方向的椭圆核各向异性滤波
    // ════════════════════════════════════════════════════════════
    half4 Frag_AnisoFilter(Varyings input) : SV_Target
    {
        float4 t = SAMPLE_TEXTURE2D_X(_TFM, sampler_LinearClamp, input.texcoord);

        int kernelRadius = _Radius;
        float a = float(kernelRadius) * clamp((_Alpha + t.w) / _Alpha, 0.1, 2.0);
        float b = float(kernelRadius) * clamp(_Alpha / (_Alpha + t.w), 0.1, 2.0);

        float cos_phi = t.x;
        float sin_phi = t.y;

        float2x2 R = { cos_phi, -sin_phi,
                       sin_phi,  cos_phi };
        float2x2 S = { 0.5 / a, 0.0,
                       0.0,     0.5 / b };
        float2x2 SR = mul(S, R);

        int max_x = int(sqrt(a * a * cos_phi * cos_phi + b * b * sin_phi * sin_phi));
        int max_y = int(sqrt(a * a * sin_phi * sin_phi + b * b * cos_phi * cos_phi));

        float2 texelSize = 1.0 / _ScreenParams.xy;

        float zeta = 1.0 / _Radius;
        float sinZeroCross = sin(_ZERO_CROSS);
        float eta = (zeta + cos(_ZERO_CROSS)) / (sinZeroCross * sinZeroCross);

        float4 m[_N];
        float3 s[_N];
        [unroll]
        for (int k = 0; k < _N; ++k)
        {
            m[k] = 0.0;
            s[k] = 0.0;
        }

        [loop]
        for (int i = 0; i < 25; ++i)
        {
            if (i >= KuwaharaSampleCount()) continue;

            float2 normalizedOffset = KuwaharaSampleOffset(i);
            float2 pixelOffset = normalizedOffset * float2(max_x, max_y);
            float2 v = mul(SR, pixelOffset);
            if (dot(v, v) <= 0.25)
            {
                float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp,
                    input.texcoord + pixelOffset * texelSize).rgb;
                c = saturate(c);

                float sum = 0;
                float w[_N];

                float vxx = zeta - eta * v.x * v.x;
                float vyy = zeta - eta * v.y * v.y;
                float z = max(0, v.y + vxx); w[0] = z * z; sum += w[0];
                z = max(0, -v.x + vyy); w[2] = z * z; sum += w[2];
                z = max(0, -v.y + vxx); w[4] = z * z; sum += w[4];
                z = max(0, v.x + vyy);  w[6] = z * z; sum += w[6];

                v = 0.70710678 * float2(v.x - v.y, v.x + v.y);
                vxx = zeta - eta * v.x * v.x;
                vyy = zeta - eta * v.y * v.y;
                z = max(0, v.y + vxx); w[1] = z * z; sum += w[1];
                z = max(0, -v.x + vyy); w[3] = z * z; sum += w[3];
                z = max(0, -v.y + vxx); w[5] = z * z; sum += w[5];
                z = max(0, v.x + vyy);  w[7] = z * z; sum += w[7];

                float g = exp(-3.125 * dot(v, v)) / max(sum, 1e-5);

                for (int k = 0; k < _N; ++k)
                {
                    float wk = w[k] * g;
                    m[k] += float4(c * wk, wk);
                    s[k] += c * c * wk;
                }
            }
        }

        float4 output = 0;
        for (int k = 0; k < _N; ++k)
        {
            m[k].rgb /= m[k].w;
            s[k] = abs(s[k] / m[k].w - m[k].rgb * m[k].rgb);

            float sigma2 = s[k].r + s[k].g + s[k].b;
            float w = KuwaharaWeight(sigma2);

            output += float4(m[k].rgb * w, w);
        }

        return saturate(output / output.w);
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
            Name "Basic"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_Basic
            ENDHLSL
        }

        Pass
        {
            Name "Generalized"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_Generalized
            ENDHLSL
        }

        Pass
        {
            Name "StructureTensor"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_StructureTensor
            ENDHLSL
        }

        Pass
        {
            Name "BlurHorizontal"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_BlurH
            ENDHLSL
        }

        Pass
        {
            Name "EigenDecomp"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_EigenDecomp
            ENDHLSL
        }

        Pass
        {
            Name "AnisoFilter"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_AnisoFilter
            ENDHLSL
        }
    }
}
