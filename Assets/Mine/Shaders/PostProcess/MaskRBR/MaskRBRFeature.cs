// ════════════════════════════════════════════════════════════════
//  Mask RBR Feature — main-light rays from radial masks and angular scattering.
// ════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Mine.Rendering;

/// <summary>
/// Produces radial rays from sky and selected visible opaque scene objects.
/// </summary>
/// <remarks>
/// The object override supports solid opaque geometry. Alpha clipping and
/// custom vertex displacement require a future original-material mask pass.
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
        public SourceMode source = SourceMode.Sky;
        [Tooltip("Solid opaque objects on these layers can produce rays.")]
        public LayerMask layerMask = 0;

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

        [Header("Debug")]
        public DebugMode debug = DebugMode.Off;

        internal static readonly int ObjectMaskTextureID = Shader.PropertyToID("_MaskRBRObjectMaskTexture");
        internal static readonly int MaskTextureID = Shader.PropertyToID("_MaskRBRMaskTexture");
        internal static readonly int RayTextureID = Shader.PropertyToID("_MaskRBRRayTexture");
        internal static readonly int CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");
        internal static readonly int SourceModeID = Shader.PropertyToID("_MaskRBRSourceMode");
        internal static readonly int RadialParametersID = Shader.PropertyToID("_MaskRBRRadialParameters");
        internal static readonly int PhaseParametersID = Shader.PropertyToID("_MaskRBRPhaseParameters");
        internal static readonly int OutputSizeID = Shader.PropertyToID("_MaskRBROutputSize");
        internal static readonly int IntensityID = Shader.PropertyToID("_MaskRBRIntensity");
        internal static readonly int DebugModeID = Shader.PropertyToID("_MaskRBRDebugMode");
    }

    sealed class MaskRBRPass : ScriptableRenderPass
    {
        sealed class MaskData
        {
            public Material material;
            public TextureHandle objectMask;
            public float sourceMode;
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
        const string LowKeyword = "_MASKRBR_QUALITY_LOW";
        const string HighKeyword = "_MASKRBR_QUALITY_HIGH";

        static readonly ShaderTagId[] ObjectShaderTags =
        {
            new("UniversalForward"), new("UniversalForwardOnly"),
            new("SRPDefaultUnlit"), new("UniversalGBuffer")
        };
        static readonly Vector4 BlitScaleBias = new(1f, 1f, 0f, 0f);

        readonly Settings _settings;
        readonly Material _material;
        readonly Material _maskMaterial;

        public MaskRBRPass(Settings settings)
        {
            _settings = settings;
            _material = CoreUtils.CreateEngineMaterial(settings.shader);
            _maskMaterial = CoreUtils.CreateEngineMaterial(settings.maskShader);
            _maskMaterial.enableInstancing = true;
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        // ════════════════════════════════════════════════════════════
        //  Render graph — visible source masks, scalar angular rays, light composite.
        // ════════════════════════════════════════════════════════════
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid()
                || !resources.cameraDepthTexture.IsValid())
                return;

            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            UniversalLightData lights = frameData.Get<UniversalLightData>();
            TextureHandle scene = resources.activeColorTexture;
            TextureHandle depth = resources.cameraDepthTexture;
            SourceMode sourceMode = _settings.source;
            int layerMask = sourceMode == SourceMode.Sky ? 0 : _settings.layerMask.value;
            TextureHandle objectMask = RecordObjectMask(graph, frameData, scene, depth, layerMask);
            TextureHandle mask = RecordMask(graph, scene, objectMask, depth, sourceMode);

            QualityMode quality = _settings.quality;
            int downsample = quality == QualityMode.Low ? 2 : 1;
            TextureDesc descriptor = CreateColorDescriptor(graph, scene, downsample);
            descriptor.colorFormat = GraphicsFormat.R16_SFloat;
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

        TextureHandle RecordObjectMask(RenderGraph graph, ContextContainer frameData,
            TextureHandle scene, TextureHandle depth, int layerMask)
        {
            return ScreenMaskRenderer.RecordObjects(graph, frameData, scene, depth,
                layerMask, ObjectShaderTags, _maskMaterial, 0, "MaskRBR.ObjectMask", Settings.ObjectMaskTextureID);
        }

        TextureHandle RecordMask(RenderGraph graph, TextureHandle scene,
            TextureHandle objectMask, TextureHandle depth, SourceMode sourceMode)
        {
            TextureDesc descriptor = ScreenMaskRenderer.CreateDescriptor(graph, scene, "MaskRBR.Mask", false);
            TextureHandle mask = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<MaskData>(descriptor.name, out var data))
            {
                data.material = _material;
                data.objectMask = objectMask;
                data.sourceMode = (float)sourceMode;
                builder.UseTexture(objectMask, AccessFlags.Read);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.ObjectMaskTextureID, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.CameraDepthTextureID, AccessFlags.Read);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(mask, Settings.MaskTextureID);
                builder.SetRenderFunc(static (MaskData pass, RasterGraphContext context) =>
                {
                    pass.material.SetFloat(Settings.SourceModeID, pass.sourceMode);
                    Blitter.BlitTexture(context.cmd, pass.objectMask, BlitScaleBias, pass.material, MaskPass);
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
    MaskRBRPass _pass;

    // ════════════════════════════════════════════════════════════
    //  Lifecycle — own both runtime materials and skip inactive cameras.
    // ════════════════════════════════════════════════════════════
    public override void Create()
    {
        _pass?.Release();
        _pass = settings.shader != null && settings.maskShader != null ? new MaskRBRPass(settings) : null;
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Release();
        _pass = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        CameraData camera = renderingData.cameraData;
        if (_pass == null || camera.renderType == CameraRenderType.Overlay
            || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
            || (settings.debug == DebugMode.Off && (settings.intensity <= 0f || settings.length <= 0f)))
            return;
        renderer.EnqueuePass(_pass);
    }
}
