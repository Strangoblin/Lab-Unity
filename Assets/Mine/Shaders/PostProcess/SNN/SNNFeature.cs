// ════════════════════════════════════════════════════════════
//  SNN Feature — symmetric nearest neighbour fullscreen filter
// ════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 对称最近邻滤波（SNN）：把全屏颜色按四组对称样本对做保边去噪。
/// 采样预算由 Shader 侧编译期常量固定为 4 对，Radius 只缩放采样偏移。
/// </summary>
public sealed class SNNFeature : ScriptableRendererFeature
{
    public enum DebugMode
    {
        Off,
        Selection
    }

    [System.Serializable]
    public sealed class Settings
    {
        [Header("Technical")]
        public Shader snnShader;

        [Header("Artistic")]
        [Range(1, 10)] public int Radius = 3;

        [Header("Debug")]
        [Tooltip("Off = 正常滤波输出；Selection = 只输出被选中的四个邻域均值（不含中心），用于观察滤波取向。")]
        public DebugMode debug = DebugMode.Off;

        internal static readonly int RadiusID    = Shader.PropertyToID("_Radius");
        internal static readonly int DebugModeID = Shader.PropertyToID("_DebugMode");
    }

    sealed class SNNPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle tempRT;
        }

        // Shader Pass 顺序：0 = SNN 滤波
        const int FilterPassIndex = 0;

        readonly Settings _settings;
        readonly Material _material;

        public SNNPass(Settings settings)
        {
            _settings = settings;
            _material = CoreUtils.CreateEngineMaterial(settings.snnShader);
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        // ════════════════════════════════════════════════════════════
        //  RecordRenderGraph — 滤波结果写回相机色，RT 由 RenderGraph 管理
        // ════════════════════════════════════════════════════════════
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || _material == null) return;

            _material.SetInt(Settings.RadiusID, _settings.Radius);
            _material.SetFloat(Settings.DebugModeID, (float)_settings.debug);

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            TextureHandle tempRT = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_SNNTempRT", false);

            using (var builder = renderGraph.AddUnsafePass<PassData>("SNN", out var passData))
            {
                passData.material = _material;
                passData.source = source;
                passData.tempRT = tempRT;

                builder.UseTexture(source, AccessFlags.ReadWrite);
                builder.UseTexture(tempRT, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    Blitter.BlitCameraTexture(cmd, data.source, data.tempRT, data.material, FilterPassIndex);
                    Blitter.BlitCameraTexture(cmd, data.tempRT, data.source);
                });
            }
        }

        public void Release()
        {
            CoreUtils.Destroy(_material);
        }
    }

    public Settings settings = new Settings();
    SNNPass snnPass;

    // ════════════════════════════════════════════════════════════
    //  生命周期 — 材质随 Pass 一起重建与释放
    // ════════════════════════════════════════════════════════════
    public override void Create()
    {
        snnPass?.Release();
        snnPass = settings.snnShader != null ? new SNNPass(settings) : null;
    }

    protected override void Dispose(bool disposing)
    {
        snnPass?.Release();
        snnPass = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (snnPass == null) return;
        renderer.EnqueuePass(snnPass);
    }
}
