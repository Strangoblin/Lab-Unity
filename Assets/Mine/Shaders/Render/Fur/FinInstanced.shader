Shader "Render/FinInstanced"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _FurMap("Fur Map", 2D) = "white" {}
        _FinWidth("Fin Width", Range(0.0, 0.1)) = 0.005
        _FinLength("Fin Length", Range(0.0, 1.0)) = 0.1
        _AlphaCutout("Alpha Cutout", Range(0.0, 1.0)) = 0.3
        _FurScale("Fur Scale", Range(0.0, 10.0)) = 1.0
        _Occlusion("Occlusion", Range(0.0, 1.0)) = 0.5
        _BendStrength("Bend Strength", Range(0.0, 5.0)) = 2.0
        _BaseMove("Base Move", Vector) = (0.0, -0.0, 0.0, 3.0)
        _Gravity("Gravity Field", Vector) = (0.0, -1.0, 0.0, 1.0)
        [HideInInspector] _LagOffset("Lag Offset", Vector) = (0, 0, 0, 0)
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
        Cull Off

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

            // ---- per-instance point data from CPU ----
            struct HairPoint
            {
                float3 positionOS;
                float3 normalOS;
                float2 uv;
                float  seed;
            };
            StructuredBuffer<HairPoint> _HairPoints;
            uint _BaseInstance;

            // ---- uniforms ----
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _FurMap_ST;
                float4 _BaseMove;
                float4 _Gravity;
                float _FinWidth;
                float _FinLength;
                float _AlphaCutout;
                float _FurScale;
                float _Occlusion;
                float _BendStrength;
                float3 _LagOffset;
            CBUFFER_END

            float4x4 _ObjectToWorld;
            float4x4 _WorldToObject;

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_FurMap);    SAMPLER(sampler_FurMap);

            // ---- hash helpers ----
            float Hash1(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float2 Hash2(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(float2(p.x * p.y, p.x + p.y));
            }

            // ---- vertex / fragment ----
            struct Attributes
            {
                float4 positionOS : POSITION;   // hair-Fin quad local (-0.5..0.5, 0..1)
                float2 uv : TEXCOORD0;          // Fin-only UV (0..1 across Fin)
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;          // passed through Fin UV
                float2 pointUV : TEXCOORD1;     // sampled mesh UV at this hair point
                float fogCoord : TEXCOORD2;
                float height : TEXCOORD3;        // 0 = root, 1 = tip
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                uint idx = input.instanceID + _BaseInstance;
                HairPoint pt = _HairPoints[idx];

                // ---- random per Fin ----
                float r0 = Hash1(pt.uv + pt.seed);
                float2 r12 = Hash2(pt.uv * 31.0 + pt.seed + 17.0);

                // ---- world transform ----
                float3 worldPos = mul(_ObjectToWorld, float4(pt.positionOS, 1.0)).xyz;
                float3 worldNormal = normalize(mul((float3x3)_ObjectToWorld, pt.normalOS));

                // ---- build local basis (random rotation around normal) ----
                float angle = r0 * 6.2831853;
                float ca = cos(angle), sa = sin(angle);

                float3 refDir = abs(worldNormal.y) < 0.999
                    ? float3(0.0, 1.0, 0.0)
                    : float3(1.0, 0.0, 0.0);
                float3 right0 = normalize(cross(refDir, worldNormal));
                float3 forward0 = cross(worldNormal, right0);
                float3 right = right0 * ca + forward0 * sa;

                // ---- Fin dimensions ----
                float FinLen = _FinLength * lerp(0.8, 1.2, r12.x);
                float FinWid = _FinWidth  * lerp(0.6, 1.4, r12.y);

                float h = input.positionOS.y;  // 0 = root, 1 = tip
                float w = input.positionOS.x;  // -0.5 .. 0.5

                // ---- bending (gravity + base move + inertia) ----
                float moveFactor = pow(abs(h), _BaseMove.w);
                float3 gravityDir = normalize(_Gravity.xyz);
                float3 bendDir = gravityDir * _Gravity.w
                               + _BaseMove.xyz
                               + _LagOffset * 10.0;   // inertia lag as bending
                float3 bend = bendDir * _BendStrength * h * h;  // parabolic bend

                // ---- final position ----
                float3 posWS = worldPos
                    + right * w * FinWid
                    + worldNormal * h * FinLen
                    + bend;

                float4 posCS = TransformWorldToHClip(posWS);

                output.vertex = posCS;
                output.uv = input.uv;
                output.pointUV = TRANSFORM_TEX(pt.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(posCS.z);
                output.height = h;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // ---- alpha from fur map (sampled at mesh UV) ----
                float4 furColor = SAMPLE_TEXTURE2D(_FurMap, sampler_FurMap,
                    input.pointUV * _FurScale);
                float alpha = furColor.r * (1.0 - input.height);
                alpha *= 1.0 - abs(input.uv.x - 0.5) * 2.0; // taper edges

                clip(alpha - _AlphaCutout);

                // ---- base color ----
                float4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.pointUV);
                float occlusion = lerp(1.0 - _Occlusion, 1.0, input.height);
                float3 color = baseColor.rgb * _BaseColor.rgb * occlusion;
                color = MixFog(color, input.fogCoord);

                return float4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
