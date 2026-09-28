// ════════════════════════════════════════════════════════════════
//  SpecularGI Feature — SSSR trace with cubemap fallback.
// ════════════════════════════════════════════════════════════════
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class SpecularGIFeature : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        public enum DebugMode
        {
            Off = 0, ScreenSource = 1, SkySource = 3,
            Trace = 4, Spatial = 5, Temporal = 6, HistoryWeight = 7
        }

        [Header("Resources")]
        public Shader shader;
        public Cubemap skyCubemap;

        [Header("Technical")]
        [Range(1f, 200f)] public float maxDistance = 50f;
        [Range(0.001f, 0.5f)] public float thickness = 0.05f;
        [Range(0f, 0.2f)] public float normalBias = 0.03f;

        [Header("Technical · Material Approximation")]
        [Range(0f, 1f)] public float roughness = 0.1f;

        [Header("Technical · Sky Sampling")]
        [Range(0f, 12f)] public float skyMaxMip = 6f;

        internal static readonly int TraceParamsID = Shader.PropertyToID("_TraceParams");
        internal static readonly int RoughnessID = Shader.PropertyToID("_Roughness");
        internal static readonly int IntensityID = Shader.PropertyToID("_Intensity");
        internal static readonly int SkyCubemapID = Shader.PropertyToID("_SkyCubemap");
        internal static readonly int SkyMaxMipID = Shader.PropertyToID("_SkyMaxMip");
        internal static readonly int SpatialRadiusID = Shader.PropertyToID("_SpatialRadius");
        internal static readonly int SpatialBlurStrengthID = Shader.PropertyToID("_SpatialBlurStrength");
        internal static readonly int FrameIndexID = Shader.PropertyToID("_FrameIndex");
        internal static readonly int DebugModeID = Shader.PropertyToID("_DebugMode");
        internal static readonly int SpecularTextureID = Shader.PropertyToID("_SpecularGITexture");
    }

    [Serializable]
    public sealed class Controls : SSGIStandaloneControls
    {
        [Header("Artistic")]
        [Range(0f, 1f)] public float intensity = 1f;

        [Header("Debug")]
        public Settings.DebugMode debug = Settings.DebugMode.Off;

        public Controls() { temporalBlend = 0.95f; }
    }

    internal sealed class SpecularGIPass : ScriptableRenderPass
    {
        sealed class TraceData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle trace;
            public TextureHandle filtered;
            public TextureHandle spatial;
            public Vector4 traceParams;
            public float roughness;
            public float skyMaxMip;
            public float spatialRadius;
            public float blurStrength;
            public float frameIndex;
            public Cubemap skyCubemap;
        }

        sealed class CompositeData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle trace;
            public TextureHandle spatial;
            public TextureHandle temporal;
            public TextureHandle target;
            public float intensity;
            public float roughness;
            public Settings.DebugMode debug;
        }

        readonly Settings _settings;
        readonly Controls _controls;
        readonly Material _material;
        readonly SSGITemporalFilter _temporal;

        public SpecularGIPass(Shader shader, Settings settings, Controls controls = null)
        {
            _settings = settings;
            _controls = controls;
            _material = CoreUtils.CreateEngineMaterial(shader);
            _temporal = controls != null ? new SSGITemporalFilter(controls.temporalShader) : null;
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Color
                | ScriptableRenderPassInput.Depth
                | ScriptableRenderPassInput.Normal
                | ScriptableRenderPassInput.Motion);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            _temporal.BeginFrame(frameData);
            TextureHandle spatial = RecordSpatial(graph, frameData,
                TextureHandle.nullHandle, _temporal.FrameIndex,
                _controls.performance, _settings.roughness, 1f, out TextureHandle trace);
            if (!spatial.IsValid())
            {
                _temporal.CompleteFrame(graph, frameData);
                return;
            }

            TextureHandle temporal = _temporal.Resolve(graph, frameData, spatial,
                SSGITemporalFilter.Signal.SpecularGI, _controls.temporalBlend);
            _temporal.CompleteFrame(graph, frameData);
            RecordComposite(graph, frameData, trace, spatial, temporal,
                _controls.intensity, _settings.roughness, _controls.debug);
        }

        public TextureHandle RecordIntegrated(RenderGraph graph, ContextContainer frameData,
            TextureHandle source, int frameIndex, SSGIQuality performance, float roughness,
            float blurStrength)
        {
            return RecordSpatial(graph, frameData, source, frameIndex,
                performance, roughness, blurStrength, out _);
        }

        TextureHandle RecordSpatial(RenderGraph graph, ContextContainer frameData,
            TextureHandle inputSource, int frameIndex, SSGIQuality performance,
            float roughness, float blurStrength, out TextureHandle traceOutput)
        {
            traceOutput = TextureHandle.nullHandle;
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            TextureHandle source = inputSource.IsValid() ? inputSource : resources.activeColorTexture;
            if (!source.IsValid() || _material == null || camera.camera == null)
                return TextureHandle.nullHandle;

            GetQuality(performance, out int downsample,
                out int stepCount, out float spatialRadius);
            blurStrength = Mathf.Clamp01(blurStrength);
            RenderTextureDescriptor descriptor = camera.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.colorFormat = RenderTextureFormat.ARGBHalf;
            RenderTextureDescriptor traceDescriptor = descriptor;
            traceDescriptor.width = Mathf.Max(descriptor.width >> downsample, 1);
            traceDescriptor.height = Mathf.Max(descriptor.height >> downsample, 1);
            TextureHandle trace = UniversalRenderer.CreateRenderGraphTexture(
                graph, traceDescriptor, "SpecularGI.Trace", false);
            traceOutput = trace;
            TextureHandle filtered = blurStrength > 0f
                ? UniversalRenderer.CreateRenderGraphTexture(
                    graph, traceDescriptor, "SpecularGI.Filtered", false)
                : TextureHandle.nullHandle;
            TextureHandle spatial = UniversalRenderer.CreateRenderGraphTexture(
                graph, descriptor, "SpecularGI.Spatial", false);

            using (var builder = graph.AddUnsafePass<TraceData>("SpecularGI.TraceFilterUpsample", out var data))
            {
                data.material = _material;
                data.source = source;
                data.trace = trace;
                data.filtered = filtered;
                data.spatial = spatial;
                data.traceParams = new Vector4(
                    Mathf.Max(_settings.maxDistance, 0.001f),
                    Mathf.Max(_settings.thickness, 0.0001f),
                    Mathf.Max(_settings.normalBias, 0f), stepCount);
                data.roughness = Mathf.Clamp01(roughness);
                data.skyMaxMip = Mathf.Max(_settings.skyMaxMip, 0f);
                data.spatialRadius = spatialRadius;
                data.blurStrength = blurStrength;
                data.frameIndex = frameIndex;
                data.skyCubemap = _settings.skyCubemap;


                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(trace, AccessFlags.ReadWrite);
                if (filtered.IsValid())
                    builder.UseTexture(filtered, AccessFlags.ReadWrite);
                builder.UseTexture(spatial, AccessFlags.ReadWrite);
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderFunc((TraceData pass, UnsafeGraphContext context) =>
                {
                    pass.material.SetVector(Settings.TraceParamsID, pass.traceParams);
                    pass.material.SetFloat(Settings.RoughnessID, pass.roughness);
                    pass.material.SetFloat(Settings.SkyMaxMipID, pass.skyMaxMip);
                    pass.material.SetFloat(Settings.SpatialRadiusID, pass.spatialRadius);
                    pass.material.SetFloat(Settings.SpatialBlurStrengthID, pass.blurStrength);
                    pass.material.SetFloat(Settings.FrameIndexID, pass.frameIndex);
                    pass.material.SetTexture(Settings.SkyCubemapID, pass.skyCubemap);
                    CommandBuffer commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    Blitter.BlitCameraTexture(commandBuffer, pass.source, pass.trace, pass.material, 0);
                    TextureHandle upsampleSource = pass.trace;
                    if (pass.filtered.IsValid())
                    {
                        Blitter.BlitCameraTexture(commandBuffer, pass.trace, pass.filtered, pass.material, 1);
                        upsampleSource = pass.filtered;
                    }
                    Blitter.BlitCameraTexture(commandBuffer, upsampleSource, pass.spatial, pass.material, 2);
                });
            }
            return spatial;
        }

        void RecordComposite(RenderGraph graph, ContextContainer frameData,
            TextureHandle trace, TextureHandle spatial, TextureHandle temporal,
            float intensity, float roughness, Settings.DebugMode debug)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            TextureHandle source = resources.activeColorTexture;
            RenderTextureDescriptor descriptor = frameData.Get<UniversalCameraData>().cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            TextureHandle target = UniversalRenderer.CreateRenderGraphTexture(
                graph, descriptor, "SpecularGI.Composite", false);

            using (var builder = graph.AddUnsafePass<CompositeData>("SpecularGI.Composite", out var data))
            {
                data.material = _material;
                data.source = source;
                data.trace = trace;
                data.spatial = spatial;
                data.temporal = temporal;
                data.target = target;
                data.intensity = Mathf.Clamp01(intensity);
                data.roughness = Mathf.Clamp01(roughness);
                data.debug = debug;

                builder.UseTexture(source, AccessFlags.ReadWrite);
                builder.UseTexture(trace, AccessFlags.Read);
                builder.UseTexture(spatial, AccessFlags.Read);
                builder.UseTexture(temporal, AccessFlags.Read);
                builder.UseTexture(target, AccessFlags.ReadWrite);
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderFunc((CompositeData pass, UnsafeGraphContext context) =>
                {
                    CommandBuffer commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    pass.material.SetFloat(Settings.IntensityID, pass.intensity);
                    pass.material.SetFloat(Settings.RoughnessID, pass.roughness);
                    if (pass.debug == Settings.DebugMode.Off)
                    {
                        commandBuffer.SetGlobalTexture(Settings.SpecularTextureID, pass.temporal);
                        Blitter.BlitCameraTexture(commandBuffer,
                            pass.source, pass.target, pass.material, 3);
                    }
                    else
                    {
                        SetDebugMode(pass.material, pass.debug);
                        TextureHandle selected = pass.debug switch
                        {
                            Settings.DebugMode.ScreenSource => pass.trace,
                            Settings.DebugMode.SkySource => pass.trace,
                            Settings.DebugMode.Trace => pass.trace,
                            Settings.DebugMode.Spatial => pass.spatial,
                            _ => pass.temporal
                        };
                        Blitter.BlitCameraTexture(commandBuffer,
                            selected, pass.target, pass.material, 4);
                    }
                    Blitter.BlitCameraTexture(commandBuffer, pass.target, pass.source);
                });
            }
        }

        public void Release()
        {
            _temporal?.Release();
            CoreUtils.Destroy(_material);
        }

        static void GetQuality(SSGIQuality quality,
            out int downsample, out int stepCount, out float spatialRadius)
        {
            switch (quality)
            {
                case SSGIQuality.Low:
                    downsample = 3;
                    stepCount = 32;
                    spatialRadius = 2f;
                    break;
                case SSGIQuality.High:
                    downsample = 1;
                    stepCount = 96;
                    spatialRadius = 1f;
                    break;
                default:
                    downsample = 2;
                    stepCount = 64;
                    spatialRadius = 2f;
                    break;
            }
        }

        static void SetDebugMode(Material material, Settings.DebugMode debug)
        {
            float mode = debug switch
            {
                Settings.DebugMode.ScreenSource => 1f,
                Settings.DebugMode.SkySource => 2f,
                Settings.DebugMode.HistoryWeight => 3f,
                _ => 0f
            };
            material.SetFloat(Settings.DebugModeID, mode);
        }
    }

    public Settings settings = new();
    public Controls controls = new();
    SpecularGIPass _pass;

    public override void Create()
    {
        _pass?.Release();
        _pass = settings.shader != null
            ? new SpecularGIPass(settings.shader, settings, controls) : null;
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Release();
        _pass = null;
    }

    public override void AddRenderPasses(
        ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass != null)
            renderer.EnqueuePass(_pass);
    }
}
