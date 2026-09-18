Shader "PostProcess/SSC"
{
    Properties
    {
        _BaseColorA ("Base Color", Color) = (1,1,1,1)
        _BaseColorB ("Base Color", Color) = (0,0,0,1)
        _MainTex2D ("Main Texture", 2D) = "white" {}
        _MainTex3D ("Main Texture", 3D) = "white" {}
        _CloudParamA ("Cloud Param A", Vector) = (1,1,1,100)
        _CloudParamB ("Cloud Param B", Vector) = (1,1,1,0.5)
        _Jitter ("Jitter", Float) = 0.5
        _Count ("Count", Int) = 64
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/DeclareCustomTexture.hlsl"

    TEXTURE2D_X(_MainTex2D);
    SAMPLER(sampler_MainTex2D);
    TEXTURE3D(_MainTex3D);
    SAMPLER(sampler_MainTex3D);
    TEXTURE2D_X(_SSCTex);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColorA;
        float4 _BaseColorB;
        float4 _CloudParamA;
        float4 _CloudParamB;
        float _Jitter;
        int _Count;
    CBUFFER_END

    float3 ReconstructWorldPos(float2 uv)
    {
        float rawDepth = SampleSceneDepth(uv);
        return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
    }

    float Noise(float3 positionWS)
    {
        float noise = 0.0;
        #if defined(SSC_NOISE_TEX2DR)
            float2 uv = positionWS.xz / 256.0;
            noise = _MainTex2D.SampleLevel(sampler_MainTex2D, uv, 0).r;
        #elif defined(SSC_NOISE_TEX2DRG)
            float3 pi = floor(positionWS);
            float3 pf = frac(positionWS);
            float3 uvw = pf * pf * (3.0 - 2.0 * pf);
            float2 uv  = (pi.xz + float2(37.0, 17.0) * pi.y) / 256.0;
            float2 rg = _MainTex2D.SampleLevel(sampler_MainTex2D, uv, 0).rg;
            noise = lerp(rg.x, rg.y, uvw.y / 10);
        #elif defined(SSC_NOISE_TEX3DXYZ)
            float3 uvw = positionWS / 256.0;
            noise = _MainTex3D.SampleLevel(sampler_MainTex3D, uvw, 0).r;
        #else
            noise = 0.0;
        #endif
        return noise;
    }

    float DensityLOD(float3 positionWS, float3 transform, int lod)
    {
        float scale = transform.x;
        float speed = transform.y;
        float rotation = transform.z * 180 / 3.14;
        float3 direction = float3(cos(rotation), 0, sin(rotation));

        float3 pos = (positionWS + speed * _Time.y * direction) * scale;
        float mid = 0.0;
        float amp = 0.5;
        float low = Noise(pos);
        float high = 0.0;

        static const float mul[4] = {2.02, 2.23, 2.41, 2.62};

        [unroll]
        for (int i = 0; i < lod; i++)
        {
            pos *= mul[i];
            mid += amp * Noise(pos);
            amp *= 0.5;
            high = mid;
        }

        float density = lerp(0, high, low);
        return low;
    }

    float DensityH(float3 positionWS, float3 paramUD)
    {
        float top = paramUD.x;
        float bottom = paramUD.y;
        float thiciness = paramUD.z;
        float height = saturate((positionWS.y - bottom) / (top - bottom));
        float middle = 0.5;

        float density = 1 - abs(height - middle) * 2;
        return pow(density, (1.0 - thiciness + 1e-5));
    }

    float CalculateDensity(float3 positionWS, float3 cloudTrans, float3 paramUD, int lod)
    {
        float density = DensityLOD(positionWS, cloudTrans, lod) * DensityH(positionWS, paramUD);
        if (density < 0.2)
        {
            density = 0.0;
        }
        return density;
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
            float3 lightPosWS = localPosWS + lightDirWS * step; \
            float localDensity = CalculateDensity(localPosWS, cloudTrans, paramUD, LOD); \
            float lightDensity = CalculateDensity(lightPosWS, cloudTrans, paramUD, LOD); \
            float3 deltaDensity = saturate(localDensity - lightDensity); \
             \
            Diffuse += step * deltaDensity * (1 - Diffuse); \
            Density += step * localDensity * (1 - Density); \
            tCurrent += step; \
        } \

    half4 Frag_SSC(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float3 positionWS = ReconstructWorldPos(uv);
        float3 cameraPosWS = GetCameraPositionWS();

        float  cloudHeight = _CloudParamA.w;
        float3 cloudSize = _CloudParamB.xyz;
        float3 cloudPosWS = float3(0, cloudHeight, 0);

        float3 rayLenWS = positionWS - cameraPosWS;
        float3 rayDirWS = normalize(rayLenWS);
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

        Light mainLight = GetMainLight();
        float3 lightDirWS = mainLight.direction;

        float3 cloudTrans = _CloudParamA.xyz;
        float  cloudThick = _CloudParamB.w;
        float  cloudTop    = cloudPosWS.y + cloudSize.y * 0.5;
        float  cloudBottom = cloudPosWS.y - cloudSize.y * 0.5;
        float3 paramUD = float3(cloudTop, cloudBottom, cloudThick);

        float Diffuse = 0.0;
        float Density = 0.0;

        int CountLOD = Count / 4;
        RAYMARCH(CountLOD, 4);
        RAYMARCH(CountLOD, 3);
        RAYMARCH(CountLOD, 2);
        RAYMARCH(CountLOD, 1);
        
        // Diffuse /= Count;
        // Density /= Count;

        float4 baseColor = lerp(_BaseColorB, _BaseColorA, Density);
        float4 Color = float4(baseColor.rgb, Density);
        return Color;
    }
    #undef RAYMARCH

    half4 Frag_Composite(Varyings input) : SV_Target
    {
        float4 cloudColor = SampleCustomTexture(_SSCTex, sampler_LinearClamp, input.texcoord);
        float4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
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
            #pragma multi_compile _ SSC_NOISE_TEX2DR SSC_NOISE_TEX2DRG SSC_NOISE_TEX3DXYZ
            #pragma multi_compile _ SSC_RAY_COUNT_64 SSC_RAY_COUNT_128 SSC_RAY_COUNT_256
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_SSC
            ENDHLSL
        }

        Pass
        {
            Name "Composite"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag_Composite
            ENDHLSL
        }
    }
}
