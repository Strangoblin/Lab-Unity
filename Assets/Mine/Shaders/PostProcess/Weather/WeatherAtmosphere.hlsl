// ════════════════════════════════════════════════════════════
//  WeatherAtmosphere — 镜头沉积、场景雾和调色的独立模块
// ════════════════════════════════════════════════════════════
#ifndef WEATHER_ATMOSPHERE_INCLUDED
#define WEATHER_ATMOSPHERE_INCLUDED

// ════════════════════════════════════════════════════════════
//  Weather_RainLens — 边缘噪声水汽的有效遮罩
// ════════════════════════════════════════════════════════════
float Weather_RainLens(float2 uv, float noise)
{
    if (_LensStrength <= 0.0)
        return 0.0;

    float2 p = uv * 2.0 - 1.0;
    float2 p2 = p * p;
    return smoothstep(0.25, 1.1, dot(p2, p2)) * lerp(0.75, 1.0, noise) * _LensStrength;
}

// ════════════════════════════════════════════════════════════
//  Weather_SandLens — 边缘噪声积尘的有效遮罩
// ════════════════════════════════════════════════════════════
float Weather_SandLens(float2 uv, float noise)
{
    if (_LensStrength <= 0.0)
        return 0.0;

    float2 p = uv * 2.0 - 1.0;
    float2 p2 = p * p;
    return smoothstep(0.3, 1.15, dot(p2, p2)) * lerp(0.35, 1.0, noise) * _LensStrength;
}

// ════════════════════════════════════════════════════════════
//  Weather_SnowLens — 两层噪声霜边，派生零宽度安全关闭
// ════════════════════════════════════════════════════════════
float Weather_SnowLens(float2 uv)
{
    if (_LensStrength <= 0.0 || (_EdgeBaseWidth <= 0.0 && _EdgeNoiseWidth <= 0.0))
        return 0.0;

    float noise7 = SAMPLE_TEXTURE2D(_WeatherNoiseTex, sampler_LinearRepeat, uv * 7.0).r;
    float noise5 = SAMPLE_TEXTURE2D(_WeatherNoiseTex, sampler_LinearRepeat, uv * 5.0).r;
    float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
    float width = _EdgeBaseWidth + _EdgeNoiseWidth * noise7 * noise5;
    if (width <= 0.0)
        return 0.0;

    float frost = 1.0 - smoothstep(0.0, width, edge);
    return frost * _LensStrength;
}

// ════════════════════════════════════════════════════════════
//  Weather_HeightFog — 雨沙共享的透视与正交中点高度雾
// ════════════════════════════════════════════════════════════
float Weather_HeightFog(float2 uv, float noise, float heightFalloff, float extinction)
{
    float rawDepth = SampleSceneDepth(uv);
    #if !UNITY_REVERSED_Z
        rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
    #endif
    float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
    float3 ray = worldPos - _WorldSpaceCameraPos;
    float3 forward = -UNITY_MATRIX_V[2].xyz;
    ray = lerp(ray, forward * max(dot(ray, forward), 0.0), unity_OrthoParams.w);
    float rayLength = max(length(ray), 0.0001);
    float distance = min(rayLength, 250.0);
    float height = worldPos.y - ray.y + ray.y * (distance / rayLength) * 0.5;
    float density = exp2(-max(height, 0.0) * heightFalloff);
    return 1.0 - exp2(-distance * density * extinction * lerp(0.8, 1.2, noise));
}

// ════════════════════════════════════════════════════════════
//  Weather_SnowFog — 独立距离与滚动参数的旧眼空间深度雾
// ════════════════════════════════════════════════════════════
float Weather_SnowFog(WeatherScreenContext context)
{
    float2 noiseUV = float2(
        context.effectUV.x * 0.5 - _FogNoiseScrollX * context.time, context.effectUV.y);
    float noise = SAMPLE_TEXTURE2D(_WeatherNoiseTex, sampler_LinearRepeat, noiseUV).r;
    float rawDepth = SampleSceneDepth(context.effectUV);
    float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    float distance = _FogBaseDistance + noise * _FogNoiseDistance;
    return 1.0 - exp2(-eyeDepth / distance);
}

// ════════════════════════════════════════════════════════════
//  Weather_ApplyTone — 统一灰度混合与冷暖倍率调色
// ════════════════════════════════════════════════════════════
float3 Weather_ApplyTone(float3 color)
{
    if (_ToneStrength <= 0.0)
        return color;

    float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    float3 tinted = lerp(color, luminance.xxx, 0.3) * _ToneTint.rgb;
    return lerp(color, tinted, _ToneStrength);
}

