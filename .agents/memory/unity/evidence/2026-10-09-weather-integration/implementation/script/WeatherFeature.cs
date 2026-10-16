// ════════════════════════════════════════════════════════════════
//  Weather Feature — 单预设粒子与氛围，按实际模块需求录制 RenderGraph。
// ════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Mine.Weather
{
    /// <summary>以独立运行材质调度天气粒子和氛围，不改写预设与旧材质。</summary>
    public sealed class WeatherFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Resources")]
            public Shader shader;
            public Texture2D noiseTexture;
            public WeatherProfile profile;

            [Header("Technical · Cameras")]
            public bool showInSceneView = true;

            [Header("Debug")]
            public WeatherDebugMode debug = WeatherDebugMode.Off;

            internal static readonly int NoiseID = Shader.PropertyToID("_WeatherNoiseTex");
            internal static readonly int RandomnessID = Shader.PropertyToID("_Randomness");
            internal static readonly int CycleRateID = Shader.PropertyToID("_CycleRate");
            internal static readonly int ParticleShapeID = Shader.PropertyToID("_ParticleShape");
            internal static readonly int ParticleOpacityID = Shader.PropertyToID("_ParticleOpacity");
            internal static readonly int RefractionScaleID = Shader.PropertyToID("_RefractionScale");
            internal static readonly int LensStrengthID = Shader.PropertyToID("_LensStrength");
            internal static readonly int EdgeBaseWidthID = Shader.PropertyToID("_EdgeBaseWidth");
            internal static readonly int EdgeNoiseWidthID = Shader.PropertyToID("_EdgeNoiseWidth");
            internal static readonly int FogStrengthID = Shader.PropertyToID("_FogStrength");
            internal static readonly int FogBaseDistanceID = Shader.PropertyToID("_FogBaseDistance");
            internal static readonly int FogNoiseDistanceID = Shader.PropertyToID("_FogNoiseDistance");
            internal static readonly int FogNoiseScrollXID = Shader.PropertyToID("_FogNoiseScrollX");
            internal static readonly int ToneStrengthID = Shader.PropertyToID("_ToneStrength");
            internal static readonly int ToneTintID = Shader.PropertyToID("_ToneTint");
            internal static readonly int LegacyAlphaID = Shader.PropertyToID("_LegacySnowAlpha");
            internal static readonly int DebugID = Shader.PropertyToID("_WeatherDebug");
            internal static readonly int TargetSizeID = Shader.PropertyToID("_WeatherTargetSize");
            internal static readonly int TimeID = Shader.PropertyToID("_WeatherTime");
        }

        sealed class WeatherPass : ScriptableRenderPass
        {
            sealed class ParticleData
            {
                public Material material;
                public Texture2D noise;
                public TextureHandle source;
                public ResolvedWeatherSettings parameters;
                public WeatherDebugMode debug;
                public Vector4 targetSize;
                public float time;
                public int passIndex;
            }

            sealed class AtmosphereData
            {
                public Material material;
                public Texture2D noise;
                public TextureHandle source;
                public TextureHandle depth;
                public ResolvedWeatherSettings parameters;
                public WeatherDebugMode debug;
                public Vector4 targetSize;
                public float time;
                public int passIndex;
            }

            readonly Material _material;
            readonly Action<string, string> _report;
            readonly int _particleIndex;
            readonly int _atmosphereIndex;
            ResolvedWeatherSettings _parameters;
            WeatherDebugMode _debug;
            Texture2D _noise;

            public WeatherPass(Material material, Action<string, string> report)
            {
                _material = material;
                _report = report;
                _particleIndex = material.FindPass("Particles");
                _atmosphereIndex = material.FindPass("Atmosphere");
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }

            public bool HasExpectedPasses => _particleIndex >= 0 && _atmosphereIndex >= 0;

            public void Prepare(ResolvedWeatherSettings parameters, Texture2D noise, WeatherDebugMode debug)
            {
                _parameters = parameters;
                _noise = noise;
                _debug = debug;
                ConfigureInput(NeedsDepth(parameters, debug)
                    ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
            }

            static bool NeedsDepth(ResolvedWeatherSettings parameters, WeatherDebugMode debug)
            {
                if (debug == WeatherDebugMode.Fog)
                    return true;
                if (debug == WeatherDebugMode.Particles || debug == WeatherDebugMode.Refraction
                    || debug == WeatherDebugMode.Lens)
                    return false;
                return parameters.FogActive;
            }

            // ════════════════════════════════════════════════════════
            //  RecordRenderGraph — 只录制活动阶段，最终颜色留给同相机输出。
            // ════════════════════════════════════════════════════════
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid())
                {
                    _report("color", "相机颜色不可采样，跳过当前相机。请检查中间颜色目标配置。");
                    return;
                }

                ResolvedWeatherSettings parameters = _parameters;
                WeatherDebugMode debug = _debug;
                bool needsDepth = NeedsDepth(parameters, debug);
                TextureHandle depth = needsDepth ? resources.cameraDepthTexture : TextureHandle.nullHandle;
                if (needsDepth && !depth.IsValid())
                {
                    _report("depth", "相机深度不可用，仅关闭雾模块；其它天气模块继续执行。");
                    parameters.FogStrength = 0f;
                    if (debug == WeatherDebugMode.Fog)
                        debug = WeatherDebugMode.Off;
                }

                bool particleDebug = debug == WeatherDebugMode.Particles || debug == WeatherDebugMode.Refraction;
                bool atmosphereDebug = debug == WeatherDebugMode.Lens || debug == WeatherDebugMode.Fog;
                bool particles = (!atmosphereDebug && parameters.ParticlesActive) || particleDebug;
                bool atmosphere = !particleDebug && (parameters.AtmosphereActive
                    || debug == WeatherDebugMode.Lens || debug == WeatherDebugMode.Fog
                    || debug == WeatherDebugMode.Final);
                if (!particles && !atmosphere)
                    return;

                TextureHandle source = resources.activeColorTexture;
                TextureDesc descriptor = graph.GetTextureDesc(source);
                Vector2Int dimensions = descriptor.CalculateFinalDimensions();
                Vector4 targetSize = new Vector4(dimensions.x, dimensions.y,
                    1f / Mathf.Max(dimensions.x, 1), 1f / Mathf.Max(dimensions.y, 1));
                float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
                descriptor.depthBufferBits = DepthBits.None;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.bindTextureMS = false;
                descriptor.clearBuffer = false;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;

                if (particles)
                {
                    descriptor.name = "Weather.Particles";
                    TextureHandle output = graph.CreateTexture(descriptor);
                    using (var builder = graph.AddRasterRenderPass<ParticleData>("Weather.Particles", out var data))
                    {
                        data.material = _material;
                        data.noise = _noise;
                        data.source = source;
                        data.parameters = parameters;
                        data.debug = particleDebug ? debug : WeatherDebugMode.Off;
                        data.targetSize = targetSize;
                        data.time = time;
                        data.passIndex = _particleIndex;
                        builder.UseTexture(source, AccessFlags.Read);
                        builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                        builder.AllowGlobalStateModification(true);
                        builder.SetRenderFunc((ParticleData pass, RasterGraphContext context) =>
                        {
                            SourceGeometry(pass.source, pass.targetSize, out Vector4 size, out Vector4 scaleBias);
                            Bind(pass.material, pass.noise, pass.parameters, pass.debug, size, pass.time);
                            Blitter.BlitTexture(context.cmd, pass.source, scaleBias,
                                pass.material, pass.passIndex);
                        });
                    }
                    source = output;
                }

                if (atmosphere)
                {
                    descriptor.name = "Weather.Atmosphere";
                    TextureHandle output = graph.CreateTexture(descriptor);
                    using (var builder = graph.AddRasterRenderPass<AtmosphereData>("Weather.Atmosphere", out var data))
                    {
                        data.material = _material;
                        data.noise = _noise;
                        data.source = source;
                        data.depth = depth;
                        data.parameters = parameters;
                        data.debug = debug;
                        data.targetSize = targetSize;
                        data.time = time;
                        data.passIndex = _atmosphereIndex;
                        builder.UseTexture(source, AccessFlags.Read);
                        if (depth.IsValid())
                            builder.UseTexture(depth, AccessFlags.Read);
                        builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                        builder.AllowGlobalStateModification(true);
                        builder.SetRenderFunc((AtmosphereData pass, RasterGraphContext context) =>
                        {
                            SourceGeometry(pass.source, pass.targetSize, out Vector4 size, out Vector4 scaleBias);
                            Bind(pass.material, pass.noise, pass.parameters, pass.debug, size, pass.time);
                            Blitter.BlitTexture(context.cmd, pass.source, scaleBias,
                                pass.material, pass.passIndex);
                        });
                    }
                    source = output;
                }

                resources.cameraColor = source;
            }

            // ════════════════════════════════════════════════════════
            //  SourceGeometry — 分开当前viewport、物理尺寸与源纹理取样区域。
            // ════════════════════════════════════════════════════════
            static void SourceGeometry(TextureHandle source, Vector4 fallback,
                out Vector4 targetSize, out Vector4 scaleBias)
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
                targetSize = new Vector4(size.x, size.y, 1f / Mathf.Max(size.x, 1), 1f / Mathf.Max(size.y, 1));
                Vector4 scale = handle.useScaling ? handle.rtHandleProperties.rtHandleScale : Vector4.one;
                scaleBias = new Vector4(scale.x, scale.y, 0f, 0f);
            }

            // ════════════════════════════════════════════════════════
            //  Bind — 在实际绘制回调中应用值快照，避免延迟录图串用参数。
            // ════════════════════════════════════════════════════════
            static void Bind(Material material, Texture2D noise, ResolvedWeatherSettings p,
                WeatherDebugMode debug, Vector4 targetSize, float time)
            {
                CoreUtils.SetKeyword(material, "WEATHER_RAIN", p.Kind == WeatherKind.Rain);
                CoreUtils.SetKeyword(material, "WEATHER_SAND", p.Kind == WeatherKind.Sand);
                CoreUtils.SetKeyword(material, "WEATHER_SNOW", p.Kind == WeatherKind.Snow);
                material.SetTexture(Settings.NoiseID, noise);
                material.SetFloat(Settings.RandomnessID, p.Randomness);
                material.SetFloat(Settings.CycleRateID, p.CycleRate);
                material.SetFloat(Settings.ParticleShapeID, p.ParticleShape);
                material.SetFloat(Settings.ParticleOpacityID, p.ParticleOpacity);
                material.SetFloat(Settings.RefractionScaleID, p.RefractionScale);
                material.SetFloat(Settings.LensStrengthID, p.LensStrength);
                material.SetFloat(Settings.EdgeBaseWidthID, p.EdgeBaseWidth);
                material.SetFloat(Settings.EdgeNoiseWidthID, p.EdgeNoiseWidth);
                material.SetFloat(Settings.FogStrengthID, p.FogStrength);
                material.SetFloat(Settings.FogBaseDistanceID, p.FogBaseDistance);
                material.SetFloat(Settings.FogNoiseDistanceID, p.FogNoiseDistance);
                material.SetFloat(Settings.FogNoiseScrollXID, p.FogNoiseScrollX);
                material.SetFloat(Settings.ToneStrengthID, p.ToneStrength);
                material.SetVector(Settings.ToneTintID, (Vector4)p.ToneTint);
                material.SetFloat(Settings.LegacyAlphaID, p.LegacySnowAlpha ? 1f : 0f);
                material.SetFloat(Settings.DebugID, (float)debug);
                material.SetVector(Settings.TargetSizeID, targetSize);
                material.SetFloat(Settings.TimeID, time);
            }
        }

        public Settings settings = new();
        readonly HashSet<string> _reported = new();
        Material _material;
        WeatherPass _pass;
        Shader _loadedShader;

        // ════════════════════════════════════════════════════════════
        //  生命周期 — 自持材质的创建、Shader切换与释放。
        // ════════════════════════════════════════════════════════════
        public override void Create()
        {
            Release();
            EnsureMaterial();
        }

        protected override void Dispose(bool disposing)
        {
            Release();
        }

        void Release()
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _pass = null;
            _loadedShader = null;
        }

        bool EnsureMaterial()
        {
            if (settings == null || settings.shader == null || !settings.shader.isSupported)
            {
                Release();
                Report("shader", "缺少支持的Weather Shader，跳过天气。");
                return false;
            }
            if (_material != null && _loadedShader == settings.shader)
                return _pass != null && _pass.HasExpectedPasses;

            Release();
            _loadedShader = settings.shader;
            _material = CoreUtils.CreateEngineMaterial(_loadedShader);
            _material.name = "Weather.Runtime";
            _pass = new WeatherPass(_material, Report);
            if (!_pass.HasExpectedPasses)
            {
                Report("passes", "Shader缺少Particles或Atmosphere命名Pass，跳过天气。");
                return false;
            }
            return true;
        }

        void Report(string key, string message)
        {
            if (_reported.Add(key))
                Debug.LogWarning("[Weather] " + message, this);
        }

        // ════════════════════════════════════════════════════════════
        //  AddRenderPasses — 相机和模块筛选早于入队与输入请求。
        // ════════════════════════════════════════════════════════════
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings == null)
                return;
            CameraData camera = renderingData.cameraData;
            if (camera.renderType == CameraRenderType.Overlay
                || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
                || (camera.isSceneViewCamera && !settings.showInSceneView))
                return;
            if (!camera.resolveFinalTarget)
            {
                Report("stack", "首版仅支持最终输出Base Camera；跳过非末端Camera Stack相机。");
                return;
            }
            if (settings.profile == null || settings.noiseTexture == null)
            {
                Report("resources", "缺少Weather Profile或共享噪声，跳过天气。");
                return;
            }

            ResolvedWeatherSettings parameters = settings.profile.Resolve();
            WeatherDebugMode debug = Enum.IsDefined(typeof(WeatherDebugMode), settings.debug)
                ? settings.debug : WeatherDebugMode.Off;
            if (!parameters.ParticlesActive && !parameters.AtmosphereActive && debug == WeatherDebugMode.Off)
                return;
            if (!EnsureMaterial())
                return;

            _pass.Prepare(parameters, settings.noiseTexture, debug);
            renderer.EnqueuePass(_pass);
        }
    }
}
