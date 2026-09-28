// ════════════════════════════════════════════════════════════════
//  SSGITemporalFilter — per-camera shared depth and per-signal color history.
// ════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

internal sealed class SSGITemporalFilter
{
    internal enum Signal { AO, DiffuseGI, SpecularGI }

    sealed class CameraHistory
    {
        public Camera camera;
        public readonly RTHandle[,] color = new RTHandle[3, 2];
        public readonly RTHandle[] depth = new RTHandle[2];
        public readonly bool[] valid = new bool[3];
        public readonly int[] lastSignalFrame = { -1, -1, -1 };
        public readonly int[] lastColorSide = { -1, -1, -1 };
        public readonly bool[] written = new bool[3];
        public int width;
        public int height;
        public int frameIndex;
        public int lastFrame = -1;
        public Matrix4x4 previousViewProjection;
        public Matrix4x4 previousViewMatrix;
        public Matrix4x4 currentViewProjection;
        public Matrix4x4 currentViewMatrix;
        public bool stable;
        public bool hasMotion;

        public void Ensure(int targetWidth, int targetHeight, int cameraId)
        {
            if (width == targetWidth && height == targetHeight && depth[0] != null)
                return;
            Release();
            width = targetWidth;
            height = targetHeight;
            depth[0] = Allocate(width, height, RenderTextureFormat.RHalf, $"SSGI_DepthA_{cameraId}");
            depth[1] = Allocate(width, height, RenderTextureFormat.RHalf, $"SSGI_DepthB_{cameraId}");
        }

        public void EnsureColor(Signal signal, int cameraId)
        {
            int channel = (int)signal;
            if (color[channel, 0] != null)
                return;
            RenderTextureFormat format = signal == Signal.AO
                ? RenderTextureFormat.RHalf : RenderTextureFormat.ARGBHalf;
            color[channel, 0] = Allocate(width, height, format, $"SSGI_{signal}_A_{cameraId}");
            color[channel, 1] = Allocate(width, height, format, $"SSGI_{signal}_B_{cameraId}");
            valid[channel] = false;
        }

        public void Release()
        {
            for (int channel = 0; channel < 3; ++channel)
            {
                for (int side = 0; side < 2; ++side)
                {
                    color[channel, side]?.Release();
                    color[channel, side] = null;
                }
                valid[channel] = false;
                written[channel] = false;
                lastSignalFrame[channel] = -1;
                lastColorSide[channel] = -1;
            }
            for (int side = 0; side < 2; ++side)
            {
                depth[side]?.Release();
                depth[side] = null;
            }
            width = 0;
            height = 0;
            frameIndex = 0;
            lastFrame = -1;
        }
    }

    sealed class ResolveData
    {
        public Material material;
        public TextureHandle current;
        public TextureHandle historyColor;
        public TextureHandle historyDepth;
        public TextureHandle motion;
        public Matrix4x4 previousViewMatrix;
        public float blend;
        public float historyValid;
        public float storeWeight;
    }

    sealed class DepthData
    {
        public Material material;
        public TextureHandle source;
    }

    static readonly int HistoryColorID = Shader.PropertyToID("_SSGIHistoryColor");
    static readonly int HistoryDepthID = Shader.PropertyToID("_SSGIHistoryDepth");
    static readonly int MotionID = Shader.PropertyToID("_SSGIMotionTexture");
    static readonly int PreviousViewID = Shader.PropertyToID("_SSGIPreviousViewMatrix");
    static readonly int BlendID = Shader.PropertyToID("_SSGITemporalBlend");
    static readonly int ValidID = Shader.PropertyToID("_SSGIHistoryValid");
    static readonly int StoreWeightID = Shader.PropertyToID("_SSGIStoreWeight");

    readonly Dictionary<int, CameraHistory> _histories = new();
    readonly Material _material;
    CameraHistory _active;
    int _cameraId;
    TextureHandle _motion;

    public int FrameIndex => _active != null && _active.hasMotion ? _active.frameIndex : 0;

    public SSGITemporalFilter(Shader shader)
    {
        _material = shader != null ? CoreUtils.CreateEngineMaterial(shader) : null;
    }

    public void BeginFrame(ContextContainer frameData)
    {
        _active = null;
        if (_material == null)
            return;

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        UniversalResourceData resources = frameData.Get<UniversalResourceData>();
        Camera camera = cameraData.camera;
        if (camera == null || !resources.cameraDepthTexture.IsValid())
            return;

        ReleaseUnusedHistories();
        _cameraId = camera.GetInstanceID();
        if (!_histories.TryGetValue(_cameraId, out CameraHistory history))
        {
            history = new CameraHistory();
            _histories.Add(_cameraId, history);
        }

        history.camera = camera;
        history.Ensure(
            Mathf.Max(cameraData.cameraTargetDescriptor.width, 1),
            Mathf.Max(cameraData.cameraTargetDescriptor.height, 1),
            _cameraId);
        history.currentViewMatrix = cameraData.GetViewMatrix();
        history.currentViewProjection = GL.GetGPUProjectionMatrix(
            cameraData.GetProjectionMatrix(), true) * history.currentViewMatrix;
        _motion = resources.motionVectorColor;
        history.hasMotion = _motion.IsValid();
        bool consecutive = history.lastFrame >= 0 && Time.frameCount - history.lastFrame <= 2;
        bool stableCamera = MatrixDifference(
            history.previousViewProjection, history.currentViewProjection) < 4f;
        history.stable = consecutive && stableCamera && history.hasMotion;
        for (int channel = 0; channel < 3; ++channel)
            history.written[channel] = false;
        _active = history;
    }

