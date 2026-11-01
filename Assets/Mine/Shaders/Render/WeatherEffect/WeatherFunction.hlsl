// ════════════════════════════════════════════════════════════
//  WeatherFunction.hlsl — 表面天气层：湿身着色与积雪覆盖
//  用法：调用方使用 Assets/Mine/Shaders/Render/WeatherEffect/WeatherFunction.hlsl 全路径 include。
//  依赖：调用方先 include Core.hlsl，提供 real 类型与 smoothstep；本库零 CBUFFER。
//  输出：湿身 albedo / 法线、积雪遮罩 / 雪面法线与闪烁；光照与合成由调用方组合。
// ════════════════════════════════════════════════════════════

#ifndef WEATHERFUNCTION_HLSL_INCLUDED
#define WEATHERFUNCTION_HLSL_INCLUDED

#include "Assets/Mine/Special/HLSL/HashFunction.hlsl"

#ifndef WEATHER_WATER_F0
#define WEATHER_WATER_F0 0.02      // 水膜 F0，对应 IOR 1.33 的垂直反射率
#endif

#ifndef WEATHER_LUMA
#define WEATHER_LUMA float3(0.2126, 0.7152, 0.0722)
#endif

#ifndef WEATHER_SNOW_UP
#define WEATHER_SNOW_UP float3(0.0, 1.0, 0.0)
#endif

// ════════════════════════════════════════════════════════════
//  湿身 — 压暗、提饱和与法线收拢
// ════════════════════════════════════════════════════════════

/// <summary>湿身 albedo：按湿度压暗，再围绕亮度提饱和；已饱和到可着色范围。</summary>
float3 Weather_WetAlbedo(real3 albedo, real wetness, real darken, real saturation)
{
    real wet = saturate(wetness);
    real3 darkened = albedo * lerp(1.0, saturate(darken), wet);
    real luminance = dot(darkened, WEATHER_LUMA);
    real3 saturated = lerp(luminance.xxx, darkened, lerp(1.0, saturation, wet));
    return saturate(saturated);
}

/// <summary>湿身法线：向几何法线收拢，削弱水膜下的细法线起伏。</summary>
float3 Weather_WetNormal(real3 normalWS, real3 geometricNormalWS, real wetness, real flatten)
{
    real weight = saturate(wetness) * saturate(flatten);
    return SafeNormalize(lerp(normalWS, geometricNormalWS, weight));
}

/// <summary>湿膜分布：低频噪声把均匀湿度打散成水膜与半干斑块。</summary>
real Weather_WetnessField(TEXTURE2D_PARAM(noiseTex, noiseSampler),
    real2 uv, real wetness, real variation, real scale)
{
    real noise = SAMPLE_TEXTURE2D(noiseTex, noiseSampler, uv * max(scale, 1e-3)).r;
    real patch = smoothstep(0.2, 0.8, noise);
    return saturate(wetness * lerp(1.0, lerp(0.55, 1.1, patch), saturate(variation)));
}

// ════════════════════════════════════════════════════════════
//  湿身细节 — 水滴圆珠与水流水痕
// ════════════════════════════════════════════════════════════

/// <summary>按物体空间法线主轴选投影面与两条切向轴，避免竖直面上的纹理被拉伸。</summary>
void Weather_ProjectAxis(real3 normalOS, real3 positionOS,
    out real2 uv, out real3 axisX, out real3 axisY)
{
    real3 absN = abs(normalOS);
    if (absN.y >= absN.x && absN.y >= absN.z)
    {
        uv = positionOS.xz;
        axisX = real3(1.0, 0.0, 0.0);
        axisY = real3(0.0, 0.0, 1.0);
    }
    else if (absN.x >= absN.z)
    {
        uv = positionOS.zy;
        axisX = real3(0.0, 0.0, 1.0);
        axisY = real3(0.0, 1.0, 0.0);
    }
    else
    {
        uv = positionOS.xy;
        axisX = real3(1.0, 0.0, 0.0);
        axisY = real3(0.0, 1.0, 0.0);
    }
}

