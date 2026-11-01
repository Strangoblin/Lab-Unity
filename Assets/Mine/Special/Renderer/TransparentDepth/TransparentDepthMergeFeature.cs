// ════════════════════════════════════════════════════════════════
//  Transparent Depth Merge Feature — opaque depth copy + transparents.
// ════════════════════════════════════════════════════════════════
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Mine.Rendering
{
    /// <summary>
    /// Writes transparent geometry depth into URP's resolved camera depth copy,
    /// so depth-reading post-processes treat transparent surfaces as real surfaces.
    /// </summary>
    /// <remarks>
    /// The transparent objects are drawn a second time with only the depth attachment bound
    /// and ZWrite forced on: the hardware depth test merges them with the opaque copy,
    /// the camera depth attachment used by the colour pass is never touched.
    /// Runs after URP's CopyDepth pass, so every depth reader recorded afterwards
    /// (SSGI, MaskBlur, MaskRBR ...) sees the merged contents without extra texture plumbing.
    /// </remarks>
    public sealed class TransparentDepthMergeFeature : ScriptableRendererFeature
    {
        public enum DebugMode { Off, Depth }

        [System.Serializable]
        public sealed class Settings
        {
            [Header("Technical")]
            [Tooltip("Objects in the transparent queue that write depth. Layer 0 = nothing.")]
            public LayerMask layerMask = -1;

            [Tooltip("URP pass tags drawn for depth. SRPDefaultUnlit covers Unlit / passes without a LightMode tag. DepthOnly suits materials whose pass already writes depth.")]
            public string[] shaderTags = { "UniversalForward", "UniversalForwardOnly", "SRPDefaultUnlit" };

            [Tooltip("Must stay after the depth copy and before the first depth reader.")]
            public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingOpaques;

            [Header("Debug")]
            public DebugMode debug = DebugMode.Off;
            public Shader debugShader;
        }

        public Settings settings = new Settings();

        TransparentDepthPass _depthPass;
        DebugPass _debugPass;

        public override void Create()
        {
            _depthPass = new TransparentDepthPass(settings);
            _debugPass = new DebugPass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraData camera = renderingData.cameraData;
            if (_depthPass == null || camera.renderType == CameraRenderType.Overlay
                || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
                return;

            _depthPass.renderPassEvent = settings.passEvent;
            renderer.EnqueuePass(_depthPass);

            if (settings.debug != DebugMode.Off && settings.debugShader != null)
            {
                _debugPass.renderPassEvent = settings.passEvent;
                renderer.EnqueuePass(_debugPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _debugPass?.Dispose();
            _debugPass = null;
            _depthPass = null;
        }

        // ════════════════════════════════════════════════════════════
        //  Transparent depth — depth-only redraw merged into _CameraDepthTexture.
        // ════════════════════════════════════════════════════════════
        sealed class TransparentDepthPass : ScriptableRenderPass
        {
            static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
            static readonly RenderStateBlock DepthWriteBlock = new(RenderStateMask.Depth)
            {
                depthState = new DepthState(true, CompareFunction.LessEqual)
            };

            readonly Settings _settings;
            bool _warned;

            sealed class PassData
            {
                public RendererListHandle[] lists;
            }

            public TransparentDepthPass(Settings settings)
            {
                _settings = settings;
                profilingSampler = new ProfilingSampler("Transparent Depth Merge");
                // Depth-only pass: it never touches the colour target, so no intermediate texture is needed.
                requiresIntermediateTexture = false;
                // Makes URP schedule its depth copy ahead of this pass even in AfterTransparents copy mode.
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                if (!resources.cameraDepthTexture.IsValid()
                    || _settings.shaderTags == null || _settings.shaderTags.Length == 0)
                {
                    WarnOnce("No resolved camera depth copy; check the renderer depth mode and pass event.");
                    return;
                }

                TextureHandle depth = resources.cameraDepthTexture;
                var filtering = new FilteringSettings(RenderQueueRange.transparent, _settings.layerMask);

                using var builder = graph.AddRasterRenderPass<PassData>("Transparent Depth Merge",
                    out PassData passData, profilingSampler);
                passData.lists = CreateLists(graph, renderingData, cameraData, lightData, filtering);
                foreach (RendererListHandle list in passData.lists)
                    builder.UseRendererList(list);

                builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.SetGlobalTextureAfterPass(depth, CameraDepthTextureId);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    foreach (RendererListHandle list in data.lists)
                        context.cmd.DrawRendererList(list);
                });
            }

            RendererListHandle[] CreateLists(RenderGraph graph, UniversalRenderingData renderingData,
                UniversalCameraData cameraData, UniversalLightData lightData, FilteringSettings filtering)
            {
                string[] tags = _settings.shaderTags;
                var lists = new RendererListHandle[tags.Length];
                for (int i = 0; i < tags.Length; i++)
                {
                    if (string.IsNullOrEmpty(tags[i]))
                        continue;

                    var tag = new ShaderTagId(tags[i]);
                    DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(tag, renderingData,
                        cameraData, lightData, SortingCriteria.CommonTransparent);
                    drawSettings.perObjectData = PerObjectData.None;

                    var tagValues = new NativeArray<ShaderTagId>(new[] { ShaderTagId.none }, Allocator.Temp);
                    var stateBlocks = new NativeArray<RenderStateBlock>(new[] { DepthWriteBlock }, Allocator.Temp);
                    var parameters = new RendererListParams(renderingData.cullResults, drawSettings, filtering)
                    {
                        tagValues = tagValues,
                        stateBlocks = stateBlocks,
                        // Apply the single state block to every pass, not only to a pass named "none".
                        isPassTagName = false
                    };
                    lists[i] = graph.CreateRendererList(parameters);
                    tagValues.Dispose();
                    stateBlocks.Dispose();
                }
                return lists;
            }

            void WarnOnce(string message)
            {
                if (_warned)
                    return;
                _warned = true;
                Debug.LogWarning($"[TransparentDepthMerge] {message}");
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Debug — grayscale view of the merged depth after transparents.
        // ════════════════════════════════════════════════════════════
        sealed class DebugPass : ScriptableRenderPass
        {
            static readonly int FarPlaneId = Shader.PropertyToID("_DebugFarPlane");
            static readonly Vector4 BlitScaleBias = new(1f, 1f, 0f, 0f);

            readonly Settings _settings;
            Material _material;

            sealed class PassData
            {
                public Material material;
                public TextureHandle source;
            }

            public DebugPass(Settings settings)
            {
                _settings = settings;
                profilingSampler = new ProfilingSampler("Transparent Depth Debug");
                // Follow the merge pass event; the feature enqueues the merge first, so the
                // debug view always reads the merged copy at the same point in the frame.
                renderPassEvent = settings.passEvent;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (!resources.cameraDepthTexture.IsValid() || !resources.activeColorTexture.IsValid()
                    || _settings.debugShader == null)
                    return;

                _material ??= CoreUtils.CreateEngineMaterial(_settings.debugShader);
                _material.SetFloat(FarPlaneId, frameData.Get<UniversalCameraData>().camera.farClipPlane);

                using var builder = graph.AddRasterRenderPass<PassData>("Transparent Depth Debug",
                    out PassData passData, profilingSampler);
                passData.material = _material;
                passData.source = resources.cameraDepthTexture;
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, data.source, BlitScaleBias, data.material, 0));
            }

            public void Dispose()
            {
                CoreUtils.Destroy(_material);
                _material = null;
            }
        }
    }
}
