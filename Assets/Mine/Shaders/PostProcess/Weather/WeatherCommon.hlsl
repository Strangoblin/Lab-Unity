// ════════════════════════════════════════════════════════════
//  WeatherCommon — 屏幕上下文、周期随机与闭式粒子复合
// ════════════════════════════════════════════════════════════
#ifndef WEATHER_COMMON_INCLUDED
#define WEATHER_COMMON_INCLUDED

struct WeatherScreenContext
{
    float2 sourceUV;
    float2 effectUV;
    float targetAspect;
    float time;
};

// ════════════════════════════════════════════════════════════
//  Weather_CreateContext — 从实际天气目标尺寸建立等比变换上下文
// ════════════════════════════════════════════════════════════
WeatherScreenContext Weather_CreateContext(float2 sourceUV)
{
    WeatherScreenContext context;
    context.sourceUV = sourceUV;
    context.effectUV = DYNAMIC_SCALING_REMOVE_SCALEBIAS(sourceUV);
    context.targetAspect = _WeatherTargetSize.x / _WeatherTargetSize.y;
    context.time = _WeatherTime;
    return context;
}

// ════════════════════════════════════════════════════════════
//  Weather_Random — 三种天气共同使用的周期随机数
// ════════════════════════════════════════════════════════════
float4 Weather_Random(float index)
{
    return frac(sin((index + 1.0) * float4(127.1, 311.7, 74.7, 269.5))
                * 43758.5453);
}

// ════════════════════════════════════════════════════════════
//  Weather_ParticleTime — 保留旧风速乘法顺序的独立周期时间
// ════════════════════════════════════════════════════════════
float Weather_ParticleTime(WeatherScreenContext context)
{
    return context.time * (_CycleRate * 0.5) * 2.0;
}

// ════════════════════════════════════════════════════════════
//  Weather_Composite10 — 同一粒子十次叠加的闭式权重
// ════════════════════════════════════════════════════════════
float Weather_Composite10(float particle)
{
    float remain = 1.0 - particle;
    float remain2 = remain * remain;
    float remain4 = remain2 * remain2;
    float remain8 = remain4 * remain4;
    return 1.0 - remain8 * remain2;
}

// ════════════════════════════════════════════════════════════
//  Weather_DebugMask — 以灰度输出模块贡献
// ════════════════════════════════════════════════════════════
float4 Weather_DebugMask(float mask)
{
    return float4(mask.xxx, 1.0);
}

#endif
