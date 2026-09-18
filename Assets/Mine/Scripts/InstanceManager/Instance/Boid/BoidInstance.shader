Shader "Boid/BoidInstance"
{
    Properties 
    {
        _BoidColor ("Boid Color", Color) = (1,1,1,1)
        _BoidTexture ("Boid Texture", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Assets/Mine/Special/HLSL/NPRFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/AdditionalLightsFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/TBN.hlsl"

    struct InstanceBuffer
    {
        float3 positionOG;
        float3 positionWS;
        float3 velocity;
        float  anime;
    };

    StructuredBuffer<float4x4> _MeshBuffer;
    StructuredBuffer<InstanceBuffer> _InstanceBuffer;
    StructuredBuffer<uint> _ClipBuffer;

    float4 _BoidColor;
    float _Smoothness;
    Texture2D _BoidTexture;
    SamplerState sampler_BoidTexture;

    struct appdata
    {
        float4 positionOS : POSITION;
        float3 normal : NORMAL;
        float2 uv : TEXCOORD0;

        uint instanceID : SV_InstanceID;
    };

    struct v2f
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 positionWS : TEXCOORD1;
        float3 normal : TEXCOORD2;
    };

    v2f vert (appdata v)
    {
        v2f o;

        uint clipIndex = _ClipBuffer[v.instanceID];
        float4x4 mesh = _MeshBuffer[0];
        InstanceBuffer instance = _InstanceBuffer[v.instanceID];

        float3 boidTransOS = mul(mesh, v.positionOS).xyz;

        float3 N = normalize(instance.velocity);
        float3 T;
        float3 B;
        TBN(N, T, B);

        float3x3 TNB = float3x3(T, N, B);
        float3 boidPosOS = mul(TNB, boidTransOS);
        float3 boidPosWS = boidPosOS + instance.positionWS;
        
        o.positionWS = float4(boidPosWS, 1);
        o.positionCS = TransformWorldToHClip(o.positionWS);
        o.normal = v.normal;
        o.uv = v.uv;
        return o;
    }

    half4 frag (v2f i) : SV_Target
    {
        half4 texColor = SAMPLE_TEXTURE2D(_BoidTexture, sampler_BoidTexture, i.uv);
        half4 baseColor = texColor * _BoidColor;

        float3 LightDirection;
        float3 LightColor;
        float DistanceAtten, ShadowAtten;

        MainLight(i.positionWS.xyz, LightDirection, LightColor, DistanceAtten, ShadowAtten);

        float3 N = normalize(i.normal);
        float3 L = normalize(LightDirection);
        float3 V = normalize(_WorldSpaceCameraPos - i.positionWS.xyz);

        float3 Diffuse = DiffuseLambert(N, L) * 0.5 + 0.5;
        float3 Specular = SpecularBlinnPhong(N, L, V, _Smoothness);
        float3 direct = (Diffuse + Specular) * LightColor;
        float3 ambient = SampleSH(N);
        float3 Light = direct + ambient;

        half4 finalColor = float4((lerp(0.6, 1, ShadowAtten) * Light), 1.0) * baseColor;
        return finalColor;
    }

    float4 fragShadow (v2f i) : SV_Target
    {
        return 0;
    }

    ENDHLSL
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Cull Front
            ZWrite On
            Blend One Zero
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            ENDHLSL
        }
    }
}
