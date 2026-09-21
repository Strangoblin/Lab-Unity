// ═══════════════════════════════════════════════════════════════
//  SpecularGI Feature — SSSR trace with SSPR and cubemap fallbacks.
// ═══════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class SpecularGIFeature : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        public enum QualityLevel
        {
            Low,
            Medium,
            High
        }

        public enum DebugMode
        {
            Off,
            ScreenSource,
            PlanarSource,
            SkySource,
            Trace,
            Spatial,
            Temporal,
            HistoryWeight
        }

        [Header("Resources")]
        public Shader shader;
        public Cubemap skyCubemap;

        [Header("Technical")]
        [Range(1f, 200f)] public float maxDistance = 50f;
        [Range(0.001f, 0.5f)] public float thickness = 0.05f;
        [Range(0f, 0.2f)] public float normalBias = 0.03f;

        [Header("Performance")]
        public QualityLevel quality = QualityLevel.Medium;

        [Header("Artistic")]
        [Range(0f, 1f)] public float roughness = 0.25f;
        [Range(0f, 1f)] public float intensity = 1f;
        [Range(0f, 1f)] public float planarStrength = 1f;
        [Range(0.8f, 1f)] public float planarThreshold = 0.95f;
        [Range(0f, 200f)] public float planarFadeStart = 15f;
        [Range(0f, 200f)] public float planarFadeEnd = 50f;
        [Range(0f, 12f)] public float skyMaxMip = 6f;
        [Range(0f, 0.98f)] public float temporalBlend = 0.95f;

        [Header("Debug")]
        public DebugMode debug = DebugMode.Off;

        internal static readonly int TraceParamsID = Shader.PropertyToID("_TraceParams");
        internal static readonly int PlanarParamsID = Shader.PropertyToID("_PlanarParams");
        internal static readonly int RoughnessID = Shader.PropertyToID("_Roughness");
        internal static readonly int IntensityID = Shader.PropertyToID("_Intensity");
        internal static readonly int SkyCubemapID = Shader.PropertyToID("_SkyCubemap");
        internal static readonly int SkyMaxMipID = Shader.PropertyToID("_SkyMaxMip");
        internal static readonly int SpatialRadiusID = Shader.PropertyToID("_SpatialRadius");
        internal static readonly int TemporalBlendID = Shader.PropertyToID("_TemporalBlend");
        internal static readonly int FrameIndexID = Shader.PropertyToID("_FrameIndex");
        internal static readonly int HistoryValidID = Shader.PropertyToID("_HistoryValid");
        internal static readonly int HasMotionVectorsID = Shader.PropertyToID("_HasMotionVectors");
        internal static readonly int DebugModeID = Shader.PropertyToID("_DebugMode");
        internal static readonly int CameraViewMatrixID = Shader.PropertyToID("_CameraViewMatrix");
        internal static readonly int CameraProjectionMatrixID = Shader.PropertyToID("_CameraProjectionMatrix");
        internal static readonly int PreviousViewMatrixID = Shader.PropertyToID("_PreviousViewMatrix");
        internal static readonly int HistoryColorID = Shader.PropertyToID("_SpecularHistoryColor");
        internal static readonly int HistoryDepthID = Shader.PropertyToID("_SpecularHistoryDepth");
        internal static readonly int MotionTextureID = Shader.PropertyToID("_SpecularMotionTexture");
        internal static readonly int SpecularTextureID = Shader.PropertyToID("_SpecularGITexture");
    }

    sealed class CameraHistory
    {
        public RTHandle colorA;
        public RTHandle colorB;
        public RTHandle depthA;
        public RTHandle depthB;
        public int frameIndex;
        public int lastRenderedFrame = -1;
        public Camera camera;
        public Matrix4x4 previousViewProjection;
        public Matrix4x4 previousViewMatrix;
        public bool valid;

        public void Ensure(int width, int height, int cameraId)
        {
            if (colorA != null && colorA.rt != null
                && colorA.rt.width == width && colorA.rt.height == height)
                return;

            Release();
            colorA = Allocate(width, height, RenderTextureFormat.ARGBHalf, $"SpecularGI_ColorA_{cameraId}");
            colorB = Allocate(width, height, RenderTextureFormat.ARGBHalf, $"SpecularGI_ColorB_{cameraId}");
            depthA = Allocate(width, height, RenderTextureFormat.RHalf, $"SpecularGI_DepthA_{cameraId}");
            depthB = Allocate(width, height, RenderTextureFormat.RHalf, $"SpecularGI_DepthB_{cameraId}");
            frameIndex = 0;
            valid = false;
        }

        public bool BeginFrame(Matrix4x4 viewProjection, Matrix4x4 viewMatrix)
        {
            bool consecutive = lastRenderedFrame < 0 || Time.frameCount - lastRenderedFrame <= 2;
            bool stableCamera = !valid || MatrixDifference(previousViewProjection, viewProjection) < 4f;
            bool canReuse = valid && consecutive && stableCamera;
            previousViewProjection = viewProjection;
            previousViewMatrix = viewMatrix;
            lastRenderedFrame = Time.frameCount;
            return canReuse;
        }

        public void CompleteFrame()
        {
            frameIndex++;
            valid = true;
        }

        public void Release()
        {
            colorA?.Release();
            colorB?.Release();
            depthA?.Release();
            depthB?.Release();
            colorA = null;
            colorB = null;
            depthA = null;
            depthB = null;
            valid = false;
        }

        static RTHandle Allocate(
            int width,
            int height,
            RenderTextureFormat format,
            string textureName)
        {
            var texture = new RenderTexture(width, height, 0, format)
            {
                name = textureName,
                filterMode = format == RenderTextureFormat.RHalf
                    ? FilterMode.Point
                    : FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.Create();
            return RTHandles.Alloc(texture);
        }

        static float MatrixDifference(Matrix4x4 left, Matrix4x4 right)
        {
            float difference = 0f;
            for (int element = 0; element < 16; ++element)
                difference += Mathf.Abs(left[element] - right[element]);
            return difference;
        }
    }

    sealed class SpecularGIPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle trace;
            public TextureHandle spatial;
            public TextureHandle temporal;
            public TextureHandle currentDepth;
            public TextureHandle composite;
            public TextureHandle motion;
            public TextureHandle historyColorRead;
            public TextureHandle historyDepthRead;
            public TextureHandle historyColorWrite;
            public TextureHandle historyDepthWrite;
            public RTHandle historyColorWriteHandle;
            public RTHandle historyDepthWriteHandle;
            public Settings.DebugMode debug;
            public bool hasMotion;
        }

        readonly Settings _settings;
        readonly Material _material;
        readonly Dictionary<int, CameraHistory> _histories = new();

        public SpecularGIPass(Shader shader, Settings settings)
        {
            _settings = settings;
            _material = CoreUtils.CreateEngineMaterial(shader);
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            ConfigureInput(
                ScriptableRenderPassInput.Color
                | ScriptableRenderPassInput.Depth
                | ScriptableRenderPassInput.Normal
                | ScriptableRenderPassInput.Motion);
        }

        public override void RecordRenderGraph(
            RenderGraph renderGraph,
            ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || _material == null || cameraData.camera == null)
                return;

            int width = Mathf.Max(cameraData.cameraTargetDescriptor.width, 1);
            int height = Mathf.Max(cameraData.cameraTargetDescriptor.height, 1);
            ReleaseUnusedHistories();
            int cameraId = cameraData.camera.GetInstanceID();
            if (!_histories.TryGetValue(cameraId, out CameraHistory history))
            {
                history = new CameraHistory();
                _histories.Add(cameraId, history);
            }

            history.camera = cameraData.camera;
            history.Ensure(width, height, cameraId);
            Matrix4x4 viewMatrix = cameraData.GetViewMatrix();
            Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(
                cameraData.GetProjectionMatrix(),
                true);
            TextureHandle motion = resourceData.motionVectorColor;
            bool hasMotion = motion.IsValid();
            Matrix4x4 previousViewMatrix = history.previousViewMatrix;
            bool historyValid = history.BeginFrame(
                projectionMatrix * viewMatrix,
                viewMatrix) && hasMotion;

            RTHandle historyColorRead = history.frameIndex % 2 == 0 ? history.colorA : history.colorB;
            RTHandle historyColorWrite = history.frameIndex % 2 == 0 ? history.colorB : history.colorA;
            RTHandle historyDepthRead = history.frameIndex % 2 == 0 ? history.depthA : history.depthB;
            RTHandle historyDepthWrite = history.frameIndex % 2 == 0 ? history.depthB : history.depthA;

            GetQuality(_settings.quality, out int downsample, out int stepCount, out float spatialRadius);
            var colorDescriptor = cameraData.cameraTargetDescriptor;
            colorDescriptor.depthBufferBits = 0;
            colorDescriptor.msaaSamples = 1;
            colorDescriptor.colorFormat = RenderTextureFormat.ARGBHalf;

            var traceDescriptor = colorDescriptor;
            traceDescriptor.width = Mathf.Max(width >> downsample, 1);
            traceDescriptor.height = Mathf.Max(height >> downsample, 1);
            TextureHandle trace = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                traceDescriptor,
                "_SpecularGI_Trace",
                false);
            TextureHandle spatial = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                colorDescriptor,
                "_SpecularGI_Spatial",
                false);
            TextureHandle temporal = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                colorDescriptor,
                "_SpecularGI_Temporal",
                false);
            TextureHandle composite = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                colorDescriptor,
                "_SpecularGI_Composite",
                false);

            var depthDescriptor = colorDescriptor;
            depthDescriptor.colorFormat = RenderTextureFormat.RHalf;
            TextureHandle currentDepth = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                depthDescriptor,
                "_SpecularGI_CurrentDepth",
                false);

            TextureHandle historyColorReadTexture = renderGraph.ImportTexture(historyColorRead);
            TextureHandle historyDepthReadTexture = renderGraph.ImportTexture(historyDepthRead);
            TextureHandle historyColorWriteTexture = renderGraph.ImportTexture(historyColorWrite);
            TextureHandle historyDepthWriteTexture = renderGraph.ImportTexture(historyDepthWrite);

            ApplyMaterialParameters(
                cameraData,
                history,
                historyValid,
                previousViewMatrix,
                stepCount,
                spatialRadius,
                hasMotion);

            using (var builder = renderGraph.AddUnsafePass<PassData>(
                       "SpecularGI",
                       out var passData))
            {
                passData.material = _material;
                passData.source = source;
                passData.trace = trace;
                passData.spatial = spatial;
                passData.temporal = temporal;
                passData.currentDepth = currentDepth;
                passData.composite = composite;
                passData.motion = motion;
                passData.historyColorRead = historyColorReadTexture;
                passData.historyDepthRead = historyDepthReadTexture;
                passData.historyColorWrite = historyColorWriteTexture;
                passData.historyDepthWrite = historyDepthWriteTexture;
                passData.historyColorWriteHandle = historyColorWrite;
                passData.historyDepthWriteHandle = historyDepthWrite;
                passData.debug = _settings.debug;
                passData.hasMotion = hasMotion;

                builder.UseTexture(source, AccessFlags.ReadWrite);
                builder.UseTexture(trace, AccessFlags.ReadWrite);
                builder.UseTexture(spatial, AccessFlags.ReadWrite);
                builder.UseTexture(temporal, AccessFlags.ReadWrite);
                builder.UseTexture(currentDepth, AccessFlags.ReadWrite);
                builder.UseTexture(composite, AccessFlags.ReadWrite);
                builder.UseTexture(historyColorReadTexture, AccessFlags.Read);
                builder.UseTexture(historyDepthReadTexture, AccessFlags.Read);
                builder.UseTexture(historyColorWriteTexture, AccessFlags.Write);
                builder.UseTexture(historyDepthWriteTexture, AccessFlags.Write);
                builder.UseAllGlobalTextures(true);
                if (hasMotion)
                    builder.UseTexture(motion, AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    Blitter.BlitCameraTexture(commandBuffer, data.source, data.trace, data.material, 0);
                    Blitter.BlitCameraTexture(commandBuffer, data.trace, data.spatial, data.material, 1);

                    commandBuffer.SetGlobalTexture(Settings.HistoryColorID, data.historyColorRead);
                    commandBuffer.SetGlobalTexture(Settings.HistoryDepthID, data.historyDepthRead);
                    if (data.hasMotion)
                        commandBuffer.SetGlobalTexture(Settings.MotionTextureID, data.motion);
                    Blitter.BlitCameraTexture(commandBuffer, data.spatial, data.temporal, data.material, 2);
                    Blitter.BlitCameraTexture(commandBuffer, data.source, data.currentDepth, data.material, 3);

                    commandBuffer.CopyTexture(data.temporal, data.historyColorWriteHandle.nameID);
                    commandBuffer.CopyTexture(data.currentDepth, data.historyDepthWriteHandle.nameID);

                    TextureHandle output = SelectDebugTexture(data);
                    if (data.debug == Settings.DebugMode.Off)
                    {
                        commandBuffer.SetGlobalTexture(Settings.SpecularTextureID, data.temporal);
                        Blitter.BlitCameraTexture(commandBuffer, data.source, data.composite, data.material, 4);
                    }
                    else
                    {
                        SetDebugMode(data.material, data.debug);
                        Blitter.BlitCameraTexture(commandBuffer, output, data.composite, data.material, 5);
                    }

                    Blitter.BlitCameraTexture(commandBuffer, data.composite, data.source);
                });
            }

            history.CompleteFrame();
        }

        public void Release()
        {
            foreach (CameraHistory history in _histories.Values)
                history.Release();
            _histories.Clear();
            CoreUtils.Destroy(_material);
        }

        void ApplyMaterialParameters(
            UniversalCameraData cameraData,
            CameraHistory history,
            bool historyValid,
            Matrix4x4 previousViewMatrix,
            int stepCount,
            float spatialRadius,
            bool hasMotion)
        {
            _material.SetVector(
                Settings.TraceParamsID,
                new Vector4(
                    Mathf.Max(_settings.maxDistance, 0.001f),
                    Mathf.Max(_settings.thickness, 0.0001f),
                    Mathf.Max(_settings.normalBias, 0f),
                    stepCount));
            _material.SetVector(
                Settings.PlanarParamsID,
                new Vector4(
                    Mathf.Clamp(_settings.planarThreshold, 0.8f, 0.9999f),
                    Mathf.Max(_settings.planarFadeStart, 0f),
                    Mathf.Max(_settings.planarFadeEnd, _settings.planarFadeStart),
                    Mathf.Clamp01(_settings.planarStrength)));
            _material.SetFloat(Settings.RoughnessID, Mathf.Clamp01(_settings.roughness));
            _material.SetFloat(Settings.IntensityID, Mathf.Clamp01(_settings.intensity));
            _material.SetFloat(Settings.SkyMaxMipID, Mathf.Max(_settings.skyMaxMip, 0f));
            _material.SetFloat(Settings.SpatialRadiusID, spatialRadius);
            _material.SetFloat(Settings.TemporalBlendID, Mathf.Clamp(_settings.temporalBlend, 0f, 0.98f));
            _material.SetFloat(Settings.FrameIndexID, history.frameIndex);
            _material.SetFloat(Settings.HistoryValidID, historyValid ? 1f : 0f);
            _material.SetFloat(Settings.HasMotionVectorsID, hasMotion ? 1f : 0f);
            _material.SetMatrix(Settings.CameraViewMatrixID, cameraData.GetViewMatrix());
            _material.SetMatrix(
                Settings.CameraProjectionMatrixID,
                GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true));
            _material.SetMatrix(Settings.PreviousViewMatrixID, previousViewMatrix);
            _material.SetTexture(Settings.SkyCubemapID, _settings.skyCubemap);
        }

        void ReleaseUnusedHistories()
        {
            var staleCameraIds = new List<int>();
            foreach (KeyValuePair<int, CameraHistory> pair in _histories)
            {
                if (pair.Value.camera == null)
                    staleCameraIds.Add(pair.Key);
            }

            foreach (int cameraId in staleCameraIds)
            {
                _histories[cameraId].Release();
                _histories.Remove(cameraId);
            }
        }

        static void GetQuality(
            Settings.QualityLevel quality,
            out int downsample,
            out int stepCount,
            out float spatialRadius)
        {
            switch (quality)
            {
                case Settings.QualityLevel.Low:
                    downsample = 3;
                    stepCount = 32;
                    spatialRadius = 2f;
                    break;
                case Settings.QualityLevel.High:
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

        static TextureHandle SelectDebugTexture(PassData data)
        {
            switch (data.debug)
            {
                case Settings.DebugMode.Trace:
                case Settings.DebugMode.ScreenSource:
                case Settings.DebugMode.PlanarSource:
                case Settings.DebugMode.SkySource:
                    return data.trace;
                case Settings.DebugMode.Spatial:
                    return data.spatial;
                default:
                    return data.temporal;
            }
        }

        static void SetDebugMode(Material material, Settings.DebugMode debug)
        {
            float mode = debug switch
            {
                Settings.DebugMode.ScreenSource => 1f,
                Settings.DebugMode.PlanarSource => 2f,
                Settings.DebugMode.SkySource => 3f,
                Settings.DebugMode.HistoryWeight => 4f,
                _ => 0f
            };
            material.SetFloat(Settings.DebugModeID, mode);
        }
    }

    public Settings settings = new();
    SpecularGIPass _pass;

    public override void Create()
    {
        _pass?.Release();
        _pass = settings.shader != null
            ? new SpecularGIPass(settings.shader, settings)
            : null;
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Release();
        _pass = null;
    }

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        if (_pass != null)
            renderer.EnqueuePass(_pass);
    }
}
