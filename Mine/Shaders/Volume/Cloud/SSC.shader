Shader "Custom/SSC"
{
    Properties
    {
        _BaseColorA ("Base Color", Color) = (1,1,1,1)
        _BaseColorB ("Base Color", Color) = (0,0,0,1)
        _MainTex2D ("Main Texture", 2D) = "white" {}
        _CloudScale ("Cloud Scale", Float) = 1
        _CloudSpeed ("Cloud Speed", Float) = 1
        _CloudRotation ("Cloud Rotation", Float) = 0
        _CloudHeight ("Cloud Height", Float) = 100
        _CloudMiddle ("Cloud Middle", Range(0,1)) = 0.5
        _CloudSize ("Cloud Size", Vector) = (100, 50, 100)
        _CloudThickness ("Cloud Thickness", Float) = 0.5
        _CloudThreshold ("Cloud Threshold", Range(0,1)) = 0.2
        _PhaseG ("Phase G", Range(-1, 1)) = 0.8
        _AmbientIntensity ("Ambient Intensity", Range(0, 2)) = 1.0
        _Jitter ("Jitter", Float) = 0.5
        _Count ("Count", Int) = 64
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/DeclareCustomTexture.hlsl"

    TEXTURE2D_X(_MainTex2D);
    SAMPLER(sampler_MainTex2D);
    TEXTURE2D_X(_SSCTex);
    TEXTURE2D_X(_MainTex);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColorA;
        float4 _BaseColorB;
        float3 _CloudSize;
        float _CloudScale;
        float _CloudSpeed;
        float _CloudRotation;
        float _CloudHeight;
        float _CloudMiddle;
        float _CloudThickness;
        float _CloudThreshold;
        float _PhaseG;
        float _AmbientIntensity;
        float _Jitter;
        int _Count;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    float3 ReconstructWorldPos(float2 uv)
    {
        float rawDepth = SampleSceneDepth(uv);
        return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
    }

    float Noise(float3 positionWS)
    {
        float2 uv = positionWS.xz / 256.0;
        return _MainTex2D.SampleLevel(sampler_MainTex2D, uv, 0).r;
    }

    // ════════════════════════════════════════════════════════════
    //  Henyey-Greenstein 相函数 — 云体各向异性散射
    // ════════════════════════════════════════════════════════════
    float HG(float cosTheta, float g)
    {
        float g2 = g * g;
        return (1.0 - g2) / pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5);
    }

    float DensityLOD(float3 positionWS, float cloudScale, float cloudSpeed, float cloudRotation, int lod)
    {
        float rotation = cloudRotation * 3.14159 / 180.0;
        float3 direction = float3(cos(rotation), 0, sin(rotation));

        float3 pos = positionWS * cloudScale;
        float3 offset = cloudSpeed * _Time.y * direction * cloudScale;
        float mid = 0.0;
        float amp = 0.5;
        float low = Noise(pos + offset);
        float high = 0.0;

        static const float mul[4] = {2.02, 2.23, 2.41, 2.62};

        [unroll]
        for (int i = 0; i < lod; i++)
        {
            pos *= mul[i];
            offset *= mul[i];

            // Higher-frequency layers move faster to create motion between cloud details.
            float layerSpeed = 1.0 + 0.15 * (i + 1);
            mid += amp * Noise(pos + offset * layerSpeed);
            amp *= 0.5;
            high = mid;
        }

        float density = low * 0.5 + high * 0.5;
        return density;
    }

    float DensityH(float3 positionWS, float cloudTop, float cloudBottom, float cloudThickness, float cloudMiddle)
    {
        float height = saturate((positionWS.y - cloudBottom) / (cloudTop - cloudBottom));

        float middle = saturate(cloudMiddle);
        float leftRange = max(0.00001, middle);
        float rightRange = max(0.00001, 1.0 - middle);

        float density = (height <= middle)
            ? (height / leftRange)
            : ((1.0 - height) / rightRange);

        density = saturate(density);
        float power = max(0.00001, 1.0 - cloudThickness);
        density = pow(density, power);
        return density;
    }

    float CalculateDensity(float3 positionWS, float cloudScale, float cloudSpeed, float cloudRotation, float cloudTop, float cloudBottom, float cloudThickness, float cloudMiddle, float cloudThreshold, int lod)
    {
        float density = DensityLOD(positionWS, cloudScale, cloudSpeed, cloudRotation, lod) * DensityH(positionWS, cloudTop, cloudBottom, cloudThickness, cloudMiddle);
        if (density < cloudThreshold)
        {
            density = 0.0;
        }
        return saturate(density);
    }

    #define RAYMARCH(COUNT, LOD) \
        [loop] \
        for (int i = 0; i < COUNT; i++) \
        { \
            if (tCurrent >= tFar || tCurrent >= tScene) \
            { \
                break; \
            } \
             \
            step = min(step, (tFar - tCurrent)); \
            float3 localPosWS = cameraPosWS + rayDirWS * tCurrent; \
            float localDensity = CalculateDensity(localPosWS, cloudScale, cloudSpeed, cloudRotation, cloudTop, cloudBottom, cloudThickness, cloudMiddle, cloudThreshold, LOD); \
            float3 lightPosWS = localPosWS + lightDirWS * step * 20; \
            float lightDensity = CalculateDensity(lightPosWS, cloudScale, cloudSpeed, cloudRotation, cloudTop, cloudBottom, cloudThickness, cloudMiddle, cloudThreshold, LOD); \
            \
            float stepAlpha = localDensity * (1.0 - Density); \
            float stepShadow = 1 - lightDensity; \
            \
            float heightT = saturate((localPosWS.y - cloudBottom) / (cloudTop - cloudBottom)); \
            float3 ambient = lerp(unity_AmbientGround, unity_AmbientSky, heightT) * _AmbientIntensity; \
            float3 albedo = lerp(_BaseColorB.rgb, _BaseColorA.rgb, stepShadow); \
            float3 light = lightColor * stepShadow * phase; \
            float3 S = light + ambient; \
            \
            float3 deltaDiffuse = S * albedo * stepAlpha; \
            float  deltaDensity = stepAlpha; \
            Diffuse += deltaDiffuse; \
            Density += deltaDensity; \
                 \
            tCurrent += step; \
        } \

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS);
        output.uv = input.uv;
        return output;
    }

    half4 Frag_SSC(Varyings input) : SV_Target
    {
        float2 uv = input.uv;
        float3 positionWS = ReconstructWorldPos(uv);
        float3 cameraPosWS = GetCameraPositionWS();

        float cloudScale = _CloudScale;
        float cloudSpeed = _CloudSpeed;
        float cloudRotation = _CloudRotation;
        float cloudHeight = _CloudHeight;
        float cloudMiddle = _CloudMiddle;
        float cloudThickness = _CloudThickness;
        float cloudThreshold = _CloudThreshold;
        float3 cloudSize = _CloudSize;
        float3 cloudPosWS = float3(0, cloudHeight, 0);

        float3 rayLenWS = positionWS - cameraPosWS;
        float3 rayDirWS = normalize(rayLenWS);

        Light mainLight = GetMainLight();
        float3 lightColor = mainLight.color;
        float3 lightDirWS = mainLight.direction;

        float rdotl = dot(rayDirWS, lightDirWS);
        float phase = HG(rdotl, _PhaseG);
        float3 invDirWS = 1.0 / rayDirWS;

        float3 tMin = (cloudPosWS - cloudSize * 0.5 - cameraPosWS) * invDirWS;
        float3 tMax = (cloudPosWS + cloudSize * 0.5 - cameraPosWS) * invDirWS;

        float3 t1 = min(tMin, tMax);
        float3 t2 = max(tMin, tMax);

        float tScene = dot(rayLenWS, rayDirWS);
        float tNear = max(max(t1.x, t1.y), t1.z);
        float tFar = min(min(t2.x, t2.y), t2.z);

        if (tNear > tFar || tFar < 0.0)
        {
            return 0;
        }

        if (tScene < tNear)
        {
            return 0;
        }

        int Count = 32;
        #if defined(SSC_RAY_COUNT_64)
            Count = 64;
        #elif defined(SSC_RAY_COUNT_128)
            Count = 128;
        #elif defined(SSC_RAY_COUNT_256)
            Count = 256;
        #else
            Count = 32;
        #endif

        float jitter = frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
        float step = (tFar - tNear) / Count;
        float tCurrent = max(tNear, 0.0) + jitter * step * _Jitter;

        float  cloudTop    = cloudPosWS.y + cloudSize.y * 0.5;
        float  cloudBottom = cloudPosWS.y - cloudSize.y * 0.5;

        float3 Diffuse = 0.0;
        float Density = 0.0;

        int CountLOD = Count / 4;
        RAYMARCH(CountLOD, 4);
        RAYMARCH(CountLOD, 3);
        RAYMARCH(CountLOD, 2);
        RAYMARCH(CountLOD, 1);
        
        // ════════════════════════════════════════════════════════════
        //  合成输出 — 散射光照已包含 ambient + direct * phase
        // ════════════════════════════════════════════════════════════
        float3 baseColor = Diffuse;
        float4 Color = float4(baseColor, Density);
        return Color;
    }
    #undef RAYMARCH

    half4 Frag_Composite(Varyings input) : SV_Target
    {
        float4 cloudColor = SampleCustomTexture(_SSCTex, sampler_LinearClamp, input.uv);
        float4 sceneColor = SampleCustomTexture(_MainTex, sampler_LinearClamp, input.uv);
        return lerp(sceneColor, cloudColor, cloudColor.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend One Zero

        Pass
        {
            Name "SSC"

            HLSLPROGRAM
            #pragma multi_compile _ SSC_RAY_COUNT_64 SSC_RAY_COUNT_128 SSC_RAY_COUNT_256
            #pragma vertex Vert
            #pragma fragment Frag_SSC
            ENDHLSL
        }

        Pass
        {
            Name "Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_Composite
            ENDHLSL
        }
    }
}
