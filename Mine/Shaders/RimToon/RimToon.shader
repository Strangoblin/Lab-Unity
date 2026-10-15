Shader "Custom/RimToon"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _MainTex ("Base Color", 2D) = "white" {}
        _NormalTex ("Normal Map", 2D) = "bump" {}
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineScale ("Outline Scale", Range(0,1)) = 0.2
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _DepthRimPower ("Depth Rim Power", Range(0,10)) = 1.0

        [Toggle(ENABLE_CELCOLOR)]_EnableCelColor ("Enable CelColor", Float) = 1
        [Toggle(ENABLE_OUTLINE)]_EnableOutline ("Enable Outline", Float) = 1
        [Toggle]_EnableStencil ("Enable Stencil", Float) = 1
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
    #include "Assets/Mine/Special/HLSL/LightFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/RimLightFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/AdditionalLightsFunction.hlsl"

    struct RimAttributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        float4 tangentOS  : TANGENT;
        float2 uv         : TEXCOORD0;
    };

    struct RimVaryings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS   : TEXCOORD1;
        float3 tangentWS  : TEXCOORD2;
        float3 bitangentWS: TEXCOORD3;
        float2 uv         : TEXCOORD4;
    };

    Texture2D _MainTex;
    SamplerState sampler_MainTex;
    Texture2D _NormalTex;
    SamplerState sampler_NormalTex;

    float4 _BaseColor;
    float4 _OutlineColor;
    float  _OutlineScale;
    float  _Smoothness;
    float  _DepthRimPower;

    float3 ComputeNormalWS(RimVaryings input)
    {
        float3 normalTS = UnpackNormal(_NormalTex.Sample(sampler_NormalTex, input.uv));
        return normalize(
            input.tangentWS   * normalTS.x +
            input.bitangentWS * normalTS.y +
            input.normalWS    * normalTS.z
        );
    }

    RimVaryings Vert(RimAttributes input)
    {
        RimVaryings output;
        output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
        output.positionCS = TransformWorldToHClip(output.positionWS);
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);

        float3 tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
        float tangentSign = input.tangentOS.w * unity_WorldTransformParams.w;
        float3 bitangentWS = cross(output.normalWS, tangentWS) * tangentSign;

        output.tangentWS   = tangentWS;
        output.bitangentWS = bitangentWS;
        output.uv = input.uv;
        return output;
    }

    RimVaryings Vert_Outline(RimAttributes input)
    {
        RimVaryings output;
        float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
        float3 offsetPosWS = TransformObjectToWorld(input.positionOS.xyz) + normalWS * _OutlineScale * 0.01;
        output.positionWS = offsetPosWS;
        output.positionCS = TransformWorldToHClip(offsetPosWS);
        output.normalWS = normalWS;

        float3 tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
        float tangentSign = input.tangentOS.w * unity_WorldTransformParams.w;
        float3 bitangentWS = cross(output.normalWS, tangentWS) * tangentSign;

        output.tangentWS   = tangentWS;
        output.bitangentWS = bitangentWS;
        output.uv = input.uv;
        return output;
    }

    half4 Frag(RimVaryings input) : SV_Target
    {
        float3 normalWS = ComputeNormalWS(input);
        float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);

        // Sample textures
        float4 baseColor = _MainTex.Sample(sampler_MainTex, input.uv) * _BaseColor;

        // Lighting calculations
        float3 mainLightDirWS;
        float3 mainLightColor;
        float  mainLightDist;
        float  mainLightShadowAtten;
        MainLight(input.positionWS, mainLightDirWS, mainLightColor, mainLightDist, mainLightShadowAtten);

        float3 addLightColor = AdditionalLights(input.positionWS, normalWS, viewDirWS, _Smoothness);
        float  shadow = mainLightShadowAtten;

        float3 Ambient  = SampleSH(normalWS);
        float  Diffuse  = smoothstep(0.45, 0.55, (DiffuseLambert(normalWS, mainLightDirWS) * 0.5 + 0.5));
        float  Specular = SpecularBlinnPhong(normalWS, mainLightDirWS, viewDirWS, _Smoothness);
        float  Fresnel  = RimFresnel(normalWS, viewDirWS, _Smoothness);

        #if defined(ENABLE_CELCOLOR)
        shadow  = Diffuse * shadow * 0.5 + 0.5;
        Diffuse = Diffuse * 0.5 + 0.5;
        #endif

        float4 positionCS = TransformWorldToHClip(input.positionWS);
        float2 screenUV   = float2(positionCS.x, positionCS.y * _ProjectionParams.x) / positionCS.w * 0.5 + 0.5;
        float  DepthRim   = RimLightDepth(mainLightDirWS, screenUV, _DepthRimPower);

        float3 lighting = (Diffuse + Specular + DepthRim) * mainLightColor.rgb * shadow + Ambient * (1.0 + Fresnel);
        lighting += addLightColor;

        return half4(lighting * baseColor.rgb, baseColor.a);
    }

    half4 Frag_Outline(RimVaryings input) : SV_Target
    {
        #if !defined(ENABLE_OUTLINE)
        clip(-1);
        #endif
        float4 baseColor = _MainTex.Sample(sampler_MainTex, input.uv);
        return _OutlineColor * baseColor;
    }

    half4 Frag_DepthOnly(RimVaryings input) : SV_Target
    {
        return half4(0,0,0,0);
    }

    half4 Frag_DepthNormals(RimVaryings input) : SV_Target
    {
        float3 normalWS = ComputeNormalWS(input);
        float linearDepth = LinearEyeDepth(input.positionCS.z / input.positionCS.w, _ZBufferParams);
        return half4(normalWS, linearDepth);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend One Zero

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
                WriteMask [_EnableStencil]
            }

            HLSLPROGRAM


            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local ENABLE_CELCOLOR
            ENDHLSL
        }

        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode"="SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Blend One Zero

            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
                WriteMask [_EnableStencil]
            }

            HLSLPROGRAM
            #pragma vertex Vert_Outline
            #pragma fragment Frag_Outline
            #pragma shader_feature_local ENABLE_OUTLINE
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_DepthOnly
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag_DepthNormals
            ENDHLSL
        }
    }
}
