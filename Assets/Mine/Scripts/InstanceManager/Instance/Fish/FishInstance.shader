Shader "Fish/FishInstance"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.2, 0.6, 1.0, 1)
        _GlowColor ("Glow Color", Color) = (0.4, 0.8, 1.0, 0.6)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    struct Particle
    {
        float3 position;
        float  curveT;
        float  radialAngle;
        float  radialDist;
    };

    StructuredBuffer<Particle> _ParticleBuffer;

    float4 _BaseColor;
    float4 _GlowColor;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 uv         : TEXCOORD0;
        uint instanceID   : SV_InstanceID;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv         : TEXCOORD0;
        float  curveT     : TEXCOORD1;
    };

    // ════════════════════════════════════════════════════════════
    //  Vert — camera-facing billboard
    // ════════════════════════════════════════════════════════════
    Varyings vert(Attributes input)
    {
        Varyings output;

        Particle p = _ParticleBuffer[input.instanceID];

        float3 toCam  = normalize(_WorldSpaceCameraPos - p.position);
        float3 up     = float3(0, 1, 0);
        float3 right  = normalize(cross(toCam, up));
        up            = normalize(cross(right, toCam));

        float size = 0.15;
        float3 worldPos = p.position
                        + right * input.positionOS.x * size
                        + up    * input.positionOS.y * size;

        output.positionCS = TransformWorldToHClip(worldPos);
        output.uv         = input.uv;
        output.curveT     = p.curveT;

        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — soft glow dot with curveT fade
    // ════════════════════════════════════════════════════════════
    half4 frag(Varyings input) : SV_Target
    {
        float dist = length(input.uv * 2.0 - 1.0);
        float alpha = 1.0 - smoothstep(0.0, 1.0, dist);
        alpha *= saturate(1.0 - input.curveT * 0.3);

        half4 col = lerp(_BaseColor, _GlowColor, dist);
        col.a *= alpha;
        return col;
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
            Name "FishForward"
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
