Shader "PostProcess/PCSSTemplate"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
    CBUFFER_END

    // 全局阴影参数（Shader.SetGlobalXXX 设置，与 Unity ShadowCasterPass 一致）
    float3 _LightDirection;
    float  _ShadowDepthBias;
    float  _ShadowNormalBias;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 normalWS   : TEXCOORD0;
    };

    // ── Forward / DepthNormals（无 shadow bias）──
    Varyings VertForward(Attributes input)
    {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
        return output;
    }

    // ── CustomShadowCaster（施加 shadow bias）──
    Varyings VertShadowCaster(Attributes input)
    {
        Varyings output;
        float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
        float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);
        output.normalWS = normalWS;

        float NdotL     = saturate(dot(_LightDirection, normalWS));
        float slopeBias = (1.0 - NdotL) * _ShadowNormalBias;
        positionWS -= _LightDirection * _ShadowDepthBias;
        positionWS += normalWS * slopeBias;

        output.positionCS = TransformWorldToHClip(positionWS);
        return output;
    }

    float FragShadowCaster(Varyings input) : SV_Target
    {
        return input.positionCS.z;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Back
        ZWrite On
        ZTest LEqual

        // ── Forward ──
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex VertForward
            #pragma fragment FragForward
            half4 FragForward(Varyings input) : SV_Target { return _BaseColor; }
            ENDHLSL
        }

        // ── DepthNormals（URP 需要此 pass 写入 _CameraNormalsTexture）──
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex VertForward
            #pragma fragment FragDepthNormals
            half4 FragDepthNormals(Varyings input) : SV_Target
            {
                return half4(input.normalWS * 0.5 + 0.5, 1);
            }
            ENDHLSL
        }

        // ── CustomShadowCaster（PCSS 阴影深度写入）──
        Pass
        {
            Name "CustomShadowCaster"
            Tags { "LightMode"="CustomShadowCaster" }
            HLSLPROGRAM
            #pragma vertex VertShadowCaster
            #pragma fragment FragShadowCaster
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
