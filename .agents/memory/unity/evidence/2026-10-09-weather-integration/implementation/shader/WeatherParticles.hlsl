// ════════════════════════════════════════════════════════════
//  WeatherParticles — 雨滴折射、沙粒与雪斑的互斥模式
// ════════════════════════════════════════════════════════════
#ifndef WEATHER_PARTICLES_INCLUDED
#define WEATHER_PARTICLES_INCLUDED

// ════════════════════════════════════════════════════════════
//  Weather_RainDepth — 噪声高度与形状合成雨滴凹陷深度
// ════════════════════════════════════════════════════════════
float Weather_RainDepth(float2 localUV, float2 textureOffset, float alpha)
{
    float height = SAMPLE_TEXTURE2D(
        _WeatherNoiseTex, sampler_LinearRepeat, localUV + textureOffset).r - 0.5;
    float sdf = _ParticleShape - length(localUV + height * 0.5 - 0.5) * 2.0;
    float particle = saturate(sdf) * alpha * _ParticleOpacity;
    return Weather_Composite10(particle);
}

// ════════════════════════════════════════════════════════════
//  Weather_RainOffset — 固定网格雨滴的高度差分折射
// ════════════════════════════════════════════════════════════
float2 Weather_RainOffset(WeatherScreenContext context)
{
    if (_ParticleShape <= 0.0 || _ParticleOpacity <= 0.0 || _RefractionScale <= 0.0)
        return 0.0;

    float2 uv = context.effectUV;
    float2 tileCount = float2(4.0, 4.0);
    float2 tile = floor(uv * tileCount);
    float2 tileUV = frac(uv * tileCount);
    float phase = frac(dot(tile, float2(0.75487766, 0.56984029)));
    float time = Weather_ParticleTime(context) + phase;
    float index = floor(time);
    float cycle = frac(time);

    float4 random = (Weather_Random(index + tile.x * 17.0 + tile.y * 101.0)
        * 2.0 - 1.0) * _Randomness;
    float2 pivot = float2(0.5, 0.5);
    float2 translation = random.xy * 0.25;
    float angle = random.x;
    float scale = (random.x * 0.25 + 0.75) * 0.35;
    float alpha = smoothstep(1.0, 0.3, cycle);
    float tileAspect = context.targetAspect * tileCount.y / tileCount.x;
    float2 localUV = TRS2D_InverseTransformUV(
        tileUV, translation, angle, scale, pivot, tileAspect);

    if (alpha <= 0.0 || any(localUV < 0.0) || any(localUV > 1.0))
        return 0.0;

    float2 textureOffset = random.xy;
    float center = Weather_RainDepth(localUV, textureOffset, alpha);
    if (center <= 0.0)
        return 0.0;

    float2 delta = float2(0.02, 0.0);
    float right = Weather_RainDepth(localUV + delta, textureOffset, alpha);
    float left = Weather_RainDepth(localUV - delta, textureOffset, alpha);
    delta = float2(0.0, 0.02);
    float up = Weather_RainDepth(localUV + delta, textureOffset, alpha);
    float down = Weather_RainDepth(localUV - delta, textureOffset, alpha);
    float2 slope = float2(right - left, up - down) / 0.04;
    float2 rotatedSlope = TRS2D_RotateIsotropic(slope, angle);
    return rotatedSlope / (1.0 + length(rotatedSlope)) * 0.015 * center * _RefractionScale;
}

