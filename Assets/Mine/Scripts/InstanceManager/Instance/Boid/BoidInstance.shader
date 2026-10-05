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

    CBUFFER_START(UnityPerMaterial)
        float4 _BoidColor;
        float _Smoothness;
    CBUFFER_END
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

    // ════════════════════════════════════════════════════════════
    //  MotionBasis — Local +Y follows velocity with a right-handed basis
    // ════════════════════════════════════════════════════════════
    void MotionBasis(float3 velocity, out float3 right, out float3 forward, out float3 normal)
    {
        float speedSq = dot(velocity, velocity);
        forward = speedSq > 1e-12 ? velocity * rsqrt(max(speedSq, 1e-12)) : float3(0, 1, 0);
        float3 referenceUp = abs(forward.y) > 0.999 ? float3(0, 0, 1) : float3(0, 1, 0);
        right = normalize(cross(referenceUp, forward));
        normal = cross(right, forward);
    }

    // ════════════════════════════════════════════════════════════
    //  OrientToMotion — Expand local coordinates along world-space basis vectors
    // ════════════════════════════════════════════════════════════
    float3 OrientToMotion(float3 local, float3 right, float3 forward, float3 normal)
    {
        return local.x * right + local.y * forward + local.z * normal;
    }

    // ════════════════════════════════════════════════════════════
    //  Vert — Orient instance vertices and normals along motion
    // ════════════════════════════════════════════════════════════
    v2f Vert (appdata v)
    {
        v2f o;

        uint clipIndex = _ClipBuffer[v.instanceID];
        float4x4 mesh = _MeshBuffer[0];
        InstanceBuffer instance = _InstanceBuffer[clipIndex];

        float3 boidTransOS = mul(mesh, v.positionOS).xyz;

        float3 right;
        float3 forward;
        float3 normal;
        MotionBasis(instance.velocity, right, forward, normal);
        float3 boidPosOS = OrientToMotion(boidTransOS, right, forward, normal);
        float3 boidPosWS = boidPosOS + instance.positionWS;
        
        o.positionWS = float4(boidPosWS, 1);
        o.positionCS = TransformWorldToHClip(o.positionWS.xyz);
        float3 meshNormal = mul((float3x3)mesh, v.normal);
        o.normal = normalize(OrientToMotion(meshNormal, right, forward, normal));
        o.uv = v.uv;
        return o;
    }

    half4 Frag (v2f i) : SV_Target
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

    float4 FragShadow (v2f i) : SV_Target
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
            Cull Off
            ZWrite On
            Blend One Zero
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
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
            #pragma vertex Vert
            #pragma fragment FragShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            ENDHLSL
        }
    }
}
