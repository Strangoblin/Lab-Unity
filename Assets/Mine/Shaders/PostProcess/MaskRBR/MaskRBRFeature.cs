// ════════════════════════════════════════════════════════════════
//  Mask RBR Feature — camera-colored rays from visible source masks.
// ════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Mine.Rendering;

/// <summary>
/// Produces radial rays from camera pixels covered by sky and selected scene objects.
/// </summary>
/// <remarks>
/// Object coverage uses solid geometry in both queues, with no material alpha
/// clipping or custom vertex displacement. Source RGB comes from the camera.
/// </remarks>
public sealed class MaskRBRFeature : ScriptableRendererFeature
{
    public enum SourceMode { Sky = 0, Objects = 1, SkyAndObjects = 2 }
    public enum QualityMode { Low = 0, Medium = 1, High = 2 }
    public enum DebugMode { Off = 0, Mask = 1, Rays = 3 }

    [System.Serializable]
    public sealed class Settings
    {
        [Header("Technical")]
        public Shader shader;
        public Shader maskShader;
        [Tooltip("Sky: visible background. Objects: objects selected by Layer Mask. SkyAndObjects: union of both.")]
        public SourceMode source = SourceMode.Sky;

        [Header("Performance")]
        public QualityMode quality = QualityMode.Medium;

        [Header("Artistic")]
        [Tooltip("Maximum angle in degrees between the viewing ray and main light; the outer 20% fades to zero.")]
        [Range(1f, 90f)] public float halfAngle = 60f;
        [Tooltip("Peak-normalized HG scattering. Higher values concentrate rays toward the main light.")]
        [Range(0f, 0.95f)] public float anisotropy = 0.7f;
        [Tooltip("Maximum radial sampling span as a fraction of final output height.")]
        [Range(0f, 1f)] public float length = 0.2f;
        [Range(0f, 8f)] public float falloff = 2f;
        [Tooltip("Stable spatial jitter; zero uses fixed sampling positions.")]
        [Range(0f, 1f)] public float jitterStrength = 0f;
        [Range(0f, 1f)] public float intensity = 0.25f;

        [Header("Occlusion")]
        [Tooltip("Subtract SSC cloud coverage from the source mask, so cloudy sky produces no rays. Requires SSC's ray march to run before this pass; it degrades to no occlusion otherwise.")]
        public bool occludeWithClouds = true;

        // Event groups the effect's frame scope: which visible objects feed the
        // mask (space) and when the pass runs (time), read as one pair.
        [Header("Event")]
        [Tooltip("Visible opaque and transparent objects on these layers can produce rays. Ignored when Source is Sky.")]
        public LayerMask layerMask = 0;
        [Tooltip("Runs no earlier than After Rendering Transparents to read selected transparent color. For cloud occlusion, run after SSC's buffer and before SSC's composite.")]
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingTransparents;

        [Header("Debug")]
        public DebugMode debug = DebugMode.Off;

        internal static readonly int ObjectMaskTextureID = Shader.PropertyToID("_MaskRBRObjectMaskTexture");
        internal static readonly int PreTransparentColorID = Shader.PropertyToID("_MaskRBRPreTransparentColor");
        internal static readonly int MaskTextureID = Shader.PropertyToID("_MaskRBRMaskTexture");
        internal static readonly int RayTextureID = Shader.PropertyToID("_MaskRBRRayTexture");
        internal static readonly int CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");
        internal static readonly int SourceModeID = Shader.PropertyToID("_MaskRBRSourceMode");
        internal static readonly int RadialParametersID = Shader.PropertyToID("_MaskRBRRadialParameters");
        internal static readonly int PhaseParametersID = Shader.PropertyToID("_MaskRBRPhaseParameters");
        internal static readonly int OutputSizeID = Shader.PropertyToID("_MaskRBROutputSize");
        internal static readonly int IntensityID = Shader.PropertyToID("_MaskRBRIntensity");
        internal static readonly int DebugModeID = Shader.PropertyToID("_MaskRBRDebugMode");
        internal static readonly int OccludeCloudsID = Shader.PropertyToID("_MaskRBROccludeClouds");
    }