// ════════════════════════════════════════════════════════════
//  Weather_SandParticle — 固定网格中的周期沙粒击打
// ════════════════════════════════════════════════════════════
float Weather_SandParticle(WeatherScreenContext context)
{
    if (_ParticleOpacity <= 0.0)
        return 0.0;

    float2 uv = context.effectUV;
    float2 tileCount = float2(40.0, 24.0);
    float2 tile = floor(uv * tileCount);
    float2 tileUV = frac(uv * tileCount);
    float phase = frac(dot(tile, float2(0.75487766, 0.56984029)));
    float time = Weather_ParticleTime(context) + phase;
    float cycle = frac(time);
    float4 random = (Weather_Random(floor(time) + tile.x * 17.0 + tile.y * 101.0)
        * 2.0 - 1.0) * _Randomness;
    float scale = (random.z * 0.25 + 0.75) * 0.22;
    float alpha = smoothstep(0.0, 0.12, cycle) * (1.0 - smoothstep(0.55, 1.0, cycle));
    float aspect = context.targetAspect * tileCount.y / tileCount.x;
    float2 localUV = TRS2D_InverseTransformUV(
        tileUV, random.xy * 0.25, random.x * PI, scale, float2(0.5, 0.5), aspect);
    float noise = SAMPLE_TEXTURE2D(
        _WeatherNoiseTex, sampler_LinearRepeat, localUV + random.xy).r;
    float shape = 1.0 - smoothstep(0.0, 0.5, length(localUV - 0.5) * 2.0);
    return shape * lerp(0.35, 1.0, noise) * alpha * _ParticleOpacity;
}

// ════════════════════════════════════════════════════════════
//  Weather_SnowParticle — 周期落点、噪声扰动形状与十次复合
// ════════════════════════════════════════════════════════════
float Weather_SnowParticle(WeatherScreenContext context)
{
    if (_ParticleShape <= 0.0 || _ParticleOpacity <= 0.0)
        return 0.0;

    float2 uv = context.effectUV;
    float time = Weather_ParticleTime(context);
    float index = floor(time);
    float cycle = frac(time);

    float4 random = (Weather_Random(index) * 2.0 - 1.0) * _Randomness;
    float2 pivot = float2(0.5, 0.5);
    float2 translation = random.xy * 0.5;
    float angle = random.x;
    float scale = (random.x * 0.25 + 0.75) * 0.1;
    float alpha = smoothstep(1.0, 0.3, cycle);
    uv = TRS2D_InverseTransformUV(
        uv, translation, angle, scale, pivot, context.targetAspect);

    float snow = SAMPLE_TEXTURE2D(
        _WeatherNoiseTex, sampler_LinearRepeat, uv + random.xy).r;
    float sdf = _ParticleShape - length(lerp(uv, snow, 0.5) - 0.5) * 2.0;
    float particle = saturate(sdf) * alpha * _ParticleOpacity;
    return Weather_Composite10(particle);
}

// ════════════════════════════════════════════════════════════
//  Weather_ApplyParticles — 选择粒子模式并输出真实贡献调试
// ════════════════════════════════════════════════════════════
float4 Weather_ApplyParticles(WeatherScreenContext context, float4 scene)
{
    #if defined(WEATHER_SAND)
        float particle = Weather_SandParticle(context);
        if (_WeatherDebug == 1.0)
            return Weather_DebugMask(particle * 0.8);
        if (_WeatherDebug == 2.0)
            return Weather_DebugMask(0.0);
        return float4(lerp(scene.rgb, float3(0.62, 0.43, 0.23), particle * 0.8), scene.a);
    #elif defined(WEATHER_SNOW)
        float particle = Weather_SnowParticle(context);
        if (_WeatherDebug == 1.0)
            return Weather_DebugMask(particle);
        if (_WeatherDebug == 2.0)
            return Weather_DebugMask(0.0);
        return float4(lerp(scene.rgb, 1.0, particle), scene.a);
    #else
        float2 offsetUV = Weather_RainOffset(context);
        if (_WeatherDebug == 1.0)
            return Weather_DebugMask(saturate(length(offsetUV) / 0.015));
        if (_WeatherDebug == 2.0)
            return float4(abs(offsetUV) / 0.015, 0.0, 1.0);
        float2 refractedUV = DYNAMIC_SCALING_APPLY_SCALEBIAS(
            saturate(context.effectUV + offsetUV));
        float3 color = SAMPLE_TEXTURE2D_X(
            _BlitTexture, sampler_LinearClamp, refractedUV).rgb;
        return float4(color, scene.a);
    #endif
}

#endif
