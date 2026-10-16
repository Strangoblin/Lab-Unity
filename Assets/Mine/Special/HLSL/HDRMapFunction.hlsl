// ════════════════════════════════════════════════════════════════════════════
// HDRMapFunction — HDR 方向映射与统一采样；不包含室内求交或光照积分。
// ⚠️ 调用方先 include URP Core.hlsl（提供 Packing / texture macros），target 4.5。
// 无全局纹理、无 CBUFFER、无兄弟 include；纹理和 sampler 由调用方传入。
//
// 轴约定：+X right / +Y up / +Z forward；UV 原点在左下。
// 经纬 lon=atan2(x,z)，v=asin(y)/PI+.5；+Z 位于 (.5,.5)。
// 八面体的折叠极轴为 Y，+Y 位于中心；标准编码复用 Unity Core Packing。
// 等面积沿用文章 Fast Equal-Area Mapping 的精确基线，轴线使用非零符号。
// Hemisphere 是 +Z 正交半球圆盘，外部透明；Interior coverage 也只保留 +Z。
// Alpha 始终属于图像内容/覆盖，不储存格式或其他元数据。
//
// 统一入口 HDRMapSampleLOD / HDRMapSampleGrad：天空传观察方向，物体传所需
// 方向，平面室内先在外层做 UV/房间求交，再将命中点转换为此坐标系。
// 双线性采样按投影重连边缘；LOD 是普通纹理 mip，绝不表示反射粗糙度。
// ════════════════════════════════════════════════════════════════════════════

#ifndef HDRMAPFUNCTION_HLSL_INCLUDED
#define HDRMAPFUNCTION_HLSL_INCLUDED

#define HDRMAP_EQUIRECTANGULAR 0
#define HDRMAP_OCTAHEDRAL 1
#define HDRMAP_EQUAL_AREA_OCTAHEDRAL 2
#define HDRMAP_HEMISPHERE 3
#define HDRMAP_PANORAMA 0
#define HDRMAP_INTERIOR 1

float2 HDRMapSignNotZero(float2 value)
{
    return float2(value.x < 0.0 ? -1.0 : 1.0, value.y < 0.0 ? -1.0 : 1.0);
}

float3 HDRMapSafeDirection(float3 direction)
{
    float lengthSquared = dot(direction, direction);
    return lengthSquared > 1e-12 ? direction * rsqrt(lengthSquared) : float3(0.0, 0.0, 1.0);
}

float HDRMapCoverageMask(float3 direction, int coverage)
{
    return coverage == HDRMAP_INTERIOR ? step(0.0, direction.z) : 1.0;
}

float2 HDRMapEncodeEqualArea(float3 direction)
{
    // r and azimuth invert the article's concentric square -> sphere formula.
    float radius = sqrt(saturate(1.0 - abs(direction.y)));
    float azimuth = abs(direction.x) + abs(direction.z) > 1e-7
        ? atan2(abs(direction.z), abs(direction.x)) * (2.0 / PI) : 0.0;
    float2 square = radius * float2(1.0 - azimuth, azimuth);
    if (direction.y < 0.0)
        square = 1.0 - square.yx;
    return (square * HDRMapSignNotZero(direction.xz)) * 0.5 + 0.5;
}

float3 HDRMapDecodeEqualArea(float2 uv)
{
    float2 square = uv * 2.0 - 1.0;
    float diamond = 1.0 - abs(square.x) - abs(square.y);
    float radius = 1.0 - abs(diamond);
    // The centre and four corners have r=0. Avoid 0/0 even at odd resolutions.
    float azimuth = radius > 1e-7
        ? (PI * 0.25) * ((abs(square.y) - abs(square.x)) / radius + 1.0) : 0.0;
    float radial = radius * sqrt(max(2.0 - radius * radius, 0.0));
    float2 signs = HDRMapSignNotZero(square);
    float vertical = (diamond < 0.0 ? -1.0 : 1.0) * (1.0 - radius * radius);
    return float3(radial * signs.x * cos(azimuth), vertical, radial * signs.y * sin(azimuth));
}

