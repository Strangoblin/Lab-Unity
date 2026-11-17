// ════════════════════════════════════════════════════════════
//  GrassWind — 间接绘制草。basecolor 直接采样地形颜色图，
//  不再做基础色/风色/地面色的多色混合；保留风场与交互偏移。
// ════════════════════════════════════════════════════════════
Shader "Render/GrassWind"
{
    Properties
    {
        [Header(Base Settings)]
        _TerrainColorMap ("Terrain Color Map", 2D) = "white" {}
        // 每块草按地形色图映射喂入；不入 Inspector，但必须声明才能稳定经 MaterialPropertyBlock 绑定。
        [HideInInspector] _TerrainColorMapTransform ("Terrain Color Map Transform", Vector) = (0,0,1,1)
        _Grass ("Grass Texture", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _AlphaClipThreshold ("Alpha Clip Threshold", Range(0,1)) = 0.5
        // 阴影 pass 在 _AlphaClipThreshold 上加这个偏移：叶片越细，阴影图里越容易抖，
        // 调高它可以让只有叶片较实的部分投射阴影，换来更稳、更块状的影子。
        _ShadowAlphaClipOffset ("Shadow Alpha Clip Offset", Range(0,0.5)) = 0
        // 投射体沿「指向光源」方向的反向推远的距离（米）。草的法线是地形法线
        // 而非叶片朝向，ApplyShadowBias 的法线偏移对竖直叶片无效，靠这个量
        // 压掉自阴影斑驳。实测 0.05 起才有可见变化，调高 → 自阴影变浅。
        _ShadowCasterOffset ("Shadow Caster Offset", Range(0,0.2)) = 0.05

        [Header(Wind Settings)]
        _WindDirection ("Wind Direction", Vector) = (1,0,0,0)
        _WindFrequency ("Wind Frequency", Float) = 0
        _WindStrength ("Wind Strength", Float) = 0
        _WindScale ("Wind Scale", Float) = 1

        [Header(Shared Wind Field)]
        _WindResponse ("Wind Response (seconds)", Range(0,0.5)) = 0.12

        [Header(Interaction Settings)]
        _InteractionDirection ("Interaction Direction", Vector) = (0,1,0,0)
        _InteractionBend ("Terrain Bend (m)", Range(0,2)) = 0.6
        _InteractionPress ("Terrain Press (m)", Range(0,1)) = 0.3
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Assets/Mine/Special/HLSL/NPRFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/AdditionalLightsFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/TBN.hlsl"
    #include "Assets/Mine/Scripts/FieldManager/Wind/WindField.hlsl"
    #include "Assets/Mine/Scripts/InteractionManager/Grass/GrassInteraction.hlsl"

    // 与 SSGI/ScreenSpaceTrace.hlsl 的 SSGI_THIN_SURFACE_FLAG 同值；
    // 草不 include 后处理头文件，只沿用这个约定。
    #define SSGI_THIN_SURFACE_FLAG (-1.0)

    struct GrassProperties
    {
        float3 normal;
        float height;
    };

    StructuredBuffer<float4x4> _MeshProperties;
    StructuredBuffer<GrassProperties> _GrassProperties;
    StructuredBuffer<uint> _ClipProperties;

    CBUFFER_START(UnityPerMaterial)
        float _AlphaClipThreshold;
        float _ShadowAlphaClipOffset;
        float _ShadowCasterOffset;
        float _Smoothness;

        float4 _WindDirection;
        float _WindFrequency;
        float _WindStrength;
        float _WindScale;

        float4 _InteractionDirection;
        float _InteractionBend;
        float _InteractionPress;

        // xy = 地表色图的世界 XZ 原点，zw = 世界尺寸；世界 XZ → UV 与地形网格一致。
        float4 _TerrainColorMapTransform;

        float _WindResponse;
    CBUFFER_END

    // Set by the legacy interaction camera; kept for logic compatibility.
    float3 _CameraPosition;
    float4x4 _CameraMatrix;

    Texture2D _TerrainColorMap;
    SamplerState sampler_TerrainColorMap;
    Texture2D _Grass;
    SamplerState sampler_Grass;
    Texture2D _WindNoiseTexture;
    SamplerState sampler_WindNoiseTexture;
    Texture2D _InteractionTexture;
    SamplerState sampler_InteractionTexture;

    struct appdata
    {
        float4 positionOS : POSITION;
        float3 normal : NORMAL;
        float2 uv : TEXCOORD0;

        uint instanceID : SV_InstanceID;
    };

    struct v2f
    {
        float4 positionHCS : SV_POSITION;
        float4 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        float2 uv : TEXCOORD2;
        // 用实例根部（而非位移后的顶点）算出的地表色图 UV，避免叶尖跨格串色。
        float2 groundUV : TEXCOORD3;
        // 位移后的顶点在叶片朝向上的高度（米），根部 0，随风与踩踏实时变化。
        float height : TEXCOORD4;
    };

    void WindEffect(float3 positionWS, out float3 windOffset)
    {
        float3 windDir = normalize(_WindDirection);
        float2 uv = positionWS.xz * _WindScale - windDir.xz * _Time * _WindFrequency;
        float  wind = _WindNoiseTexture.SampleLevel(sampler_WindNoiseTexture, uv, 0).r;
        windOffset = wind * _WindStrength * windDir;
    }

    void InteractionEffect(float3 positionWS, out float3 interactionOffset, out float2 uv)
    {
        float3 interactionDir = normalize(_InteractionDirection);
        uv = mul(_CameraMatrix, float4(positionWS - _CameraPosition, 1)).xz;
        float  interaction = _InteractionTexture.SampleLevel(sampler_InteractionTexture, uv, 0).r;
        interactionOffset = interaction * interactionDir;
    }

    // ════════════════════════════════════════════════════════════
    //  FieldWindOffsetWS — 共享风场速度（m/s）× 响应时间，根部固定。
    //  只应用风场给出的效果，不做幅度钳制；风场缺席时返回 0。
    // ════════════════════════════════════════════════════════════
    float3 FieldWindOffsetWS(float3 instancePositionWS, float canopyWeight, float heightScale)
    {
        float3 windVelocity = SampleWindVelocityWS(instancePositionWS);
        return windVelocity * max(_WindResponse, 0.0) *
            saturate(canopyWeight) * saturate(heightScale);
    }

    // ════════════════════════════════════════════════════════════
    //  顶点位移 — 主 pass 与阴影投射共用同一份计算。
    //  影子必须跟着草一起被风吹、被踩弯，否则会看到影子留在原地。
    // ════════════════════════════════════════════════════════════
    struct GrassVertex
    {
        float3 positionWS;
        float3 normalWS;
        float2 uv;
        float2 groundUV;
        float height;
    };

    GrassVertex GrassDisplace(appdata v)
    {
        GrassVertex o;

        uint clipIndex = _ClipProperties[v.instanceID];
        float4x4 meshProp = _MeshProperties[clipIndex];
        GrassProperties grassProp = _GrassProperties[clipIndex];

        float3 grassPos = mul(meshProp, v.positionOS).xyz;
        float3 instancePos = float3(meshProp._m03, meshProp._m13, meshProp._m23);
        float3 windOffset;
        float3 interactionOffset;
        float2 CameraUV;
        WindEffect(grassPos, windOffset);
        InteractionEffect(grassPos, interactionOffset, CameraUV);
        float3 grassOffset = (windOffset + interactionOffset) * v.uv.y;
        float3 grassNormal = grassProp.normal;

        float3 N = grassNormal;
        float3 T;
        float3 B;
        TBN(N, T, B);

        float3x3 TNB = float3x3(T, N, B);
        float4 positionOffsetNS = float4(mul(grassOffset, TNB), 0);

        // Field wind is a world-space velocity, so it bypasses the TBN mapping.
        float3 fieldWindOffset = FieldWindOffsetWS(instancePos, v.uv.y, grassProp.height);

        // 踩踏交互：压弯量的梯度给出「远离压点」的方向，叶尖位移最大。
        // 与风一样是世界空间偏移，不经过 TBN；没有草地子集时 _InteractionGrassValid = 0，偏移为 0。
        float3 terrainOffset = GrassBendOffsetWS(instancePos, v.uv.y, _InteractionBend, _InteractionPress);

        float3 displacedWS = grassPos + fieldWindOffset + terrainOffset + positionOffsetNS.xyz;
        o.positionWS = displacedWS;
        o.normalWS = grassNormal;
        o.uv = v.uv;
        o.groundUV = (instancePos.xz - _TerrainColorMapTransform.xy) / _TerrainColorMapTransform.zw;
        // 运动后的高度：位移后的顶点相对根部、沿叶片朝向的投影；草被吹弯或踩下时随之降低。
        o.height = dot(displacedWS - instancePos, grassNormal);

        return o;
    }

    v2f vert(appdata v)
    {
        GrassVertex g = GrassDisplace(v);
        v2f o;
        o.positionWS = float4(g.positionWS, 1);
        o.positionHCS = TransformWorldToHClip(o.positionWS);
        o.normalWS = g.normalWS;
        o.uv = g.uv;
        o.groundUV = g.groundUV;
        o.height = g.height;
        return o;
    }

    half4 frag(v2f i): SV_Target
    {
        // Base Color：地表色图直接作为基色，草纹理只做覆盖与裁剪。
        half4 grassTex = _Grass.Sample(sampler_Grass, i.uv);
        clip(grassTex.a - _AlphaClipThreshold);
        half4 groundColor = _TerrainColorMap.Sample(sampler_TerrainColorMap, i.groundUV);
        half4 basecolor = groundColor * (i.height + 1.0) * grassTex;

        // Lighting Functions
        float3 LightDirection;
        float3 LightColor;
        float DistanceAtten, ShadowAtten;

        MainLight(i.positionWS.xyz, LightDirection, LightColor, DistanceAtten, ShadowAtten);

        float3 N = normalize(i.normalWS.xyz);
        float3 L = normalize(LightDirection);
        float3 V = normalize(_WorldSpaceCameraPos - i.positionWS.xyz);

        float3 Diffuse = DiffuseLambert(N, L) * 0.5 + 0.5;
        float3 Specular = SpecularBlinnPhong(N, L, V, _Smoothness);
        float3 direct = (Diffuse + Specular) * LightColor;
        float3 ambient = SampleSH(N);
        float3 Light = direct + ambient;

        half4 finalColor = float4(lerp(0.6, 1, ShadowAtten) * Light, 1.0) * basecolor;
        return finalColor;
    }

    // ════════════════════════════════════════════════════════════
    //  FragDepth — 深度 prepass，裁剪轮廓与可见草叶保持一致
    // ════════════════════════════════════════════════════════════
    half4 FragDepth(v2f i) : SV_Target
    {
        clip(_Grass.Sample(sampler_Grass, i.uv).a - _AlphaClipThreshold);
        return 0;
    }

    // ════════════════════════════════════════════════════════════
    //  FragNormals — 法线 prepass，输出叶片贴合地形的法线
    //
    //  alpha 写 SSGI 的「薄壳」标记（见 SSGI/ScreenSpaceTrace.hlsl 的
    //  SSGI_THIN_SURFACE_FLAG）：这里输出的地形法线是**着色法线**，
    //  不是叶片的几何朝向，所以乐观点的命中面朝向判定对草没有意义。
    //  AO 与 DiffuseGI 的接收端法线语义不变，仍然用这条向上的法线。
    // ════════════════════════════════════════════════════════════
    half4 FragNormals(v2f i) : SV_Target
    {
        clip(_Grass.Sample(sampler_Grass, i.uv).a - _AlphaClipThreshold);
        float3 normalWS = NormalizeNormalPerPixel(i.normalWS);
        #if defined(_GBUFFER_NORMALS_OCT)
            float2 octNormal = PackNormalOctQuadEncode(normalWS);
            return half4(PackFloat2To888(saturate(octNormal * 0.5 + 0.5)), SSGI_THIN_SURFACE_FLAG);
        #else
            return half4(normalWS, SSGI_THIN_SURFACE_FLAG);
        #endif
    }

    // ════════════════════════════════════════════════════════════
    //  阴影投射 — 由 GrassRenderFeature 在 AfterRenderingShadows 直接画进
    //  URP 的主光阴影图（草不是场景 Renderer，进不了 shadow caster 收集）。
    //  视口与级联矩阵由 pass 在 CPU 侧按 URP 的切片设置；
    //  _LightDirection / _ShadowBias 来自 ShadowUtils.SetupShadowCasterConstantBuffer。
    // ════════════════════════════════════════════════════════════
    float3 _LightDirection;

    struct ShadowVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    ShadowVaryings ShadowVert(appdata v)
    {
        GrassVertex g = GrassDisplace(v);
        ShadowVaryings o;
        o.uv = g.uv;

        // 沿「远离光源」的方向把投射体推远一点点，抵消「草自己投到自己身上」。
        //
        // 符号容易搞反：_LightDirection = -light.forward，是**指向光源**的方向
        // （见 ShadowUtils.SetupShadowCasterConstantBuffer）。要减少自遮挡必须
        // 沿它取负，往远离光源的一侧挪 —— 遮挡深度变大，受影面才判为受光。
        // 实测正向偏移反而让 47% 的画面变暗，就是踩了这个坑。
        //
        // 为什么 ApplyShadowBias 的法线偏移救不了这个：
        // 草的法线是**地形法线**（朝上），不是叶片朝向。叶片基本竖直，
        // 沿 +Y 推等于原地不动，自阴影全留下。
        float3 casterPositionWS = g.positionWS - _LightDirection * _ShadowCasterOffset;

        float4 positionCS = TransformWorldToHClip(
            ApplyShadowBias(casterPositionWS, g.normalWS, _LightDirection));
        o.positionCS = ApplyShadowClamping(positionCS);
        return o;
    }

    half4 ShadowFrag(ShadowVaryings i) : SV_Target
    {
        clip(_Grass.Sample(sampler_Grass, i.uv).a
            - (_AlphaClipThreshold + _ShadowAlphaClipOffset));
        return 0;
    }

    ENDHLSL
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalRenderPipeline"
            "RenderType"="Opaque"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            Blend One Zero
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // 接收主光阴影：MainLight() 走 GetMainLight(shadowCoord)，没有这些变体
            // 就退化成 shadowAtten=1 —— 草会画进阴影图，却永远收不到影子。
            // 故意不含 _MAIN_LIGHT_SHADOWS_SCREEN：AdditionalLightsFunction 的
            // SHADOWS_SCREEN 分支是 Built-in 管线遗留、从不生效，开启屏幕空间
            // 阴影时草会退化成「不投影也不接收」，而不是画错。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            ENDHLSL
        }

        // ════════════════════════════════════════════════════════════
        //  深度 / 法线 prepass — 草由 GrassRenderFeature 直接下发，
        //  这两个 Pass 让草进入 _CameraDepthTexture / _CameraNormalsTexture
        // ════════════════════════════════════════════════════════════
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Off
            ZTest LEqual
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            Cull Off
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment FragNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }

        // ════════════════════════════════════════════════════════════
        //  阴影投射 — 草不在 CullResults 里，URP 的 shadow caster 阶段不会画它。
        //  GrassRenderFeature 在 AfterRenderingShadows 用这个 Pass 自己画进阴影图。
        // ════════════════════════════════════════════════════════════
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off
            ZTest LEqual
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
