Shader "InteractionManager/InteractorObject"
{
    // ═══════════════════════════════════════════════════════════════
    //  互动物体 Shader — 两个 Pass，两个相机各取一个：
    //
    //  Pass 0 "InteractionCapture" (LightMode = InteractionCapture)
    //      CustomFeature 的颜色捕获用它：采样 _CustomDepthTexture，
    //      输出穿透深度到交互 RT。只有挂了该 Feature 的正交相机会命中这个 Tag。
    //
    //  Pass 1 "Forward" (LightMode = UniversalForward)
    //      主相机走标准 URP 前向，渲染物体本该有的外观。
    //
    //  同一个物体、同一个 Layer 就够了：主相机看不到 Pass 0，正交相机看不到 Pass 1。
    // ═══════════════════════════════════════════════════════════════

    Properties
    {
        // 物体互动强度。它缩放捕获到的穿透深度（1 = 1 m），所以是「物体推水有多重」的总音量，
        // 与水体参数（WaterInteractionProcessor._clarity）完全解耦：换物体 = 换这个材质值。
        // 实测有效区间约 0.05~0.45：捕获值 saturate 到 0..1、水面输出钳位 ±1，
        // 再往上先下压饱和、后整体过冲（1 = 削顶，2 = 失真）。默认取 0.4 ≈ 未饱和的最大值。
        // 注意它同时是地面压痕的饱和点：press = d · _Intensity · _ContactGain（子集里恒为 5），
        // 所以「压满」对应的真实穿透深度 = 1 / (_Intensity · 5)，会随这个值一起变。
        _Intensity ("Interaction Intensity", Range(0.05, 0.45)) = 0.4

        // 虚拟下沉深度，归一化到「刚好压满」（0 = 不陷，1 = 接触点恰好输出 1.0）。
        // 真实互动里网格不会互相插入，深度差恒 ≤ 0，所以贴地的物体需要这个偏置才能写入。
        // 用归一化而不是米：压满深度 = 1 / (_Intensity · _ContactGain)，两个常量分别住在
        // 材质和子集 C# 里，写死成米会随 _Intensity 一起漂；归一化后 1 永远等于压满。
        // 当前 _Intensity = 0.4 时 1.0 ≈ 0.5 m；想留出形状（不削平）建议 ≤ 0.5。
        _ContactDepth ("Contact Depth", Range(0, 1)) = 0

        _BaseColor ("Base Color", Color) = (1, 0.45, 0.15, 1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    // 三个地面子集（草/雪/沙）的 kContactGain 都是 5，见各 *InteractionProcessor.cs。
    // 改那边要同步这里，否则 _ContactDepth = 1 不再等于「压满」。
    #define CONTACT_GAIN 5.0

    float _Intensity;
    float _ContactDepth;
    half4 _BaseColor;

    struct Attributes
    {
        float3 positionOS : POSITION;
        float2 uv         : TEXCOORD0;
    };
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalRenderPipeline"
        }
        LOD 100

        // ════════════════════════════════════════════════════════════
        //  Pass 0 — 交互捕获（只被交互相机的颜色捕获命中）
        //  Cull Front 渲染背面对比场景深度，输出按压深度。
        // ════════════════════════════════════════════════════════════

        Pass
        {
            Name "InteractionCapture"
            Tags { "LightMode" = "InteractionCapture" }

            Cull Front
            ZWrite Off
            ZTest Always
            Blend One Zero

            Stencil
            {
                Ref 2
                Comp Always
                Pass Replace
                WriteMask 2
            }

            HLSLPROGRAM
            // 参照面深度来自交互相机自己的 _CameraDepthTexture：
            // 由 TransparentDepthMergeFeature 把水面（透明队列）按 DepthOnly 再画一遍并进去。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #pragma vertex CaptureVert
            #pragma fragment CaptureFrag

            struct CaptureVaryings
            {
                float4 positionHCS : SV_POSITION;
                float4 positionHS  : TEXCOORD0;
                float  viewDepth   : TEXCOORD1;   // 相机视空间的 z（米），和场景深度用同一个空间比较
            };

            CaptureVaryings CaptureVert(Attributes i)
            {
                CaptureVaryings o;
                float3 positionWS = TransformObjectToWorld(i.positionOS);
                float3 positionVS = TransformWorldToView(positionWS);
                o.positionHCS = TransformObjectToHClip(i.positionOS);
                o.positionHS  = ComputeScreenPos(o.positionHCS);
                o.viewDepth   = positionVS.z;
                return o;
            }

            half4 CaptureFrag(CaptureVaryings i) : SV_Target
            {
                float2 screenUV = i.positionHS.xy / i.positionHS.w;

                // 采样参照面深度（交互相机的 _CameraDepthTexture，已被透明队列合并过）
                float sceneRaw = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, screenUV).r;

                // 两边都换算到「相机视空间距离（米）」再相减。
                // 以前是拿 NDC 的 z 直接减原始深度纹理值：reversed-Z 的 0..1 和 NDC 的 -1..1 差一个常数，
                // 乘上 depthScale=20 后变成 10 m 的假穿透，整张盘被 saturate 成 0/1 的二值图 ——
                // 上下移动量全被吃掉（水面没反应），左右移动只剩边缘 0↔1 的满幅阶跃（水面过冲）。
                //
                // 不能用 LinearEyeDepth：它是按透视的 1/(z·d+w) 反解的，
                // 这台正交相机 near = -10 时会解到远裁面上（实测恒 10 m）。正交投影是线性的，直接按视锥算。
            #if UNITY_REVERSED_Z
                float sceneEye = _ProjectionParams.y + (1.0 - sceneRaw) * (_ProjectionParams.z - _ProjectionParams.y);
            #else
                float sceneEye = _ProjectionParams.y + sceneRaw * (_ProjectionParams.z - _ProjectionParams.y);
            #endif
                float fragEye  = -i.viewDepth;                              // 相机 → 物体背面

                // 虚拟下沉：把背面沿视轴再压下去 _ContactDepth·压满深度 米。
                // 真实互动里网格不会互相插入（贴地时差值恒 ≤ 0，永远写不进场），这个偏置等价于
                // 「物体按 b 米沉进地面」，而且因为取的是背面，压痕恰好是物体在 h = b 处的真实截面：
                // 圆球得到 √(2Rb−b²) 的圆盘、值从中心线性衰减到边缘 0，软边是免费的。
                // 偏置放在这里（逐像素 fragEye）而不是在 compute 里给整张图加常数 ——
                // 后者会把物体轮廓整片抬成同一个值，截面形状和华软边缘都没了，退化成平底硬边的圆盘。
                float fullPressMeters = 1.0 / max(_Intensity * CONTACT_GAIN, 1e-4);  // 压满所需穿透深度（米）
                float sunkEye = fragEye + _ContactDepth * fullPressMeters;

                float penetration = max(0.0, sunkEye - sceneEye) * _Intensity;  // 米，向下为正

                // RT 约定 0..1：material 的 _Intensity = 1 时 1 = 1 m；子集侧再乘 _ContactGain = 5，
                // 所以「压满」= 真实穿透（或虚拟下沉）0.5 m（_Intensity = 0.4 时）。
                // 当前 Demo 的球半径 0.70、球心 y = 0.30 → 背面沉到 −0.40 m → RT ≈ 0.159 → press ≈ 0.79。
                return saturate(penetration);
            }
            ENDHLSL
        }

        // ════════════════════════════════════════════════════════════
        //  Pass 1 — 可见外观（主相机的标准 URP 前向使用）
        // ════════════════════════════════════════════════════════════

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex ForwardVert
            #pragma fragment ForwardFrag

            struct ForwardVaryings
            {
                float4 positionHCS : SV_POSITION;
            };

            ForwardVaryings ForwardVert(Attributes i)
            {
                ForwardVaryings o;
                o.positionHCS = TransformObjectToHClip(i.positionOS);
                return o;
            }

            half4 ForwardFrag(ForwardVaryings i) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}
