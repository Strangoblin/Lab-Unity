// ════════════════════════════════════════════════════════════════
//  Mask Blur Feature — selected scene colors diffuse into a soft glow.
// ════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Mine.Rendering;

/// <summary>
/// Collects visible MaskBlur material passes and diffuses their scene colors.
/// </summary>
public sealed class MaskBlurFeature : ScriptableRendererFeature
{
    public enum QualityMode { Low, Medium, High }
    public enum KernelMode { Gaussian, JitteredGaussian }
    public enum RadiusMode { Pixels, Normalized }
    public enum DebugMode { Off, Mask, Blur }

    [System.Serializable]
    public sealed class Settings
    {
        [Header("Technical")]
        public Shader shader;
        public LayerMask layerMask = -1;
        public KernelMode kernel = KernelMode.Gaussian;

        [Header("Performance")]
        public QualityMode quality = QualityMode.Medium;

        [Header("Artistic")]
        public RadiusMode radiusMode = RadiusMode.Pixels;
        [Tooltip("Kernel step in final output pixels; used in Pixels mode.")]
        [Min(0f)] public float blurRadius = 0.5f;
        [Tooltip("Kernel step as a fraction of output height; used in Normalized mode.")]
        [Range(0f, 0.05f)] public float normalizedRadius = 0.005f;
        [Tooltip("Spatial offset jitter. Zero reproduces the fixed Gaussian kernel.")]
        [Range(0f, 1f)] public float jitterStrength = 1f;
        [Range(0f, 1f)] public float intensity = 0.5f;

        [Header("Debug")]
        public DebugMode debug = DebugMode.Off;

        internal static readonly int MaskTextureID = Shader.PropertyToID("_MaskBlurMaskTexture");
        internal static readonly int BlurTextureID = Shader.PropertyToID("_MaskBlurTexture");
        internal static readonly int CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");
        internal static readonly int BlurParametersID = Shader.PropertyToID("_BlurParameters");
        internal static readonly int BlurOutputSizeID = Shader.PropertyToID("_BlurOutputSize");
        internal static readonly int ExtractDownsampleID = Shader.PropertyToID("_ExtractDownsample");
        internal static readonly int IntensityID = Shader.PropertyToID("_Intensity");
        internal static readonly int DebugModeID = Shader.PropertyToID("_DebugMode");
    }

    sealed class MaskBlurPass : ScriptableRenderPass
    {
        sealed class ExtractData
        {
            public Material material;
            public TextureHandle scene;
            public bool downsample;
        }

        sealed class HorizontalData
        {
            public Material material;
            public TextureHandle source;
            public Vector4 blurParameters;
            public Vector4 outputSize;
            public bool jittered;
            public QualityMode quality;
        }

        sealed class VerticalData
        {
            public Material material;
            public TextureHandle source;
            public Vector4 blurParameters;
            public Vector4 outputSize;
            public bool jittered;
            public QualityMode quality;
        }

        sealed class CompositeData
        {
            public Material material;
            public TextureHandle scene;
            public float intensity;
            public float debug;
        }

        const int ExtractPass = 0;
        const int HorizontalPass = 1;
        const int VerticalPass = 2;
        const int CompositePass = 3;
        const string JitterKeyword = "_MASKBLUR_JITTERED";
        const string MediumKeyword = "_MASKBLUR_QUALITY_MEDIUM";
        const string HighKeyword = "_MASKBLUR_QUALITY_HIGH";

        static readonly ShaderTagId[] MaskShaderTags = { new("MaskBlur") };
        static readonly Vector4 BlitScaleBias = new(1f, 1f, 0f, 0f);

        readonly Settings _settings;
        readonly Material _material;

        public MaskBlurPass(Settings settings)
        {
            _settings = settings;
            _material = CoreUtils.CreateEngineMaterial(settings.shader);
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        // ════════════════════════════════════════════════════════════
        //  Render graph — mask, selected HDR color, blur, camera composite.
        // ════════════════════════════════════════════════════════════
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid()
                || !resources.cameraDepthTexture.IsValid())
                return;

