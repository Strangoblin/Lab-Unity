// ═══════════════════════════════════════════════════════════════
//  SpatialChecks — isolated GPU harness for SSGISpatial_Resolve.
//  Self-built spatial input, linear eye depth and scene normals replace the
//  production SST helpers so the shared filter is exercised without camera
//  geometry. _BlitTexture is remapped onto _SpatialInput before the formal
//  SSGISpatialFilter.hlsl is included.
// ═══════════════════════════════════════════════════════════════
Shader "PostProcess/SpatialChecks"
{
    Properties { }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    TEXTURE2D_X(_SpatialInput);
    TEXTURE2D(_SpatialDepth);
    TEXTURE2D(_SpatialNormal);

    float4 _TestSize;
    float4 _TestGuide;
    float4 _TestFallback;
    int _SpatialFault;

    float SST_SampleDepth(float2 sampleUv)
    {
        return SAMPLE_TEXTURE2D_LOD(_SpatialDepth, sampler_PointClamp, sampleUv, 0).r;
    }

    bool SST_IsSurface(float rawDepth)
    {
        return rawDepth > 0.0;
    }

    float SST_EyeDepth(float rawDepth)
    {
        return rawDepth;
    }

    float3 SampleSceneNormals(float2 sampleUv)
    {
        return SAMPLE_TEXTURE2D_LOD(_SpatialNormal, sampler_PointClamp, sampleUv, 0).xyz;
    }

    #define _BlitTexture _SpatialInput
    #include "Assets/Mine/Shaders/PostProcess/SSGI/SSGISpatialFilter.hlsl"

    float4 Frag_Spatial(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 screenUv = input.texcoord;
        float rawDepth = SST_SampleDepth(screenUv);
        float3 centerNormal = SampleSceneNormals(screenUv);
        bool normalizeSampleNormals = _TestGuide.w > 0.5;
        bool centerSurface = SST_IsSurface(rawDepth);
        bool centerNormalValid = !normalizeSampleNormals || dot(centerNormal, centerNormal) >= 0.25;
        if (!centerSurface || !centerNormalValid)
            return float4(0.0, 0.0, 0.0, 0.0);
        float centerDepth = SST_EyeDepth(rawDepth);
        if (normalizeSampleNormals)
            centerNormal = normalize(centerNormal);
        float4 resolved = SSGISpatial_Resolve(screenUv, _TestSize.xy, _TestGuide.z, centerDepth,
            centerNormal, _TestGuide.x, _TestGuide.y, normalizeSampleNormals, _TestFallback);
        if (_SpatialFault != 0)
            resolved.r += 0.25;
        return resolved;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Spatial"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag_Spatial
            ENDHLSL
        }
    }
    Fallback Off
}
