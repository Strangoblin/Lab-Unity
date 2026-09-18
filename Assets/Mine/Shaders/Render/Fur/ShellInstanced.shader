Shader "Render/ShellInstanced"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _FurMap("Fur Map", 2D) = "white" {}
        [IntRange] _LayerCount("Layer Count", Range(1, 42)) = 16
        _LayerStep("Layer Step", Range(0.0, 1.0)) = 0.001
        _AlphaCutout("Alpha Cutout", Range(0.0, 1.0)) = 0.2
        _FurScale("Fur Scale", Range(0.0, 10.0)) = 1.0
        _Occlusion("Occlusion", Range(0.0, 1.0)) = 0.5
        _BaseMove("Base Move", Vector) = (0.0, -0.0, 0.0, 3.0)
        _Gravity("Gravity Field", Vector) = (0.0, -1.0, 0.0, 1.0)
        _FaceViewProdThresh("Direction Threshold", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _LagOffset("Lag Offset", Vector) = (0, 0, 0, 0)
        [Header(Marschner Hair)]
        _SpecularColor("Specular Color", Color) = (1, 1, 1, 1)
        _SpecularStrength("Specular Strength", Range(0.0, 2.0)) = 0.5
        _SpecularShift("Specular Shift", Range(-0.5, 0.5)) = 0.0
        _SecondaryColor("Secondary Color", Color) = (0.8, 0.5, 0.3, 1)
        _SecondaryStrength("Secondary Strength", Range(0.0, 2.0)) = 0.3
        _SecondaryShift("Secondary Shift", Range(-0.5, 0.5)) = 0.2
        _Roughness("Roughness", Range(0.0, 1.0)) = 0.3
        _RoughnessSecondary("Roughness Secondary", Range(0.0, 1.0)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        LOD 100

        ZWrite On
        Cull Back

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma exclude_renderers gles gles3 glcore

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Mine/Special/HLSL/PBRFunction.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _FurMap_ST;
                float4 _BaseMove;
                float4 _Gravity;
                float _LayerCount;
                float _LayerStep;
                float _AlphaCutout;
                float _FurScale;
                float _Occlusion;
                float _FaceViewProdThresh;
                float3 _LagOffset;
                float4 _SpecularColor;
                float4 _SecondaryColor;
                float _SpecularStrength;
                float _SecondaryStrength;
                float _SpecularShift;
                float _SecondaryShift;
                float _Roughness;
                float _RoughnessSecondary;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_FurMap);
            SAMPLER(sampler_FurMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogCoord : TEXCOORD1;
                float layer : TEXCOORD2;
                float3 worldNormal : TEXCOORD3;
                float3 worldPos : TEXCOORD4;
            };

            float3 GetViewDirectionOS(float3 positionOS)
            {
                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 viewDirWS = GetCameraPositionWS() - positionWS;
                return TransformWorldToObjectDir(viewDirWS, true);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float instanceIndex = (float)input.instanceID;
                float safeAmount = max(_LayerCount, 1.0);
                float moveFactor = pow(abs(instanceIndex / safeAmount), _BaseMove.w);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                float3 posOS = input.positionOS.xyz;
                float3 gravityDir = normalize(_Gravity.xyz);
                float gravityStrength = _Gravity.w;
                float3 gravityMove = gravityDir * gravityStrength * moveFactor;
                float3 move = moveFactor * _BaseMove.xyz;

                // inertia: layer-based lag offset (outer shells trail more)
                float3 lagOffset = _LagOffset * (instanceIndex / safeAmount);

                float3 shellDir = normalize(normalInput.normalWS + move + gravityMove);
                float3 posWS = vertexInput.positionWS + shellDir * (_LayerStep * instanceIndex) + lagOffset;
                float4 posCS = TransformWorldToHClip(posWS);

                output.vertex = posCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(posCS.z);
                output.layer = instanceIndex / safeAmount;
                output.worldNormal = normalInput.normalWS;
                output.worldPos = posWS;

                return output;
            }

            // ---- Marschner via PBRFunction.hlsl ----
            half4 frag(Varyings input) : SV_Target
            {
                float4 furColor = SAMPLE_TEXTURE2D(_FurMap, sampler_FurMap, input.uv * _FurScale);
                float alpha = furColor.r * (1.0 - input.layer);

                if (input.layer > 0.0 && alpha < _AlphaCutout)
                {
                    discard;
                }

                float4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float3 albedo = baseColor.rgb * _BaseColor.rgb;
                float occlusion = lerp(1.0 - _Occlusion, 1.0, input.layer);

                // ---- Marschner hair (via PBRFunction.hlsl) ----
                float3 N = normalize(input.worldNormal);
                float3 V = normalize(GetCameraPositionWS() - input.worldPos);

                Light mainLight = GetMainLight();
                float3 L = mainLight.direction;
                float3 H = normalize(L + V);

                // Shell fur: fiber tangent = surface normal (T = N)
                real TdotH = dot((real3)N, (real3)H);
                real NdotH = TdotH;          // T = N
                real TdotN = 1.0;             // T = N → aligned

                real3 spec = BRDF_Hair(
                    TdotH, NdotH, TdotN,
                    _SpecularShift, _Roughness,       _SpecularColor.rgb, _SpecularStrength,
                    _SecondaryShift, _RoughnessSecondary, _SecondaryColor.rgb, _SecondaryStrength);

                real NdotL = saturate(dot((real3)N, (real3)L));
                real3 diffuse = (real3)albedo * NdotL * occlusion;

                float3 color = (float3)(diffuse + spec);
                color = MixFog(color, input.fogCoord);

                return float4(color, alpha * _BaseColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
