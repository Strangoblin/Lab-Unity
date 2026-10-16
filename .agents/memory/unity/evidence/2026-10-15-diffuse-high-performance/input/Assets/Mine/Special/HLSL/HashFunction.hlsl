// ═══════════════════════════════════════════════════════════════
//  HashFunction — 纯函数 Hash；算法族与输入输出维度显式区分
//  零管线/资源依赖。调用方提供种子，并负责时间、坐标与输出范围映射。
//  数字后缀表示输入→输出维度；接口与原式来源见 HashFunction.md。
// ═══════════════════════════════════════════════════════════════
#ifndef HASHFUNCTION_HLSL_INCLUDED
#define HASHFUNCTION_HLSL_INCLUDED

// ═══════════════════════════════════════════════════════════════
//  Hash_Sine21 — 二维点积正弦映射，输出 float [0,1)
// ═══════════════════════════════════════════════════════════════
float Hash_Sine21(float2 key)
{
    return frac(sin(dot(key, float2(12.9898, 78.233))) * 43758.5453);
}

// ═══════════════════════════════════════════════════════════════
//  Hash_Frac22 — 二维 frac/dot 混合，输出 float2 [0,1)
// ═══════════════════════════════════════════════════════════════
float2 Hash_Frac22(float2 key)
{
    key = frac(key * float2(123.34, 456.21));
    key += dot(key, key + 45.32);
    return frac(float2(key.x * key.y, key.x + key.y));
}

// ═══════════════════════════════════════════════════════════════
//  Hash_FracGrid22 — 格子/像素尺度的二维键，三分量混合，输出 float2 [0,1)
//  key 的单位间隔对应格子/像素；细密归一化 UV 不应直接使用此入口。
// ═══════════════════════════════════════════════════════════════
float2 Hash_FracGrid22(float2 key)
{
    float3 value = frac(float3(key.xyx) * float3(0.1031, 0.1030, 0.0973));
    value += dot(value, value.yzx + 33.33);
    return frac((value.xx + value.yz) * value.zy);
}

// ═══════════════════════════════════════════════════════════════
//  Hash_Sine14 — 标量种子的四通道正弦映射，输出 float4 [0,1)
// ═══════════════════════════════════════════════════════════════
float4 Hash_Sine14(float key)
{
    return frac(sin(key * float4(127.1, 311.7, 74.7, 269.5)) * 43758.5453);
}

// ═══════════════════════════════════════════════════════════════
//  Hash_UInt2SeedStream01 — 二维整数键、种子与序列编号混合，取高 24 位
// ═══════════════════════════════════════════════════════════════
#if SHADER_TARGET >= 35
float Hash_UInt2SeedStream01(uint2 key, uint seed, uint stream)
{
    uint value = key.x * 0x9E3779B9u;
    value ^= key.y * 0x85EBCA6Bu;
    value ^= seed * 0xC2B2AE35u;
    value ^= (stream + 1u) * 0x27D4EB2Du;
    value ^= value >> 16;
    value *= 0x7FEB352Du;
    value ^= value >> 15;
    value *= 0x846CA68Bu;
    value ^= value >> 16;
    return (float)(value >> 8) * (1.0 / 16777216.0);
}
#endif

#endif // HASHFUNCTION_HLSL_INCLUDED
