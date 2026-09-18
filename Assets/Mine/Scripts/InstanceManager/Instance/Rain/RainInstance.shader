Shader "Rain/RainInstance"
{
    Properties
    {
        _RainColor ("Rain Color", Color) = (0.6, 0.7, 1.0, 0.4)
        _RainLength ("Rain Streak Length", Float) = 0.5
        _RainWidth ("Rain Width", Float) = 0.03
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    struct RainParticle
    {
        float3 position;
        float3 velocity;
    };

    StructuredBuffer<RainParticle> _RainBuffer;

    float4 _RainColor;
    float _RainLength;
    float _RainWidth;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
        uint instanceID : SV_InstanceID;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    Varyings vert(Attributes input)
    {
        Varyings output;

        RainParticle p = _RainBuffer[input.instanceID];

        float3 velDir = normalize(p.velocity + float3(0.001, 0.001, 0.001));
        float speed = length(p.velocity);

        // Stretch quad along velocity direction for rain streak effect
        float stretch = saturate(speed * 0.1) * _RainLength;

        // Build a right vector perpendicular to velocity
        float3 right = normalize(cross(velDir, float3(0, 1, 0)));
        if (length(right) < 0.001)
            right = normalize(cross(velDir, float3(1, 0, 0)));

        // Transform mesh-local position to world: X=width along right, Y=stretch along velocity
        float3 localPos = input.positionOS.xyz;
        float3 worldPos = p.position
                        + localPos.x * _RainWidth * right
                        + localPos.y * stretch * velDir;

        output.positionCS = TransformWorldToHClip(worldPos);
        output.uv = input.uv;
        return output;
    }

    half4 frag(Varyings input) : SV_Target
    {
        return _RainColor;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "RainForward"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
    }
}
