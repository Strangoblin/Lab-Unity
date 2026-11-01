// ════════════════════════════════════════════════════════════
//  RainEffect — 雨天湿身：水膜、吸水、水滴、水痕与雨滴涟漪
//  与积雪分离；仅依赖 WeatherFunction.hlsl 与基础 PBR 光照验证。
// ════════════════════════════════════════════════════════════
Shader "Render/RainEffect"
{
    Properties
    {
        [Header(Basic)]
        [MainColor] _BaseColor ("Base Color", Color) = (0.85, 0.74, 0.67, 1.0)
        [MainTexture] _MainTex ("Base Color", 2D) = "white" {}
        _Roughness ("Roughness", Range(0.0, 1.0)) = 0.5
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0

        [Header(Wet Mask)]
        _WetMask ("Wet Mask", 2D) = "white" {}
        [Header(Weather Noise)]
        _WeatherNoise ("Weather Noise", 2D) = "grey" {}

        [Header(Wetness)]
        _Wetness ("Wetness", Range(0.0, 1.0)) = 0.0
        _WetDarken ("Wet Darken", Range(0.0, 1.0)) = 0.45
        _WetSaturation ("Wet Saturation", Range(0.0, 2.0)) = 1.25
        _WetRoughness ("Wet Roughness", Range(0.0, 1.0)) = 0.15
        _WetSpecular ("Wet Specular", Range(0.0, 4.0)) = 1.0
        _WetNormalFlatten ("Wet Normal Flatten", Range(0.0, 1.0)) = 0.5
        _WetVariation ("Wet Variation", Range(0.0, 1.0)) = 0.18
        _WetVariationScale ("Wet Variation Scale", Range(0.1, 16.0)) = 0.5
        _WaterSheen ("Water Sheen", Range(0.0, 2.0)) = 0.35

        [Header(Water Droplets)]
        _DropletAmount ("Droplet Amount", Range(0.0, 1.0)) = 0.5
        _DropletScale ("Droplet Scale", Range(1.0, 128.0)) = 24.0
        _DropletNormal ("Droplet Normal", Range(0.0, 2.0)) = 0.85
        _DropletSpecular ("Droplet Specular", Range(0.0, 4.0)) = 2.0
        _DropletGloss ("Droplet Gloss", Range(0.0, 2.0)) = 0.6
        _DropletDarken ("Droplet Darken", Range(0.0, 1.0)) = 0.15

        [Header(Water Flow)]
        _FlowAmount ("Flow Amount", Range(0.0, 1.0)) = 1.0
        _FlowScale ("Flow Scale", Range(0.1, 32.0)) = 2.2
        _FlowSpeed ("Flow Speed", Range(0.0, 4.0)) = 0.4
        _FlowNormal ("Flow Normal", Range(0.0, 2.0)) = 0.45
        _FlowSpecular ("Flow Specular", Range(0.0, 4.0)) = 0.6

        [Header(Absorbency)]
        _Absorbency ("Absorbency", Range(0.0, 1.0)) = 0.0
        _AbsorbScale ("Absorb Scale", Range(1.0, 64.0)) = 10.0
        _AbsorbSpeed ("Absorb Speed", Range(0.1, 4.0)) = 0.5

        [Header(Rain Ripples)]
        _RippleAmount ("Ripple Amount", Range(0.0, 1.0)) = 0.35
        _RippleScale ("Ripple Scale", Range(1.0, 64.0)) = 12.0
        _RippleSpeed ("Ripple Speed", Range(0.1, 4.0)) = 0.6
        _RippleNormal ("Ripple Normal", Range(0.0, 1.0)) = 0.25
        _RippleSpecular ("Ripple Specular", Range(0.0, 4.0)) = 1.0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Assets/Mine/Special/HLSL/ENVFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/PBRFunction.hlsl"
    #include "Assets/Mine/Shaders/Render/WeatherEffect/WeatherFunction.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    TEXTURE2D(_WetMask);
    SAMPLER(sampler_WetMask);
    TEXTURE2D(_WeatherNoise);
    SAMPLER(sampler_WeatherNoise);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _MainTex_ST;
        float4 _WetMask_ST;
        float _Roughness;
        float _Metallic;
        float _Wetness;
        float _WetDarken;
        float _WetSaturation;
        float _WetRoughness;
        float _WetSpecular;
        float _WetNormalFlatten;
        float _WetVariation;
        float _WetVariationScale;
        float _WaterSheen;
        float _DropletAmount;
        float _DropletScale;
        float _DropletNormal;
        float _DropletSpecular;
        float _DropletGloss;
        float _DropletDarken;
        float _FlowAmount;
        float _FlowScale;
        float _FlowSpeed;
        float _FlowNormal;
        float _FlowSpecular;
        float _Absorbency;
        float _AbsorbScale;
        float _AbsorbSpeed;
        float _RippleAmount;
        float _RippleScale;
        float _RippleSpeed;
        float _RippleNormal;
        float _RippleSpecular;
    CBUFFER_END

    float3 _LightDirection;
    float3 _LightPosition;

    struct RainAttributes
    {
        float4 positionOS   : POSITION;
        float3 normalOS     : NORMAL;
        float2 uv           : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct RainVaryings
    {
        float4 positionCS   : SV_POSITION;
        float3 positionWS   : TEXCOORD0;
        float3 positionOS   : TEXCOORD1;
        float3 normalWS     : TEXCOORD2;
        float2 uv           : TEXCOORD3;
        half fogFactor      : TEXCOORD4;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // ════════════════════════════════════════════════════════════
    //  Vert — 输出世界/物体空间位置、法线与雾因子
    // ════════════════════════════════════════════════════════════
    RainVaryings Vert(RainAttributes input)
    {
        RainVaryings output = (RainVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
        output.positionCS = TransformWorldToHClip(output.positionWS);
        output.positionOS = input.positionOS.xyz;
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
        output.uv = input.uv;
        output.fogFactor = ComputeFogFactor(output.positionCS.z);
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 水膜、水滴、水痕与涟漪后，走 Lambert + GGX 基础光照
    // ════════════════════════════════════════════════════════════
    half4 Frag(RainVaryings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
            float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
        #else
            float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
        #endif
        Light mainLight = GetMainLight(shadowCoord);
        float3 lightDirWS = mainLight.direction;
        float3 lightColor = mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
        float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);

        float3 geometricNormalWS = normalize(input.normalWS);
        float3 normalOS = normalize(TransformWorldToObjectNormal(geometricNormalWS));
        float3 normalWS = geometricNormalWS;
        float3 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(input.uv, _MainTex)).rgb * _BaseColor.rgb;
        float roughness = _Roughness;
        float metallic = _Metallic;

        float wetMask = SAMPLE_TEXTURE2D(_WetMask, sampler_WetMask, TRANSFORM_TEX(input.uv, _WetMask)).r;
        float wetness = saturate(_Wetness * wetMask);

        // 湿度分布按物体空间低频噪声打散，避免整件物体均匀变湿
        float2 wetUV;
        float3 wetAxisX;
        float3 wetAxisY;
        Weather_ProjectAxis(normalOS, input.positionOS, wetUV, wetAxisX, wetAxisY);
        wetness = Weather_WetnessField(TEXTURE2D_ARGS(_WeatherNoise, sampler_WeatherNoise),
            wetUV, wetness, _WetVariation, _WetVariationScale);

        baseColor = Weather_WetAlbedo(baseColor, wetness, _WetDarken, _WetSaturation);
        roughness = lerp(roughness, _WetRoughness, wetness);
        normalWS = Weather_WetNormal(normalWS, geometricNormalWS, wetness, _WetNormalFlatten);
        float3 wetFilmNormalWS = normalWS;

        float dropletMask;
        float flowMask;
        normalWS = Weather_WaterDetail(TEXTURE2D_ARGS(_WeatherNoise, sampler_WeatherNoise),
            input.positionOS, normalOS, normalWS, wetness, _Time.y,
            _DropletAmount, _DropletScale, _DropletNormal,
            _FlowAmount, _FlowScale, _FlowSpeed, _FlowNormal,
            dropletMask, flowMask);

        // 吸水：布料把水吸进纤维后表面细节退回水膜法线，压暗与光滑度仍保留
        float absorb = Weather_AbsorbFade(wetUV, _AbsorbScale, _AbsorbSpeed, _Time.y)
            * saturate(_Absorbency);
        float detailKeep = 1.0 - absorb;
        normalWS = lerp(normalWS, wetFilmNormalWS, absorb);
        dropletMask *= detailKeep;
        flowMask *= detailKeep;

        float rippleMask;
        normalWS = Weather_RainRipples(input.positionOS, normalOS, normalWS, wetness, _Time.y,
            _RippleAmount, _RippleScale, _RippleSpeed, _RippleNormal, rippleMask);
        normalWS = normalize(normalWS);

        // 水珠内部略暗，模拟折射后的吸收；光斑由水膜高光负责
        baseColor *= lerp(1.0, 1.0 - saturate(_DropletDarken), dropletMask);

        float3 halfVec = normalize(lightDirWS + viewDirWS);
        float NdotL = max(0.0, dot(normalWS, lightDirWS));
        float NdotV = max(1e-4, dot(normalWS, viewDirWS));
        float NdotH = max(0.0, dot(normalWS, halfVec));
        float VdotH = max(0.0, dot(viewDirWS, halfVec));

        float3 F0 = lerp(float3(0.04, 0.04, 0.04), baseColor, metallic);
        float3 F = F_P5(F0, VdotH);
        float3 diffuse = Diff_Lambert(baseColor) * (1.0 - metallic) * (1.0 - F);
        float3 specular = Spec_CK(NdotH, NdotV, NdotL, VdotH, roughness) * F;

        // 水膜高光：主瓣贴近水膜粗糙度，宽瓣补湿面整体的柔和反光，珠面另加锐利闪点
        float3 waterF = F_P5(float3(WEATHER_WATER_F0, WEATHER_WATER_F0, WEATHER_WATER_F0), VdotH);
        float waterGain = wetness * _WetSpecular
            * (1.0 + dropletMask * _DropletSpecular
                 + flowMask * _FlowSpecular
                 + rippleMask * _RippleSpecular);
        specular += Spec_CK(NdotH, NdotV, NdotL, VdotH, max(_WetRoughness, 0.04)) * waterF * waterGain;
        specular += Spec_CK(NdotH, NdotV, NdotL, VdotH, 0.32) * waterF * (wetness * _WaterSheen);
        specular += Spec_CK(NdotH, NdotV, NdotL, VdotH, 0.05) * waterF * (dropletMask * _DropletGloss);

        float3 color = (diffuse + specular) * PI * lightColor * NdotL;
        color += BRDF_Env(baseColor, NdotV, normalWS, viewDirWS, roughness, metallic,
            unity_SpecCube0, samplerunity_SpecCube0);
        // 水珠镜面反射：扰动后的法线采样环境，按水膜菲涅耳加权，避免整颗珠泛白
        float3 beadReflection = unity_SpecCube0.SampleLevel(samplerunity_SpecCube0,
            reflect(-viewDirWS, normalWS), 0.0).rgb;
        color += beadReflection * waterF * (dropletMask * 2.0);
        color = MixFog(color, input.fogFactor);
        return half4(color, 1.0);
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_DepthOnly — 阴影与深度 Pass 只输出几何
    // ════════════════════════════════════════════════════════════
    half4 Frag_DepthOnly() : SV_Target
    {
        return half4(0, 0, 0, 0);
    }

    RainVaryings VertShadow(RainAttributes input)
    {
        RainVaryings output = Vert(input);
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

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ColorMask 0
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
            Tags { "LightMode" = "DepthOnly" }

            ColorMask R
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag_DepthOnly
            ENDHLSL
        }
    }
    Fallback Off
}
