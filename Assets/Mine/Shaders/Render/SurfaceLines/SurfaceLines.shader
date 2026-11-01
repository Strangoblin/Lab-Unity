// ═══════════════════════════════════════════════════════════════
//  SurfaceLines — surface-attached procedural lines
//
//  纯线条版本：解析式线场，不依赖 3D 纹理或分形层级。
//  UV0 / 对象空间 / 世界空间三种锚定方式；对象/世界模式按法线
//  主轴投影，轴切换处存在接缝。屏幕导数只用于抗锯齿和最小线宽。
// ═══════════════════════════════════════════════════════════════
Shader "Render/SurfaceLines"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _MainTex ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        [Header(Lighting)]
        _AmbientStrength ("Ambient", Range(0, 2)) = 1
        _MainLightStrength ("Main Light", Range(0, 2)) = 1
        _MinShade ("Min Shade", Range(0, 1)) = 0.35
        [Toggle(_RECEIVE_SHADOWS)] _ReceiveShadows ("Receive Shadows", Float) = 1

        [Header(Line Pattern)]
        [Enum(UV0, 0, Object Space, 1, World Space, 2)] _CoordinateMode ("Surface Coordinates", Float) = 0
        _LineTiling ("Line Tiling", Range(0.01, 10)) = 1
        _LineSpacing ("Line Spacing", Range(0.01, 2)) = 0.10
        _LineWidth ("Line Width (fraction)", Range(0, 0.5)) = 0.18
        _LineSoftness ("Line Softness", Range(0, 0.5)) = 0.01
        _LineAngle ("Line Angle", Range(0, 180)) = 45
        _LinePhase ("Line Phase", Range(0, 1)) = 0
        _MinPixelWidth ("Min Pixel Width", Range(0, 4)) = 1
        _CrossAngleOffset ("Cross Angle", Range(0, 180)) = 90
        _CrossIntensity ("Cross Intensity", Range(0, 1)) = 0

        [Header(Line Color)]
        _LineColor ("Line Color", Color) = (0.92, 0.90, 0.84, 1)
        _BackgroundColor ("Background Color", Color) = (0.02, 0.03, 0.04, 1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
    #include "Assets/Mine/Shaders/Render/SurfaceLines/SurfaceLinesFunction.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    float4 _MainTex_TexelSize;

    CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float4 _BaseColor;
        float _AlphaClip;
        float _Cutoff;

        float _AmbientStrength;
        float _MainLightStrength;
        float _MinShade;
        float _ReceiveShadows;

        float _CoordinateMode;
        float _LineTiling;
        float _LineSpacing;
        float _LineWidth;
        float _LineSoftness;
        float _LineAngle;
        float _LinePhase;
        float _MinPixelWidth;
        float _CrossAngleOffset;
        float _CrossIntensity;

        float4 _LineColor;
        float4 _BackgroundColor;
    CBUFFER_END

    float3 _LightDirection;
    float3 _LightPosition;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float2 lineSpace : TEXCOORD1;
        float3 normalWS : TEXCOORD2;
        float3 positionWS : TEXCOORD3;
        half fogFactor : TEXCOORD4;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // ════════════════════════════════════════════════════════════
    //  SurfacePositionWS — 所有 Pass 共用同一几何位置
    // ════════════════════════════════════════════════════════════
    float3 SurfacePositionWS(Attributes input)
    {
        return TransformObjectToWorld(input.positionOS.xyz);
    }

    // ════════════════════════════════════════════════════════════
    //  SurfaceNormalWS — 与几何位置匹配的世界空间法线
    // ════════════════════════════════════════════════════════════
    float3 SurfaceNormalWS(Attributes input)
    {
        return TransformObjectToWorldNormal(input.normalOS);
    }

    // ════════════════════════════════════════════════════════════
    //  SurfaceLines_BuildLineSpace — 按锚定模式生成 2D 线场坐标
    // ════════════════════════════════════════════════════════════
    float2 SurfaceLines_BuildLineSpace(
        Attributes input,
        float3 positionWS,
        float3 normalWS)
    {
        float2 uvSpace = input.uv;
        float2 objectSpace = SurfaceLines_ProjectDominant(input.positionOS.xyz, input.normalOS);
        float2 worldSpace = SurfaceLines_ProjectDominant(positionWS, normalWS);

        float2 space = lerp(uvSpace, objectSpace, step(0.5, _CoordinateMode));
        space = lerp(space, worldSpace, step(1.5, _CoordinateMode));
        return space * max(_LineTiling, 1e-3);
    }

    // ════════════════════════════════════════════════════════════
    //  SampleSurface — 统一贴图变换与 AlphaClip
    // ════════════════════════════════════════════════════════════
    half4 SampleSurface(float2 uv)
    {
        half4 surface = _MainTex_TexelSize.z >= 1.0
            ? SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex))
            : half4(1.0, 1.0, 1.0, 1.0);
        surface *= _BaseColor;
        #if defined(_ALPHATEST_ON)
            clip(surface.a - _Cutoff);
        #endif
        return surface;
    }

    // ════════════════════════════════════════════════════════════
    //  Vert — Forward、DepthOnly、DepthNormals 共用
    // ════════════════════════════════════════════════════════════
    Varyings Vert(Attributes input)
    {
        Varyings output = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.normalWS = SurfaceNormalWS(input);
        output.positionWS = SurfacePositionWS(input);
        output.positionCS = TransformWorldToHClip(output.positionWS);
        output.uv = input.uv;
        output.lineSpace = SurfaceLines_BuildLineSpace(input, output.positionWS, output.normalWS);
        output.fogFactor = ComputeFogFactor(output.positionCS.z);
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  VertShadow — 共享几何位置，加 URP 光源偏置与近裁剪
    // ════════════════════════════════════════════════════════════
    Varyings VertShadow(Attributes input)
    {
        Varyings output = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.normalWS = SurfaceNormalWS(input);
        output.positionWS = SurfacePositionWS(input);
        #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
            float3 lightDirectionWS = normalize(_LightPosition - output.positionWS);
        #else
            float3 lightDirectionWS = _LightDirection;
        #endif
        output.positionCS = TransformWorldToHClip(
            ApplyShadowBias(output.positionWS, output.normalWS, lightDirectionWS));
        output.positionCS = ApplyShadowClamping(output.positionCS);
        output.uv = input.uv;
        output.lineSpace = SurfaceLines_BuildLineSpace(input, output.positionWS, output.normalWS);
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 主光 + SH 环境光；解析线场作为程序化反照率
    // ════════════════════════════════════════════════════════════
    half4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        half4 surface = SampleSurface(input.uv);
        float3 normalWS = NormalizeNormalPerPixel(input.normalWS);

        #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
            float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
        #else
            float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
        #endif
        Light mainLight = GetMainLight(shadowCoord);
        half diffuse = saturate(dot(normalWS, mainLight.direction));
        half3 ambient = max(SampleSH(normalWS), 0.0);
        half3 lighting = ambient * _AmbientStrength;

        #if defined(_RECEIVE_SHADOWS)
            lighting += mainLight.color * diffuse * mainLight.distanceAttenuation
                * mainLight.shadowAttenuation * _MainLightStrength;
        #else
            lighting += mainLight.color * diffuse * mainLight.distanceAttenuation
                * _MainLightStrength;
        #endif

        float lineMask = SurfaceLines_Combine(
            input.lineSpace,
            radians(_LineAngle),
            radians(_CrossAngleOffset),
            _LineSpacing,
            _LineWidth,
            _LineSoftness,
            _MinPixelWidth,
            _LinePhase,
            _CrossIntensity);

        half lightLevel = SurfaceLines_Luminance(lighting);
        half shade = lerp(_MinShade, 1.0, lightLevel);
        half3 albedo = lerp(
            surface.rgb * _BackgroundColor.rgb,
            _LineColor.rgb,
            lineMask);
        half3 litColor = MixFog(albedo * shade, input.fogFactor);
        return half4(litColor, 1.0);
    }

    // ════════════════════════════════════════════════════════════
    //  FragDepth — 与 Forward 一致的裁剪轮廓
    // ════════════════════════════════════════════════════════════
    half4 FragDepth(Varyings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        SampleSurface(input.uv);
        return 0;
    }

    // ════════════════════════════════════════════════════════════
    //  FragNormals — 世界空间法线，兼容 URP 八面体编码
    // ════════════════════════════════════════════════════════════
    half4 FragNormals(Varyings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        SampleSurface(input.uv);
        float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
        #if defined(_GBUFFER_NORMALS_OCT)
            float2 octNormal = PackNormalOctQuadEncode(normalWS);
            return half4(PackFloat2To888(saturate(octNormal * 0.5 + 0.5)), 0);
        #else
            return half4(normalWS, 0);
        #endif
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }
        LOD 100

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _RECEIVE_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ColorMask R
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragNormals
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ColorMask 0
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertShadow
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
    }

    Fallback Off
}
