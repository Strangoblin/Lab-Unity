// ════════════════════════════════════════════════════════════════
//  Custom Feature — configurable layer-restricted capture baseline.
//
//  A working starting point for project-specific pipelines: capture
//  depth, normals and/or colour for configured layers into published
//  globals or the camera target. Every layer mask, pass tag, output
//  name and pass event is a setting, so a specialised pipeline can
//  configure this feature or copy it as a template.
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
    /// Configurable custom pipeline: layer-restricted depth, normal and colour capture.
    /// </summary>
    /// <remarks>
    /// Depth is published as <see cref="Settings.depthTextureName"/> (default
    /// <c>_CustomDepthTexture</c>) and normals as <see cref="Settings.normalTextureName"/>.
    /// The colour pass clears the camera target to transparent black and then draws the
    /// configured colour layers, so the feature owns the target when attached to a
    /// <see cref="UniversalRendererData"/> whose own layer masks are empty.
    /// </remarks>
    public sealed class CustomFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public sealed class Settings
        {
            [Header("Outputs")]
            [Tooltip("Capture layer-restricted depth into depthTextureName.")]
            public bool copyDepth = true;

            [Tooltip("Draw layer-restricted colour into the camera target.")]
            public bool copyColor = true;

            [Tooltip("Capture layer-restricted normals into normalTextureName.")]
            public bool copyNormal = false;

            [Header("Output textures")]
            [Tooltip("Global texture name used for the captured depth.")]
            public string depthTextureName = "_CustomDepthTexture";

            [Tooltip("Global texture name used for the captured normals.")]
            public string normalTextureName = "_CameraNormalTexture";

            [Header("Layers")]
            [Tooltip("Objects whose depth is captured. Layer 0 = nothing.")]
            public LayerMask depthLayerMask = -1;

            [Tooltip("Objects drawn into the colour target. Layer 0 = nothing.")]
            public LayerMask colorLayerMask = -1;

            [Tooltip("Objects whose normals are captured. Layer 0 = nothing.")]
            public LayerMask normalLayerMask = -1;

            [Header("Pass tags")]
            [Tooltip("Pass tags used for the depth capture. Empty = skip depth.")]
            public string[] depthPassTags = { "DepthOnly" };

            [Tooltip("Pass tags used for the normal capture. Empty = skip normals.")]
            public string[] normalPassTags = { "DepthNormals", "DepthNormalsOnly" };

            [Tooltip("Pass tags used for the colour capture. Later tags take priority per object.")]
            public string[] colorPassTags =
            {
                "SRPDefaultUnlit",
                "UniversalForward",
                "UniversalForwardOnly"
            };

            [Header("Queue")]
            [Tooltip("Draw every queue and move the capture behind transparency instead of capturing opaque only.")]
            public bool transparentMode = false;
        }

        public Settings settings = new Settings();

        CapturePass _capturePass;
        ColorCapturePass _colorPass;

        public override void Create()
        {
            _capturePass = new CapturePass(settings);
            _colorPass = new ColorCapturePass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_capturePass == null || _colorPass == null)
                return;

            CameraData camera = renderingData.cameraData;
            // Overlay cameras share the base camera's target; clearing it here would erase the frame.
            if (camera.renderType == CameraRenderType.Overlay)
                return;

            if (!settings.copyDepth && !settings.copyColor && !settings.copyNormal)
                return;

            if (settings.copyDepth || settings.copyNormal)
            {
                _capturePass.renderPassEvent = settings.transparentMode
                    ? RenderPassEvent.AfterRenderingTransparents
                    : RenderPassEvent.BeforeRenderingPrePasses;
                renderer.EnqueuePass(_capturePass);
            }

            if (settings.copyColor)
            {
                // After transparents the depth global already includes transparent surfaces
                // merged by a depth-merge feature; the colour capture can use it as the
                // reference surface instead of sampling the camera attachment.
                _colorPass.renderPassEvent = settings.transparentMode
                    ? RenderPassEvent.BeforeRenderingTransparents
                    : RenderPassEvent.AfterRenderingTransparents;
                renderer.EnqueuePass(_colorPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _capturePass = null;
            _colorPass = null;
        }

        // ════════════════════════════════════════════════════════════
        //  Shared helpers
        // ════════════════════════════════════════════════════════════

        static List<ShaderTagId> ToTags(string[] names)
        {
            var tags = new List<ShaderTagId>(names?.Length ?? 0);
            if (names == null)
                return tags;

            foreach (string name in names)
            {
                if (!string.IsNullOrEmpty(name))
                    tags.Add(new ShaderTagId(name));
            }
            return tags;
        }

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

        static int TextureId(string name, string fallback)
        {
            return Shader.PropertyToID(string.IsNullOrEmpty(name) ? fallback : name);
        }

        // ════════════════════════════════════════════════════════════
        //  Depth / normal capture — the legacy prepasses, layer-restricted.
        // ════════════════════════════════════════════════════════════
        sealed class CapturePass : ScriptableRenderPass
        {
            readonly Settings _settings;

            sealed class PassData
            {
                public RendererListHandle list;
            }

            public CapturePass(Settings settings)
            {
                _settings = settings;
                profilingSampler = new ProfilingSampler("Custom Capture Depth");
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
                List<ShaderTagId> tags = ToTags(_settings.depthPassTags);
                if (tags.Count == 0)
                    return;

                string textureName = string.IsNullOrEmpty(_settings.depthTextureName)
                    ? "_CustomDepthTexture"
                    : _settings.depthTextureName;
                int depthId = TextureId(textureName, "_CustomDepthTexture");

                TextureHandle depth = graph.CreateTexture(CreateDepthDesc(cameraData, textureName));
                RendererListHandle list = CreateList(graph, renderingData, cameraData, lightData, tags,
                    queue, _settings.depthLayerMask, cameraData.defaultOpaqueSortFlags);

                using var builder = graph.AddRasterRenderPass<PassData>("Custom Capture Depth",
                    out PassData passData, profilingSampler);
                passData.list = list;
                builder.UseRendererList(list);

                builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(depth, depthId);

                // The global is consumed outside this graph, so an empty capture must still
                // produce a cleared texture.
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    context.cmd.DrawRendererList(data.list));
            }

            void RecordNormals(RenderGraph graph, UniversalRenderingData renderingData,
                UniversalCameraData cameraData, UniversalLightData lightData, RenderQueueRange queue)
            {
                List<ShaderTagId> tags = ToTags(_settings.normalPassTags);
                if (tags.Count == 0)
                    return;

                string textureName = string.IsNullOrEmpty(_settings.normalTextureName)
                    ? "_CameraNormalTexture"
                    : _settings.normalTextureName;
                int normalId = TextureId(textureName, "_CameraNormalTexture");

                RenderTextureDescriptor cameraDesc = cameraData.cameraTargetDescriptor;
                var desc = new TextureDesc(cameraDesc.width, cameraDesc.height)
                {
                    format = DepthNormalOnlyPass.GetGraphicsFormat(),
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = true,
                    clearColor = Color.black,
                    msaaSamples = MSAASamples.None,
                    name = textureName,
                };
                TextureHandle normals = graph.CreateTexture(desc);
                // Private depth: the normal layer set must not overwrite the published depth capture.
                TextureHandle depth = graph.CreateTexture(CreateDepthDesc(cameraData, textureName + "Depth"));

                RendererListHandle list = CreateList(graph, renderingData, cameraData, lightData, tags,
                    queue, _settings.normalLayerMask, cameraData.defaultOpaqueSortFlags);

                using var builder = graph.AddRasterRenderPass<PassData>("Custom Capture Normals",
                    out PassData passData, profilingSampler);
                passData.list = list;
                builder.UseRendererList(list);

                builder.SetRenderAttachment(normals, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(normals, normalId);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    context.cmd.DrawRendererList(data.list));
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Colour capture — clears the camera target, draws the colour layers.
        // ════════════════════════════════════════════════════════════
        sealed class ColorCapturePass : ScriptableRenderPass
        {
            static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");

            readonly Settings _settings;

            sealed class PassData
            {
                public RendererListHandle list;
            }

            public ColorCapturePass(Settings settings)
            {
                _settings = settings;
                profilingSampler = new ProfilingSampler("Custom Capture Color");
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

                List<ShaderTagId> tags = ToTags(_settings.colorPassTags);
                if (tags.Count == 0)
                    return;

                RenderQueueRange queue = _settings.transparentMode
                    ? RenderQueueRange.transparent
                    : RenderQueueRange.opaque;
                SortingCriteria sorting = _settings.transparentMode
                    ? SortingCriteria.CommonTransparent
                    : cameraData.defaultOpaqueSortFlags;

                RendererListHandle list = CreateList(graph, renderingData, cameraData, lightData, tags,
                    queue, _settings.colorLayerMask, sorting);

                using var builder = graph.AddRasterRenderPass<PassData>("Custom Capture Color",
                    out PassData passData, profilingSampler);
                passData.list = list;
                builder.UseRendererList(list);

                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);

                // When a depth capture runs, the colour pass may bind the camera depth as a
                // reference attachment. The same resource cannot be both an attachment and a
                // global texture, so the reference depth is read from _CameraDepthTexture.
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