    static readonly Vector4 BlitScaleBias = new(1f, 1f, 0f, 0f);

    sealed class FrameSources : ContextItem
    {
        public TextureHandle preTransparentColor;
        public FrameSources() { }
        public override void Reset() => preTransparentColor = TextureHandle.nullHandle;
    }

    sealed class PreTransparentColorPass : ScriptableRenderPass
    {
        sealed class CopyData
        {
            public TextureHandle source;
        }

        public PreTransparentColorPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            requiresIntermediateTexture = true;
            // Keep the sky and visibility tests based on opaque depth.
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var sources = frameData.GetOrCreate<FrameSources>();
            TextureHandle scene = resources.activeColorTexture;
            if (resources.isActiveTargetBackBuffer || !scene.IsValid())
                return;

            TextureDesc descriptor = graph.GetTextureDesc(scene);
            descriptor.name = "MaskRBR.PreTransparentColor";
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            descriptor.clearBuffer = false;
            sources.preTransparentColor = graph.CreateTexture(descriptor);

            using var builder = graph.AddRasterRenderPass<CopyData>(descriptor.name, out var data);
            data.source = scene;
            builder.UseTexture(scene, AccessFlags.Read);
            builder.SetRenderAttachment(sources.preTransparentColor, 0, AccessFlags.WriteAll);
            builder.SetGlobalTextureAfterPass(sources.preTransparentColor, Settings.PreTransparentColorID);
            builder.SetRenderFunc(static (CopyData pass, RasterGraphContext context) =>
                Blitter.BlitTexture(context.cmd, pass.source, BlitScaleBias, 0f, false));
        }
    }

    sealed class MaskRBRPass : ScriptableRenderPass
    {
        sealed class MaskData
        {
            public Material material;
            public TextureHandle scene;
            public float sourceMode;
            public float occludeClouds;
        }

        sealed class RadialData
        {
            public Material material;
            public TextureHandle source;
            public Vector4 parameters;
            public Vector4 phase;
            public Vector4 outputSize;
            public QualityMode quality;
        }

        sealed class CompositeData
        {
            public Material material;
            public TextureHandle scene;
            public Vector4 phase;
            public float intensity;
            public float debug;
        }

        const int MaskPass = 0;
        const int RadialPass = 1;
        const int CompositePass = 2;
        const int OpaqueObjectMaskPass = 0;
        const int TransparentObjectMaskPass = 1;
        const int TransparentSkyOccludersPass = 2;
        const string LowKeyword = "_MASKRBR_QUALITY_LOW";
        const string HighKeyword = "_MASKRBR_QUALITY_HIGH";

        static readonly ShaderTagId[] ObjectShaderTags =
        {
            new("UniversalForward"), new("UniversalForwardOnly"),
            new("SRPDefaultUnlit"), new("UniversalGBuffer")
        };

        // Order guard state: a feature with intermediates has two times, so the
        // contract is a partial order rather than a single passEvent. Report once
        // per distinct configuration instead of every frame.
        static RenderPassEvent? s_seenBuffer, s_seenComposite, s_seenConsumer;
        static bool s_warnedLateBuffer, s_warnedEarlyComposite;

        // Source and layer mask are not independent: Sky ignores the mask, and an
        // object source with Nothing renders an all-zero mask. Warn on transitions.
        static bool s_warnedEmptyLayers;

        readonly Settings _settings;
        readonly Material _material;
        readonly Material _maskMaterial;

        public MaskRBRPass(Settings settings)
        {
            _settings = settings;
            _material = CoreUtils.CreateEngineMaterial(settings.shader);
            _maskMaterial = CoreUtils.CreateEngineMaterial(settings.maskShader);
            _maskMaterial.enableInstancing = true;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        // ════════════════════════════════════════════════════════════
        //  Render graph — camera-colored source mask, radial rays, angular composite.
        // ════════════════════════════════════════════════════════════
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            ValidateSourceLayers();
            ValidateCloudOrder();
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            TextureHandle preTransparentColor = frameData.GetOrCreate<FrameSources>().preTransparentColor;
            if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid()
                || !resources.cameraDepthTexture.IsValid() || !preTransparentColor.IsValid())
                return;

            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            UniversalLightData lights = frameData.Get<UniversalLightData>();
            TextureHandle scene = resources.activeColorTexture;
            TextureHandle depth = resources.cameraDepthTexture;
            SourceMode sourceMode = _settings.source;
            int layerMask = sourceMode == SourceMode.Sky ? 0 : _settings.layerMask.value;
            int skyOccluderPass = sourceMode == SourceMode.Objects ? -1 : TransparentSkyOccludersPass;
            TextureHandle objectMask = ScreenMaskRenderer.RecordObjects(graph, frameData, scene, depth,
                layerMask, ObjectShaderTags, _maskMaterial, OpaqueObjectMaskPass, "MaskRBR.ObjectMask",
                Settings.ObjectMaskTextureID, TransparentObjectMaskPass, skyOccluderPass,
                GraphicsFormat.R8G8B8A8_UNorm);
            TextureHandle mask = RecordMask(graph, scene, objectMask, depth, sourceMode);

            QualityMode quality = _settings.quality;
            int downsample = quality == QualityMode.Low ? 2 : 1;
            TextureDesc descriptor = CreateColorDescriptor(graph, scene, downsample);
            Vector2Int size = GetOutputSize(camera);
            Vector4 outputSize = new(size.x, size.y, 0f, 0f);
            Vector4 radialParameters = new(Mathf.Clamp01(_settings.length), Mathf.Clamp(_settings.falloff, 0f, 8f),
                Mathf.Clamp01(_settings.jitterStrength), 0f);
            float angle = Mathf.Clamp(_settings.halfAngle, 1f, 90f) * Mathf.Deg2Rad;
            bool hasMainLight = lights.mainLightIndex >= 0
                && lights.visibleLights[lights.mainLightIndex].lightType == LightType.Directional;
            Vector4 phaseParameters = new(Mathf.Clamp(_settings.anisotropy, 0f, 0.95f),
                Mathf.Cos(angle), Mathf.Cos(angle * 0.8f), hasMainLight ? 1f : 0f);
            TextureHandle rays = RecordRadial(graph, mask, descriptor, radialParameters,
                phaseParameters, outputSize, quality);

            resources.cameraColor = RecordComposite(graph, scene, mask, rays, phaseParameters,
                Mathf.Clamp01(_settings.intensity), (float)_settings.debug);
        }

        // ════════════════════════════════════════════════════════════
        //  Source/layer guard — object sources with Nothing emit nothing.
        // ════════════════════════════════════════════════════════════
        void ValidateSourceLayers()
        {
            bool emptyLayers = _settings.source != SourceMode.Sky && _settings.layerMask.value == 0;
            if (emptyLayers == s_warnedEmptyLayers)
                return;
            s_warnedEmptyLayers = emptyLayers;
            if (emptyLayers)
                Debug.LogWarning($"[MaskRBR] Source is {_settings.source} but Layer Mask is Nothing, "
                    + "so no object can produce rays. Select object layers or switch Source to Sky.");
        }

        // ════════════════════════════════════════════════════════════
        //  Cloud order guard — buffer before the mask, composite after the rays.
        // ════════════════════════════════════════════════════════════
        void ValidateCloudOrder()
        {
            RenderPassEvent? buffer = SSCFeature.CloudBufferEvent;
            RenderPassEvent? composite = SSCFeature.CloudCompositeEvent;
            RenderPassEvent consumer = renderPassEvent;

            if (buffer != s_seenBuffer || composite != s_seenComposite || consumer != s_seenConsumer)
            {
                s_seenBuffer = buffer;
                s_seenComposite = composite;
                s_seenConsumer = consumer;
                s_warnedLateBuffer = false;
                s_warnedEarlyComposite = false;
            }

            if (!_settings.occludeWithClouds || buffer == null || composite == null) return;

            if (!s_warnedLateBuffer && buffer.Value >= consumer)
            {
                s_warnedLateBuffer = true;
                Debug.LogWarning($"[MaskRBR] Cloud occlusion is off this frame: SSC produces _SSCCloudTexture at "
                    + $"{buffer.Value}, which is not earlier than MaskRBR's {consumer}. "
                    + "Move SSC's Cloud Buffer Event before MaskRBR's Event.");
            }

            if (!s_warnedEarlyComposite && composite.Value <= consumer)
            {
                s_warnedEarlyComposite = true;
                Debug.LogWarning($"[MaskRBR] SSC composites at {composite.Value}, which is not later than "
                    + $"MaskRBR's {consumer}. Clouds are drawn before the rays are added, so the rays appear "
                    + "on top of them. Use a later SSC Composite Event, such as BeforeRenderingPostProcessing.");
            }
        }

        TextureHandle RecordMask(RenderGraph graph, TextureHandle scene,
            TextureHandle objectMask, TextureHandle depth, SourceMode sourceMode)
        {
            // 只有 SSC 本帧确实记录了 ray march 才能声明该全局纹理，否则 UseGlobalTexture 会抛异常。
            bool occludeClouds = _settings.occludeWithClouds && SSCFeature.CloudOcclusionReady;
            TextureDesc descriptor = CreateColorDescriptor(graph, scene, 1);
            descriptor.name = "MaskRBR.Mask";
            TextureHandle mask = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<MaskData>(descriptor.name, out var data))
            {
                data.material = _material;
                data.scene = scene;
                data.sourceMode = (float)sourceMode;
                data.occludeClouds = occludeClouds ? 1f : 0f;
                builder.UseTexture(scene, AccessFlags.Read);
                builder.UseTexture(objectMask, AccessFlags.Read);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.ObjectMaskTextureID, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.PreTransparentColorID, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.CameraDepthTextureID, AccessFlags.Read);
                if (occludeClouds)
                    builder.UseGlobalTexture(SSCFeature.CloudTextureID, AccessFlags.Read);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(mask, Settings.MaskTextureID);
                builder.SetRenderFunc(static (MaskData pass, RasterGraphContext context) =>
                {
                    pass.material.SetFloat(Settings.SourceModeID, pass.sourceMode);
                    pass.material.SetFloat(Settings.OccludeCloudsID, pass.occludeClouds);
                    Blitter.BlitTexture(context.cmd, pass.scene, BlitScaleBias, pass.material, MaskPass);
                });
            }
            return mask;
        }

        // ════════════════════════════════════════════════════════════
        //  Texture dimensions — retain XR layout and final viewport units.
        // ════════════════════════════════════════════════════════════
        static Vector2Int GetOutputSize(UniversalCameraData data)
        {
            Camera camera = data.camera;
            if (!data.xr.enabled)
                return new Vector2Int(Mathf.Max(1, camera.pixelWidth), Mathf.Max(1, camera.pixelHeight));

            Rect viewport = data.xr.GetViewport();
            Rect rect = camera.rect;
            float x = viewport.x + rect.x * viewport.width;
            float y = viewport.y + rect.y * viewport.height;
            int width = (int)System.Math.Round(x + rect.width * viewport.width) - (int)System.Math.Round(x);
            int height = (int)System.Math.Round(y + rect.height * viewport.height) - (int)System.Math.Round(y);
            return new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
        }

        static TextureDesc CreateColorDescriptor(RenderGraph graph, TextureHandle scene, int downsample)
        {
            TextureDesc descriptor = graph.GetTextureDesc(scene);
            if (downsample > 1)
            {
                RenderTargetInfo source = graph.GetRenderTargetInfo(scene);
                descriptor.sizeMode = TextureSizeMode.Explicit;
                descriptor.width = Mathf.Max(1, source.width / downsample);
                descriptor.height = Mathf.Max(1, source.height / downsample);
            }
            descriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.clearBuffer = false;
            return descriptor;
        }

        TextureHandle RecordRadial(RenderGraph graph, TextureHandle source, TextureDesc descriptor,
            Vector4 parameters, Vector4 phase, Vector4 outputSize, QualityMode quality)
        {
            descriptor.name = "MaskRBR.Rays";
            TextureHandle rays = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<RadialData>("MaskRBR.Radial", out var data))
            {
                data.material = _material;
                data.source = source;
                data.parameters = parameters;
                data.phase = phase;
                data.outputSize = outputSize;
                data.quality = quality;
                builder.UseTexture(source, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.MaskTextureID, AccessFlags.Read);
                builder.SetRenderAttachment(rays, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(rays, Settings.RayTextureID);
                builder.SetRenderFunc(static (RadialData pass, RasterGraphContext context) =>
                {
                    pass.material.SetVector(Settings.RadialParametersID, pass.parameters);
                    pass.material.SetVector(Settings.PhaseParametersID, pass.phase);
                    pass.material.SetVector(Settings.OutputSizeID, pass.outputSize);
                    CoreUtils.SetKeyword(pass.material, LowKeyword, pass.quality == QualityMode.Low);
                    CoreUtils.SetKeyword(pass.material, HighKeyword, pass.quality == QualityMode.High);
                    Blitter.BlitTexture(context.cmd, pass.source, BlitScaleBias, pass.material, RadialPass);
                });
            }
            return rays;
        }

        TextureHandle RecordComposite(RenderGraph graph, TextureHandle scene,
            TextureHandle mask, TextureHandle rays, Vector4 phase, float intensity, float debug)
        {
            TextureDesc descriptor = CreateColorDescriptor(graph, scene, 1);
            descriptor.name = "MaskRBR.Composite";
            TextureHandle output = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<CompositeData>(descriptor.name, out var data))
            {
                data.material = _material;
                data.scene = scene;
                data.phase = phase;
                data.intensity = intensity;
                data.debug = debug;
                builder.UseTexture(scene, AccessFlags.Read);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.UseTexture(rays, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.MaskTextureID, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.RayTextureID, AccessFlags.Read);
                builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (CompositeData pass, RasterGraphContext context) =>
                {
                    pass.material.SetFloat(Settings.IntensityID, pass.intensity);
                    pass.material.SetVector(Settings.PhaseParametersID, pass.phase);
                    pass.material.SetFloat(Settings.DebugModeID, pass.debug);
                    Blitter.BlitTexture(context.cmd, pass.scene, BlitScaleBias, pass.material, CompositePass);
                });
            }
            return output;
        }

        public void Release()
        {
            CoreUtils.Destroy(_material);
            CoreUtils.Destroy(_maskMaterial);
        }
    }

    public Settings settings = new();
    PreTransparentColorPass _preTransparentColorPass;
    MaskRBRPass _pass;

    // ════════════════════════════════════════════════════════════
    //  Lifecycle — own both runtime materials and skip inactive cameras.
    // ════════════════════════════════════════════════════════════
    public override void Create()
    {
        _pass?.Release();
        _pass = settings.shader != null && settings.maskShader != null ? new MaskRBRPass(settings) : null;
        _preTransparentColorPass = _pass != null ? new PreTransparentColorPass() : null;
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Release();
        _pass = null;
        _preTransparentColorPass = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        CameraData camera = renderingData.cameraData;
        if (_pass == null || camera.renderType == CameraRenderType.Overlay
            || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
            || (settings.debug == DebugMode.Off && (settings.intensity <= 0f || settings.length <= 0f)))
            return;
        _pass.renderPassEvent = settings.passEvent < RenderPassEvent.AfterRenderingTransparents
            ? RenderPassEvent.AfterRenderingTransparents : settings.passEvent;
        renderer.EnqueuePass(_preTransparentColorPass);
        renderer.EnqueuePass(_pass);
    }
}