/// <summary>水滴遮罩：每格一个哈希半球珠，侧壁水珠沿重力拉长并拖出水痕。</summary>
real Weather_DropletMask(real2 uv, real scale, real amount, real slope, out real2 gradient)
{
    // 轻微域扭曲，打断规则网格的横平竖直
    real2 warp = real2(
        sin(uv.y * 5.3 + 1.7) + sin(uv.y * 11.7 - 0.9),
        cos(uv.x * 4.7 - 2.1) + cos(uv.x * 12.1 + 0.6)) * (0.18 / max(scale, 1.0));
    uv += warp;

    real2 cellUV = uv * max(scale, 1.0);
    real2 cell = floor(cellUV);
    real2 local = frac(cellUV);
    real4 rnd = Hash_Sine14(dot(cell, real2(1.0, 311.7)) + 0.37);
    real2 center = lerp(real2(0.3, 0.32), real2(0.7, 0.68), rnd.xy);
    // 大小偏小分布，少数大珠撑起轮廓
    real radius = max(lerp(0.1, 0.34, rnd.z * rnd.z), 1e-3);
    // 侧壁水珠被拉长：竖直方向距离压缩，珠体更高更窄
    real stretch = 1.0 + saturate(slope) * 2.0;
    real2 offset = local - center;
    real2 scaled = real2(offset.x, offset.y / stretch);
    real dist = length(scaled);
    // 球冠斜率：珠心为 0、边缘趋近 1，和遮罩相乘得到圆顶而不是环
    gradient = scaled / radius;
    gradient.y /= stretch;

    real presence = step(rnd.w, saturate(amount));
    real bead = smoothstep(radius, radius * 0.4, dist);

    // 珠下滑出的水痕：从珠心向格底收窄，只在侧壁出现
    real down = saturate(-offset.y / 0.5);
    real tailHalf = radius * 0.5 * (1.0 - down);
    real tail = (1.0 - smoothstep(0.0, max(tailHalf, 1e-4), abs(offset.x)))
        * smoothstep(0.0, 0.15, down) * (1.0 - down);
    tail *= step(0.35, slope) * 0.6;

    return saturate(max(bead, tail)) * presence;
}

/// <summary>水痕遮罩：沿物体 Y 滚动的各向异性噪声，输出二维梯度供法线扰动。</summary>
real Weather_FlowMask(TEXTURE2D_PARAM(noiseTex, noiseSampler),
    real2 uv, real scale, real speed, real time, out real2 gradient)
{
    real s = max(scale, 0.1);
    // 横向频率高、纵向频率低：噪声被拉成细长的竖直水痕而不是大块斑
    // 时间先取小数再进 UV：避免长会话下 UV 过大，有限差分被精度抹平
    real2 flowUV = real2(uv.x * s * 3.2, (uv.y + frac(time * speed) * 4.0) * s * 0.55);
    real center = SAMPLE_TEXTURE2D(noiseTex, noiseSampler, flowUV).r;
    real offsetX = SAMPLE_TEXTURE2D(noiseTex, noiseSampler, flowUV + real2(0.02, 0.0)).r;
    real offsetY = SAMPLE_TEXTURE2D(noiseTex, noiseSampler, flowUV + real2(0.0, 0.02)).r;
    // 只取噪声的等值带，得到窄水痕；两侧渐隐，避免硬边
    real band = smoothstep(0.52, 0.66, center) * (1.0 - smoothstep(0.74, 0.9, center));
    // 梯度按带内幅度限幅，防止整片法线被抹平
    gradient = real2(offsetX - center, offsetY - center) * 8.0 * band;
    return band;
}

