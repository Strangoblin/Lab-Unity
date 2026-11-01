// ═══════════════════════════════════════════════════════════════
//  SurfaceDither — surface-stable fractal dithering material
//
//  UV0 或对象空间投影作为表面坐标；屏幕导数决定分形层级，
//  点随表面移动，屏幕上的大小/间距近似恒定。
//  结构规范：.agents/knowledge/unity/standard/shader/shader-structure.md
// ═══════════════════════════════════════════════════════════════
Shader "Render/SurfaceDither"
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
        [Toggle(_RECEIVE_SHADOWS)] _ReceiveShadows ("Receive Shadows", Float) = 1

        [Header(Dither Input)]
        _InputExposure ("Exposure", Range(0, 5)) = 1
        _InputOffset ("Offset", Range(-1, 1)) = 0
        [Toggle(_INVERSE_DOTS)] _InverseDots ("Inverse Dots", Float) = 0

        [Header(Dither Pattern)]
        [NoScaleOffset] _DitherTex ("3D Pattern", 3D) = "white" {}
        [NoScaleOffset] _DitherRampTex ("Brightness Ramp", 2D) = "white" {}
        [Enum(UV0, 0, Object Space, 1)] _CoordinateMode ("Surface Coordinates", Float) = 0
        _DitherTiling ("Pattern Tiling", Range(0.01, 10)) = 1
        _DitherScale ("Dot Scale", Range(2, 10)) = 5
        _DotSizeVariability ("Dot Size Variability", Range(0, 1)) = 0
        _DotContrast ("Dot Contrast", Range(0, 2)) = 1
        _StretchSmoothness ("Stretch Smoothness", Range(0, 2)) = 1
        [Toggle(_QUANTIZE_LAYERS)] _QuantizeLayers ("Quantize Layers", Float) = 0
        [Toggle(_RADIAL_COMPENSATION)] _RadialCompensation ("Radial Compensation", Float) = 0
        [Toggle(_DEBUG_FRACTAL)] _DebugFractal ("Debug Fractal", Float) = 0

        [Header(Dither Color)]
        _DarkColor ("Dark Color", Color) = (0.02, 0.03, 0.04, 1)
        _LightColor ("Light Color", Color) = (0.92, 0.90, 0.84, 1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
    #include "Assets/Mine/Shaders/Render/SurfaceDither/SurfaceDitherFunction.hlsl"

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
        float _ReceiveShadows;

        float _InputExposure;
        float _InputOffset;
        float _InverseDots;

        float _CoordinateMode;
        float _DitherTiling;
        float _DitherScale;
        float _DotSizeVariability;
        float _DotContrast;
        float _StretchSmoothness;
        float _QuantizeLayers;
        float _RadialCompensation;
        float _DebugFractal;

        float4 _DarkColor;
        float4 _LightColor;
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
        float2 ditherUV : TEXCOORD1;
        float3 normalWS : TEXCOORD2;
        float3 positionWS : TEXCOORD3;
        float4 screenPos : TEXCOORD4;
        half fogFactor : TEXCOORD5;
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
    //  SurfaceDither_GetDitherUV — UV0 或按法线主轴的对象空间投影
    // ════════════════════════════════════════════════════════════
    float2 SurfaceDither_GetDitherUV(Attributes input)
    {
        float2 uv = input.uv;
        float3 absNormal = abs(input.normalOS);
        float2 projected;

        if (absNormal.z >= absNormal.x && absNormal.z >= absNormal.y)
        {
            projected = input.positionOS.xy;
        }
        else if (absNormal.x >= absNormal.y)
        {
            projected = input.positionOS.zy;
        }
        else
        {
            projected = input.positionOS.xz;
        }

        return lerp(uv, projected, step(0.5, _CoordinateMode)) * max(_DitherTiling, 1e-3);
    }

    // ════════════════════════════════════════════════════════════
    //  SampleSurface — 统一贴图变换与 AlphaClip，供颜色/深度/阴影共用
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
        output.screenPos = ComputeScreenPos(output.positionCS);
        output.uv = input.uv;
        output.ditherUV = SurfaceDither_GetDitherUV(input);
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
        output.screenPos = ComputeScreenPos(output.positionCS);
        output.uv = input.uv;
        output.ditherUV = SurfaceDither_GetDitherUV(input);
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 主光 + SH 环境光，雾后按亮度做表面稳定抖动
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

        half3 litColor = MixFog(surface.rgb * lighting, input.fogFactor);
        float brightness = saturate(
            SurfaceDither_Luminance(litColor) * _InputExposure + _InputOffset);

        float3 debugData;
        float ditherValue = SurfaceDither_Evaluate(
            input.ditherUV,
            ddx(input.ditherUV),
            ddy(input.ditherUV),
            input.screenPos,
            brightness,
            exp2(_DitherScale),
            _DotSizeVariability,
            _DotContrast,
            _StretchSmoothness,
            debugData);

        half3 ditherColor = lerp(_DarkColor.rgb, _LightColor.rgb, ditherValue);
        #if defined(_DEBUG_FRACTAL)
            ditherColor = lerp(ditherColor, debugData, 0.7);
        #endif

        return half4(ditherColor, 1.0);
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
            #pragma shader_feature_local_fragment _INVERSE_DOTS
            #pragma shader_feature_local_fragment _QUANTIZE_LAYERS
            #pragma shader_feature_local_fragment _RADIAL_COMPENSATION
            #pragma shader_feature_local_fragment _DEBUG_FRACTAL
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