float2 HDRMapDirectionToUV(float3 direction, int projection, out float valid)
{
    valid = step(1e-12, dot(direction, direction));
    direction = HDRMapSafeDirection(direction);
    if (projection == HDRMAP_EQUIRECTANGULAR)
    {
        float horizontalLength = abs(direction.x) + abs(direction.z);
        float longitude = horizontalLength > 1e-7 ? atan2(direction.x, direction.z) : 0.0;
        return float2(frac(longitude * (0.5 / PI) + 0.5), asin(clamp(direction.y, -1.0, 1.0)) / PI + 0.5);
    }
    if (projection == HDRMAP_OCTAHEDRAL)
        return PackNormalOctQuadEncode(direction.xzy) * 0.5 + 0.5;
    if (projection == HDRMAP_EQUAL_AREA_OCTAHEDRAL)
        return HDRMapEncodeEqualArea(direction);
    if (projection == HDRMAP_HEMISPHERE)
    {
        valid *= step(0.0, direction.z);
        return direction.xy * 0.5 + 0.5;
    }
    valid = 0.0;
    return float2(0.5, 0.5);
}

float3 HDRMapUVToDirection(float2 uv, int projection, out float valid)
{
    valid = 1.0;
    if (projection == HDRMAP_EQUIRECTANGULAR)
    {
        float longitude = (uv.x - 0.5) * (2.0 * PI);
        float latitude = (uv.y - 0.5) * PI;
        float latitudeCosine = cos(latitude);
        return float3(sin(longitude) * latitudeCosine, sin(latitude), cos(longitude) * latitudeCosine);
    }
    if (projection == HDRMAP_OCTAHEDRAL)
        return UnpackNormalOctQuadEncode(uv * 2.0 - 1.0).xzy;
    if (projection == HDRMAP_EQUAL_AREA_OCTAHEDRAL)
        return HDRMapDecodeEqualArea(uv);
    if (projection == HDRMAP_HEMISPHERE)
    {
        float2 disk = uv * 2.0 - 1.0;
        float radiusSquared = dot(disk, disk);
        valid = step(radiusSquared, 1.0);
        return float3(disk, sqrt(max(1.0 - radiusSquared, 0.0)));
    }
    valid = 0.0;
    return float3(0.0, 0.0, 1.0);
}

int2 HDRMapRemapTexel(int2 texel, int2 size, int projection)
{
    if (projection == HDRMAP_EQUIRECTANGULAR)
    {
        if (texel.x < 0) texel.x += size.x;
        else if (texel.x >= size.x) texel.x -= size.x;
        texel.y = clamp(texel.y, 0, size.y - 1);
    }
    else if (projection == HDRMAP_OCTAHEDRAL || projection == HDRMAP_EQUAL_AREA_OCTAHEDRAL)
    {
        // Continuing across an octahedral edge reflects the other coordinate.
        if (texel.x < 0) { texel.x = -texel.x - 1; texel.y = size.y - texel.y - 1; }
        else if (texel.x >= size.x) { texel.x = 2 * size.x - texel.x - 1; texel.y = size.y - texel.y - 1; }
        if (texel.y < 0) { texel.y = -texel.y - 1; texel.x = size.x - texel.x - 1; }
        else if (texel.y >= size.y) { texel.y = 2 * size.y - texel.y - 1; texel.x = size.x - texel.x - 1; }
        texel = clamp(texel, int2(0, 0), size - 1);
    }
    else
        texel = clamp(texel, int2(0, 0), size - 1);
    return texel;
}

float4 HDRMapSampleMip(TEXTURE2D_PARAM(map, mapSampler), float2 uv, int projection, int mip)
{
    uint width, height, mipCount;
    map.GetDimensions(mip, width, height, mipCount);
    int2 size = int2(width, height);
    float2 pixel = uv * float2(width, height) - 0.5;
    int2 baseTexel = int2(floor(pixel));
    float2 weight = frac(pixel);
    float4 c00 = LOAD_TEXTURE2D_LOD(map, HDRMapRemapTexel(baseTexel, size, projection), mip);
    float4 c10 = LOAD_TEXTURE2D_LOD(map, HDRMapRemapTexel(baseTexel + int2(1, 0), size, projection), mip);
    float4 c01 = LOAD_TEXTURE2D_LOD(map, HDRMapRemapTexel(baseTexel + int2(0, 1), size, projection), mip);
    float4 c11 = LOAD_TEXTURE2D_LOD(map, HDRMapRemapTexel(baseTexel + int2(1, 1), size, projection), mip);
    return lerp(lerp(c00, c10, weight.x), lerp(c01, c11, weight.x), weight.y);
}

