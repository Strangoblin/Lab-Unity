#ifndef RAINDROP_INCLUDED
#define RAINDROP_INCLUDED

#include "Assets/Mine/Special/HLSL/HashFunction.hlsl"

// ============================================================================
//  RainDrop.hlsl — 程序化雨滴工具库
//  ============================================================================
//  依赖: Core.hlsl (Unity URP)；内部包含 HashFunction.hlsl
//  使用: #include "Assets/Mine/Shaders/Render/RainDrops/RainDrop.hlsl"
//  ============================================================================

// ---- 数据结构 ----

struct DropShapeResult
{
    float  mask;
    float  normDist;
    float2 radialDir;
    float2 offset;
};

struct DropLayerResult
{
    float2 uv;
    float2 offset;
    float  mask;
    float  normDist;
    float2 radialDir;
};

// ---- 工具函数 ----

float2 Hash2D(float2 p)
{
    return Hash_FracGrid22(p);
}

float Saw(float b, float t)
{
    return smoothstep(0.0, b, t) * smoothstep(1.0, b, t);
}

// ---- 空间变换 ----

float2 RemapUV(float2 objectUV, float2 gridScale, float time, float speed, float wiggle)
{
    float2 uv = objectUV;
    float  rand = sin(uv.y * 6.2831) * 0.25 + 1;
    uv.x += sin(uv.y * 6.2831 * rand) * 0.02;
    uv.x += sin(uv.x * 6.2831 * rand) * 0.05;

    float2 gridUV = uv * gridScale;
    float colIdx = floor(gridUV.x);
    float colR = Hash2D(float2(colIdx, 137.0)).x;
    time *= speed;
    uv.y *= sin(uv.y * colR * 6.2831 * 0.25) * 0.5 + 0.5;
    uv.y += time * colR;

    float slipCycle = time * 0.12 + colR * 2.0;
    float slipSlope = pow(frac(slipCycle), 4.0);
    float slipOffset = (floor(slipCycle) + slipSlope) * 0.4 * wiggle;
    uv.y += slipOffset;

    return uv;
}

float2 RemapGridUV(float2 st, float2 rand, float dropLen, float aspect, float size)
{
    float2 uv = st - float2(0.5, 0.5);
    float scale = (rand.x * 0.4 + 1.2) / max(size, 1e-6);

    uv.y *= lerp(aspect, 1.0 / scale, dropLen);
    uv *= scale;
    uv += rand;

    return uv;
}

// ---- 形状采样 ----

DropShapeResult DropShape(float2 shapeUV, float intensity, float refraction, bool facingCamera)
{
    DropShapeResult r = (DropShapeResult)0;

    float dx = shapeUV.x;
    float dy = shapeUV.y;

    float dist;
    if (dy > 0.0)
    {
        float taper = 1.0 - saturate(dy);
        float rx = max(taper, 0.01);
        dist = length(float2(dx / rx, dy));
    }
    else
    {
        dist = length(float2(dx, dy));
    }

    r.mask = (1.0 - smoothstep(0.7, 1.0, dist)) * intensity;
    r.normDist = saturate(dist);
    r.radialDir = length(shapeUV) > 1e-6 ? normalize(shapeUV) : float2(0.0, 1.0);

    float t = r.normDist;
    if (facingCamera)
    {
        float lens = (t - 0.45) * 2.5;
        r.offset = r.radialDir * lens * refraction * r.mask;
    }
    else
    {
        float lens = t * (1.0 - t) * 4.0;
        r.offset = r.radialDir * lens * refraction * r.mask;
    }

    return r;
}

// ---- 编排层 ----

struct DropConfig
{
    float  speed;
    float  wiggle;
    float  range;
    float2 gridScale;
    float  coverage;
    float  dropLen;
    float  dropSize;
    float  dropFacing;
    float  sawSpeed;
    float  sawSmooth;
    float  refraction;
    bool   applyRemap;
};

DropLayerResult DropLayer(float2 objectUV, float time, DropConfig cfg)
{
    DropLayerResult result = (DropLayerResult)0;

    float aspect = cfg.gridScale.x / cfg.gridScale.y;
    float2 uv = cfg.applyRemap ? RemapUV(objectUV, cfg.gridScale, time, cfg.speed, cfg.wiggle) : objectUV;

    float2 gridUV = uv * cfg.gridScale;
    float2 id = floor(gridUV);
    float2 st = frac(gridUV);
    float2 rand = Hash2D(id);
    result.uv = st;

    if (rand.x >= cfg.coverage)
        return result;

    float ti = frac(time * cfg.sawSpeed + rand.y * 0.618 + rand.x * 0.382);
    float intensity = Saw(cfg.sawSmooth, ti);
    rand = (rand - 0.5) * cfg.range;
    bool facing = cfg.dropFacing > 0.5;
    float2 shapeUV = RemapGridUV(st, rand, cfg.dropLen, aspect, cfg.dropSize);
    DropShapeResult drop = DropShape(shapeUV, intensity, cfg.refraction, facing);

    result.mask   = drop.mask;
    result.offset = drop.offset;

    if (drop.mask > 0.0)
    {
        result.normDist  = drop.normDist;
        result.radialDir = drop.radialDir;
    }

    return result;
}

// ---- 层级混合 ----

DropLayerResult BlendLayer(DropLayerResult a, DropLayerResult b)
{
    float wa = a.mask;
    float wb = b.mask;
    float total = wa + wb;

    DropLayerResult r;
    r.mask = max(wa, wb);

    if (total > 0.001)
    {
        r.offset    = (a.offset    * wa + b.offset    * wb) / total;
        r.normDist  = (a.normDist  * wa + b.normDist  * wb) / total;
        r.radialDir = normalize(a.radialDir * wa + b.radialDir * wb);
    }
    else
    {
        r = (DropLayerResult)0;
    }

    r.uv = wa > wb ? a.uv : b.uv;
    return r;
}

#endif