            TextureHandle scene = resources.activeColorTexture;
            TextureHandle mask = RecordMask(graph, frameData, scene, resources.cameraDepthTexture);
            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            Vector2Int size = GetOutputSize(camera);
            Vector4 outputSize = new(size.x, size.y, 0f, 0f);
            float radius = Mathf.Max(0f, _settings.radiusMode == RadiusMode.Normalized
                ? _settings.normalizedRadius : _settings.blurRadius);
            QualityMode quality = _settings.quality;
            bool jittered = _settings.kernel == KernelMode.JitteredGaussian
                && _settings.jitterStrength > 0f;
            int downsample = radius > 0f && quality == QualityMode.Low ? 2 : 1;
            const int iterations = 2;
            float iterationRadius = radius / Mathf.Sqrt(iterations);
            TextureDesc colorDescriptor = CreateColorDescriptor(graph, scene, downsample);
            TextureHandle selectedColor = RecordExtract(graph, scene, mask, colorDescriptor, downsample > 1, radius <= 0f);
            TextureHandle blurredColor = selectedColor;

            if (radius > 0f)
            {
                for (int i = 0; i < iterations; i++)
                {
                    Vector4 blurParameters = new(iterationRadius, (float)_settings.radiusMode,
                        Mathf.Clamp01(_settings.jitterStrength), i * 2f);
                    TextureHandle horizontal = RecordHorizontal(graph, blurredColor, colorDescriptor,
                        blurParameters, outputSize, jittered, quality, i);
                    blurredColor = RecordVertical(graph, horizontal, colorDescriptor,
                        blurParameters, outputSize, jittered, quality, i, i == iterations - 1);
                }
            }

            resources.cameraColor = RecordComposite(graph, scene, mask, blurredColor);
        }

        TextureHandle RecordMask(RenderGraph graph, ContextContainer frameData,
            TextureHandle scene, TextureHandle depth)
        {
            return ScreenMaskRenderer.RecordObjects(graph, frameData, scene, depth,
                _settings.layerMask.value, MaskShaderTags, null, 0, "MaskBlur.Mask", Settings.MaskTextureID);
        }

        // ════════════════════════════════════════════════════════════
        //  Output size — final camera viewport pixels, including per-eye XR.
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