/// <summary>湿身细节：水滴与水痕扰动法线，并输出两者遮罩给水膜高光加权。</summary>
float3 Weather_WaterDetail(TEXTURE2D_PARAM(noiseTex, noiseSampler),
    real3 positionOS, real3 normalOS, real3 normalWS, real wetness, real time,
    real dropletAmount, real dropletScale, real dropletNormal,
    real flowAmount, real flowScale, real flowSpeed, real flowNormal,
    out real dropletMask, out real flowMask)
{
    dropletMask = 0.0;
    flowMask = 0.0;
    real wet = saturate(wetness);
    if (wet <= 0.0)
        return normalWS;

    real2 uv;
    real3 axisX;
    real3 axisY;
    Weather_ProjectAxis(normalOS, positionOS, uv, axisX, axisY);

    // 水痕只在侧壁明显：朝上表面重力方向被法线吸收，不产生流动条纹
    real slope = 1.0 - saturate(abs(normalOS.y));

    // 两层不同尺度、相互旋转的珠格叠加，打散单一网格的排列感
    real2 dropletGradient0;
    real droplet0 = Weather_DropletMask(uv, dropletScale, dropletAmount * 0.75, slope, dropletGradient0);
    // 旋转 37° 的第二层，梯度按逆旋转回到原 UV 空间
    real2 uvRot = real2(uv.x * 0.8 - uv.y * 0.6, uv.x * 0.6 + uv.y * 0.8);
    real2 dropletGradient1;
    real droplet1 = Weather_DropletMask(uvRot, dropletScale * 0.71, dropletAmount * 0.55, slope, dropletGradient1);
    real dropletGradientX = dropletGradient1.x * 0.8 + dropletGradient1.y * 0.6;
    real dropletGradientY = -dropletGradient1.x * 0.6 + dropletGradient1.y * 0.8;
    real droplet = max(droplet0, droplet1);
    real2 dropletGradient = droplet0 >= droplet1
        ? dropletGradient0
        : real2(dropletGradientX, dropletGradientY);

    real2 flowGradient;
    real flow = Weather_FlowMask(TEXTURE2D_ARGS(noiseTex, noiseSampler), uv, flowScale, flowSpeed, time, flowGradient)
        * smoothstep(0.05, 0.5, slope) * saturate(flowAmount);

    dropletMask = droplet * wet;
    flowMask = flow * wet;

    real3 worldX = TransformObjectToWorldDir(axisX);
    real3 worldY = TransformObjectToWorldDir(axisY);
    real3 tilt = (worldX * dropletGradient.x + worldY * dropletGradient.y) * (dropletMask * dropletNormal)
        - (worldX * flowGradient.x + worldY * flowGradient.y) * (flowMask * flowNormal);
    return SafeNormalize(normalWS + tilt);
}

// ════════════════════════════════════════════════════════════
//  吸水 — 布料把水吸进去后，表面细节法线逐渐消失
// ════════════════════════════════════════════════════════════

/// <summary>吸水遮罩：格点内的吸收中心按各自相位呼吸，距离越近吸得越彻底。</summary>
real Weather_AbsorbFade(real2 uv, real scale, real speed, real time)
{
    real2 cellUV = uv * max(scale, 1.0);
    real2 cell = floor(cellUV);
    real4 rnd = Hash_Sine14(dot(cell, real2(1.0, 311.7)) + 0.13);
    real2 center = lerp(real2(0.3, 0.3), real2(0.7, 0.7), rnd.xy);
    real radius = lerp(0.25, 0.75, rnd.z);
    real dist = length(frac(cellUV) - center);
    // 过渡带很窄：吸收区内部几乎吃干净细节，块状轮廓才读得出来
    real spot = 1.0 - smoothstep(radius * 0.82, max(radius, 1e-3), dist);
    // 相位错开：整片不同时干湿，避免吸水像资源切换
    real pulse = 0.5 + 0.5 * sin(time * max(speed, 1e-3) * 2.0 + rnd.w * 6.2831853);
    return saturate(spot * lerp(0.75, 1.0, pulse));
}

