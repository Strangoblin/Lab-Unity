Shader "Render/XR1"
{
    Properties
    {
        [IntRange]_StencilID ("Stencil ID", Range(0, 255)) = 0
        _TopColor ("Top Color", Color) = (0.4, 0.7, 1.0, 1.0)     // 浅蓝色
        _BottomColor ("Bottom Color", Color) = (0.0, 0.1, 0.4, 1.0)   // 深蓝色
    }
    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry"
        }

        // 这个Pass用于设置模板缓冲区
        Pass
        {
            Stencil
            {
                Ref [_StencilID]
                Comp Equal
            }
        }
        
        // 新增的Pass用于渲染渐变效果
        Pass
        {
            Name "Gradient Pass"
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 positionWS : TEXCOORD1;
            };
            
            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _BottomColor;
            CBUFFER_END
            
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = mul(unity_ObjectToWorld, IN.positionOS);
                OUT.uv = IN.uv;
                return OUT;
            }
            
            half4 frag(Varyings IN) : SV_Target
            {
                // 使用对象空间的Y坐标进行渐变混合，可以根据实际效果调整
                float gradientFactor = normalize(IN.positionWS).y * 0.5 + 0.5; // 将值映射到0-1范围
                
                // 混合颜色
                half4 finalColor = lerp(_BottomColor, _TopColor, gradientFactor);
                return finalColor;
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}