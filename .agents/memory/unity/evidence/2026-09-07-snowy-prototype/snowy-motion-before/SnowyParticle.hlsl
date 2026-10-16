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
//  SnowyParticleUV — 连续平移后分格，输出粒子采样 UV 和范围遮罩
// ════════════════════════════════════════════════════════════
float2 SnowyParticleUV(float2 uv, float time, out float bounds)
{
    float2 grid = max(floor(_Grid.xy + 0.5), 1.0);
    float2 position = uv - _Offset.xy - _Velocity.xy * time;
    float2 local = _Layout > 0.5 ? frac(position * grid) - 0.5 : position - 0.5;
    float angle = radians(_Rotation + _RotationSpeed * time);
    float2 size = clamp(_ParticleSize * abs(_Scale.xy), 0.0001, 0.7);
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
