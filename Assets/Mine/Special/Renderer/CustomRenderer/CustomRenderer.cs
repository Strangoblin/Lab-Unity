using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// CustomRenderer — 正交相机专用最小渲染管线。
/// 源自 PreparationRenderer，使用 DrawObjectsPass 只渲染指定层物体。
/// </summary>
public class CustomRenderer : ScriptableRenderer
{
    DepthOnlyPass       DepthPrepass;
    DepthNormalOnlyPass DepthNormalPrepass;
    DrawObjectsPass     ColorPrepass;

    RTHandle DepthTexture;
    RTHandle NormalTexture;

    private CustomRendererData m_Data;

    public CustomRenderer(CustomRendererData data) : base(data)
    {
        m_Data = data;

        var queueRange = data.transparentMode
            ? RenderQueueRange.all
            : RenderQueueRange.opaque;

        var passEvent = data.transparentMode
            ? RenderPassEvent.AfterRenderingTransparents
            : RenderPassEvent.BeforeRenderingPrePasses;

        DepthPrepass = new DepthOnlyPass(
            passEvent,
            queueRange,
            data.depthLayerMask
        );

        DepthNormalPrepass = new DepthNormalOnlyPass(
            passEvent,
            queueRange,
            data.normalLayerMask
        );

        if (!data.transparentMode)
        {
            ColorPrepass = new DrawObjectsPass(
                "Render Opaques",
                true,
                RenderPassEvent.BeforeRenderingOpaques,
                RenderQueueRange.opaque,
                data.colorLayerMask,
                StencilState.defaultValue,
                0
            );
        }
        else
        {
            ColorPrepass = new DrawObjectsPass(
                "Render Transparents",
                false,
                RenderPassEvent.BeforeRenderingTransparents,
                RenderQueueRange.transparent,
                data.transparentLayerMask,
                StencilState.defaultValue,
                0
            );
        }
    }

#pragma warning disable CS0618, CS0672
    public override void Setup(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var data = m_Data;
        var desc = renderingData.cameraData.cameraTargetDescriptor;

        if (data.copyDepth)
        {
            desc.graphicsFormat     = GraphicsFormat.None;
            desc.depthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;
            desc.depthBufferBits    = 32;
            desc.msaaSamples        = 1;

            RenderingUtils.ReAllocateIfNeeded(
                ref DepthTexture, desc,
                FilterMode.Point, TextureWrapMode.Clamp,
                name: "_CustomDepthTexture");
            Shader.SetGlobalTexture("_CustomDepthTexture", DepthTexture);
            DepthPrepass.Setup(desc, DepthTexture);
            EnqueuePass(DepthPrepass);
        }

        if (data.copyNormal)
        {
            desc.graphicsFormat     = DepthNormalOnlyPass.GetGraphicsFormat();
            desc.depthStencilFormat = GraphicsFormat.None;
            desc.depthBufferBits    = 0;
            desc.msaaSamples        = 1;

            RenderingUtils.ReAllocateIfNeeded(
                ref NormalTexture, desc,
                FilterMode.Bilinear, TextureWrapMode.Clamp,
                name: "_CameraNormalTexture");
            DepthNormalPrepass.Setup(DepthTexture, NormalTexture);
            EnqueuePass(DepthNormalPrepass);
        }

        if (data.copyColor)
        {
            // 使用相机的 targetTexture（方形 RT）作为颜色输出，不创建额外 RT
            ConfigureCameraTarget(k_CameraTarget, DepthTexture ?? k_CameraTarget);
            EnqueuePass(ColorPrepass);
        }
    }
#pragma warning restore CS0618, CS0672
}
