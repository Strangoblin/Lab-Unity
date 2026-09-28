// ═══════════════════════════════════════════════════════════════
//  SpecularGI — SSSR screen trace with cubemap fallback.
// ═══════════════════════════════════════════════════════════════
Shader "PostProcess/SpecularGI"
{
    Properties
    {
        _SkyCubemap ("Sky Cubemap", Cube) = "black" {}
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

    TEXTURECUBE(_SkyCubemap);
    SAMPLER(sampler_SkyCubemap);
    TEXTURE2D_X(_SpecularGITexture);

    CBUFFER_START(UnityPerMaterial)
        float4 _TraceParams;
        float _Roughness;
        float _Intensity;
        float _SkyMaxMip;
        float _SpatialRadius;
        float _FrameIndex;
        float _DebugMode;
    CBUFFER_END

    #include "Assets/Mine/Shaders/PostProcess/SSGI/ScreenSpaceTrace.hlsl"
    #include "Assets/Mine/Shaders/PostProcess/SSGI/SpecularGI/SpecularGISampling.hlsl"
    #include "Assets/Mine/Shaders/PostProcess/SSGI/SpecularGI/SpecularGITrace.hlsl"
    #include "Assets/Mine/Shaders/PostProcess/SSGI/SpecularGI/SpecularGIFilter.hlsl"

    // ════════════════════════════════════════════════════════════
    //  Trace — screen hit first, cubemap on misses and low-confidence edges
    // ════════════════════════════════════════════════════════════
    half4 Frag_Trace(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float rawDepth = SST_SampleDepth(uv);
        if (!SST_IsSurface(rawDepth))
            return 0.0;

        float3 positionWS = SST_WorldPosition(uv, rawDepth);
        float3 normalWS = normalize(SampleSceneNormals(uv));
        float3 viewDirectionWS = -GetWorldSpaceNormalizeViewDir(positionWS);
        if (dot(normalWS, -viewDirectionWS) <= 0.0)
            return 0.0;

        float3 rayDirectionWS = SpecularGI_CreateRayDirection(uv, normalWS, viewDirectionWS);
        if (dot(normalWS, rayDirectionWS) <= 0.0001)
            rayDirectionWS = normalize(reflect(viewDirectionWS, normalWS));

        float3 originWS = positionWS + normalWS * _TraceParams.z;
        SpecularTraceResult screenResult = SpecularGI_TraceScreen(
            originWS,
            rayDirectionWS,
            _TraceParams.x,
            _TraceParams.y,
            (int)_TraceParams.w);

        float3 screenRadiance = screenResult.valid
            ? SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, screenResult.uv, 0).rgb
            : 0.0;
        float screenConfidence = screenResult.valid ? screenResult.confidence : 0.0;

        float3 skyRadiance = SpecularGI_SampleSky(rayDirectionWS);
        float3 radiance = lerp(skyRadiance, screenRadiance, screenConfidence);
        return float4(radiance, screenConfidence);
    }

    // ════════════════════════════════════════════════════════════
    //  Resolve stages — spatial geometry weights then temporal history
    // ════════════════════════════════════════════════════════════
    half4 Frag_Spatial(Varyings input) : SV_Target
    {
        return SpecularGI_SpatialResolve(input.texcoord);
    }

    half4 Frag_Upsample(Varyings input) : SV_Target
    {
        return SpecularGI_Upsample(input.texcoord);
    }

    // ════════════════════════════════════════════════════════════
    //  Composite and debug — Fresnel blend or source contribution view
    // ════════════════════════════════════════════════════════════
    half4 Frag_Composite(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
        float rawDepth = SST_SampleDepth(uv);
        if (!SST_IsSurface(rawDepth) || _Intensity <= 0.0)
            return sceneColor;

        float3 positionWS = SST_WorldPosition(uv, rawDepth);
        float3 normalWS = normalize(SampleSceneNormals(uv));
        float3 viewToCameraWS = GetWorldSpaceNormalizeViewDir(positionWS);
        float fresnel = 0.04 + 0.96 * pow(1.0 - saturate(dot(normalWS, viewToCameraWS)), 5.0);
        fresnel *= 1.0 - _Roughness * 0.5;

        float3 reflection = SAMPLE_TEXTURE2D_X_LOD(
            _SpecularGITexture,
            sampler_LinearClamp,
            uv,
            0).rgb;
        sceneColor.rgb = lerp(sceneColor.rgb, reflection, saturate(_Intensity * fresnel));
        return sceneColor;
    }

    half4 Frag_Debug(Varyings input) : SV_Target
    {
        float4 value = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, 0);
        if (_DebugMode < 0.5)
            return value;
        if (_DebugMode < 1.5)
            return float4(saturate(value.a).xxx, 1.0);
        if (_DebugMode < 2.5)
            return float4(saturate(1.0 - value.a).xxx, 1.0);
        return float4(value.aaa, 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Trace"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Trace
            ENDHLSL
        }

        Pass
        {
            Name "SpatialResolve"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Spatial
            ENDHLSL
        }

        Pass
        {
            Name "Upsample"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Upsample
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Composite
            ENDHLSL
        }

        Pass
        {
            Name "Debug"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Debug
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
