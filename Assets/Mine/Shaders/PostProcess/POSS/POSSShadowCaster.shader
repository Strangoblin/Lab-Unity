// ════════════════════════════════════════════════════════════
//  POSS Shadow Caster — 光源空间深度写入
//  通过 cmd.SetViewProjectionMatrices 设置光源 VP，
//  TransformObjectToHClip 自动输出光源空间的深度。
//  由 POSSFeature CasterPass 作为 override material 使用。
// ════════════════════════════════════════════════════════════
Shader "PostProcess/POSSShadowCaster"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "POSSShadowCaster"
            Tags { "LightMode" = "POSSShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings  { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float Frag(Varyings input) : SV_Target
            {
                return input.positionCS.z / input.positionCS.w;
            }
            ENDHLSL
        }
    }
}
