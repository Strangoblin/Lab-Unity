// ════════════════════════════════════════════════════════════
//  ScreenWarp — 只重映射最终场景颜色，不修改相机与几何缓冲。
// ════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Mine.ScreenWarp
{
    /// <summary>在主后处理之后，以一次颜色采样实现独立屏幕变形。</summary>
    public sealed class ScreenWarpFeature : ScriptableRendererFeature
    {
        public enum WarpMode { Barrel, Pincushion, Fisheye, Panini }
        public enum BorderMode { Clamp, Color }
        public enum DebugMode { Off, SourceUV, OutOfBounds }

        [System.Serializable]
        public sealed class Settings
        {
            [Header("Technical")]
            public Shader shader;
            public bool showInSceneView;
            public BorderMode border = BorderMode.Color;
            [ColorUsage(true, true)] public Color borderColor = Color.black;

            [Header("Artistic")]
            public WarpMode mode = WarpMode.Barrel;
            [Tooltip("0 = 完全关闭。连续插值采样坐标，包含 Zoom 的影响。")]
            [Range(0f, 1f)] public float strength = 0.35f;
            [Tooltip("归一化画面坐标；不移动或修改 Camera。")]
            public Vector2 center = new Vector2(0.5f, 0.5f);
            [Tooltip("裁切已有画面以减少露边，不会生成视野之外的内容。")]
            [Range(1f, 2f)] public float zoom = 1f;

            [Header("Debug")]
            public DebugMode debug = DebugMode.Off;

            internal static readonly int ParametersID = Shader.PropertyToID("_WarpParameters");
            internal static readonly int CenterID = Shader.PropertyToID("_WarpCenter");
            internal static readonly int BorderColorID = Shader.PropertyToID("_WarpBorderColor");
            internal static readonly int SourceSizeID = Shader.PropertyToID("_WarpSourceSize");
            internal static readonly int DebugID = Shader.PropertyToID("_WarpDebug");
        }

        readonly struct Parameters
        {
            public readonly Vector4 warp;
            public readonly Vector4 center;
            public readonly Color borderColor;
            public readonly DebugMode debug;

            public Parameters(Settings settings)
            {
                int mode = Mathf.Clamp((int)settings.mode, 0, 3);
                warp = new Vector4(mode, Mathf.Clamp01(Finite(settings.strength, 0f)),
                    Mathf.Clamp(Finite(settings.zoom, 1f), 1f, 2f),
                    settings.border == BorderMode.Color ? 1f : 0f);
                center = new Vector4(Mathf.Clamp01(Finite(settings.center.x, 0.5f)),
                    Mathf.Clamp01(Finite(settings.center.y, 0.5f)), 0f, 0f);
                Color color = settings.borderColor;
                borderColor = new Color(Finite(color.r, 0f), Finite(color.g, 0f),
                    Finite(color.b, 0f), Finite(color.a, 1f));
                debug = (DebugMode)Mathf.Clamp((int)settings.debug, 0, 2);
            }

            public bool Active => warp.y > 0f || debug != DebugMode.Off;

            static float Finite(float value, float fallback)
            {
                return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
            }
        }

        sealed class ScreenWarpPass : ScriptableRenderPass
        {
            sealed class PassData
            {
                public Material material;
                public TextureHandle source;
                public Vector4 fallbackSize;
                public Parameters parameters;
                public int passIndex;
            }

            readonly Material _material;
            readonly int _passIndex;
            Parameters _parameters;

            public ScreenWarpPass(Material material)
            {
                _material = material;
                _passIndex = material.FindPass("ScreenWarp");
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.None);
            }

            public bool Valid => _passIndex >= 0;
            public void Prepare(Parameters parameters) => _parameters = parameters;

            // ════════════════════════════════════════════════════════
            //  RecordRenderGraph — 单次颜色重映射，新输出交回URP继续最终解析。
            // ════════════════════════════════════════════════════════
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (!_parameters.Active || resources.isActiveTargetBackBuffer)
                    return;
                TextureHandle source = resources.activeColorTexture;
                if (!source.IsValid())
                    return;

                TextureDesc descriptor = graph.GetTextureDesc(source);
                Vector2Int size = descriptor.CalculateFinalDimensions();
                descriptor.name = "ScreenWarp.Color";
                descriptor.depthBufferBits = DepthBits.None;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.bindTextureMS = false;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;
                descriptor.clearBuffer = false;
                TextureHandle output = graph.CreateTexture(descriptor);

                using (var builder = graph.AddRasterRenderPass<PassData>("ScreenWarp", out var data))
                {
                    data.material = _material;
                    data.source = source;
                    data.fallbackSize = new Vector4(size.x, size.y, 1f / Mathf.Max(size.x, 1), 1f / Mathf.Max(size.y, 1));
                    data.parameters = _parameters;
                    data.passIndex = _passIndex;
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                    builder.SetRenderFunc((PassData pass, RasterGraphContext context) =>
                    {
                        SourceGeometry(pass.source, pass.fallbackSize, out Vector4 sourceSize, out Vector4 scaleBias);
                        pass.material.SetVector(Settings.ParametersID, pass.parameters.warp);
                        pass.material.SetVector(Settings.CenterID, pass.parameters.center);
                        pass.material.SetColor(Settings.BorderColorID, pass.parameters.borderColor);
                        pass.material.SetVector(Settings.SourceSizeID, sourceSize);
                        pass.material.SetFloat(Settings.DebugID, (float)pass.parameters.debug);
                        Blitter.BlitTexture(context.cmd, pass.source, scaleBias, pass.material, pass.passIndex);
                    });
                }
                resources.cameraColor = output;
            }

            // ════════════════════════════════════════════════════════
            //  SourceGeometry — 取当前有效视口，保持动态RT区域与等比坐标一致。
            // ════════════════════════════════════════════════════════
            static void SourceGeometry(TextureHandle source, Vector4 fallback, out Vector4 sourceSize, out Vector4 scaleBias)
            {
                RTHandle handle = source;
                Vector2Int size = handle.useScaling
                    ? handle.GetScaledSize(handle.rtHandleProperties.currentViewportSize)
                    : handle.rt != null ? new Vector2Int(handle.rt.width, handle.rt.height)
                    : new Vector2Int((int)fallback.x, (int)fallback.y);
                if (handle.rt != null && handle.rt.useDynamicScale)
                {
                    size.x = Mathf.Max(1, Mathf.RoundToInt(size.x * ScalableBufferManager.widthScaleFactor));
                    size.y = Mathf.Max(1, Mathf.RoundToInt(size.y * ScalableBufferManager.heightScaleFactor));
                }
                sourceSize = new Vector4(size.x, size.y, 1f / Mathf.Max(size.x, 1), 1f / Mathf.Max(size.y, 1));
                Vector4 scale = handle.useScaling ? handle.rtHandleProperties.rtHandleScale : Vector4.one;
                scaleBias = new Vector4(scale.x, scale.y, 0f, 0f);
            }
        }

        public Settings settings = new Settings();
        Material _material;
        ScreenWarpPass _pass;
        Shader _loadedShader;
        bool _reportedMissingShader;

        // ════════════════════════════════════════════════════════════
        //  生命周期 — 只拥有运行材质，不持有RT或修改相机状态。
        // ════════════════════════════════════════════════════════════
        public override void Create()
        {
            Release();
            EnsureMaterial();
        }

        protected override void Dispose(bool disposing) => Release();

        void Release()
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _pass = null;
            _loadedShader = null;
        }

        bool EnsureMaterial()
        {
            if (settings == null)
                return false;
            if (settings.shader == null)
                settings.shader = Shader.Find("PostProcess/ScreenWarp");
            if (settings.shader == null || !settings.shader.isSupported)
            {
                Release();
                if (!_reportedMissingShader)
                    Debug.LogWarning("[ScreenWarp] 缺少受支持的 PostProcess/ScreenWarp Shader。", this);
                _reportedMissingShader = true;
                return false;
            }
            if (_material != null && _loadedShader == settings.shader)
                return _pass != null && _pass.Valid;
            Release();
            _loadedShader = settings.shader;
            _material = CoreUtils.CreateEngineMaterial(_loadedShader);
            _material.name = "ScreenWarp.Runtime";
            _pass = new ScreenWarpPass(_material);
            _reportedMissingShader = false;
            return _pass.Valid;
        }

        // ════════════════════════════════════════════════════════════
        //  AddRenderPasses — 无效果时不请求输入、不入队；只支持最终Base相机。
        // ════════════════════════════════════════════════════════════
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings == null)
                return;
            CameraData camera = renderingData.cameraData;
            if (camera.renderType == CameraRenderType.Overlay || !camera.resolveFinalTarget
                || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
                || (camera.isSceneViewCamera && !settings.showInSceneView)
                || camera.camera.stereoEnabled)
                return;
            Parameters parameters = new Parameters(settings);
            if (!parameters.Active || !EnsureMaterial())
                return;
            _pass.Prepare(parameters);
            renderer.EnqueuePass(_pass);
        }
    }
}
