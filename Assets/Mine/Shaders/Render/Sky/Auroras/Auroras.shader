Shader "Render/Auroras"
{
    Properties
    {
        _SkyColor ("Sky Color", Color) = (0,0,0,1)
        _SkyCurvature ("Sky Curvature", Float) = 0.1
        _AurorasTiling ("Auroras Tiling", Float) = 1
        _RayMarchStep ("Ray March Step", Float) = 64
        _AurorasTex ("Auroras Texture", 2D) = "white" {}
        _AurorasColor ("Auroras Color", Color) = (1,1,1,1)
        _AurorasAttention ("Auroras Attention", Float) = 0.5
        _AurorasIntensity ("Auroras Intensity", Float) = 2
        _AurorasSpeed ("Auroras Speed", Float) = 0.1
        _AurorasNoiseTex ("Auroras Noise Texture", 2D) = "white" {}
        _RayMarchDistance ("Ray March Distance", Float) = 2.5
        _WarpIntensity ("Warp Intensity", Float) = 0.1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float3 WorldPos : TEXCOORD1;
                float4 vertex : SV_POSITION;
            };

            float3 _SkyColor;
            float _SkyCurvature;
            float _AurorasTiling;
            float _RayMarchStep;
            float _RayMarchDistance;
            sampler2D _AurorasTex;  
            float4 _AurorasTex_ST;
            float4 _AurorasColor;
            float _AurorasAttention;
            float _AurorasIntensity;
            float _AurorasSpeed;
            sampler2D _AurorasNoiseTex;
            float4 _AurorasNoiseTex_ST;
            float _WarpIntensity;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.WorldPos = mul( v.vertex , unity_ObjectToWorld);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                //Light direction
                float3 color = 0;
                float3 RayOriginal = 0;
                float3 TotalDir = i.WorldPos - RayOriginal;
                float3 RayDir = normalize(TotalDir);
                //Sky RayMarching Simulation
                float3 SkyCurvatureFacture = rcp(RayDir.y + _SkyCurvature);
                float3 BasicRayPlane = RayDir * SkyCurvatureFacture * _AurorasTiling;
                float3 RayMarchBegin = RayOriginal + BasicRayPlane;
                //Step Size
                float StepSize = rcp(_RayMarchStep);
                float3 AvgColor = 0;
                // Ray Marching Loop
                for (int step = 0; step < _RayMarchStep; step++)
                {
                    float CurrentStep = StepSize * step;
                    CurrentStep = pow(CurrentStep, 2);
                    float CurrentDistance = CurrentStep * _RayMarchDistance;
                    float3 CurrentPos = RayMarchBegin + RayDir * CurrentDistance * SkyCurvatureFacture;
                    float2 uv = float2(CurrentPos.x, CurrentPos.z);

                    float2 WarpVector = tex2D(_AurorasNoiseTex, TRANSFORM_TEX((uv * 2 + _Time.y * _AurorasSpeed), _AurorasNoiseTex));
                    float CurrentAuroras = tex2D(_AurorasTex, TRANSFORM_TEX((uv + WarpVector * _WarpIntensity) , _AurorasTex)).r;

                    CurrentAuroras = CurrentAuroras * saturate(1 - pow(CurrentDistance , 1 - _AurorasAttention));
                    float3 CurrentColor = sin((_AurorasColor * 2 - 1) + step * 0.043) * 0.5 + 0.5;
                    AvgColor = (AvgColor + CurrentColor) * 0.5;
                    color += CurrentAuroras * AvgColor * StepSize;
                }
                color *= _AurorasIntensity;
                color += _SkyColor;
                return float4(color,1);
            }
            ENDCG
        }
    }
}