        TextureHandle RecordExtract(RenderGraph graph, TextureHandle scene, TextureHandle mask,
            TextureDesc descriptor, bool downsample, bool publishBlur)
        {
            descriptor.name = "MaskBlur.SelectedColor";
            TextureHandle selectedColor = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<ExtractData>("MaskBlur.Extract", out var data))
            {
                data.material = _material;
                data.scene = scene;
                data.downsample = downsample;
                builder.UseTexture(scene, AccessFlags.Read);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.MaskTextureID, AccessFlags.Read);
                builder.SetRenderAttachment(selectedColor, 0, AccessFlags.Write);
                if (publishBlur)
                    builder.SetGlobalTextureAfterPass(selectedColor, Settings.BlurTextureID);
                builder.SetRenderFunc(static (ExtractData pass, RasterGraphContext context) =>
                {
                    pass.material.SetFloat(Settings.ExtractDownsampleID, pass.downsample ? 1f : 0f);
                    Blitter.BlitTexture(context.cmd, pass.scene, BlitScaleBias, pass.material, ExtractPass);
                });
            }
            return selectedColor;
        }

        TextureHandle RecordHorizontal(RenderGraph graph, TextureHandle source,
            TextureDesc descriptor, Vector4 blurParameters, Vector4 outputSize, bool jittered, QualityMode quality, int iteration)
        {
            descriptor.name = $"MaskBlur.Horizontal.{iteration}";
            TextureHandle horizontal = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<HorizontalData>(
                descriptor.name, out var data))
            {
                data.material = _material;
                data.source = source;
                data.blurParameters = blurParameters;
                data.outputSize = outputSize;
                data.jittered = jittered;
                data.quality = quality;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(horizontal, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (HorizontalData pass, RasterGraphContext context) =>
                {
                    ConfigureBlur(pass.material, pass.blurParameters, pass.outputSize, pass.jittered, pass.quality);
                    Blitter.BlitTexture(context.cmd, pass.source, BlitScaleBias, pass.material, HorizontalPass);
                });
            }
            return horizontal;
        }

        TextureHandle RecordVertical(RenderGraph graph, TextureHandle source,
            TextureDesc descriptor, Vector4 blurParameters, Vector4 outputSize, bool jittered,
            QualityMode quality, int iteration, bool publishBlur)
        {
            descriptor.name = $"MaskBlur.Vertical.{iteration}";
            TextureHandle vertical = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<VerticalData>(
                descriptor.name, out var data))
            {
                data.material = _material;
                data.source = source;
                data.blurParameters = blurParameters;
                data.outputSize = outputSize;
                data.jittered = jittered;
                data.quality = quality;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(vertical, 0, AccessFlags.Write);
                if (publishBlur)
                    builder.SetGlobalTextureAfterPass(vertical, Settings.BlurTextureID);
                builder.SetRenderFunc(static (VerticalData pass, RasterGraphContext context) =>
                {
                    ConfigureBlur(pass.material, pass.blurParameters, pass.outputSize, pass.jittered, pass.quality);
                    Blitter.BlitTexture(context.cmd, pass.source, BlitScaleBias, pass.material, VerticalPass);
                });
            }
            return vertical;
        }

        static void ConfigureBlur(Material material, Vector4 parameters, Vector4 outputSize,
            bool jittered, QualityMode quality)
        {
            material.SetVector(Settings.BlurParametersID, parameters);
            material.SetVector(Settings.BlurOutputSizeID, outputSize);
            CoreUtils.SetKeyword(material, JitterKeyword, jittered);
            CoreUtils.SetKeyword(material, MediumKeyword, jittered && quality == QualityMode.Medium);
            CoreUtils.SetKeyword(material, HighKeyword, jittered && quality == QualityMode.High);
        }

        TextureHandle RecordComposite(RenderGraph graph, TextureHandle scene,
            TextureHandle mask, TextureHandle blurredColor)
        {
            TextureDesc descriptor = graph.GetTextureDesc(scene);
            descriptor.name = "MaskBlur.Composite";
            descriptor.clearBuffer = false;
            TextureHandle output = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<CompositeData>(
                "MaskBlur.Composite", out var data))
            {
                data.material = _material;
                data.scene = scene;
                data.intensity = Mathf.Clamp01(_settings.intensity);
                data.debug = (float)_settings.debug;
                builder.UseTexture(scene, AccessFlags.Read);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.UseTexture(blurredColor, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.MaskTextureID, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.BlurTextureID, AccessFlags.Read);
                builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (CompositeData pass, RasterGraphContext context) =>
                {
                    pass.material.SetFloat(Settings.IntensityID, pass.intensity);
                    pass.material.SetFloat(Settings.DebugModeID, pass.debug);
                    Blitter.BlitTexture(context.cmd, pass.scene, BlitScaleBias, pass.material, CompositePass);
                });
            }
            return output;
        }

        public void Release()
        {
            CoreUtils.Destroy(_material);
        }
    }

    public Settings settings = new();
    MaskBlurPass _pass;

    // ════════════════════════════════════════════════════════════
    //  Lifecycle — recreate and dispose the runtime material together.
    // ════════════════════════════════════════════════════════════
    public override void Create()
    {
        _pass?.Release();
        _pass = settings.shader != null ? new MaskBlurPass(settings) : null;
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
            || (settings.intensity <= 0f && settings.debug == DebugMode.Off))
            return;
        renderer.EnqueuePass(_pass);
    }
}
