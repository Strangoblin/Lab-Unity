Shader "Render/RainDrops"
{
    Properties
    {
        [Header(Drop Layer)]
        [Space]
        _Columns ("Columns", Range(4, 40)) = 12
        _Rows ("Rows", Range(1, 6)) = 2
        _Coverage ("Coverage", Range(0.0, 1.0)) = 0.85
        _DropSize ("Drop Size", Range(0.0, 0.5)) = 0.35

        [Header(Motion)]
        [Space]
        _FlowSpeed ("Flow Speed", Range(0.0, 1.0)) = 0.2
        _Wiggle ("Wiggle Strength", Range(0.0, 1.0)) = 0.5

        [Header(Shape)]
        [Space]
        _DropLength ("Drop Length", Range(0.0, 1.0)) = 0.6
        [Toggle] _DropFacing ("Camera Facing", Float) = 0

        [Header(Refraction)]
        [Space]
        _Refraction ("Refraction Strength", Range(0.0, 0.06)) = 0.025

        [Header(Wetness)]
        [Space]
        _Darken ("Darken", Range(0.0, 0.5)) = 0.15
        _Desaturate ("Desaturate", Range(0.0, 1.0)) = 0.25

        [Header(Specular)]
        [Space]
        _SpecIntensity ("Intensity", Range(0.0, 5.0)) = 1.2
        _Roughness ("Roughness", Range(0.0, 1.0)) = 0.15
        _NormalTilt ("Normal Tilt", Range(0.0, 1.0)) = 0.4
    }

    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
    #include "Assets/Mine/Special/HLSL/NPRFunction.hlsl"
    #include "RainDrop.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float _Columns;
        float _Rows;
        float _Coverage;
        float _DropSize;
        float _FlowSpeed;
        float _Wiggle;
        float _DropLength;
        float _DropFacing;
        float _Refraction;
        float _Darken;
        float _Desaturate;
        float _SpecIntensity;
        float _Roughness;
        float _NormalTilt;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        float4 tangentOS  : TANGENT;
        float2 uv         : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv         : TEXCOORD0;
        float4 screenPos  : TEXCOORD1;
        float3 normalWS   : TEXCOORD2;
        float4 tangentWS  : TEXCOORD3;
        float3 positionWS : TEXCOORD4;
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv         = input.uv;
        output.screenPos  = ComputeScreenPos(output.positionCS);

        VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);
        output.normalWS   = nrm.normalWS;
        output.tangentWS  = float4(nrm.tangentWS, input.tangentOS.w);
        output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
        return output;
    }

    float4 Frag(Varyings input) : SV_Target
    {
        float2 screenUV = input.screenPos.xy / input.screenPos.w;

        float cols = _Columns;
        float rows = _Rows;

        DropLayerResult drops = (DropLayerResult)0;

        // 流动水滴
        DropConfig mainCfg = { _FlowSpeed, _Wiggle, 0.4,
            float2(cols, rows), _Coverage, _DropLength, _DropSize, _DropFacing,
            0.1, 0.85, _Refraction, true };
        drops = BlendLayer(drops, DropLayer(input.uv, _Time.y, mainCfg));

        // 静态小雨滴
        DropConfig smallCfg = { _FlowSpeed * 0.01, _Wiggle, 2,
            float2(cols, rows * 6), _Coverage * 0.2, 0.5, _DropSize, _DropFacing,
            0.2, 0.75, _Refraction, true };
        drops = BlendLayer(drops, DropLayer(input.uv, _Time.y, smallCfg));

        // 细长水痕
        DropConfig streakCfg = { _FlowSpeed * 0.01, _Wiggle, 0,
            float2(cols * 0.5, rows * 0.75), _Coverage * 0.5, 0.99, _DropSize * 0.5, _DropFacing,
            0.03, 0.9, _Refraction * 0.2, true };
        drops = BlendLayer(drops, DropLayer(input.uv, _Time.y, streakCfg));

        float3 sceneColor = SampleSceneColor(screenUV + drops.offset).rgb;
        float3 wetColor = sceneColor * (1.0 - _Darken * drops.mask);
        float luma = dot(wetColor, float3(0.299, 0.587, 0.114));
        wetColor = lerp(wetColor, luma.xxx, _Desaturate * drops.mask);
        // return float4(drops.uv, drops.mask, 1);
        if (drops.mask > 0.001 && _SpecIntensity > 0.0)
        {
            Light light = GetMainLight();
            float3 viewDir  = normalize(GetCameraPositionWS() - input.positionWS);
            float3 tangent  = input.tangentWS.xyz;
            float3 bitangent = cross(input.normalWS, tangent) * input.tangentWS.w;

            float tilt = drops.normDist * _NormalTilt;
            float3 pert = tangent * drops.radialDir.x * tilt
                        + bitangent * drops.radialDir.y * tilt;
            float3 dropNormal = normalize(input.normalWS + pert);

            float smoothness = 1.0 - _Roughness;
            float spec = SpecularBlinnPhong(dropNormal, light.direction, viewDir, smoothness);
            wetColor += spec * _SpecIntensity * drops.mask;
        }

        return float4(wetColor, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "RainDrops"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
