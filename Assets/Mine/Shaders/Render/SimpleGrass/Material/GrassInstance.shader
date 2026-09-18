Shader "Render/GrassInstance"
{
    Properties 
    {
        [Header(Base Settings)]
        _Base_Color_A ("Base Color A", Color) = (1,1,1,1)
        _Base_Color_B ("Base Color B", Color) = (0,0,0,1)
        _Grass ("Grass Texture", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _AlphaClipThreshold ("Alpha Clip Threshold", Range(0,1)) = 0.5

        [Header(Wind Settings)]
        _Wind_Color_A ("Wind Color A", Color) = (1,1,1,1)
        _Wind_Color_B ("Wind Color B", Color) = (0,0,0,1)

        [Header(Color Blend Settings)]
        _GroundColorTexture ("Ground Color Texture", 2D) = "white" {}
        _GroundColorBlend ("Ground Color Blend", Range(0,1)) = 1.0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Assets/Mine/Special/HLSL/NPRFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/AdditionalLightsFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/TBN.hlsl"
    #include "Assets/Mine/Special/HLSL/SDF.hlsl"

    struct GrassProperties
    {
        // float3 offset;
        float3 normal;
        float height;
    };

    StructuredBuffer<float4x4> _MeshProperties;
    StructuredBuffer<GrassProperties> _GrassProperties;
    StructuredBuffer<uint> _ClipProperties;

    // Textures and Properties
    float4 _Base_Color_A;
    float4 _Base_Color_B;
    float _AlphaClipThreshold;
    float _Smoothness;

    float4 _Wind_Color_A;
    float4 _Wind_Color_B;

    float3 _CameraPosition;
    float4x4 _CameraMatrix;

    float3 _WindDirection;
    float  _WindFrequency;
    float  _WindStrength;
    float  _WindScale;

    float3 _InteractionDirection;

    float _GroundColorBlend;

    Texture2D _Grass;
    SamplerState sampler_Grass;
    Texture2D _WindNoiseTexture;
    SamplerState sampler_WindNoiseTexture;
    Texture2D _InteractionTexture;
    SamplerState sampler_InteractionTexture;
    Texture2D _SceneColorTex;
    SamplerState sampler_SceneColorTex;

    struct appdata
    {
        float4 positionOS : POSITION;
        float3 normal : NORMAL;
        float2 uv : TEXCOORD0;

        uint instanceID : SV_InstanceID;
    };

    struct v2f
    {
        float4 positionHCS : SV_POSITION;
        float4 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        float2 uv : TEXCOORD2;
        float2 cameraUV : TEXCOORD3;
        float  posdepth : TEXCOORD4;
    };

    void WindEffect(float3 positionWS, out float3 windOffset)
    {
        float3 windDir = normalize(_WindDirection);
        float2 uv = positionWS.xz * _WindScale - windDir.xz * _Time * _WindFrequency;
        float  wind = _WindNoiseTexture.SampleLevel(sampler_WindNoiseTexture, uv, 0).r;
        windOffset = wind * _WindStrength * windDir;
    }

    void InteractionEffect(float3 positionWS, out float3 interactionOffset, out float2 uv)
    {
        float3 interactionDir = normalize(_InteractionDirection);
        uv = mul(_CameraMatrix, float4(positionWS - _CameraPosition, 1)).xz;
        float  interaction = _InteractionTexture.SampleLevel(sampler_InteractionTexture, uv, 0).r;
        interactionOffset = interaction * interactionDir;
    }

    v2f vert(appdata v)
    {
        v2f o;

        uint clipIndex = _ClipProperties[v.instanceID];
        float4x4 meshProp = _MeshProperties[clipIndex];
        GrassProperties grassProp = _GrassProperties[clipIndex];

        float3 grassPos = mul(meshProp, v.positionOS).xyz;
        float3 windOffset;
        float3 interactionOffset;
        float2 CameraUV;
        WindEffect(grassPos, windOffset);
        InteractionEffect(grassPos, interactionOffset, CameraUV);
        float3 grassOffset = (windOffset + interactionOffset) * v.uv.y;
        float3 grassNormal = grassProp.normal;

        float3 N = grassNormal;
        float3 T;
        float3 B;
        TBN(N, T, B);

        float3x3 TNB = float3x3(T, N, B);
        float4 positionOffsetNS = float4(mul(grassOffset, TNB), 0);

        o.positionWS = float4(grassPos, 1) + positionOffsetNS;
        o.positionHCS = TransformWorldToHClip(o.positionWS);
        o.normalWS = grassNormal;
        o.uv = v.uv;
        o.cameraUV = CameraUV;
        o.posdepth = grassOffset.y;

        return o;
    }

    half4 frag(v2f i): SV_Target
    {
        // Base Color
        half4 grassTex = _Grass.Sample(sampler_Grass, i.uv);
        clip(grassTex.a - _AlphaClipThreshold);
        half4 grasscolor = lerp(_Base_Color_B, _Base_Color_A, i.uv.y) * grassTex;
        half4 windColor = lerp(_Wind_Color_B, _Wind_Color_A, i.posdepth);
        half4 groundColor = _SceneColorTex.Sample(sampler_SceneColorTex, i.cameraUV);
        half4 basecolor = lerp(saturate(grasscolor * windColor), groundColor, _GroundColorBlend);

        // Lighting Functions
        float3 LightDirection;
        float3 LightColor;
        float DistanceAtten, ShadowAtten;

        MainLight(i.positionWS.xyz, LightDirection, LightColor, DistanceAtten, ShadowAtten);

        float3 N = normalize(i.normalWS.xyz);
        float3 L = normalize(LightDirection);
        float3 V = normalize(_WorldSpaceCameraPos - i.positionWS.xyz);

        float3 Diffuse = DiffuseLambert(N, L) * 0.5 + 0.5;
        float3 Specular = SpecularBlinnPhong(N, L, V, _Smoothness);   
        float3 direct = (Diffuse + Specular) * LightColor;
        float3 ambient = SampleSH(N);
        float3 Light = direct + ambient;

        half4 finalColor = float4(lerp(0.6, 1, ShadowAtten) * Light, 1.0) * basecolor;
        return finalColor;
    }

    ENDHLSL
    SubShader
    {
        Tags 
        { 
            "RenderPipeline" = "UniversalRenderPipeline" 
            "RenderType"="Opaque" 
        }

        Pass
        {
            Cull Off
            Blend One Zero
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            ENDHLSL

        }
    }
}