    public TextureHandle Resolve(RenderGraph graph, ContextContainer frameData,
        TextureHandle current, Signal signal, float blend)
    {
        if (_active == null || !current.IsValid())
            return current;

        UniversalResourceData resources = frameData.Get<UniversalResourceData>();
        _active.EnsureColor(signal, _cameraId);
        int channel = (int)signal;
        int readSide = _active.frameIndex & 1;
        int writeSide = readSide ^ 1;
        TextureHandle historyColor = graph.ImportTexture(_active.color[channel, readSide]);
        TextureHandle historyDepth = graph.ImportTexture(_active.depth[readSide]);
        TextureHandle output = graph.ImportTexture(_active.color[channel, writeSide]);
        bool channelConsecutive = _active.lastSignalFrame[channel] >= 0
            && Time.frameCount - _active.lastSignalFrame[channel] <= 2;
        bool historyValid = _active.stable && _active.valid[channel]
            && channelConsecutive && _active.lastColorSide[channel] == readSide;

        using (var builder = graph.AddRasterRenderPass<ResolveData>(
                   $"SSGI.{signal}.Temporal", out var data))
        {
            data.material = _material;
            data.current = current;
            data.historyColor = historyColor;
            data.historyDepth = historyDepth;
            data.motion = _motion.IsValid() ? _motion : current;
            data.previousViewMatrix = _active.previousViewMatrix;
            data.blend = Mathf.Clamp(blend, 0f, 0.98f);
            data.historyValid = historyValid ? 1f : 0f;
            data.storeWeight = signal == Signal.SpecularGI ? 1f : 0f;

            builder.UseTexture(current, AccessFlags.Read);
            builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
            builder.UseTexture(historyColor, AccessFlags.Read);
            builder.UseTexture(historyDepth, AccessFlags.Read);
            if (_motion.IsValid())
                builder.UseTexture(_motion, AccessFlags.Read);
            builder.SetRenderAttachment(output, 0, AccessFlags.Write);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc((ResolveData pass, RasterGraphContext context) =>
            {
                pass.material.SetMatrix(PreviousViewID, pass.previousViewMatrix);
                pass.material.SetFloat(BlendID, pass.blend);
                pass.material.SetFloat(ValidID, pass.historyValid);
                pass.material.SetFloat(StoreWeightID, pass.storeWeight);
                context.cmd.SetGlobalTexture(HistoryColorID, pass.historyColor);
                context.cmd.SetGlobalTexture(HistoryDepthID, pass.historyDepth);
                context.cmd.SetGlobalTexture(MotionID, pass.motion);
                Blitter.BlitTexture(context.cmd, pass.current,
                    new Vector4(1f, 1f, 0f, 0f), pass.material, 0);
            });
        }

        _active.written[channel] = true;
        return output;
    }

    public void CompleteFrame(RenderGraph graph, ContextContainer frameData)
    {
        if (_active == null)
            return;

        bool anyWritten = false;
        for (int channel = 0; channel < 3; ++channel)
            anyWritten |= _active.written[channel];
        if (anyWritten)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            TextureHandle output = graph.ImportTexture(_active.depth[(_active.frameIndex & 1) ^ 1]);
            using (var builder = graph.AddRasterRenderPass<DepthData>(
                       "SSGI.HistoryDepth", out var data))
            {
                data.material = _material;
                data.source = resources.cameraDepthTexture;
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((DepthData pass, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, pass.source,
                        new Vector4(1f, 1f, 0f, 0f), pass.material, 1);
                });
            }
            for (int channel = 0; channel < 3; ++channel)
            {
                if (!_active.written[channel])
                    continue;
                _active.valid[channel] = true;
                _active.lastSignalFrame[channel] = Time.frameCount;
                _active.lastColorSide[channel] = (_active.frameIndex & 1) ^ 1;
            }
            _active.previousViewMatrix = _active.currentViewMatrix;
            _active.previousViewProjection = _active.currentViewProjection;
            _active.lastFrame = Time.frameCount;
            _active.frameIndex++;
        }
        _active = null;
    }

    public void Release()
    {
        foreach (CameraHistory history in _histories.Values)
            history.Release();
        _histories.Clear();
        CoreUtils.Destroy(_material);
        _active = null;
    }

    void ReleaseUnusedHistories()
    {
        var stale = new List<int>();
        foreach (KeyValuePair<int, CameraHistory> entry in _histories)
            if (entry.Value.camera == null)
                stale.Add(entry.Key);
        foreach (int cameraId in stale)
        {
            _histories[cameraId].Release();
            _histories.Remove(cameraId);
        }
    }

    static RTHandle Allocate(int width, int height,
        RenderTextureFormat format, string textureName)
    {
        var texture = new RenderTexture(width, height, 0, format)
        {
            name = textureName,
            filterMode = format == RenderTextureFormat.RHalf
                ? FilterMode.Point : FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.Create();
        return RTHandles.Alloc(texture);
    }

    static float MatrixDifference(Matrix4x4 left, Matrix4x4 right)
    {
        float difference = 0f;
        for (int index = 0; index < 16; ++index)
            difference += Mathf.Abs(left[index] - right[index]);
        return difference;
    }
}
