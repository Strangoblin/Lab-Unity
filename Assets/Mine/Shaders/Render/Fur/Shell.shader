Shader "Render/Shell"
{
	Properties
	{
		[MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
		[MainTexture] _BaseMap("Base Map", 2D) = "white" {}
		_FurMap("Fur Map", 2D) = "white" {}
		[IntRange] _ShellAmount("Shell Amount", Range(1, 42)) = 16
		_ShellStep("Shell Step", Range(0.0, 0.01)) = 0.001
		_AlphaCutout("Alpha Cutout", Range(0.0, 1.0)) = 0.2
		_FurScale("Fur Scale", Range(0.0, 10.0)) = 1.0
		_Occlusion("Occlusion", Range(0.0, 1.0)) = 0.5
		_BaseMove("Base Move", Vector) = (0.0, -0.0, 0.0, 3.0)
		_Gravity("Gravity Field", Vector) = (0.0, -1.0, 0.0, 1.0)
		_FaceViewProdThresh("Direction Threshold", Range(0.0, 1.0)) = 0.0
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
			#pragma target 4.0
			#pragma vertex vert
			#pragma geometry geom
			#pragma fragment frag
			#pragma multi_compile_fog
			#pragma exclude_renderers gles gles3 glcore

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
				float4 _BaseMap_ST;
				float4 _FurMap_ST;
				float4 _BaseMove;
				float4 _Gravity;
				float _ShellAmount;
				float _ShellStep;
				float _AlphaCutout;
				float _FurScale;
				float _Occlusion;
				float _FaceViewProdThresh;
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
			};

			struct Varyings
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
				float fogCoord : TEXCOORD1;
				float layer : TEXCOORD2;
			};

			Attributes vert(Attributes input)
			{
				return input;
			}

			float3 GetViewDirectionOS(float3 positionOS)
			{
				float3 positionWS = TransformObjectToWorld(positionOS);
				float3 viewDirWS = GetCameraPositionWS() - positionWS;
				return TransformWorldToObjectDir(viewDirWS, true);
			}

			void AppendShellVertex(inout TriangleStream<Varyings> stream, Attributes input, int index)
			{
				Varyings output = (Varyings)0;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

				float moveFactor = pow(abs((float)index / max(_ShellAmount, 1.0)), _BaseMove.w);
				float3 posOS = input.positionOS.xyz;
				float3 gravityDir = normalize(_Gravity.xyz);
				float gravityStrength = _Gravity.w;
				float3 gravityMove = gravityDir * gravityStrength * moveFactor;
				float3 move = moveFactor * _BaseMove.xyz;

				float3 shellDir = normalize(normalInput.normalWS + move + gravityMove);
				float3 posWS = vertexInput.positionWS + shellDir * (_ShellStep * index);
				float4 posCS = TransformWorldToHClip(posWS);

				if (index > 0)
				{
					float3 viewDirOS = GetViewDirectionOS(posOS);
					float eyeDotN = dot(viewDirOS, input.normalOS);
					if (abs(eyeDotN) < _FaceViewProdThresh)
					{
						return;
					}
				}

				output.vertex = posCS;
				output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
				output.fogCoord = ComputeFogFactor(posCS.z);
				output.layer = (float)index / max(_ShellAmount, 1.0);

				stream.Append(output);
			}

			[maxvertexcount(128)]
			void geom(triangle Attributes input[3], inout TriangleStream<Varyings> stream)
			{
				[loop]
				for (float i = 0.0; i < _ShellAmount; ++i)
				{
					[unroll]
					for (float j = 0.0; j < 3.0; ++j)
					{
						AppendShellVertex(stream, input[(int)j], (int)i);
					}

					stream.RestartStrip();
				}
			}

			half4 frag(Varyings input) : SV_Target
			{
				float4 furColor = SAMPLE_TEXTURE2D(_FurMap, sampler_FurMap, input.uv * _FurScale);
				float alpha = furColor.r * (1.0 - input.layer);

				if (input.layer > 0.0 && alpha < _AlphaCutout)
				{
					discard;
				}

				float4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
				float occlusion = lerp(1.0 - _Occlusion, 1.0, input.layer);
				float3 color = baseColor.rgb * _BaseColor.rgb * occlusion;
				color = MixFog(color, input.fogCoord);

				return float4(color, alpha * _BaseColor.a);
			}
			ENDHLSL
		}
	}

	FallBack Off
}
