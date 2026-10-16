# Weather 实施契约（2026-10-08）

主代理统一合入和Editor操作。并行任务只写各自 `.codex/tmp/weather-implementation/` 暂存产物，不写Assets或修改旧效果；不回退其它人的改动。

## Shader契约

新菜单：PostProcess/Weather。命名Pass：Particles（0）、Atmosphere（1）。模式为互斥关键词WEATHER_RAIN、WEATHER_SAND、WEATHER_SNOW（multi_compile_local）。静态噪声普通TEXTURE2D；相机色/深度走URP _X。

CBUFFER与Properties字段（float，另注明者除外）：

- _Randomness，_CycleRate（2*旧Wind），_ParticleShape（SDF threshold），_ParticleOpacity（在Composite10之前），_RefractionScale（乘0.015，默认1）。
- _LensStrength，_EdgeBaseWidth，_EdgeNoiseWidth。
- _FogStrength，_FogBaseDistance，_FogNoiseDistance，_FogNoiseScrollX。
- _ToneStrength，_ToneTint（float4，RGB是原冷暖倍率）。
- _LegacySnowAlpha（0/1），_WeatherDebug（Off0/Particles1/Refraction2/Lens3/Fog4/Final5）。
- _WeatherTargetSize（float4=width,height,1/width,1/height），_WeatherTime（float：Feature从Time.time传入；验证可固定同帧输入，不使用旧材质配置）。
- _WeatherNoiseTex（Texture2D，不进CBUFFER）。

没有blur函数/参数/采样/Pass。Rain保留水汽遮罩/染色，直接从输入scene进行fog与mist tint合并。Rain差分偏移保持原响应（现有坡度直接用于UV的长宽比限制保留）。Snow原浮点求值顺序尽量保持，最终宽度<=0返回零mask，FogBaseDistance>0、FogNoiseDistance>=0。噪声不改像素/GUID/色彩空间。

Debug：1在P0展示粒子贡献（雨可用offset magnitude），2仅雨显示offset，3在P1显示有效lens遮罩，4在P1显示基础fog项（不乘FogStrength，因此即使FogStrength=0也需depth），5是正常Final输出，0正常输出。FogDebug复用真实fog函数，不改Profile。LensDebug有效强度0返回零。粒子Debug即使强度0也执行P0但显示零。深度缺失主代理快照将FogStrength=0并将FogDebug改Off；其它模块继续。

## C# Profile契约

命名空间Mine.Weather；文件WeatherProfile.cs，public enum WeatherKind { Rain, Sand, Snow }，public enum WeatherDebugMode { Off, Particles, Refraction, Lens, Fog, Final }，public enum WeatherAlphaPolicy { PreserveInput, LegacySnow }。

WeatherProfile : ScriptableObject。使用Common/Particles/Lens/Fog/Tone嵌套Serializable settings，面板按Technical/Artistic分层。美术值唯一来源Profile；资源由Feature持有。提供：

```text
public WeatherKind Kind { get; }
public void ConfigurePreset(WeatherKind kind, float randomness=1, float wind=.5, float legacyStrength=.5, float toneStrength=1)
public ResolvedWeatherSettings Resolve()
```

ResolvedWeatherSettings为public struct，public字段：Kind, Randomness, CycleRate, ParticleShape, ParticleOpacity, RefractionScale, LensStrength, EdgeBaseWidth, EdgeNoiseWidth, FogStrength, FogBaseDistance, FogNoiseDistance, FogNoiseScrollX, ToneStrength, ToneTint(Color), LegacySnowAlpha(bool)。纯值快照，无可变对象引用。

提供bool ParticlesActive（雨shape/opacity/refraction均正；雪shape/opacity均正；沙opacity正）与bool AtmosphereActive（lens/fog/tone有活动贡献；雪lens需存在宽度），bool FogActive。开关参数0关闭。

Resolve集中校验NaN/Inf及范围，不回写资产。OnValidate用同一范围规则钳制字段。ConfigurePreset从三旧实际值迁移（主代理会读取材质参数调用，不把常数当唯一基线）：RainF=.5，SandD=1，SnowF=.3；随机1，Wind=.5，温度1；雪EdgeBase=.1F、EdgeNoise=F、Opacity=F、FogBase=100(1-W)、FogNoise=100、FogScroll=W，Alpha LegacySnow；Rain/Sand Alpha PreserveInput；冷ToneTint(.88,.96,1.05)，沙(1.08,.96,.82)。

## 主代理Feature

WeatherFeature.Settings public shader、noiseTexture、profile、showInSceneView、debug；固定事件AfterRenderingPostProcessing。只支持resolveFinalTarget=true Base，过滤Preview/Reflection/Overlay及非末端Stack。两RasterPass独立数据：source desc派生、执行回调绑定Snapshot、更新resources.cameraColor。需要fog或FogDebug才请求Depth，无Color/Normal/Motion输入请求。全部关且DebugOff不入队。自持材质释放，不回写旧Mat/Profile；依赖无效诊断一次。

主代理负责生成Preset资产、README、Renderer接入、基线记录、验证脚本与提交，不由并行任务写这些文件。
