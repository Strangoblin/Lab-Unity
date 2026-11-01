// ════════════════════════════════════════════════════════════════
//  SSGI Composite — combine AO, diffuse and specular exactly once.
// ════════════════════════════════════════════════════════════════
Shader "PostProcess/SSGI/Composite"
{
    Properties { }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
    #include "Assets/Mine/Shaders/PostProcess/SSGI/ScreenSpaceTrace.hlsl"

    TEXTURE2D_X(_SSGIAOTexture);
    TEXTURE2D_X(_SSGIDiffuseTexture);
    TEXTURE2D_X(_SSGISpecularTexture);

    CBUFFER_START(UnityPerMaterial)
        float4 _SSGIParams;
        float4 _SSGIModuleFlags;
        float _SSGIRoughness;
        float _SSGIDebugMode;
    CBUFFER_END

    // ════════════════════════════════════════════════════════════════
    //  Composite — preserve source alpha and blend reflection by Fresnel.
    // ════════════════════════════════════════════════════════════════
    float4 Frag_Composite(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float4 scene = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
        float rawDepth = SST_SampleDepth(uv);
        if (!SST_IsSurface(rawDepth))
            return scene;

        float visibility = 1.0;
        if (_SSGIModuleFlags.x > 0.5)
            visibility = saturate(SAMPLE_TEXTURE2D_X_LOD(_SSGIAOTexture, sampler_PointClamp, uv, 0).r);
        if (_SSGIDebugMode > 0.5 && _SSGIDebugMode < 1.5)
            return float4(visibility.xxx, 1.0);

        float3 diffuse = 0.0;
        if (_SSGIModuleFlags.y > 0.5)
            diffuse = max(SAMPLE_TEXTURE2D_X_LOD(_SSGIDiffuseTexture, sampler_PointClamp, uv, 0).rgb, 0.0);
        if (_SSGIDebugMode > 1.5 && _SSGIDebugMode < 2.5)
            return float4(diffuse, 1.0);

        float3 reflection = 0.0;
        if (_SSGIModuleFlags.z > 0.5)
            reflection = max(SAMPLE_TEXTURE2D_X_LOD(_SSGISpecularTexture, sampler_LinearClamp, uv, 0).rgb, 0.0);
        if (_SSGIDebugMode > 2.5)
            return float4(reflection, 1.0);

        float aoFactor = 1.0 - saturate((1.0 - visibility) * _SSGIParams.x);
        float3 color = scene.rgb * aoFactor;
        color += diffuse * _SSGIParams.y * aoFactor;

        if (_SSGIModuleFlags.z > 0.5)
        {
            float3 positionWS = SST_WorldPosition(uv, rawDepth);
            float3 normalWS = normalize(SampleSceneNormals(uv));
            float3 viewToCameraWS = GetWorldSpaceNormalizeViewDir(positionWS);
            float fresnel = 0.04 + 0.96 * pow(1.0 - saturate(dot(normalWS, viewToCameraWS)), 5.0);
            fresnel *= 1.0 - _SSGIRoughness * 0.5;
            color = lerp(color, reflection, saturate(_SSGIParams.z * fresnel));
        }

        return float4(color, scene.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Composite
            ENDHLSL
        }
    }
    Fallback Off
}
