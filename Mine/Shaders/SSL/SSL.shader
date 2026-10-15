Shader "Hidden/CelToon/SSL"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" { }
        _Intensity("SSL Intensity", Float) = 1.0
        _MaxSteps("Max Steps", Int) = 32
        _MaxDistance("Max Distance", Float) = 10.0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/BlurFunction.hlsl"

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

    int    _SSLType;
    int    _MaxSteps;
    float  _MaxDistance;
    float  _Intensity;
    float  _SSLScale;
    float  _BlurScale;
    float  _JitterScale;

    TEXTURE2D_X(_MainTex);
    TEXTURE2D_X(_SSLResultTex);

    // Reconstruct world position from depth
    float3 ReconstructWorldPos(float2 uv, float3 CameraPos, float MaxDistance)
    {
        float rawDepth = SampleSceneDepth(uv);
        return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
    }

    // Sample shadow map at given world position
    float SampleShadow(float3 positionCS)
    {
        float4 shadowCoord = TransformWorldToShadowCoord(positionCS);
        return SAMPLE_TEXTURE2D_SHADOW(
            _MainLightShadowmapTexture, 
            sampler_MainLightShadowmapTexture, 
            shadowCoord);
    }

    float HG(float cosTheta, float g)
    {
        return (1 - g * g) / pow(1 + g * g - 2 * g * cosTheta, 1.5);
    }

    // Screen Space Light calculation
    float SSL(
        float3 CameraPos,
        float3 LightDir,
        float3 WorldPosFromDepth,
        int MaxSteps,
        float MaxDistance,
        float2 uv
    )
    {
        float3 RayVec = WorldPosFromDepth - CameraPos;
        float3 RayDir = normalize(RayVec);
        float  RayLen = clamp(length(RayVec), 0.0, MaxDistance);

        float  StepSize   = RayLen / MaxSteps;
        float3 jitter     = frac(sin(dot(uv ,float2(12.9898,78.233))) * 43758.5453);
        float3 CurrentPos = CameraPos + RayDir * (StepSize * jitter.x * _JitterScale);
        float Density = 0.0;

        #if defined(SSL_LIGHT)
        [loop]
        for (int i = 0; i < MaxSteps; i++)
        {
            if (length(CurrentPos - CameraPos) > MaxDistance)
            {    
                break;
            }
            float lighting = SampleShadow(CurrentPos);
            float phase = HG(dot(RayDir, LightDir), _SSLScale);
            lighting *= saturate(phase);
            CurrentPos += RayDir * StepSize;
            Density += StepSize * lighting;
        }
        #elif defined(SSL_FOG)
        [loop]
        for (int i = 0; i < MaxSteps; i++)
        {
            if (length(CurrentPos - CameraPos) > MaxDistance)
            {    
                break;
            }
            CurrentPos += RayDir * StepSize;
            Density += StepSize;
        }
        #else
        Density = 0.0;
        #endif

        Density /= MaxSteps;
        return Density;
    }

    Varyings Vert (Attributes v)
    {
        Varyings o;
        o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
        o.uv = v.uv;
        return o;
    }

    // Main SSL fragment shader
    half4 Frag (Varyings i) : SV_Target
    {
        float2 uv = i.uv;
        Light mainLight = GetMainLight();
        float3 lightDirWS  = mainLight.direction;
        float3 cameraPosWS = GetCameraPositionWS();
        float3 positionWS  = ReconstructWorldPos(uv, cameraPosWS, _MaxDistance);

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

    // Horizontal blur pass
    float4 Frag_BlurHorizontal(Varyings i) : SV_Target
    {
        float2 uv = i.uv;
        float2 texelSize = 1.0 / _ScreenParams.xy;
        float4 color = BlurHorizontal(uv, texelSize, _BlurScale, _MainTex, sampler_LinearClamp);
        return color;
    }

    // Vertical blur pass
    float4 Frag_BlurVertical(Varyings i) : SV_Target
    {
        float2 uv = i.uv;
        float2 texelSize = 1.0 / _ScreenParams.xy;
        float4 color = BlurVertical(uv, texelSize, _BlurScale, _MainTex, sampler_LinearClamp); 
        return color;
    }

    // Mix pass to combine the original scene color with the SSL effect
    half4 Frag_Mix(Varyings i) : SV_Target
    {
        half4 mainColor = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv);
        half4 lightColor = SAMPLE_TEXTURE2D_X(_SSLResultTex, sampler_LinearClamp, i.uv);
        return mainColor + lightColor;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Transparent"
        }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            Name "SSL_ScreenSpaceLight"
            HLSLPROGRAM
            #pragma multi_compile _ SSL_FOG SSL_LIGHT
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "SSL_BlurHorizontal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_BlurHorizontal
            ENDHLSL
        }

        Pass
        {
            Name "SSL_BlurVertical"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_BlurVertical
            ENDHLSL
        }

        Pass
        {
            Name "SSL_Mix"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_Mix
            ENDHLSL
        }
    }

    FallBack Off
}