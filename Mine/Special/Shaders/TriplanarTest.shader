Shader "Mine/TriplanarTest"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Sharpness ("Sharpness", Float) = 4.0
        _Scale ("Scale", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Mine/Special/HLSL/ProjectionFunction.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Sharpness;
                float _Scale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 weight = DirectionWeight(normalWS, _Sharpness);
                float2 uv = LowCostTriplanarProjection(normalWS, input.positionWS, _Sharpness, _Scale);
                float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
