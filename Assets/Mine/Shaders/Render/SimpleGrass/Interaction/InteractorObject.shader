Shader "Render/InteractorObject"
{
    Properties
    {
        _Power("Power", Range(0, 1)) = 0.5
        _SceneDepthTex("Scene Depth Texture", 2D) = "white" {}
    }
    
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

    float _Power;

    TEXTURE2D(_SceneDepthTex);
    SAMPLER(sampler_SceneDepthTex);

    struct Attributes
    {
        float3 positionOS : POSITION;
        float2 uv         : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionHCS : SV_POSITION;
        float2 uv          : TEXCOORD0;
        float  viewDepth   : TEXCOORD1;
        float4 positionHS  : TEXCOORD2;
    };

    Varyings Vert(Attributes i)
    {
        Varyings o;
        o.positionHCS = TransformObjectToHClip(i.positionOS);
        o.positionHS  = ComputeScreenPos(o.positionHCS);
        o.viewDepth   = o.positionHCS.z / o.positionHCS.w;
        o.uv = i.uv;
        return o;
    }

    half4 Frag(Varyings i) : SV_Target
    {
        float4 positionCS = i.positionHCS;
        float2 screenUV = i.positionHS.xy / i.positionHS.w;

        float sceneRawDepth = SAMPLE_TEXTURE2D(_SceneDepthTex, sampler_SceneDepthTex, screenUV).r;
        float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);

        float viewRawDepth  = i.viewDepth;
        float viewEyeDepth = LinearEyeDepth(viewRawDepth, _ZBufferParams);

        float depthDiff = pow(saturate(viewEyeDepth - sceneEyeDepth), _Power);
        return depthDiff;
    }

    ENDHLSL
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Name "DepthDifferencePass"
            Tags { "LightMode"="UniversalForward" }

            Cull Front
            ZWrite Off
            ZTest Always
            Blend One Zero

            Stencil
            {
                Ref 2
                Comp Always
                Pass Replace
                WriteMask 2
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