float4 HDRMapSampleLOD(TEXTURE2D_PARAM(map, mapSampler), float3 direction, int projection, int coverage, float lod)
{
    float valid;
    float2 uv = HDRMapDirectionToUV(direction, projection, valid);
    valid *= HDRMapCoverageMask(direction, coverage);
    if (valid < 0.5)
        return float4(0.0, 0.0, 0.0, 0.0);

    uint width, height, mipCount;
    map.GetDimensions(0, width, height, mipCount);
    lod = clamp(lod, 0.0, max((float)mipCount - 1.0, 0.0));
    int lowerMip = (int)floor(lod);
    float4 lower = HDRMapSampleMip(TEXTURE2D_ARGS(map, mapSampler), uv, projection, lowerMip);
    float blend = frac(lod);
    if (blend < 1e-5)
        return lower;
    float4 upper = HDRMapSampleMip(TEXTURE2D_ARGS(map, mapSampler), uv, projection, lowerMip + 1);
    return lerp(lower, upper, blend);
}

float2 HDRMapUnwrapUV(float2 originUV, float2 nextUV, int projection)
{
    if (projection == HDRMAP_EQUIRECTANGULAR)
        return nextUV - float2(round(nextUV.x - originUV.x), 0.0);
    if (projection != HDRMAP_OCTAHEDRAL && projection != HDRMAP_EQUAL_AREA_OCTAHEDRAL)
        return nextUV;

    float2 closest = nextUV;
    float2 candidates[4] = {
        float2(-nextUV.x, 1.0 - nextUV.y), float2(2.0 - nextUV.x, 1.0 - nextUV.y),
        float2(1.0 - nextUV.x, -nextUV.y), float2(1.0 - nextUV.x, 2.0 - nextUV.y)
    };
    float bestDistance = dot(closest - originUV, closest - originUV);
    [unroll] for (int index = 0; index < 4; index++)
    {
        float distanceSquared = dot(candidates[index] - originUV, candidates[index] - originUV);
        if (distanceSquared < bestDistance) { bestDistance = distanceSquared; closest = candidates[index]; }
    }
    return closest;
}

// Gradients are derivatives of the unnormalized input direction, not UVs.
float4 HDRMapSampleGrad(TEXTURE2D_PARAM(map, mapSampler), float3 direction, float3 directionDx, float3 directionDy, int projection, int coverage)
{
    float valid;
    float2 uv = HDRMapDirectionToUV(direction, projection, valid);
    float2 nextX = HDRMapDirectionToUV(direction + directionDx, projection, valid);
    float2 nextY = HDRMapDirectionToUV(direction + directionDy, projection, valid);
    uint width, height, mipCount;
    map.GetDimensions(0, width, height, mipCount);
    float2 dx = (HDRMapUnwrapUV(uv, nextX, projection) - uv) * float2(width, height);
    float2 dy = (HDRMapUnwrapUV(uv, nextY, projection) - uv) * float2(width, height);
    float lod = 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-12));
    return HDRMapSampleLOD(TEXTURE2D_ARGS(map, mapSampler), direction, projection, coverage, lod);
}

// Sample uses mip zero; callers with direction derivatives can opt into
// HDRMapSampleGrad. Neither path performs lighting prefiltering.
float4 HDRMapSample(TEXTURE2D_PARAM(map, mapSampler), float3 direction, int projection, int coverage)
{
    return HDRMapSampleLOD(TEXTURE2D_ARGS(map, mapSampler), direction, projection, coverage, 0.0);
}

#endif // HDRMAPFUNCTION_HLSL_INCLUDED
