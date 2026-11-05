// ════════════════════════════════════════════════════════════
//  Unity 6 / URP 17 标准材质 — 能力范围见同目录 README.md
// ════════════════════════════════════════════════════════════
Shader "Render/YourShader"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _MainTex ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);

    CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        half4 _BaseColor;
        float _AlphaClip;
        float _Cutoff;
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
        float3 normalWS : TEXCOORD1;
        float3 positionWS : TEXCOORD2;
        half fogFactor : TEXCOORD3;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // ════════════════════════════════════════════════════════════
    //  SurfacePositionWS — 所有 Pass 共用；在此实现顶点变形并更新法线策略
    // ════════════════════════════════════════════════════════════
    float3 SurfacePositionWS(Attributes input)
    {
        return TransformObjectToWorld(input.positionOS.xyz);
    }

    // ════════════════════════════════════════════════════════════
    //  SurfaceNormalWS — 与变形匹配的几何法线，阴影偏置也使用此入口
    // ════════════════════════════════════════════════════════════
    float3 SurfaceNormalWS(Attributes input)
    {
        return TransformObjectToWorldNormal(input.normalOS);
    }

    // ════════════════════════════════════════════════════════════
    //  SampleSurface — 统一贴图变换与 AlphaClip，供颜色/深度/阴影共用
    // ════════════════════════════════════════════════════════════
    half4 SampleSurface(float2 uv)
    {
        half4 surface = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex)) * _BaseColor;
        #if defined(_ALPHATEST_ON)
            clip(surface.a - _Cutoff);
        #endif
        return surface;
    }

    // ════════════════════════════════════════════════════════════
    //  Vert — Forward、DepthOnly、DepthNormals 共用相同几何位置
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
        output.fogFactor = ComputeFogFactor(output.positionCS.z);
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  VertShadow — 共享变形位置，加 URP 光源偏置和近裁剪处理
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
        output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, lightDirectionWS));
        output.positionCS = ApplyShadowClamping(output.positionCS);
        output.uv = input.uv;
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 基础 Lambert 主光、主光阴影、SH 环境光与雾
    // ════════════════════════════════════════════════════════════
    half4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half4 surface = SampleSurface(input.uv);
        half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
        #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
            float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
        #else
            float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
        #endif
        Light light = GetMainLight(shadowCoord);
        half diffuse = saturate(dot(normalWS, light.direction));
        half3 lighting = max(SampleSH(normalWS), 0)
                       + light.color * diffuse * light.distanceAttenuation * light.shadowAttenuation;
        return half4(MixFog(surface.rgb * lighting, input.fogFactor), 1);
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
    //  FragNormals — 输出世界空间法线，兼容 URP 八面体编码
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
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.0
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
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
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