// ════════════════════════════════════════════════════════════
//  Weather_ApplyRainAtmosphere — 水汽与高度雾合并染色后调色
// ════════════════════════════════════════════════════════════
float4 Weather_ApplyRainAtmosphere(WeatherScreenContext context, float4 scene)
{
    float2 noise = 0.0;
    if (_LensStrength > 0.0 || _FogStrength > 0.0 || _WeatherDebug == 4.0)
        noise = SAMPLE_TEXTURE2D(
            _WeatherNoiseTex, sampler_LinearRepeat, context.effectUV * 2.0).rg;

    float mist = Weather_RainLens(context.effectUV, noise.r);
    if (_WeatherDebug == 3.0)
        return Weather_DebugMask(mist);

    float fogBase = 0.0;
    if (_FogStrength > 0.0 || _WeatherDebug == 4.0)
        fogBase = Weather_HeightFog(context.effectUV, noise.g, 0.08, 0.018);
    if (_WeatherDebug == 4.0)
        return Weather_DebugMask(fogBase);

    float fog = fogBase * _FogStrength * 0.65;
    float3 color = lerp(scene.rgb, float3(0.65, 0.72, 0.78),
        1.0 - (1.0 - fog) * (1.0 - mist * 0.12));
    return float4(Weather_ApplyTone(color), scene.a);
}

// ════════════════════════════════════════════════════════════
//  Weather_ApplySandAtmosphere — 高度雾、积尘、暖调色按序合成
// ════════════════════════════════════════════════════════════
float4 Weather_ApplySandAtmosphere(WeatherScreenContext context, float4 scene)
{
    float2 noise = 0.0;
    if (_LensStrength > 0.0 || _FogStrength > 0.0 || _WeatherDebug == 4.0)
        noise = SAMPLE_TEXTURE2D(
            _WeatherNoiseTex, sampler_LinearRepeat, context.effectUV * 3.0).rg;

    float deposit = Weather_SandLens(context.effectUV, noise.r);
    if (_WeatherDebug == 3.0)
        return Weather_DebugMask(deposit);

    float fogBase = 0.0;
    if (_FogStrength > 0.0 || _WeatherDebug == 4.0)
        fogBase = Weather_HeightFog(context.effectUV, noise.g, 0.12, 0.025);
    if (_WeatherDebug == 4.0)
        return Weather_DebugMask(fogBase);

    float fog = fogBase * _FogStrength * 0.75;
    float3 color = lerp(scene.rgb, float3(0.66, 0.49, 0.30), fog);
    color = lerp(color, float3(0.46, 0.31, 0.16), deposit * 0.55);
    return float4(Weather_ApplyTone(color), scene.a);
}

// ════════════════════════════════════════════════════════════
//  Weather_ApplySnowAtmosphere — 霜边、滚动雾、冷调色与兼容 Alpha
// ════════════════════════════════════════════════════════════
float4 Weather_ApplySnowAtmosphere(WeatherScreenContext context, float4 scene)
{
    float lens = Weather_SnowLens(context.effectUV);
    if (_WeatherDebug == 3.0)
        return Weather_DebugMask(lens);

    float fogBase = 0.0;
    if (_FogStrength > 0.0 || _WeatherDebug == 4.0)
        fogBase = Weather_SnowFog(context);
    if (_WeatherDebug == 4.0)
        return Weather_DebugMask(fogBase);

    float fog = fogBase * _FogStrength;
    float4 atmosphere = lerp(scene, 1.0, lens);
    atmosphere = lerp(atmosphere, 1.0, fog);
    if (_LegacySnowAlpha <= 0.0)
        atmosphere.a = scene.a;
    return float4(Weather_ApplyTone(atmosphere.rgb), atmosphere.a);
}

// ════════════════════════════════════════════════════════════
//  Weather_ApplyAtmosphere — 编排当前模式的独立气氛模块
// ════════════════════════════════════════════════════════════
float4 Weather_ApplyAtmosphere(WeatherScreenContext context, float4 scene)
{
    #if defined(WEATHER_SAND)
        return Weather_ApplySandAtmosphere(context, scene);
    #elif defined(WEATHER_SNOW)
        return Weather_ApplySnowAtmosphere(context, scene);
    #else
        return Weather_ApplyRainAtmosphere(context, scene);
    #endif
}

#endif
