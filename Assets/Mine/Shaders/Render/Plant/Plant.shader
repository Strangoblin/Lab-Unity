// ════════════════════════════════════════════════════════════
//  Plant — UV 驱动的逐面 billboard；网格要求与来源见 README.md
// ════════════════════════════════════════════════════════════
Shader "Render/Plant"
{
    Properties
    {
        [Header(Leaf)]
        [MainTexture] _MainTex ("Leaf Texture (RGBA)", 2D) = "white" {}
        [MainColor] _BaseColor ("Leaf Color", Color) = (0.23, 0.52, 0.12, 1)
        _Cutoff ("Alpha Cutoff", Range(0.01, 1)) = 0.5
        [Toggle] _ProceduralLeaf ("Procedural Leaf Mask", Float) = 1
        [Header(Billboard)]
        _BillboardSize ("Billboard Offset Radius", Range(0, 0.5)) = 0.16
        _Inflate ("Normal Inflation", Range(0, 0.5)) = 0
        [Header(Lighting)]
        _Wrap ("Diffuse Wrap", Range(0, 1)) = 0.35
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1
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
        float _Cutoff;
        float _ProceduralLeaf;
        float _BillboardSize;
        float _Inflate;
        float _Wrap;
        float _AmbientStrength;
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
        float3 positionOS : TEXCOORD4;
        half fogFactor : TEXCOORD3;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // ════════════════════════════════════════════════════════════
    //  PlantScale — 最小轴缩放保证面片等比，且位移不超过网格预留包围盒
    // ════════════════════════════════════════════════════════════
    float PlantScale()
    {
        float3 scale = float3(length(TransformObjectToWorldDir(float3(1, 0, 0), false)),
                              length(TransformObjectToWorldDir(float3(0, 1, 0), false)),
                              length(TransformObjectToWorldDir(float3(0, 0, 1), false)));
        return min(scale.x, min(scale.y, scale.z));
    }

    // ════════════════════════════════════════════════════════════
    //  PlantPositionWS — 教程的 UV 相机偏移叠加原顶点，保留球壳形状
    // ════════════════════════════════════════════════════════════
    float3 PlantPositionWS(Attributes input, float3 normalWS)
    {
        float2 corner = input.uv * 2.0 - 1.0;
        float3 cameraOffset = mul(float3(corner, 0), (float3x3)UNITY_MATRIX_V);
        cameraOffset *= rsqrt(max(dot(cameraOffset, cameraOffset), 1e-8));
        float scale = PlantScale();
        return TransformObjectToWorld(input.positionOS.xyz)
             + (cameraOffset * clamp(_BillboardSize, 0, 0.5)
             + normalWS * clamp(_Inflate, 0, 0.5)) * scale;
    }

    // ════════════════════════════════════════════════════════════
    //  SampleLeaf — 贴图 alpha 或默认叶形遮罩；所有颜色/深度 Pass 共用
    // ════════════════════════════════════════════════════════════
    half4 SampleLeaf(float2 uv)
    {
        half4 leaf = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex)) * _BaseColor;
        float2 p = uv * 2.0 - 1.0;
        float width = 0.78 * max(0, 1.0 - p.y * p.y);
        float mask = saturate((width - abs(p.x)) * 24.0);
        leaf.a *= lerp(1.0, mask, saturate(_ProceduralLeaf));
        clip(leaf.a - _Cutoff);
        return leaf;
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
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
        output.positionWS = PlantPositionWS(input, output.normalWS);
        output.positionCS = TransformWorldToHClip(output.positionWS);
        output.positionOS = TransformWorldToObject(output.positionWS);
        output.uv = input.uv;
        output.fogFactor = ComputeFogFactor(output.positionCS.z);
        return output;
    }

    // ════════════════════════════════════════════════════════════
    //  VertShadow — 原球壳近似投影，不使用阴影视图构造相机 billboard
    // ════════════════════════════════════════════════════════════
    Varyings VertShadow(Attributes input)
    {
        Varyings output = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
        output.positionWS = TransformObjectToWorld(input.positionOS.xyz)
                          + output.normalWS * clamp(_Inflate, 0, 0.5) * PlantScale();
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
    //  Frag — 保留球壳法线形成树冠光照，接受主光阴影与环境光
    // ════════════════════════════════════════════════════════════
    half4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half4 leaf = SampleLeaf(input.uv);
        half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
        normalWS = normalize(input.positionOS);
        #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
            float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
        #else
            float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
        #endif
        Light light = GetMainLight(shadowCoord);
        half diffuse = saturate((dot(normalWS, light.direction) + _Wrap) / (1.0 + _Wrap));
        half3 lighting = max(SampleSH(normalWS), 0) * _AmbientStrength
                       + light.color * diffuse * light.distanceAttenuation * light.shadowAttenuation;
        return half4(MixFog(leaf.rgb * lighting, input.fogFactor), 1);
    }

    // ════════════════════════════════════════════════════════════
    //  FragDepth — 与可见叶片一致的裁剪轮廓
    // ════════════════════════════════════════════════════════════
    half4 FragDepth(Varyings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        SampleLeaf(input.uv);
        return 0;
    }

    // ════════════════════════════════════════════════════════════
    //  FragNormals — 输出树冠法线，兼容 URP 八面体编码
    // ════════════════════════════════════════════════════════════
    half4 FragNormals(Varyings input) : SV_Target
    {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        SampleLeaf(input.uv);
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
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
    }
    Fallback Off
}
