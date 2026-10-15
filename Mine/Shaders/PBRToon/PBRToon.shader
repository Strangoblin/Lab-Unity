Shader "Custom/PBRToon"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _MainTex ("Base Color", 2D) = "white" {}
        _NormalTex ("Normal Map", 2D) = "bump" {}
        _AOTex ("AO Map", 2D) = "white" {}
        _EmissionTex ("Emission Map", 2D) = "black" {}
        _Roughness ("Roughness", Range(0.0, 1.0)) = 0.5
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0
        _Anisotropy ("Anisotropy", Range(-1.0, 1.0)) = 0.0
        _ToonSharpness ("Toon Sharpness", Range(0.0, 1.0)) = 1.0
        _ToonShadowBias ("Toon Shadow Bias", Range(-1.0, 1.0)) = 0.0
        _RimColor ("Rim Color", Range(0.0, 1.0)) = 0.5
        _RimScale ("Rim Range", Range(0.0, 1.0)) = 0.5
        _OutlineColor ("Outline Color", Range(0.0, 1.0)) = 0.5
        _OutlineScale ("Outline Scale", Range(0.0, 1.0)) = 0.2
        _FresnelColor ("Fresnel Color", Color) = (1.0, 1.0, 1.0, 1.0)

        [Toggle(ENABLE_OUTLINE)]_EnableOutline ("Enable Outline", Float) = 1
        [Toggle(ENABLE_CELTOON)]_EnableCelToon ("Enable CelToon", Float) = 1
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
    #include "Assets/Mine/Special/HLSL/RimLightFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/OutlineFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/ShadowFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/ENVFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/PBRFunction.hlsl"

    TEXTURE2D_X(_MainTex);
    SAMPLER(sampler_MainTex);
    TEXTURE2D_X(_NormalTex);
    SAMPLER(sampler_NormalTex);
    TEXTURE2D_X(_AOTex);
    SAMPLER(sampler_AOTex);
    TEXTURE2D_X(_EmissionTex);
    SAMPLER(sampler_EmissionTex);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float _Roughness;
        float _Metallic;
        float _Anisotropy;
        float _ToonRemap;
        float _ToonSharpness;
        float _ToonShadowBias;
        float _RimScale;
        float _RimColor;
        float _OutlineColor;
        float _OutlineScale;
        float4 _ShadowColor;
        float4 _FresnelColor;
    CBUFFER_END

    struct PBRAttributes
    {
        float4 positionOS   : POSITION;
        float3 normalOS     : NORMAL;
        float4 tangentOS    : TANGENT;
        float3 colorOS      : COLOR;
        float2 UV           : TEXCOORD0;
    };

    struct PBRVaryings
    {
        float4 positionCS   : SV_POSITION;
        float3 positionWS   : TEXCOORD0;
        float3 normalWS     : TEXCOORD1;
        float3 tangentWS    : TEXCOORD2;
        float3 bitanentWS   : TEXCOORD3;
        float2 uv           : TEXCOORD4;
    };

    float3 ComputeNormalWS(PBRVaryings input)
    {
        float3 normalTS = UnpackNormal(_NormalTex.Sample(sampler_NormalTex, input.uv));
        return normalize(
            input.tangentWS   * normalTS.x +
            input.bitanentWS * normalTS.y +
            input.normalWS   * normalTS.z
        );
    }

    PBRVaryings Vert(PBRAttributes input)
    {
        PBRVaryings output;
        output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
        output.positionCS = TransformWorldToHClip(output.positionWS);
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);

        float3 tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
        float tangentSign = input.tangentOS.w * unity_WorldTransformParams.w;
        float3 bitangentWS = cross(output.normalWS, tangentWS) * tangentSign;

        output.tangentWS = tangentWS;
        output.bitanentWS = bitangentWS;
        output.uv = input.UV;
        return output;
    }

    PBRVaryings Vert_Outline(PBRAttributes input)
    {
        PBRVaryings output;
        output.normalWS = TransformObjectToWorldNormal(input.colorOS * 2 - 1);

        float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
        float4 posCS = TransformWorldToHClip(posWS);
        posCS = ComputeOutlineCS(posWS, posCS, output.normalWS, _OutlineScale * 0.01);

        output.positionWS = posWS;
        output.positionCS = posCS;

        float3 tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
        float tangentSign = input.tangentOS.w * unity_WorldTransformParams.w;
        float3 bitangentWS = cross(output.normalWS, tangentWS) * tangentSign;

        output.tangentWS = tangentWS;
        output.bitanentWS = bitangentWS;
        output.uv = input.UV;
        return output;
    }

    half4 Frag(PBRVaryings input) : SV_Target
    {
        float3 mainLitDir;
        float3 mainLitColor;
        float  mainLitDistanceAtten;
        float  mainLitShadowAtten;
        MainLight(input.positionWS, mainLitDir, mainLitColor, mainLitDistanceAtten, mainLitShadowAtten);

        float3 camPos = GetCameraPositionWS();
        float3 normalWS = ComputeNormalWS(input);
        float3 tangentWS = input.tangentWS;
        float3 bitangentWS = input.bitanentWS;
        float3 viewDirWS = normalize(camPos - input.positionWS);
        float3 lightDirWS = normalize(mainLitDir);
        
        float4 baseColor = _MainTex.Sample(sampler_MainTex, input.uv) * _BaseColor;
        float roughness = lerp(0.15, 1.0, _Roughness);

        float3 halfVec = normalize(lightDirWS + viewDirWS);
        float NdotL = max(0.0, dot(normalWS, lightDirWS));
        float NdotV = max(0.0, dot(normalWS, viewDirWS));
        float NdotH = max(0.0, dot(normalWS, halfVec));
        float VdotH = max(0.0, dot(viewDirWS, halfVec));
        float LdotH = max(0.0, dot(lightDirWS, halfVec));
        float TdotH = dot(tangentWS, halfVec);
        float BdotH = dot(bitangentWS, halfVec);

        float  shadowAO    = _AOTex.Sample(sampler_AOTex, input.uv).r;
        float  shadowAdd   = 1;
        float  shadowNdotL = NdotL;
        float  shadowMain  = mainLitShadowAtten;
        float  shadowRemap = 0;
        #if defined(ENABLE_CELTOON)
        shadowNdotL = lerp(dot(normalWS, lightDirWS) * 0.5 + 0.5, NdotL, _ToonSharpness);
        shadowRemap = _ToonSharpness / (1 + _ToonSharpness);
        shadowNdotL = smoothstep(0.5 - (1 - _ToonSharpness) * 0.5, 0.5 + (1 - _ToonSharpness) * 0.5, shadowNdotL);

        float3 positionWSBiased = input.positionWS + lightDirWS * _ToonShadowBias;
        float4 positionCSBiased = TransformWorldToHClip(positionWSBiased);
        float depthBasic = LinearEyeDepth(input.positionCS.z / input.positionCS.w, _ZBufferParams);
        float depthBiased = LinearEyeDepth(positionCSBiased.z / positionCSBiased.w, _ZBufferParams);
        float depthDiff = depthBiased - depthBasic;
        return depthDiff > 1e-5 ? half4(1, 1, 1, 1) : half4(0, 0, 0, 1);
        #endif
        float  shadowArea  = shadowMain * shadowNdotL * shadowAO * shadowAdd * (1 - shadowRemap) + shadowRemap;
        // return shadowArea;
        float3 radiance1 = mainLitColor * mainLitDistanceAtten * shadowArea;
        float3 radiance2 = mainLitColor * mainLitDistanceAtten * mainLitShadowAtten * NdotL;
        float3 F0 = lerp(0.04, baseColor.rgb, _Metallic);
        float3 F  = F_Fast(F0, VdotH);

        float3 Diffuse  = Diff_Lambert(baseColor.rgb) * PI * radiance1 * (1.0 - _Metallic) * (1.0 - F);
        float3 Specular = Spec_Unity(NdotH, LdotH, VdotH, TdotH, BdotH, roughness, _Anisotropy) * PI * radiance2 * F;
        float3 Ambient  = BRDF_Env(baseColor.rgb, NdotV, normalWS, viewDirWS, roughness, _Metallic, unity_SpecCube0, samplerunity_SpecCube0);

        float2 screenUV    = GetNormalizedScreenSpaceUV(input.positionCS);
        float  rimLight    = RimLightDepth(normalWS, screenUV, _RimScale * 0.01);
        float  rimFresnel  = pow(1.0 - NdotV, 4);
        float  rimVertical = normalWS.y * 0.5 + 0.5;
        float3 rim         = rimLight * rimFresnel * rimVertical * radiance2 * baseColor.rgb * _RimColor * 10;

        float  fresnelArea = pow(1.0 - NdotV, 1);
        float3 fresnel     = lerp(1.0, _FresnelColor.rgb, fresnelArea);
        float3 emission    = _EmissionTex.Sample(sampler_EmissionTex, input.uv).rgb;

        float3 color = (Diffuse + Specular + Ambient + rim) * fresnel + emission;
        return half4(color, 1.0);
    }

    half4 Frag_Outline(PBRVaryings input) : SV_Target
    {
        #if !defined(ENABLE_OUTLINE)
        clip(-1);
        #endif
        float4 baseColor = _MainTex.Sample(sampler_MainTex, input.uv) * _BaseColor;
        return _OutlineColor * baseColor;
    }

    half4 Frag_DepthOnly(PBRVaryings input) : SV_Target
    {
        return half4(0,0,0,0);
    }

    half4 Frag_DepthNormals(PBRVaryings input) : SV_Target
    {
        float3 normalWS = ComputeNormalWS(input);
        float linearDepth = LinearEyeDepth(input.positionCS.z / input.positionCS.w, _ZBufferParams);
        return half4(normalWS, linearDepth);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual
            Blend One Zero

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local ENABLE_CELTOON
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