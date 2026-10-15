Shader "Custom/SSO"
{
    Properties
    {
        _Thickness ("Thickness", Range(1,5)) = 1
        _Intensity ("Intensity", Range(0,1)) = 1
        _Threshold ("Threshold", Range(0,1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 uv         : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv         : TEXCOORD0;
    };

    float _ColorThickness;
    float _ColorIntensity;
    float2 _ColorThreshold;

    float _DepthThickness;
    float _DepthIntensity;
    float2 _DepthThreshold;

    float _NormalThickness;
    float _NormalIntensity;
    float2 _NormalThreshold;

    float _Jitter;

    static const float2 UVoffsetsBasic[4] = {
        float2(-1, 0), float2(1, 0),
        float2(0, -1), float2(0, 1)
    };

    static const float2 UVoffsetsSobel[8] = {
        float2(-1, -1), float2(0, -1), float2(1, -1),
        float2(-1,  0),                float2(1,  0),
        float2(-1,  1), float2(0,  1), float2(1,  1)
    };

    float SampleColor(float2 uv)
    {
        return Luminance(SampleSceneColor(uv));
    }

    float SampleDepth(float2 uv)
    {
        return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
    }

    float3 SampleNormal(float2 uv)
    {
        return normalize(SampleSceneNormals(uv));
    }

    float2 Hash21(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(float2(p.x * p.y, p.x + p.y)) * 2.0 - 1.0;
    }

    float2 OffsetUV(float2 uv, float2 uvoffsets, float thickness)
    {
        float2 jitter = Hash21(uv + uvoffsets);
        float2 offset = uvoffsets * thickness;
        return uv + offset + jitter * offset * _Jitter;
    }

    float ColorDiffBasic(float luminance, float2 uv, float2 thickness)
    {
        float lumDiff = 0.0;
        float lumBase = max(luminance, 1e-6);
        
        [unroll]
        for (int i = 0; i < UVoffsetsBasic.Length; i++)
        {
            float2 offsetUV = OffsetUV(uv, UVoffsetsBasic[i], thickness);
            float  offsetLum = SampleColor(offsetUV);
            lumDiff = max(abs(luminance - offsetLum) / lumBase, lumDiff);
        }
        return lumDiff;
    }

    float ColorDiffSobel(float luminance, float2 uv, float2 thickness)
    {
        float lumDiff = 0.0;
        float lumDiffs[8];

        [unroll]
        for (int i = 0; i < UVoffsetsSobel.Length; i++)
        {
            float2 offsetUV = OffsetUV(uv, UVoffsetsSobel[i], thickness);
            float  offsetLum = SampleColor(offsetUV);
            lumDiffs[i] = abs(luminance - offsetLum);
        }

        float gradx = lumDiffs[0] + 2.0 * lumDiffs[3] + lumDiffs[5]
                    - lumDiffs[2] - 2.0 * lumDiffs[4] - lumDiffs[7];
        float grady = lumDiffs[0] + 2.0 * lumDiffs[1] + lumDiffs[2]
                    - lumDiffs[5] - 2.0 * lumDiffs[6] - lumDiffs[7];
        float2 grad = float2(gradx, grady);
        lumDiff = length(grad);
        return lumDiff;
    }

    float DepthDiffBasic(float depth, float2 uv, float2 thickness)
    {
        float depthDiff = 0.0;
        float depthBase = max(depth, 1e-6);

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

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS);
        output.uv = input.uv;
        return output;
    }

    half4 Frag_SSO(Varyings input) : SV_Target
    {
        float2 uv = input.uv;
        float2 texelSize = 1.0 / _ScreenParams.xy;

        float  color  = SampleColor(uv);
        float  depth  = SampleDepth(uv);
        float3 normal = SampleNormal(uv);

        float3 positionWS = ComputeWorldSpacePosition(uv, SampleSceneDepth(uv), UNITY_MATRIX_I_VP);
        float3 viewDir = normalize(positionWS - _WorldSpaceCameraPos);

        float jitter = Hash21(uv).x;
        float colorDiff;
        float depthDiff;
        float normalDiff;

        #ifdef SSO_Basic
            colorDiff = ColorDiffBasic(color, uv, _ColorThickness * texelSize);
            depthDiff = DepthDiffBasic(depth, uv, _DepthThickness * texelSize);
            normalDiff = NormalDiffBasic(normal, uv, _NormalThickness * texelSize);
        #elif defined(SSO_Sobel)
            colorDiff = ColorDiffSobel(color, uv, _ColorThickness * texelSize);
            depthDiff = DepthDiffSobel(depth, uv, _DepthThickness * texelSize);
            normalDiff = NormalDiffBasic(normal, uv, _NormalThickness * texelSize);
        #else
            colorDiff = 0.0;
            depthDiff = 0.0;
            normalDiff = 0.0;
        #endif


        float depthMask = pow(dot(normal, -viewDir), 2);
        colorDiff = smoothstep(_ColorThreshold.x, _ColorThreshold.y, colorDiff + jitter * _Jitter) * _ColorIntensity;
        depthDiff = smoothstep(_DepthThreshold.x, _DepthThreshold.y, depthDiff * depthMask + jitter * _Jitter) * _DepthIntensity;
        normalDiff = smoothstep(_NormalThreshold.x, _NormalThreshold.y, normalDiff + jitter * _Jitter) * _NormalIntensity;

        float DiffTotal = saturate(colorDiff + depthDiff + normalDiff);
        return DiffTotal;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Blend One Zero

        Pass
        {
            Name "SSO"
            HLSLPROGRAM
            #pragma multi_compile _ SSO_Basic SSO_Sobel
            #pragma vertex Vert
            #pragma fragment Frag_SSO
            ENDHLSL
        }
    }
}