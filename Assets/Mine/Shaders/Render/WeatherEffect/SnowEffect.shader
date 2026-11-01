// ════════════════════════════════════════════════════════════
//  SnowEffect — 雪天积雪：朝上积雪、雪面法线与闪烁
//  与雨天湿身分离；仅依赖 WeatherFunction.hlsl 与基础 PBR 光照验证。
// ════════════════════════════════════════════════════════════
Shader "Render/SnowEffect"
{
    Properties
    {
        [Header(Basic)]
        [MainColor] _BaseColor ("Base Color", Color) = (0.85, 0.74, 0.67, 1.0)
        [MainTexture] _MainTex ("Base Color", 2D) = "white" {}
        _Roughness ("Roughness", Range(0.0, 1.0)) = 0.5
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0

        [Header(Snow Texture)]
        _WeatherNoise ("Weather Noise", 2D) = "grey" {}

        [Header(Snow)]
        _SnowAmount ("Snow Amount", Range(0.0, 1.0)) = 0.0
        _SnowColor ("Snow Color", Color) = (0.92, 0.95, 1.0, 1.0)
        _SnowThreshold ("Snow Threshold", Range(-1.0, 1.0)) = 0.25
        _SnowSoftness ("Snow Softness", Range(0.0, 1.0)) = 0.3
        _SnowRoughness ("Snow Roughness", Range(0.0, 1.0)) = 0.85
        _SnowNormalBlend ("Snow Normal Blend", Range(0.0, 1.0)) = 0.6
        _SnowNoiseScale ("Snow Noise Scale", Range(0.01, 64.0)) = 1.0
        _SnowSparkle ("Snow Sparkle", Range(0.0, 2.0)) = 0.5
        _SnowSparkleDensity ("Snow Sparkle Density", Range(0.0, 1.0)) = 0.08
        _SnowSparkleScale ("Snow Sparkle Scale", Range(1.0, 256.0)) = 32.0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Assets/Mine/Special/HLSL/ENVFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/PBRFunction.hlsl"
    #include "Assets/Mine/Shaders/Render/WeatherEffect/WeatherFunction.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    TEXTURE2D(_WeatherNoise);
    SAMPLER(sampler_WeatherNoise);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _MainTex_ST;
        float _Roughness;
        float _Metallic;
        float4 _SnowColor;
        float _SnowAmount;
        float _SnowThreshold;
        float _SnowSoftness;
        float _SnowRoughness;
        float _SnowNormalBlend;
        float _SnowNoiseScale;
        float _SnowSparkle;
        float _SnowSparkleDensity;
        float _SnowSparkleScale;
    CBUFFER_END

    float3 _LightDirection;
    float3 _LightPosition;

    struct SnowAttributes
    {
        float4 positionOS   : POSITION;
        float3 normalOS     : NORMAL;
        float2 uv           : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct SnowVaryings
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
    SnowVaryings Vert(SnowAttributes input)
    {
        SnowVaryings output = (SnowVaryings)0;
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
    //  Frag — 积雪覆盖后，走 Lambert + GGX 基础光照
    // ════════════════════════════════════════════════════════════
    half4 Frag(SnowVaryings input) : SV_Target
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

        // 积雪噪声按物体空间 XZ / XY 双轴投影，图案跟随网格不再随世界位置漂移
        float2 noiseUVXZ = input.positionOS.xz * _SnowNoiseScale;
        float2 noiseUVXY = input.positionOS.xy * _SnowNoiseScale;
        float noiseWeightXZ = abs(normalOS.y);
        float noiseWeightXY = abs(normalOS.z);
        float snowNoise = (SAMPLE_TEXTURE2D(_WeatherNoise, sampler_WeatherNoise, noiseUVXZ).r * noiseWeightXZ
            + SAMPLE_TEXTURE2D(_WeatherNoise, sampler_WeatherNoise, noiseUVXY).r * noiseWeightXY)
            / max(noiseWeightXZ + noiseWeightXY, 1e-3);
        float snowMask = Weather_SnowMask(dot(geometricNormalWS, WEATHER_SNOW_UP), snowNoise,
            _SnowAmount, _SnowThreshold, _SnowSoftness);

        // 过渡带里法线被混向天空，NdotH 会短暂抬高；闪烁只用稳定覆盖的区域，
        // 否则雪缘会出现一圈细碎亮点
        float snowSolid = smoothstep(0.5, 0.9, snowMask);

        baseColor = lerp(baseColor, _SnowColor.rgb * lerp(0.9, 1.0, snowNoise), snowMask);
        roughness = lerp(roughness, _SnowRoughness, snowMask);
        metallic = lerp(metallic, 0.0, snowMask);
        normalWS = Weather_SnowNormal(normalWS, snowMask, _SnowNormalBlend);
        normalWS = normalize(normalWS);

        float3 halfVec = normalize(lightDirWS + viewDirWS);
        float NdotL = max(0.0, dot(normalWS, lightDirWS));
        float NdotV = max(1e-4, dot(normalWS, viewDirWS));
        float NdotH = max(0.0, dot(normalWS, halfVec));
        float VdotH = max(0.0, dot(viewDirWS, halfVec));

        float3 F0 = lerp(float3(0.04, 0.04, 0.04), baseColor, metallic);
        float3 F = F_P5(F0, VdotH);
        float3 diffuse = Diff_Lambert(baseColor) * (1.0 - metallic) * (1.0 - F);
        float3 specular = Spec_CK(NdotH, NdotV, NdotL, VdotH, roughness) * F;

        float3 color = (diffuse + specular) * PI * lightColor * NdotL;
        // 闪烁格点取世界 XZ 投影，只在朝上的雪面成立；侧壁会被拉成竖条，按朝上程度压掉
        float sparkleUp = saturate(normalWS.y * normalWS.y);
        color += Weather_SnowSparkle(input.positionWS, _SnowSparkleScale,
            _SnowSparkleDensity, NdotH) * lightColor * NdotL * _SnowSparkle * snowSolid * sparkleUp;
        color += BRDF_Env(baseColor, NdotV, normalWS, viewDirWS, roughness, metallic,
            unity_SpecCube0, samplerunity_SpecCube0);
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

    SnowVaryings VertShadow(SnowAttributes input)
    {
        SnowVaryings output = Vert(input);
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
