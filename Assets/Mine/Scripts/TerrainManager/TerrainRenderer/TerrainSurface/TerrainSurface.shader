// ════════════════════════════════════════════════════════════
//  TerrainSurface — 采样 TerrainColorMapBuilder 按地表层烘焙的颜色图
// ════════════════════════════════════════════════════════════
Shader "TerrainSurface/TerrainSurface"
{
    Properties
    {
        [MainTexture] _ColorMap ("Terrain Color Map", 2D) = "white" {}
        [MainColor] _Tint ("Tint", Color) = (1, 1, 1, 1)
        // 地表是纯 unlit 的（颜色全部来自烘焙色图），但它必须能接收主光阴影，
        // 否则草、树、角色投下来的影子在地面上完全不可见。
        // 这里只把颜色乘一个阴影衰减，不引入任何光照模型，保持原来的平涂观感。
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.4
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    // 只为了 MainLightShadow / TransformWorldToShadowCoord 这几个符号；
    // 地表不调用任何 BRDF，Lighting.hlsl 里的光照函数都不会被实例化。
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

    TEXTURE2D(_ColorMap);
    SAMPLER(sampler_ColorMap);

    CBUFFER_START(UnityPerMaterial)
        half4 _Tint;
        half _ShadowStrength;
    CBUFFER_END

    struct TerrainAttributes
    {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct TerrainVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 positionWS : TEXCOORD1;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    struct TerrainNormalAttributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct TerrainNormalVaryings
    {
        float4 positionCS : SV_POSITION;
        float3 normalWS : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // ════════════════════════════════════════════════════════════
    //  Vert — 使用地表生成器写入的全图 UV
    // ════════════════════════════════════════════════════════════
    TerrainVaryings Vert(TerrainAttributes input)
    {
        TerrainVaryings output = (TerrainVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
        output.uv = input.uv;
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Vert_Normals — 法线 prepass 用，只取地表网格已有的平滑顶点法线
    // ════════════════════════════════════════════════════════════
    TerrainNormalVaryings Vert_Normals(TerrainNormalAttributes input)
    {
        TerrainNormalVaryings output = (TerrainNormalVaryings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag — 地表颜色直接来自区域与道路的统一数据图
    //
    //  阴影只作为一层「变暗」乘在最终色上：地表依然不参与任何光照计算，
    //  只是从阴影贴图里取一次遮挡率。没有阴影关键字时 MainLightShadow
    //  返回 1，退化成原来那条纯色路径。
    // ════════════════════════════════════════════════════════════
    half4 Frag(TerrainVaryings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half4 color = SAMPLE_TEXTURE2D(_ColorMap, sampler_ColorMap, input.uv) * _Tint;

        float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
        // 用 MainLightShadow 而不是 MainLightRealtimeShadow：后者不含阴影距离淡出，
        // 超出 shadow distance 后坐标越界，clamp 采样会读到贴图边缘 —— 远处随机变暗。
        half shadowAtten = MainLightShadow(shadowCoord, input.positionWS,
            half4(1, 1, 1, 1), half4(0, 0, 0, 0));
        half shade = lerp(half(1.0) - _ShadowStrength, half(1.0), shadowAtten);

        return half4(color.rgb * shade, color.a);
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_Depth — 深度 prepass，只写深度不写颜色
    // ════════════════════════════════════════════════════════════
    half4 Frag_Depth(TerrainVaryings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return 0;
    }

    // ════════════════════════════════════════════════════════════
    //  Frag_Normals — 法线 prepass，兼容 URP 八面体编码分支
    // ════════════════════════════════════════════════════════════
    half4 Frag_Normals(TerrainNormalVaryings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
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
            // 接收主光阴影的变体。故意不含 _MAIN_LIGHT_SHADOWS_SCREEN：
            // 我们是按世界坐标算阴影坐标的，屏幕空间阴影那条路径需要屏幕坐标，
            // 打开后地表会画错而不是简单失效。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            ENDHLSL
        }

        // ════════════════════════════════════════════════════════════
        //  深度 / 法线 prepass — 让地表进入 _CameraDepthTexture 与
        //  _CameraNormalsTexture，供 SSGI 等屏幕空间效果使用
        // ════════════════════════════════════════════════════════════
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag_Depth
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert_Normals
            #pragma fragment Frag_Normals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    Fallback Off
}
