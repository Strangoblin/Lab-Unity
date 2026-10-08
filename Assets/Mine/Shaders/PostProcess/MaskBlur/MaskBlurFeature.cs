// ════════════════════════════════════════════════════════════════
//  Mask Blur Feature — selected scene colors diffuse into a soft glow.
// ════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Collects visible MaskBlur material passes and diffuses their scene colors.
/// </summary>
public sealed class MaskBlurFeature : ScriptableRendererFeature
{
    public enum QualityMode { Low, Medium, High }
    public enum DebugMode { Off, Mask, Blur }

    [System.Serializable]
    public sealed class Settings
    {
        [Header("Technical")]
        public Shader shader;
        public LayerMask layerMask = -1;

        [Header("Performance")]
        public QualityMode quality = QualityMode.Medium;

        [Header("Artistic")]
        [Range(0f, 5f)] public float blurRadius = 0.5f;
        [Range(0f, 1f)] public float intensity = 0.5f;

        [Header("Debug")]
        public DebugMode debug = DebugMode.Off;

        internal static readonly int MaskTextureID = Shader.PropertyToID("_MaskBlurMaskTexture");
        internal static readonly int BlurTextureID = Shader.PropertyToID("_MaskBlurTexture");
        internal static readonly int CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");
        internal static readonly int BlurRadiusID = Shader.PropertyToID("_BlurRadius");
        internal static readonly int IntensityID = Shader.PropertyToID("_Intensity");
        internal static readonly int DebugModeID = Shader.PropertyToID("_DebugMode");
    }

    sealed class MaskBlurPass : ScriptableRenderPass
    {
        sealed class MaskData
        {
            public RendererListHandle rendererList;
        }

        sealed class ExtractData
        {
            public Material material;
            public TextureHandle scene;
        }

        sealed class HorizontalData
        {
            public Material material;
            public TextureHandle source;
            public float radius;
        }

        sealed class VerticalData
        {
            public Material material;
            public TextureHandle source;
            public float radius;
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

        static readonly ShaderTagId MaskShaderTag = new("MaskBlur");
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
            float radius = Mathf.Max(0f, _settings.blurRadius);
            int downsample = radius > 0f && _settings.quality == QualityMode.Low ? 2 : 1;
            int iterations = _settings.quality == QualityMode.High ? 2 : 1;
            float iterationRadius = radius / (downsample * Mathf.Sqrt(iterations));
            TextureDesc colorDescriptor = CreateColorDescriptor(graph, frameData, scene, downsample);
            TextureHandle selectedColor = RecordExtract(graph, scene, mask, colorDescriptor, radius <= 0f);
            TextureHandle blurredColor = selectedColor;

            if (radius > 0f)
            {
                for (int i = 0; i < iterations; i++)
                {
                    TextureHandle horizontal = RecordHorizontal(graph, blurredColor, colorDescriptor, iterationRadius, i);
                    blurredColor = RecordVertical(graph, horizontal, colorDescriptor, iterationRadius, i,
                        i == iterations - 1);
                }
            }

            resources.cameraColor = RecordComposite(graph, scene, mask, blurredColor);
        }

        TextureHandle RecordMask(RenderGraph graph, ContextContainer frameData,
            TextureHandle scene, TextureHandle depth)
        {
            UniversalRenderingData rendering = frameData.Get<UniversalRenderingData>();
            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            UniversalLightData lights = frameData.Get<UniversalLightData>();
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                MaskShaderTag, rendering, camera, lights, camera.defaultOpaqueSortFlags);
            FilteringSettings filtering = new(RenderQueueRange.opaque,
                rendering.opaqueLayerMask.value & _settings.layerMask.value);
            RendererListHandle rendererList = graph.CreateRendererList(
                new RendererListParams(rendering.cullResults, drawing, filtering));

            TextureDesc descriptor = graph.GetTextureDesc(scene);
            descriptor.name = "MaskBlur.Mask";
            descriptor.colorFormat = GraphicsFormat.R8_UNorm;
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            TextureHandle mask = graph.CreateTexture(descriptor);

            using (var builder = graph.AddRasterRenderPass<MaskData>("MaskBlur.Mask", out var data))
            {
                data.rendererList = rendererList;
                builder.UseRendererList(rendererList);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.CameraDepthTextureID, AccessFlags.Read);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(mask, Settings.MaskTextureID);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (MaskData pass, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(pass.rendererList);
                });
            }

            return mask;
        }

        static TextureDesc CreateColorDescriptor(RenderGraph graph, ContextContainer frameData,
            TextureHandle scene, int downsample)
        {
            TextureDesc descriptor = graph.GetTextureDesc(scene);
            if (downsample > 1)
            {
                UniversalCameraData camera = frameData.Get<UniversalCameraData>();
                descriptor.sizeMode = TextureSizeMode.Explicit;
                descriptor.width = Mathf.Max(1, camera.cameraTargetDescriptor.width / downsample);
                descriptor.height = Mathf.Max(1, camera.cameraTargetDescriptor.height / downsample);
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
            TextureDesc descriptor, bool publishBlur)
        {
            descriptor.name = "MaskBlur.SelectedColor";
            TextureHandle selectedColor = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<ExtractData>("MaskBlur.Extract", out var data))
            {
                data.material = _material;
                data.scene = scene;
                builder.UseTexture(scene, AccessFlags.Read);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.UseGlobalTexture(Settings.MaskTextureID, AccessFlags.Read);
                builder.SetRenderAttachment(selectedColor, 0, AccessFlags.Write);
                if (publishBlur)
                    builder.SetGlobalTextureAfterPass(selectedColor, Settings.BlurTextureID);
                builder.SetRenderFunc(static (ExtractData pass, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, pass.scene, BlitScaleBias, pass.material, ExtractPass);
                });
            }
            return selectedColor;
        }

        TextureHandle RecordHorizontal(RenderGraph graph, TextureHandle source,
            TextureDesc descriptor, float radius, int iteration)
        {
            descriptor.name = $"MaskBlur.Horizontal.{iteration}";
            TextureHandle horizontal = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<HorizontalData>(
                descriptor.name, out var data))
            {
                data.material = _material;
                data.source = source;
                data.radius = radius;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(horizontal, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (HorizontalData pass, RasterGraphContext context) =>
                {
                    pass.material.SetFloat(Settings.BlurRadiusID, pass.radius);
                    Blitter.BlitTexture(context.cmd, pass.source, BlitScaleBias, pass.material, HorizontalPass);
                });
            }
            return horizontal;
        }

        TextureHandle RecordVertical(RenderGraph graph, TextureHandle source,
            TextureDesc descriptor, float radius, int iteration, bool publishBlur)
        {
            descriptor.name = $"MaskBlur.Vertical.{iteration}";
            TextureHandle vertical = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<VerticalData>(
                descriptor.name, out var data))
            {
                data.material = _material;
                data.source = source;
                data.radius = radius;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(vertical, 0, AccessFlags.Write);
                if (publishBlur)
                    builder.SetGlobalTextureAfterPass(vertical, Settings.BlurTextureID);
                builder.SetRenderFunc(static (VerticalData pass, RasterGraphContext context) =>
                {
                    pass.material.SetFloat(Settings.BlurRadiusID, pass.radius);
                    Blitter.BlitTexture(context.cmd, pass.source, BlitScaleBias, pass.material, VerticalPass);
                });
            }
            return vertical;
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