// ════════════════════════════════════════════════════════════
//  雨滴涟漪 — 朝上表面的扩散环
// ════════════════════════════════════════════════════════════

/// <summary>涟漪遮罩：每格一圈随机的扩散环，输出径向梯度与环遮罩。</summary>
real Weather_RainRipple(real2 uv, real scale, real speed, real time, out real2 gradient)
{
    real2 cellUV = uv * max(scale, 1.0);
    real2 cell = floor(cellUV);
    real2 local = frac(cellUV);
    real4 rnd = Hash_Sine14(dot(cell, real2(1.0, 311.7)) + 0.71);
    real2 center = lerp(real2(0.25, 0.25), real2(0.75, 0.75), rnd.xy);
    real2 offset = local - center;
    real dist = length(offset);

    real phase = frac(time * max(speed, 1e-3) + rnd.w);
    real radius = phase * 0.45;
    real ring = saturate(1.0 - abs(dist - radius) / 0.045);
    ring *= ring * (1.0 - phase) * step(0.15, rnd.z);
    // 环内外斜率相反，形成凸起的波峰而不是单向凹坑
    gradient = offset / max(dist, 1e-4) * (dist < radius ? -1.0 : 1.0);
    return ring;
}

/// <summary>雨滴涟漪：只作用于朝上表面，法线沿径向起伏并按遮罩加权。</summary>
float3 Weather_RainRipples(real3 positionOS, real3 normalOS, real3 normalWS,
    real wetness, real time, real amount, real scale, real speed, real normalStrength,
    out real rippleMask)
{
    rippleMask = 0.0;
    real wet = saturate(wetness) * saturate(amount);
    real upward = smoothstep(0.0, 0.5, normalOS.y);
    if (wet <= 0.0 || upward <= 0.0)
        return normalWS;

    real2 uv;
    real3 axisX;
    real3 axisY;
    Weather_ProjectAxis(normalOS, positionOS, uv, axisX, axisY);

    real2 gradient;
    real ripple = Weather_RainRipple(uv, scale, speed, time, gradient) * upward * wet;
    rippleMask = ripple;

    real3 worldX = TransformObjectToWorldDir(axisX);
    real3 worldY = TransformObjectToWorldDir(axisY);
    real3 tilt = (worldX * gradient.x + worldY * gradient.y) * (ripple * normalStrength);
    return SafeNormalize(normalWS + tilt);
}

// ════════════════════════════════════════════════════════════
//  积雪 — 朝上程度、噪声抖动与雪面法线
// ════════════════════════════════════════════════════════════

/// <summary>积雪遮罩：世界朝上分量经噪声抖动后过 smoothstep，再乘总雪量。</summary>
real Weather_SnowMask(real normalUp, real noise, real amount, real threshold, real softness)
{
    real soft = max(softness, 1e-4);
    real signal = normalUp + (noise - 0.5) * soft;
    return saturate(smoothstep(threshold - soft, threshold + soft, signal) * saturate(amount));
}

/// <summary>雪面法线：按遮罩向世界上方混合，让积雪按自身朝向受光。</summary>
float3 Weather_SnowNormal(real3 normalWS, real snowMask, real blend)
{
    real weight = saturate(snowMask * blend);
    return SafeNormalize(lerp(normalWS, WEATHER_SNOW_UP, weight));
}

/// <summary>雪面闪烁：世界 XZ 格点 Hash 阈值，按 NdotH 收束到高光方向。</summary>
real Weather_SnowSparkle(real3 positionWS, real scale, real density, real NdotH)
{
    float2 cell = floor(positionWS.xz * max(scale, 1.0));
    real hashed = Hash_Sine21(cell);
    real glint = step(1.0 - saturate(density), hashed);
    return glint * pow(saturate(NdotH), 8.0);
}

#endif // WEATHERFUNCTION_HLSL_INCLUDED
