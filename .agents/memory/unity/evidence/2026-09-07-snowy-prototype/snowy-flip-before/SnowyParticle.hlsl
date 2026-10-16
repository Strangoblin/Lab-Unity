// ════════════════════════════════════════════════════════════
//  SnowyParticle — 单层网格映射与以粒子中心为枢轴的逆变换
// ════════════════════════════════════════════════════════════
#ifndef MINE_SNOWY_PARTICLE_INCLUDED
#define MINE_SNOWY_PARTICLE_INCLUDED

// ════════════════════════════════════════════════════════════
//  SnowyInverseRotate — 将采样坐标逆旋转到粒子局部空间
// ════════════════════════════════════════════════════════════
float2 SnowyInverseRotate(float2 position, float angle)
{
    float sine;
    float cosine;
    sincos(angle, sine, cosine);
    return float2(cosine * position.x + sine * position.y,
                 -sine * position.x + cosine * position.y);
}

// ════════════════════════════════════════════════════════════
//  SnowyMotionOffset — 指定方向漂移与垂直于该方向的周期摆动
// ════════════════════════════════════════════════════════════
float2 SnowyMotionOffset(float time)
{
    float speed = length(_Velocity.xy);
    float2 sideways = speed > 0.00001
        ? float2(-_Velocity.y, _Velocity.x) / max(speed, 0.00001)
        : float2(1.0, 0.0);
    float sway = _SwayAmplitude * sin(TWO_PI * _SwayFrequency * time);
    return _Velocity.xy * time + sideways * sway;
}

// ════════════════════════════════════════════════════════════
//  SnowyCellPhase — 从随粒子移动的网格编号生成稳定相位
// ════════════════════════════════════════════════════════════
float SnowyCellPhase(float2 cellID)
{
    float2 wrappedID = cellID - floor(cellID / 289.0) * 289.0;
    float hash = frac(sin(dot(wrappedID, float2(127.1, 311.7))) * 43758.5453);
    return hash * TWO_PI * saturate(_PhaseVariation);
}

// ════════════════════════════════════════════════════════════
//  SnowyParticleUV — 连续平移后分格，输出粒子采样 UV 和范围遮罩
// ════════════════════════════════════════════════════════════
float2 SnowyParticleUV(float2 uv, float time, out float bounds)
{
    float2 grid = max(floor(_Grid.xy + 0.5), 1.0);
    float2 position = uv - _Offset.xy - SnowyMotionOffset(time);
    float2 local = _Layout > 0.5 ? frac(position * grid) - 0.5 : position - 0.5;
    float phase = _Layout > 0.5 ? SnowyCellPhase(floor(position * grid)) : 0.0;
    float rotationWave = sin(TWO_PI * _RotationFrequency * time + phase) - sin(phase);
    float angle = radians(_Rotation + _RotationSpeed * time + _RotationAmplitude * rotationWave);
    float pulse = 1.0 + clamp(_ScaleAmplitude, 0.0, 0.95)
        * sin(TWO_PI * _ScaleFrequency * time + phase);
    float2 size = clamp(_ParticleSize * abs(_Scale.xy) * pulse, 0.0001, 0.7);
    float2 particleUV = SnowyInverseRotate(local, angle) / size + 0.5;
    float2 inside = step(0.0, particleUV) * step(particleUV, 1.0);
    bounds = inside.x * inside.y * step(0.00001, _ParticleSize)
             * step(0.00001, abs(_Scale.x)) * step(0.00001, abs(_Scale.y));
    return particleUV;
}

// ════════════════════════════════════════════════════════════
//  SnowyGridLines — 固定屏幕网格，便于观察粒子跨格位移
// ════════════════════════════════════════════════════════════
float SnowyGridLines(float2 uv)
{
    float2 grid = _Layout > 0.5 ? max(floor(_Grid.xy + 0.5), 1.0) : 1.0;
    float2 cell = uv * grid;
    float2 edge = min(frac(cell), 1.0 - frac(cell));
    float2 gridLine = 1.0 - smoothstep(0.0, max(fwidth(cell), 0.00001), edge);
    return max(gridLine.x, gridLine.y) * _ShowGrid;
}
#endif
