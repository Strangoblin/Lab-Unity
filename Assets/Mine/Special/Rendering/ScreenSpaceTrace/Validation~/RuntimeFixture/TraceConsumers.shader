// ═══════════════════════════════════════════════════════════════
//  TraceConsumers — reflection-hit and shadow-occlusion fragment adapters.
// ═══════════════════════════════════════════════════════════════
Shader "RuntimeFixture/TraceConsumers"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Assets/Mine/Special/HLSL/RayStepFunction.hlsl"
        #include "Assets/Mine/Special/HLSL/ScreenSpaceTraceFunction.hlsl"
        #include "Assets/Mine/Special/Rendering/ScreenSpaceTrace/Validation~/TraceTestContext.hlsl"
        float4 _TestOriginRange;
        float4 _TestDirectionRange;
        int _TestBackend;

        ScreenTraceHit TraceConsumer_Query()
        {
            RayStepRay ray;
            ray.origin = _TestOriginRange.xyz;
            ray.direction = _TestDirectionRange.xyz;
            ray.distanceRange = float2(_TestOriginRange.w, _TestDirectionRange.w);
            ScreenTraceHit hit;
            if (_TestBackend == 0)
                ScreenTrace_TraceRay(ray, TraceTest_Context(), TraceTest_Config(), hit);
            else if (_TestBackend == 1)
                ScreenTrace_TraceDDA(ray, TraceTest_Context(), TraceTest_Config(), hit);
            else
                ScreenTrace_TraceHiZ(ray, TraceTest_Context(), TraceTest_Config(), hit);
            return hit;
        }

        float4 ReflectionFrag(Varyings input) : SV_Target
        {
            ScreenTraceHit hit = TraceConsumer_Query();
            return float4(hit.uv, hit.distance, hit.status == SCREEN_TRACE_HIT ? 1.0 : 0.0);
        }

        float4 OcclusionFrag(Varyings input) : SV_Target
        {
            ScreenTraceHit hit = TraceConsumer_Query();
            return float4(hit.status == SCREEN_TRACE_HIT ? 1.0 : 0.0, hit.distance, hit.status, 1.0);
        }
        ENDHLSL
        Pass
        {
            Name "ReflectionHit"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment ReflectionFrag
            ENDHLSL
        }
        Pass
        {
            Name "ShadowOcclusion"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment OcclusionFrag
            ENDHLSL
        }
    }
}
