// ════════════════════════════════════════════════════════════
//  PBRToon — Unity 6 / URP 17 材质；保留 Toon、SSS 与描边算法
// ════════════════════════════════════════════════════════════
Shader "Render/PBRToon"
{
    Properties
    {
        [Header(Basic Textures)]
        [MainColor] _BaseColor ("Base Color", Color) = (1.0, 1.0, 1.0, 1.0)
        [MainTexture] _MainTex ("Base Color", 2D) = "white" {}
        _NormalTex ("Normal Map", 2D) = "bump" {}
        _AOTex ("AO Map", 2D) = "white" {}
        _EmissionTex ("Emission Map", 2D) = "black" {}

        [Header(PBR Parameters)]
        _Brightness ("Brightness", Range(0.0, 2.0)) = 1.0
        _Roughness ("Roughness", Range(0.0, 1.0)) = 0.5
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0
        _Anisotropy ("Anisotropy", Range(-1.0, 1.0)) = 0.0

        [Header(SSS Parameters)]
        _SSSLut ("SSS Lut", 2D) = "black" {}
        _SSSScatter ("SSS Scatter", Range(0.0, 1.0)) = 0.15
        _SSSWeight ("SSS Weight", Range(0.0, 1.0)) = 0.5

        [Header(Additional Fill Light)]
        _FillLightStrength ("Fill Light Strength", Range(0.0, 2.0)) = 1.0
        _FillLightWeight ("Fill Light Direction Weight", Range(0.0, 1.0)) = 1.0

        [Header(Toon Parameters)]
        [Toggle(ENABLE_CELTOON)]_EnableCelToon ("Enable CelToon", Float) = 1
        _ToonWeight ("Toon Weight", Range(0.0, 1.0)) = 1.0
        _ToonRange ("Toon Range", Range(0.0, 1.0)) = 1.0
        _ToonSmoothness ("Toon Smoothness", Range(0.0, 1.0)) = 1.0

        [Header(Custom Parameters)]
        _RimRange ("Rim Range", Range(0.0, 1.0)) = 0.5
        _RimStrength ("Rim Strength", Range(0.0, 1.0)) = 0.5
        _FresnelColor ("Fresnel Color", Color) = (1.0, 1.0, 1.0, 1.0)

        [Header(Outline Parameters)]
        [Toggle(ENABLE_OUTLINE)]_EnableOutline ("Enable Outline", Float) = 1
        _OutlineColor ("Outline Color", Range(0.0, 1.0)) = 0.5
        _OutlineScale ("Outline Scale", Range(0.0, 1.0)) = 0.2

    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
    #include "Assets/Mine/Special/HLSL/RimLightFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/LightFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/ENVFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/PBRFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/SSSFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/NormalFunction.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    TEXTURE2D(_NormalTex);
    SAMPLER(sampler_NormalTex);
    TEXTURE2D(_AOTex);
    SAMPLER(sampler_AOTex);
    TEXTURE2D(_EmissionTex);
    SAMPLER(sampler_EmissionTex);
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _MainTex_ST;
        float4 _NormalTex_ST;
        float4 _AOTex_ST;
        float4 _EmissionTex_ST;
        float _EnableCelToon;
        float _EnableOutline;
        float _Brightness;
        float _Roughness;
        float _Metallic;
        float _FillLightStrength;
        float _FillLightWeight;
        float _Anisotropy;
        float _SSSScatter;
        float _SSSWeight;
        float _ToonWeight;
        float _ToonRange;
        float _ToonSmoothness;
        float _RimRange;
        float _RimStrength;
        float _OutlineColor;
        float _OutlineScale;
        float4 _ShadowColor;
        float4 _FresnelColor;
        float4 _SSSLut_TexelSize;
    CBUFFER_END

    float3 _LightDirection;
    float3 _LightPosition;

    struct PBRAttributes
    {
        float4 positionOS   : POSITION;
        float3 normalOS     : NORMAL;
        float4 tangentOS    : TANGENT;
        float2 UV           : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct PBRVaryings
    {
        float4 positionCS   : SV_POSITION;
        float3 positionWS   : TEXCOORD0;
        float3 normalWS     : TEXCOORD1;
        float3 tangentWS    : TEXCOORD2;
        float3 bitanentWS   : TEXCOORD3;
        float2 uv           : TEXCOORD4;
        half fogFactor      : TEXCOORD5;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // ════════════════════════════════════════════════════════════
    //  SurfacePositionWS — 基础几何入口，Forward/Depth/Shadow 与描边共用
    // ════════════════════════════════════════════════════════════
    float3 SurfacePositionWS(PBRAttributes input)
    {
        return TransformObjectToWorld(input.positionOS.xyz);
    }

    // ════════════════════════════════════════════════════════════
    //  ComputeNormalWS — 使用独立法线贴图 UV 变换重建世界法线
    // ════════════════════════════════════════════════════════════
    float3 ComputeNormalWS(PBRVaryings input)
    {
        float3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, TRANSFORM_TEX(input.uv, _NormalTex)));
        return normalize(
            input.tangentWS   * normalTS.x +
            input.bitanentWS * normalTS.y +
            input.normalWS   * normalTS.z
        );
    }

    PBRVaryings Vert(PBRAttributes input)
    {
        PBRVaryings output = (PBRVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionWS = SurfacePositionWS(input);
        output.positionCS = TransformWorldToHClip(output.positionWS);
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);

        float3 tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
        float tangentSign = input.tangentOS.w * unity_WorldTransformParams.w;
        float3 bitangentWS = cross(output.normalWS, tangentWS) * tangentSign;

        output.tangentWS = tangentWS;
        output.bitanentWS = bitangentWS;
        output.uv = input.UV;
        output.fogFactor = ComputeFogFactor(output.positionCS.z);
        return output;
    }

    PBRVaryings Vert_Outline(PBRAttributes input)
    {
        PBRVaryings output = (PBRVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
        output.positionWS = SurfacePositionWS(input) + output.normalWS * _OutlineScale * 0.01;
        output.positionCS = TransformWorldToHClip(output.positionWS);

        float3 tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
        float tangentSign = input.tangentOS.w * unity_WorldTransformParams.w;
        float3 bitangentWS = cross(output.normalWS, tangentWS) * tangentSign;

        output.tangentWS = tangentWS;
        output.bitanentWS = bitangentWS;
        output.uv = input.UV;
        output.fogFactor = ComputeFogFactor(output.positionCS.z);
        return output;
    }

    half4 Frag(PBRVaryings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

        float3 mainLitDir;
        float3 mainLitColor;
        float  mainLitDistanceAtten;
        float  mainLitShadowAtten;
        #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
            float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
        #else
            float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
        #endif
        Light mainLight = GetMainLight(shadowCoord);
        mainLitDir = mainLight.direction;
        mainLitColor = mainLight.color;
        mainLitDistanceAtten = mainLight.distanceAttenuation;
        mainLitShadowAtten = mainLight.shadowAttenuation;

        float3 fillDirection;
        float  fillDistanceAtten;
        float3 fillColor = LightFunction_FillColor(input.positionWS, screenUV,
            fillDirection, fillDistanceAtten);

        float  lightWeight = saturate(fillDistanceAtten * _FillLightWeight);
        float3 lightDirWS = lerp(mainLitDir, fillDirection, lightWeight);
        lightDirWS = normalize(lightDirWS);

        float3 camPos = GetCameraPositionWS();
        float3 normalWS = ComputeNormalWS(input);
        float3 tangentWS = input.tangentWS;
        float3 bitangentWS = input.bitanentWS;
        float3 viewDirWS = normalize(camPos - input.positionWS);

        float4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(input.uv, _MainTex)) * _BaseColor;
        float roughness = lerp(0.15, 1.0, _Roughness);

        float3 halfVec = normalize(lightDirWS + viewDirWS);
        float signedNdotL = dot(normalWS, lightDirWS);
        float NdotL = max(0.0, signedNdotL);
        float NdotV = max(0.0, dot(normalWS, viewDirWS));
        float NdotH = max(0.0, dot(normalWS, halfVec));
        float VdotH = max(0.0, dot(viewDirWS, halfVec));
        float LdotH = max(0.0, dot(lightDirWS, halfVec));
        float TdotH = dot(tangentWS, halfVec);
        float BdotH = dot(bitangentWS, halfVec);
        float shadowNdotL;
        float sssNdotL;

        #if defined(ENABLE_CELTOON)
            float halfNdotL = signedNdotL * 0.5 + 0.5;
            float steppedNdotL = smoothstep(
                _ToonRange - _ToonSmoothness * 0.5,
                _ToonRange + _ToonSmoothness * 0.5, halfNdotL);
            sssNdotL = steppedNdotL * 2.0 - 1.0;
            shadowNdotL = lerp(halfNdotL, steppedNdotL, _ToonWeight);
        #else
            sssNdotL = signedNdotL;
            shadowNdotL = NdotL;
        #endif

        float3 SSS = SSS_SampleLut(sssNdotL, input.positionWS, input.normalWS,
            _SSSScatter, _SSSLut_TexelSize);
        float  shadowAO   = SAMPLE_TEXTURE2D(_AOTex, sampler_AOTex, TRANSFORM_TEX(input.uv, _AOTex)).r;
        float  shadowMain = lerp(1, mainLitShadowAtten, saturate(dot(normalWS, mainLitDir)));
        float3 shadowSSS  = lerp(shadowNdotL, SSS, _SSSWeight);
        float3 shadowArea = shadowMain * shadowSSS * shadowAO;

        float3 radiance1 = mainLitColor * mainLitDistanceAtten * shadowArea + fillColor * _FillLightStrength;
        float3 radiance2 = mainLitColor * mainLitDistanceAtten * mainLitShadowAtten * NdotL;
        float3 F0 = lerp(0.04, baseColor.rgb, _Metallic);
        float3 F  = F_Fast(F0, VdotH);

        float3 Diffuse  = Diff_Lambert(baseColor.rgb)
            * PI * radiance1 * (1.0 - _Metallic) * (1.0 - F);
        float3 Specular = Spec_Unity(NdotH, LdotH, VdotH, TdotH, BdotH, roughness, _Anisotropy)
            * PI * radiance2 * F;
        float3 Ambient  = BRDF_Env(baseColor.rgb, NdotV, normalWS, viewDirWS, roughness, _Metallic, unity_SpecCube0, samplerunity_SpecCube0);

        float  rimLight    = RimLightDepth(normalWS, screenUV, _RimRange * 10);
        float  rimFresnel  = pow(1.0 - NdotV, 4);
        float  rimVertical = normalWS.y * 0.5 + 0.5;
        float3 rim         = rimLight * rimFresnel * rimVertical * radiance2 * baseColor.rgb * _RimStrength * 10;

        float  fresnelArea = pow(1.0 - NdotV, 1);
        float3 fresnel     = lerp(1.0, _FresnelColor.rgb, fresnelArea);
        float3 emission    = SAMPLE_TEXTURE2D(_EmissionTex, sampler_EmissionTex, TRANSFORM_TEX(input.uv, _EmissionTex)).rgb;

        float3 color = (Diffuse + Specular + Ambient + rim) * fresnel * _Brightness + emission;
        return half4(MixFog(color, input.fogFactor), 1.0);
    }

    half4 Frag_Outline(PBRVaryings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        #if !defined(ENABLE_OUTLINE)
        clip(-1);
        #endif
        float4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(input.uv, _MainTex)) * _BaseColor;
        half4 outline = _OutlineColor * baseColor;
        outline.rgb = MixFog(outline.rgb, input.fogFactor);
        return outline;
    }

    half4 Frag_DepthOnly(PBRVaryings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return half4(0,0,0,0);
    }

    half4 Frag_DepthNormals(PBRVaryings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float3 normalWS = ComputeNormalWS(input);
        #if defined(_GBUFFER_NORMALS_OCT)
            float2 octNormal = PackNormalOctQuadEncode(normalWS);
            return half4(PackFloat2To888(saturate(octNormal * 0.5 + 0.5)), 0);
        #else
            return half4(normalWS, 0);
        #endif
    }
    // ════════════════════════════════════════════════════════════
    //  VertShadow — 共享基础几何，应用主光/局部光偏置与近裁剪
    // ════════════════════════════════════════════════════════════
    PBRVaryings VertShadow(PBRAttributes input)
    {
        PBRVaryings output = Vert(input);
        #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
            float3 lightDirectionWS = normalize(_LightPosition - output.positionWS);
        #else
            float3 lightDirectionWS = _LightDirection;
        #endif
        output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, lightDirectionWS));
        output.positionCS = ApplyShadowClamping(output.positionCS);
        return output;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 200
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back
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
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma instancing_options renderinglayer
            #pragma multi_compile_fog
            #pragma shader_feature_local ENABLE_CELTOON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex Vert_Outline
            #pragma fragment Frag_Outline
            #pragma multi_compile_fog
            #pragma shader_feature_local ENABLE_OUTLINE
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            ColorMask 0
            Tags { "LightMode"="ShadowCaster" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex VertShadow
            #pragma fragment Frag_DepthOnly
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            ColorMask R
            Tags { "LightMode"="DepthOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag_DepthOnly
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag_DepthNormals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    Fallback Off
}
