// ════════════════════════════════════════════════════════════════
//  Interaction Capture Feature — layer-restricted depth / normal / colour.
// ════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace Mine.Rendering
{
    /// <summary>
    /// Render Graph replacement for the legacy <c>CustomRenderer</c>: captures a depth buffer,
    /// an optional normal buffer and a colour buffer restricted to configured layers.
    /// </summary>
    /// <remarks>
    /// URP 17 records render-graph passes through <see cref="ScriptableRenderer.OnRecordRenderGraph"/>,
    /// which is internal, so a custom <see cref="ScriptableRenderer"/> can no longer record passes.
    /// Attach this feature to a dedicated <see cref="UniversalRendererData"/> whose own layer masks are
    /// all empty: URP then draws no opaques, transparents or prepasses of its own, and this feature owns
    /// the camera target. Depth is published as <c>_CustomDepthTexture</c>, normals as
    /// <c>_CameraNormalTexture</c>, and the colour pass clears the camera target to transparent black
    /// before drawing its own layer set.
    /// </remarks>
    public sealed class InteractionCaptureFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public sealed class Settings
        {
            [Header("Outputs")]
            [Tooltip("Capture layer-restricted depth into _CustomDepthTexture.")]
            public bool copyDepth = true;

            [Tooltip("Capture layer-restricted geometry into the camera colour target.")]
            public bool copyColor = true;

            [Tooltip("Capture layer-restricted normals into _CameraNormalTexture.")]
            public bool copyNormal = false;

            [Header("Layers")]
            [Tooltip("Objects whose depth is captured. Layer 0 = nothing.")]
            public LayerMask depthLayerMask = -1;

            [Tooltip("Objects drawn into the colour target. Layer 0 = nothing.")]
            public LayerMask colorLayerMask = -1;

            [Tooltip("Objects whose normals are captured. Layer 0 = nothing.")]
            public LayerMask normalLayerMask = -1;

            [Header("Queue")]
            [Tooltip("Draw every queue and move the capture behind transparency instead of capturing opaque only.")]
            public bool transparentMode = false;
        }

        public Settings settings = new Settings();

        DepthCapturePass _depthPass;
        ColorCapturePass _colorPass;

        public override void Create()
        {
            _depthPass = new DepthCapturePass(settings);
            _colorPass = new ColorCapturePass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_depthPass == null || _colorPass == null)
                return;

            CameraData camera = renderingData.cameraData;
            // Overlay cameras share the base camera's target; clearing it here would erase the frame.
            if (camera.renderType == CameraRenderType.Overlay)
                return;

            if (!settings.copyDepth && !settings.copyColor && !settings.copyNormal)
                return;

            if (settings.copyDepth || settings.copyNormal)
            {
                _depthPass.renderPassEvent = settings.transparentMode
                    ? RenderPassEvent.AfterRenderingTransparents
                    : RenderPassEvent.BeforeRenderingPrePasses;
                renderer.EnqueuePass(_depthPass);
            }

            if (settings.copyColor)
            {
                // 非 transparentMode 时颜色捕获要排在透明深度合并之后：那一步才把水面
                // （透明队列）的深度并进 _CameraDepthTexture，捕获 Pass 要拿它当参照面。
                _colorPass.renderPassEvent = settings.transparentMode
                    ? RenderPassEvent.BeforeRenderingTransparents
                    : RenderPassEvent.AfterRenderingTransparents;
                renderer.EnqueuePass(_colorPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _depthPass = null;
            _colorPass = null;
        }

        // ════════════════════════════════════════════════════════════
        //  Shared helpers
        // ════════════════════════════════════════════════════════════

        static RendererListHandle CreateList(RenderGraph graph, UniversalRenderingData renderingData,
            UniversalCameraData cameraData, UniversalLightData lightData, List<ShaderTagId> tags,
            RenderQueueRange queue, LayerMask layerMask, SortingCriteria sorting)
        {
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(tags, renderingData,
                cameraData, lightData, sorting);
            drawSettings.perObjectData = PerObjectData.None;

            var filtering = new FilteringSettings(queue, layerMask);
            var parameters = new RendererListParams(renderingData.cullResults, drawSettings, filtering);
            return graph.CreateRendererList(parameters);
        }

        static TextureDesc CreateDepthDesc(UniversalCameraData cameraData, string name)
        {
            RenderTextureDescriptor cameraDesc = cameraData.cameraTargetDescriptor;
            return new TextureDesc(cameraDesc.width, cameraDesc.height)
            {
                format = GraphicsFormat.D32_SFloat_S8_UInt,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = true,
                clearColor = Color.black,
                msaaSamples = MSAASamples.None,
                name = name,
            };
        }

        // ════════════════════════════════════════════════════════════
        //  Depth / normal capture — the legacy depth and depth-normal prepasses.
        // ════════════════════════════════════════════════════════════
        sealed class DepthCapturePass : ScriptableRenderPass
        {
            internal static readonly int CustomDepthId = Shader.PropertyToID("_CustomDepthTexture");
            static readonly int CameraNormalId = Shader.PropertyToID("_CameraNormalTexture");

            static readonly List<ShaderTagId> DepthOnlyTags = new() { new ShaderTagId("DepthOnly") };
            static readonly List<ShaderTagId> DepthNormalTags = new()
            {
                new ShaderTagId("DepthNormals"), new ShaderTagId("DepthNormalsOnly")
            };

            readonly Settings _settings;

            sealed class PassData
            {
                public RendererListHandle list;
            }

            public DepthCapturePass(Settings settings)
            {
                _settings = settings;
                profilingSampler = new ProfilingSampler("Interaction Capture Depth");
                // Only private textures are written; the camera colour target is untouched.
                requiresIntermediateTexture = false;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                RenderQueueRange queue = _settings.transparentMode
                    ? RenderQueueRange.all
                    : RenderQueueRange.opaque;

                if (_settings.copyDepth)
                    RecordDepth(graph, renderingData, cameraData, lightData, queue);

                if (_settings.copyNormal)
                    RecordNormals(graph, renderingData, cameraData, lightData, queue);
            }

            void RecordDepth(RenderGraph graph, UniversalRenderingData renderingData,
                UniversalCameraData cameraData, UniversalLightData lightData, RenderQueueRange queue)
            {
                TextureHandle depth = graph.CreateTexture(CreateDepthDesc(cameraData, "_CustomDepthTexture"));
                RendererListHandle list = CreateList(graph, renderingData, cameraData, lightData, DepthOnlyTags,
                    queue, _settings.depthLayerMask, cameraData.defaultOpaqueSortFlags);

                using var builder = graph.AddRasterRenderPass<PassData>("Interaction Capture Depth",
                    out PassData passData, profilingSampler);
                passData.list = list;
                builder.UseRendererList(list);

                builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(depth, CustomDepthId);

                // The global is consumed outside this graph, so an empty or colour-only capture
                // must still produce a cleared texture.
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    context.cmd.DrawRendererList(data.list));
            }

            void RecordNormals(RenderGraph graph, UniversalRenderingData renderingData,
                UniversalCameraData cameraData, UniversalLightData lightData, RenderQueueRange queue)
            {
                RenderTextureDescriptor cameraDesc = cameraData.cameraTargetDescriptor;
                var desc = new TextureDesc(cameraDesc.width, cameraDesc.height)
                {
                    format = DepthNormalOnlyPass.GetGraphicsFormat(),
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = true,
                    clearColor = Color.black,
                    msaaSamples = MSAASamples.None,
                    name = "_CameraNormalTexture",
                };
                TextureHandle normals = graph.CreateTexture(desc);
                // Private depth: the normal layer set must not overwrite the published depth capture.
                TextureHandle depth = graph.CreateTexture(CreateDepthDesc(cameraData, "_CameraNormalDepth"));

                RendererListHandle list = CreateList(graph, renderingData, cameraData, lightData, DepthNormalTags,
                    queue, _settings.normalLayerMask, cameraData.defaultOpaqueSortFlags);

                using var builder = graph.AddRasterRenderPass<PassData>("Interaction Capture Normals",
                    out PassData passData, profilingSampler);
                passData.list = list;
                builder.UseRendererList(list);

                builder.SetRenderAttachment(normals, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(normals, CameraNormalId);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    context.cmd.DrawRendererList(data.list));
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Colour capture — clears the camera target, draws the colour layer set.
        // ════════════════════════════════════════════════════════════
        sealed class ColorCapturePass : ScriptableRenderPass
        {
            static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");

            // 每个物体只会被画一次，选哪个 Pass 由这张表决定——**表里靠后的 Tag 优先**。
            // 实测（Unity 6.3 / URP 17）：InteractionCapture 放在最前时，带两个 Pass 的互动物体
            // 永远被 UniversalForward 命中，捕获 RT 里存的是 Forward 的颜色（球是红色 → 恒 1.0），
            // 于是穿透深度退化成一个二值圆盘：上下移动水面没反应、左右移动只剩边缘满幅阶跃。
            // 把 InteractionCapture 放到最后，互动物体才会走捕获 Pass；
            // 没有该 Pass 的普通物体不受影响，仍会落到前面几个标准 Tag。
            static readonly List<ShaderTagId> ForwardTags = new()
            {
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("InteractionCapture"),
            };

            readonly Settings _settings;

            sealed class PassData
            {
                public RendererListHandle list;
            }

            public ColorCapturePass(Settings settings)
            {
                _settings = settings;
                profilingSampler = new ProfilingSampler("Interaction Capture Color");
                requiresIntermediateTexture = false;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                if (!resources.activeColorTexture.IsValid())
                    return;

                RenderQueueRange queue = _settings.transparentMode
                    ? RenderQueueRange.transparent
                    : RenderQueueRange.opaque;
                SortingCriteria sorting = _settings.transparentMode
                    ? SortingCriteria.CommonTransparent
                    : cameraData.defaultOpaqueSortFlags;

                RendererListHandle list = CreateList(graph, renderingData, cameraData, lightData, ForwardTags,
                    queue, _settings.colorLayerMask, sorting);

                using var builder = graph.AddRasterRenderPass<PassData>("Interaction Capture Color",
                    out PassData passData, profilingSampler);
                passData.list = list;
                builder.UseRendererList(list);

                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                // 参照面深度从全局 _CameraDepthTexture 读：TransparentDepthMergeFeature 会先把
                // 透明表面（水面）合并进去。copyDepth 关掉时这个全局就是相机自己的深度附件
                // （Renderer 用 CopyDepthMode.ForcePrepass），此时**不能**再把它挂成本 Pass 的
                // 深度附件——同一个资源不能既当附件又当全局纹理采样。
                // 深度附件原本只为交互物的 stencil 服务，本管线里没有 stencil 读者。
                if (_settings.copyDepth && resources.activeDepthTexture.IsValid())
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);

                builder.UseGlobalTexture(CameraDepthTextureId, AccessFlags.Read);

                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1f, 0);
                    context.cmd.DrawRendererList(data.list);
                });
            }
        }
    }
}
