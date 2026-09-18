Shader "Picker/Picker"
{
    // ════════════════════════════════════════════════════════════
    //  Picker — GPU 屏幕空间选物 MRT Shader
    //
    //  Pass 0 (PickerMRT):     MRT → ObjectID(RGB24) / Depth / Normal
    //  Pass 1 (UniversalForward): 可见渲染（不同 ID 不同色相）
    // ════════════════════════════════════════════════════════════

    Properties
    {
        [IntRange] _ObjectID ("Object ID", Range(0, 255)) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        uint _ObjectID;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
    };

    struct Varyings
    {
        float4 positionCS  : SV_POSITION;
        float3 normalWS    : TEXCOORD0;
        float  linearDepth : TEXCOORD1;
    };

    // ════════════════════════════════════════════════════════════
    //  PickerMRT Vert
    // ════════════════════════════════════════════════════════════
    Varyings Vert(Attributes input)
    {
        Varyings output;
        VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
        VertexNormalInputs   nmlInputs = GetVertexNormalInputs(input.normalOS);

        output.positionCS = posInputs.positionCS;
        output.normalWS   = nmlInputs.normalWS;

        float3 viewPos = posInputs.positionWS - GetCameraPositionWS();
        output.linearDepth = length(viewPos) / _ProjectionParams.z;

        return output;
    }

    struct FragOutput
    {
        float4 objectID : SV_Target0;
        float  depth    : SV_Target1;
        float3 normal   : SV_Target2;
    };

    // ════════════════════════════════════════════════════════════
    //  Frag — RGB24 ID 编码
    //    r = (id>>16)&255, g = (id>>8)&255, b = id&255
    // ════════════════════════════════════════════════════════════
    FragOutput Frag(Varyings input)
    {
        FragOutput output;

        uint id = _ObjectID;
        output.objectID = float4(
            (float)((id >> 16) & 255) / 255.0,
            (float)((id >> 8)  & 255) / 255.0,
            (float)( id        & 255) / 255.0,
            1.0);

        output.depth = input.linearDepth;

        float3 wn = normalize(input.normalWS);
        output.normal = wn * 0.5 + 0.5;

        return output;
    }

    // ── Forward 渲染 ──────────────────────────────────────────

    struct ForwardVaryings
    {
        float4 positionCS : SV_POSITION;
        float3 normalWS   : TEXCOORD0;
    };

    ForwardVaryings ForwardVert(Attributes input)
    {
        ForwardVaryings output;
        VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
        VertexNormalInputs   nmlInputs = GetVertexNormalInputs(input.normalOS);
        output.positionCS = posInputs.positionCS;
        output.normalWS   = nmlInputs.normalWS;
        return output;
    }

    float3 HueToRGB(float h)
    {
        float r = abs(h * 6.0 - 3.0) - 1.0;
        float g = 2.0 - abs(h * 6.0 - 2.0);
        float b = 2.0 - abs(h * 6.0 - 4.0);
        return saturate(float3(r, g, b));
    }

    half4 ForwardFrag(ForwardVaryings input) : SV_Target
    {
        float3 N = normalize(input.normalWS);
        float3 L = normalize(_MainLightPosition.xyz);
        float  diff = saturate(dot(N, L)) * 0.5 + 0.5;

        float hue = frac((float)_ObjectID * 0.61803398875);
        float3 color = saturate(HueToRGB(hue) * diff);

        return half4(color, 1);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PICKER_MRT"
            Tags { "LightMode" = "PickerMRT" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "PICKER_FORWARD"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex ForwardVert
            #pragma fragment ForwardFrag
            ENDHLSL
        }
    }

    Fallback Off
}
