Shader "Render/SimpleParallax"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [Enum(Single, 0, Steep, 1, Relief, 2)] _ParallaxMode("Parallax Mode", Float) = 0
        _HeightMap("Height Map", 2D) = "white" {}
        _HeightScale("Height Scale", Range(-1, 1)) = 0
        [Toggle] _UseSilhouetteClip("Silhouette Clip", Float) = 0
        _ClipUVMin("Clip UV Min", Vector) = (0, 0, 0, 0)
        _ClipUVMax("Clip UV Max", Vector) = (1, 1, 0, 0)
        [Toggle] _UseCurvedSilhouette("Curved Silhouette", Float) = 0
        _HorizonClipStrength("Horizon Clip Strength", Range(0, 4)) = 1
        _HorizonFalloffPower("Horizon Falloff Power", Range(0.5, 8)) = 2
        [Toggle] _UseSelfShadow("Self Shadow", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Back
        ZWrite On
        AlphaToMask On

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Mine/Special/HLSL/ParallaxFunction.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _HeightMap_ST;
                float _ParallaxMode;
                float _HeightScale;
                float _UseSilhouetteClip;
                float4 _ClipUVMin;
                float4 _ClipUVMax;
                float _UseCurvedSilhouette;
                float _HorizonClipStrength;
                float _HorizonFalloffPower;
                float _UseSelfShadow;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float tangentSign : TEXCOORD4;
                float3 positionOS : TEXCOORD5;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = normalInputs.tangentWS;
                output.tangentSign = input.tangentOS.w * GetOddNegativeScale();
                output.uv = input.uv;
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 baseUV = TRANSFORM_TEX(input.uv, _BaseMap);
                float2 heightUV = TRANSFORM_TEX(input.uv, _HeightMap);

                float3 normalWS = normalize(input.normalWS);
                float3 tangentWS = normalize(input.tangentWS);
                float3 bitangentWS = normalize(cross(normalWS, tangentWS) * input.tangentSign);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 viewDirTS = float3(
                    dot(viewDirWS, tangentWS),
                    dot(viewDirWS, bitangentWS),
                    dot(viewDirWS, normalWS)
                );

                const int parallaxLayerCount = 10;
                const int parallaxRefineCount = 5;
                float parallaxHeightScale = _HeightScale * 0.1;

                float2 parallaxOffset = (_ParallaxMode < 0.5)
                    ? GetParallaxOffsetSingle(heightUV, viewDirTS, parallaxHeightScale)
                    : (_ParallaxMode < 1.5
                        ? GetParallaxOffsetSteep(heightUV, viewDirTS, parallaxHeightScale, parallaxLayerCount)
                        : GetParallaxOffsetRelief(heightUV, viewDirTS, parallaxHeightScale, parallaxLayerCount, parallaxRefineCount));
                
                float2 parallaxUV = baseUV + parallaxOffset;

                if (_UseSilhouetteClip > 0.5)
                {
                    clip(UVClipValue(parallaxUV, _ClipUVMin.xy, _ClipUVMax.xy));
                }
                
                float curvature = length(ddx(normalWS)) + length(ddy(normalWS));
                float curvatureFactor = saturate(curvature * 5.0);
                if (_UseCurvedSilhouette > 0.5 && curvatureFactor > 0.1)
                {
                    float curvedClip = CSClipValue(parallaxUV, input.normalWS, viewDirWS, input.positionOS, _HorizonFalloffPower, _HorizonClipStrength);
                    clip(curvedClip);
                }
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, parallaxUV) * _BaseColor;

                if (_UseSelfShadow > 0.5)
                {
                    Light mainLight = GetMainLight();
                    float3 lightDirWS = normalize(mainLight.direction);
                    float3 lightDirTS = float3(
                        dot(lightDirWS, tangentWS),
                        dot(lightDirWS, bitangentWS),
                        dot(lightDirWS, normalWS)
                    );
                    float shadow = GetParallaxShadow(parallaxUV, lightDirTS, parallaxHeightScale, parallaxLayerCount);
                    color.rgb *= shadow;
                }

                // clip(color.a - _Cutoff);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}