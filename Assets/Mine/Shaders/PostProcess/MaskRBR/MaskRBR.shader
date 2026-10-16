// ════════════════════════════════════════════════════════════════
//  Mask RBR — main-light colored rays from a radial mask and angular phase.
// ════════════════════════════════════════════════════════════════
Shader "PostProcess/MaskRBR"
{
    Properties
    {
        [HideInInspector] _MaskRBRSourceMode ("Source Mode", Float) = 0
        [HideInInspector] _MaskRBRRadialParameters ("Length, Falloff, Jitter", Vector) = (0.2, 2, 0, 0)
        [HideInInspector] _MaskRBRPhaseParameters ("Anisotropy, Outer Cos, Inner Cos, Main Light", Vector) = (0.7, 0.5, 0.6691306, 0)
        [HideInInspector] _MaskRBROutputSize ("Output Size", Vector) = (1920, 1080, 0, 0)
        [HideInInspector] _MaskRBRIntensity ("Intensity", Float) = 0.25
        [HideInInspector] _MaskRBRDebugMode ("Debug Mode", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"

    TEXTURE2D_X(_MaskRBRObjectMaskTexture);
    TEXTURE2D_X(_MaskRBRMaskTexture);
    TEXTURE2D_X(_MaskRBRRayTexture);

    CBUFFER_START(UnityPerMaterial)
        float _MaskRBRSourceMode;
        float4 _MaskRBRRadialParameters;
        float4 _MaskRBRPhaseParameters;
        float4 _MaskRBROutputSize;
        float _MaskRBRIntensity;
        float _MaskRBRDebugMode;
    CBUFFER_END

    #include "Assets/Mine/Special/HLSL/HashFunction.hlsl"
    #include "Assets/Mine/Special/HLSL/MaskFunction.hlsl"

    #if defined(_MASKRBR_QUALITY_LOW)
        #define MASKRBR_SAMPLE_COUNT 16
    #elif defined(_MASKRBR_QUALITY_HIGH)
        #define MASKRBR_SAMPLE_COUNT 48
    #else
        #define MASKRBR_SAMPLE_COUNT 24
    #endif

    // ════════════════════════════════════════════════════════════
    //  Sky mask — identify the camera depth clear value on each graphics API.
    // ════════════════════════════════════════════════════════════
    float MaskRBRSkyMask(float2 uv)
    {
        float rawDepth = SampleSceneDepth(uv);
        return Mask_BackgroundRawDepth(rawDepth, 0.0000001);
    }

    // ════════════════════════════════════════════════════════════
    //  Angular phase — peak-normalized HG within a smooth main-light cone.
    // ════════════════════════════════════════════════════════════
    float MaskRBRAngularResponse(float2 uv)
    {
        if (_MaskRBRPhaseParameters.w < 0.5 || !IsPerspectiveProjection())
            return 0.0;

        float2 viewportUV = DYNAMIC_SCALING_REMOVE_SCALEBIAS(uv);
        float3 pointWS = ComputeWorldSpacePosition(viewportUV, 0.5, UNITY_MATRIX_I_VP);
        float3 viewDirection = -SafeNormalize(GetWorldSpaceViewDir(pointWS));
        float3 lightDirection = SafeNormalize(_MainLightPosition.xyz);
        float cosine = clamp(dot(viewDirection, lightDirection), -1.0, 1.0);
        float g = clamp(_MaskRBRPhaseParameters.x, 0.0, 0.95);
        float phase = HenyeyGreensteinPhaseFunction(g, cosine)
            / HenyeyGreensteinPhaseFunction(g, 1.0);
        float cone = smoothstep(_MaskRBRPhaseParameters.y, _MaskRBRPhaseParameters.z, cosine);
        return saturate(phase) * cone;
    }

    // ════════════════════════════════════════════════════════════
    //  Focus — project a world light direction with the current-eye convention.
    // ════════════════════════════════════════════════════════════
    float3 MaskRBRGetFocus()
    {
        if (_MaskRBRPhaseParameters.w < 0.5 || !IsPerspectiveProjection())
            return float3(0.0, 0.0, 0.0);

        float3 lightDirection = _MainLightPosition.xyz;
        float directionLengthSquared = dot(lightDirection, lightDirection);
        if (directionLengthSquared <= 0.00000001)
            return float3(0.0, 0.0, 0.0);

        lightDirection *= rsqrt(directionLengthSquared);
        float4 focusCS = mul(UNITY_MATRIX_VP, float4(lightDirection, 0.0));
        if (focusCS.w <= 0.0)
            return float3(0.0, 0.0, 0.0);

        float4 focusScreen = ComputeScreenPos(focusCS);
        float2 focusUV = focusScreen.xy / max(focusCS.w, 0.000001);
        return float3(focusUV, saturate(focusCS.w / 0.1));
    }

    // ════════════════════════════════════════════════════════════
    //  Radial span — limit travel in final-output-height space without overshoot.
    // ════════════════════════════════════════════════════════════
    float2 MaskRBRGetSpan(float2 uv, float2 focusUV)
    {
        float aspect = max(_MaskRBROutputSize.x, 1.0) / max(_MaskRBROutputSize.y, 1.0);
        float2 towardFocus = focusUV - uv;
        float distanceToFocus = length(towardFocus * float2(aspect, 1.0));
        float spanFraction = saturate(saturate(_MaskRBRRadialParameters.x) / max(distanceToFocus, 0.000001));
        return towardFocus * spanFraction;
    }

    // ════════════════════════════════════════════════════════════
    //  Radial sample — keep out-of-viewport samples black instead of clamping.
    // ════════════════════════════════════════════════════════════
    float MaskRBRSampleMask(float2 uv)
    {
        if (any(uv < 0.0) || any(uv > 1.0))
            return 0.0;
        return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).r;
    }

    // ════════════════════════════════════════════════════════════
    //  Radial kernel — fixed-budget mask taps with normalized scalar coverage.
    // ════════════════════════════════════════════════════════════
    float MaskRBRRadial(float2 uv, float2 focusUV)
    {
        float2 spanUV = MaskRBRGetSpan(uv, focusUV);
        uint2 pixel = (uint2)floor(uv * max(_MaskRBROutputSize.xy, float2(1.0, 1.0)));
        float falloff = clamp(_MaskRBRRadialParameters.y, 0.0, 8.0);
        float jitterStrength = saturate(_MaskRBRRadialParameters.z);
        float accumulated = 0.0;
        float accumulatedWeight = 0.0;

        [unroll]
        for (int sampleIndex = 0; sampleIndex < MASKRBR_SAMPLE_COUNT; ++sampleIndex)
        {
            float t = (float)sampleIndex / (float)(MASKRBR_SAMPLE_COUNT - 1);
            float sampleT = t;
            if (sampleIndex > 0)
            {
                float jitter = Hash_UInt2SeedStream01(pixel, 0u, (uint)sampleIndex) - 0.5;
                sampleT = saturate(t + jitter * jitterStrength / (float)(MASKRBR_SAMPLE_COUNT - 1));
            }
            float weight = exp2(-falloff * t);
            accumulated += MaskRBRSampleMask(uv + spanUV * sampleT) * weight;
            accumulatedWeight += weight;
        }
        return accumulated / max(accumulatedWeight, 0.00001);
    }

    // ════════════════════════════════════════════════════════════
    //  Mask — union selected visible geometry with optional depth-based sky.
    // ════════════════════════════════════════════════════════════
    float4 Frag_Mask(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float objectMask = SAMPLE_TEXTURE2D_X(_MaskRBRObjectMaskTexture, sampler_PointClamp, input.texcoord).r;
        float skyMask = MaskRBRSkyMask(input.texcoord);
        float mask = skyMask;
        if (_MaskRBRSourceMode > 1.5)
            mask = Mask_Union(skyMask, objectMask);
        else if (_MaskRBRSourceMode > 0.5)
            mask = objectMask;
        return float4(saturate(mask), 0.0, 0.0, 1.0);
    }

    // ════════════════════════════════════════════════════════════
    //  Radial — filter scalar source coverage toward the available main light.
    // ════════════════════════════════════════════════════════════
    float4 Frag_Radial(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float3 focus = MaskRBRGetFocus();
        if (focus.z <= 0.0)
            return float4(0.0, 0.0, 0.0, 0.0);
        float rays = MaskRBRSampleMask(input.texcoord);
        if (_MaskRBRRadialParameters.x > 0.0)
            rays = MaskRBRRadial(input.texcoord, focus.xy);
        rays *= focus.z;
        return float4(rays, 0.0, 0.0, 1.0);
    }

    // ════════════════════════════════════════════════════════════
    //  Composite — expose actual intermediates or add rays with camera alpha.
    // ════════════════════════════════════════════════════════════
    float4 Frag_Composite(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
        if (_MaskRBRDebugMode > 0.5 && _MaskRBRDebugMode < 1.5)
        {
            float mask = SAMPLE_TEXTURE2D_X(_MaskRBRMaskTexture, sampler_PointClamp, input.texcoord).r;
            return float4(mask, mask, mask, scene.a);
        }
        float rays = SAMPLE_TEXTURE2D_X(_MaskRBRRayTexture, sampler_LinearClamp, input.texcoord).r;
        rays *= MaskRBRAngularResponse(input.texcoord);
        if (_MaskRBRDebugMode > 1.5)
            return float4(rays, rays, rays, scene.a);
        return float4(scene.rgb + _MainLightColor.rgb * rays * saturate(_MaskRBRIntensity), scene.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            Name "Mask"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Mask
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "Radial"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Radial
            #pragma multi_compile_instancing
            #pragma multi_compile_local_fragment _ _MASKRBR_QUALITY_LOW _MASKRBR_QUALITY_HIGH
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag_Composite
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
