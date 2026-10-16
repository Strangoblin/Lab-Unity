// ═══════════════════════════════════════════════════════════════
//  Weather Profile — 模块化粒子与氛围参数、迁移预设和纯值快照
// ═══════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Weather
{
    public enum WeatherKind { Rain, Sand, Snow }
    public enum WeatherDebugMode { Off, Particles, Refraction, Lens, Fog, Final }
    public enum WeatherAlphaPolicy { PreserveInput, LegacySnow }

    /// <summary>天气参数的唯一美术数据源；资源和调试由 WeatherFeature 持有。</summary>
    [CreateAssetMenu(fileName = "WeatherProfile", menuName = "Mine/Weather/Profile")]
    public sealed class WeatherProfile : ScriptableObject
    {
        public const float MinimumFogDistance = 0.01f;
        private const float MaximumFogDistance = 10000f;
        private const float MaximumRefractionScale = 4f;
        private const float MaximumTintMultiplier = 4f;
        private const float MinimumParticleTileCount = 0.01f;
        private const float MaximumParticleTileCount = 256f;
        private const float MinimumParticleScale = 0.001f;
        private const float MaximumParticleScale = 4f;

        [Header("Technical · Alpha")]
        [Tooltip("LegacySnow 仅用于保留旧雪天气的 Alpha 合成。")]
        public WeatherAlphaPolicy AlphaPolicy = WeatherAlphaPolicy.PreserveInput;

        [Header("Artistic · Common")]
        public CommonSettings Common = new CommonSettings();

        [Header("Artistic · Particles")]
        public ParticleSettings Particles = new ParticleSettings();

        [Header("Artistic · Lens")]
        public LensSettings Lens = new LensSettings();

        [Header("Artistic · Fog")]
        public FogSettings Fog = new FogSettings();

        [Header("Artistic · Tone")]
        public ToneSettings Tone = new ToneSettings();

        [Serializable]
        public sealed class CommonSettings
        {
            [Tooltip("每份预设只选择一种天气，模块可独立关闭。")]
            public WeatherKind Kind = WeatherKind.Rain;

            [Tooltip("粒子位置和相位的随机程度。")]
            [Range(0f, 1f)] public float Randomness = 1f;

            [Tooltip("粒子每秒循环速率；0 = 冻结。旧 Wind 的两倍。")]
            [Range(0f, 2f)] public float CycleRate = 1f;
        }

        [Serializable]
        public sealed class ParticleSettings
        {
            [Tooltip("水平、垂直粒子网格数量，范围 0.01–256；越大分布越密。")]
            public Vector2 TileCount = DefaultParticleTileCount(WeatherKind.Rain);

            [Tooltip("单个网格内粒子尺度，范围 0.001–4；关闭粒子使用 Opacity。")]
            [Range(MinimumParticleScale, MaximumParticleScale)] public float Scale = DefaultParticleScale(WeatherKind.Rain);

            [Tooltip("雨、雪的形状阈值；0 = 关闭粒子。沙不使用此值。")]
            [Range(0f, 1f)] public float Shape = 0.5f;

            [Tooltip("粒子贡献强度；0 = 关闭。")]
            [Range(0f, 1f)] public float Opacity = 0.5f;

            [Tooltip("雨滴折射尺度；0 = 关闭折射。沙、雪不使用此值。")]
            [Range(0f, MaximumRefractionScale)] public float RefractionScale = 1f;
        }

        [Serializable]
        public sealed class LensSettings
        {
            [Tooltip("镜头水汽、积尘或霜边的强度；0 = 关闭。")]
            [Range(0f, 1f)] public float Strength = 0.5f;

            [Tooltip("雪霜边的基础宽度；与噪声宽度同时为 0 时关闭霜边。")]
            [Range(0f, 1f)] public float EdgeBaseWidth;

            [Tooltip("雪霜边宽度的噪声变化。雨、沙不使用此值。")]
            [Range(0f, 1f)] public float EdgeNoiseWidth;
        }

        [Serializable]
        public sealed class FogSettings
        {
            [Tooltip("场景雾强度；0 = 关闭并停止请求天气深度。")]
            [Range(0f, 1f)] public float Strength = 0.5f;

            [Tooltip("雪雾的基础距离，单位为世界单位；最小 0.01。雨、沙不使用。")]
            [Range(MinimumFogDistance, MaximumFogDistance)] public float BaseDistance = 50f;

            [Tooltip("雪雾距离的噪声变化，单位为世界单位。")]
            [Range(0f, MaximumFogDistance)] public float NoiseDistance = 100f;

            [Tooltip("雪雾噪声的水平滚动速率；0 = 冻结，负值反向。")]
            [Range(-2f, 2f)] public float NoiseScrollX = 0.5f;
        }

        [Serializable]
        public sealed class ToneSettings
        {
            [Tooltip("冷暖调色强度；0 = 关闭。")]
            [Range(0f, 1f)] public float Strength = 1f;

            [Tooltip("RGB 颜色倍率，范围 0–4；调色包含去饱和。Alpha 不参与调色。")]
            [ColorUsage(false, true)] public Color Tint = new Color(0.88f, 0.96f, 1.05f, 1f);
        }

        /// <summary>返回合法的单一天气模式，不修改资产。</summary>
        public WeatherKind Kind => NormalizeKind(Common != null ? Common.Kind : WeatherKind.Rain);

        // ════════════════════════════════════════════════════════════
        //  公开 API — 从旧材质迁移参数，或生成独立的运行时快照
        // ════════════════════════════════════════════════════════════

        /// <summary>按旧材质参数配置预设；调用方负责提供实际资产值与保存资产。</summary>
        public void ConfigurePreset(WeatherKind kind, float randomness = 1f, float wind = 0.5f,
            float legacyStrength = 0.5f, float toneStrength = 1f)
        {
            kind = NormalizeKind(kind);
            float migratedWind = ClampFinite(wind, 0f, 1f, 0.5f);
            float migratedStrength = ClampFinite(legacyStrength, 0f, 1f, 0.5f);
            bool snow = kind == WeatherKind.Snow;

            Common = new CommonSettings { Kind = kind, Randomness = randomness, CycleRate = 2f * migratedWind };
            Particles = new ParticleSettings
            {
                TileCount = DefaultParticleTileCount(kind),
                Scale = DefaultParticleScale(kind),
                Shape = migratedStrength,
                Opacity = migratedStrength,
                RefractionScale = 1f
            };
            Lens = new LensSettings
            {
                Strength = migratedStrength,
                EdgeBaseWidth = snow ? 0.1f * migratedStrength : 0f,
                EdgeNoiseWidth = snow ? migratedStrength : 0f
            };
            Fog = new FogSettings
            {
                Strength = migratedStrength,
                BaseDistance = 100f * (1f - migratedWind),
                NoiseDistance = 100f,
                NoiseScrollX = migratedWind
            };
            Tone = new ToneSettings
            {
                Strength = toneStrength,
                Tint = kind == WeatherKind.Sand
                    ? new Color(1.08f, 0.96f, 0.82f, 1f)
                    : new Color(0.88f, 0.96f, 1.05f, 1f)
            };
            AlphaPolicy = snow ? WeatherAlphaPolicy.LegacySnow : WeatherAlphaPolicy.PreserveInput;
            ApplyResolved(Resolve());
        }

        /// <summary>集中校验范围和非有限输入；返回无对象引用的快照，不回写 Profile。</summary>
        public ResolvedWeatherSettings Resolve()
        {
            CommonSettings common = Common ?? new CommonSettings();
            WeatherKind kind = NormalizeKind(common.Kind);
            Vector2 defaultTileCount = DefaultParticleTileCount(kind);
            float defaultScale = DefaultParticleScale(kind);
            ParticleSettings particles = Particles ?? new ParticleSettings { TileCount = defaultTileCount, Scale = defaultScale };
            LensSettings lens = Lens ?? new LensSettings();
            FogSettings fog = Fog ?? new FogSettings();
            ToneSettings tone = Tone ?? new ToneSettings();
            Color tint = tone.Tint;

            return new ResolvedWeatherSettings
            {
                Kind = kind,
                Randomness = ClampFinite(common.Randomness, 0f, 1f, 1f),
                CycleRate = ClampFinite(common.CycleRate, 0f, 2f, 1f),
                ParticleTileCount = new Vector2(
                    ClampFinite(particles.TileCount.x, MinimumParticleTileCount, MaximumParticleTileCount, defaultTileCount.x),
                    ClampFinite(particles.TileCount.y, MinimumParticleTileCount, MaximumParticleTileCount, defaultTileCount.y)),
                ParticleScale = ClampFinite(particles.Scale, MinimumParticleScale, MaximumParticleScale, defaultScale),
                ParticleShape = ClampFinite(particles.Shape, 0f, 1f, 0.5f),
                ParticleOpacity = ClampFinite(particles.Opacity, 0f, 1f, 0.5f),
                RefractionScale = ClampFinite(particles.RefractionScale, 0f, MaximumRefractionScale, 1f),
                LensStrength = ClampFinite(lens.Strength, 0f, 1f, 0.5f),
                EdgeBaseWidth = ClampFinite(lens.EdgeBaseWidth, 0f, 1f, 0f),
                EdgeNoiseWidth = ClampFinite(lens.EdgeNoiseWidth, 0f, 1f, 0f),
                FogStrength = ClampFinite(fog.Strength, 0f, 1f, 0.5f),
                FogBaseDistance = ClampFinite(fog.BaseDistance, MinimumFogDistance, MaximumFogDistance, 50f),
                FogNoiseDistance = ClampFinite(fog.NoiseDistance, 0f, MaximumFogDistance, 100f),
                FogNoiseScrollX = ClampFinite(fog.NoiseScrollX, -2f, 2f, 0.5f),
                ToneStrength = ClampFinite(tone.Strength, 0f, 1f, 1f),
                ToneTint = new Color(
                    ClampFinite(tint.r, 0f, MaximumTintMultiplier, 0.88f),
                    ClampFinite(tint.g, 0f, MaximumTintMultiplier, 0.96f),
                    ClampFinite(tint.b, 0f, MaximumTintMultiplier, 1.05f), 1f),
                LegacySnowAlpha = kind == WeatherKind.Snow
                    && AlphaPolicy == WeatherAlphaPolicy.LegacySnow
            };
        }

        // ════════════════════════════════════════════════════════════
        //  编辑校验 — 使用 Resolve 的同一范围规则写回可序列化字段
        // ════════════════════════════════════════════════════════════

        private void OnValidate()
        {
            ApplyResolved(Resolve());
        }

        private void ApplyResolved(ResolvedWeatherSettings settings)
        {
            Common ??= new CommonSettings();
            Particles ??= new ParticleSettings();
            Lens ??= new LensSettings();
            Fog ??= new FogSettings();
            Tone ??= new ToneSettings();

            Common.Kind = settings.Kind;
            Common.Randomness = settings.Randomness;
            Common.CycleRate = settings.CycleRate;
            Particles.TileCount = settings.ParticleTileCount;
            Particles.Scale = settings.ParticleScale;
            Particles.Shape = settings.ParticleShape;
            Particles.Opacity = settings.ParticleOpacity;
            Particles.RefractionScale = settings.RefractionScale;
            Lens.Strength = settings.LensStrength;
            Lens.EdgeBaseWidth = settings.EdgeBaseWidth;
            Lens.EdgeNoiseWidth = settings.EdgeNoiseWidth;
            Fog.Strength = settings.FogStrength;
            Fog.BaseDistance = settings.FogBaseDistance;
            Fog.NoiseDistance = settings.FogNoiseDistance;
            Fog.NoiseScrollX = settings.FogNoiseScrollX;
            Tone.Strength = settings.ToneStrength;
            Tone.Tint = settings.ToneTint;
            AlphaPolicy = settings.LegacySnowAlpha ? WeatherAlphaPolicy.LegacySnow : WeatherAlphaPolicy.PreserveInput;
        }

        private static Vector2 DefaultParticleTileCount(WeatherKind kind)
        {
            float count = kind == WeatherKind.Sand ? 3f : kind == WeatherKind.Snow ? 1.5f : 4.5f;
            return new Vector2(count, count);
        }

        private static float DefaultParticleScale(WeatherKind kind)
        {
            return kind == WeatherKind.Sand ? 0.2f : kind == WeatherKind.Snow ? 0.1f : 0.35f;
        }

        private static float ClampFinite(float value, float minimum, float maximum, float fallback)
        {
            return Mathf.Clamp(float.IsNaN(value) || float.IsInfinity(value) ? fallback : value, minimum, maximum);
        }

        private static WeatherKind NormalizeKind(WeatherKind kind)
        {
            return kind == WeatherKind.Rain || kind == WeatherKind.Sand || kind == WeatherKind.Snow
                ? kind : WeatherKind.Rain;
        }
    }

    /// <summary>录制渲染图时捕获的纯值参数，供每个绘制独立绑定。</summary>
    public struct ResolvedWeatherSettings
    {
        public WeatherKind Kind;
        public float Randomness;
        public float CycleRate;
        public Vector2 ParticleTileCount;
        public float ParticleScale;
        public float ParticleShape;
        public float ParticleOpacity;
        public float RefractionScale;
        public float LensStrength;
        public float EdgeBaseWidth;
        public float EdgeNoiseWidth;
        public float FogStrength;
        public float FogBaseDistance;
        public float FogNoiseDistance;
        public float FogNoiseScrollX;
        public float ToneStrength;
        public Color ToneTint;
        public bool LegacySnowAlpha;

        public bool ParticlesActive => ParticleOpacity > 0f
            && (Kind == WeatherKind.Sand || ParticleShape > 0f)
            && (Kind != WeatherKind.Rain || RefractionScale > 0f);

        public bool FogActive => FogStrength > 0f;

        public bool AtmosphereActive => FogActive
            || (LensStrength > 0f && (Kind != WeatherKind.Snow || EdgeBaseWidth > 0f || EdgeNoiseWidth > 0f))
            || ToneStrength > 0f;
    }
}
