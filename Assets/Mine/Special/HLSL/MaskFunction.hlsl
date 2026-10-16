// ═══════════════════════════════════════════════════════════════
//  MaskFunction — 显式原始深度测试与标量覆盖度组合
//  ⚠️ 调用方先 include Core.hlsl，提供当前 API 的 UNITY_REVERSED_Z。
//  输入深度须为同一相机/坐标约定的有限 [0,1] raw depth；tolerance >= 0，
//  单位为 raw depth。采样、资源绑定和像素坐标转换由调用方负责。
//  组合输入为 [0,1] 覆盖度：0 不参与，1 完全参与；支持软覆盖度。
//  零纹理/全局参数声明、零 CBUFFER、零兄弟 include。
// ═══════════════════════════════════════════════════════════════
#ifndef MASKFUNCTION_HLSL_INCLUDED
#define MASKFUNCTION_HLSL_INCLUDED

// ═══════════════════════════════════════════════════════════════
//  Mask_VisibleRawDepth — 比较片元与场景原始深度，容差内视为可见
// ═══════════════════════════════════════════════════════════════
float Mask_VisibleRawDepth(float fragmentDepth, float sceneDepth, float tolerance)
{
    #if UNITY_REVERSED_Z
        return step(sceneDepth - tolerance, fragmentDepth);
    #else
        return step(fragmentDepth, sceneDepth + tolerance);
    #endif
}

// ═══════════════════════════════════════════════════════════════
//  Mask_BackgroundRawDepth — 判断原始深度是否处于远平面清屏值的容差内
// ═══════════════════════════════════════════════════════════════
float Mask_BackgroundRawDepth(float rawDepth, float tolerance)
{
    #if UNITY_REVERSED_Z
        return step(rawDepth, tolerance);
    #else
        return step(1.0 - tolerance, rawDepth);
    #endif
}

// ═══════════════════════════════════════════════════════════════
//  Mask_Union — max 软并集，重叠部分保留较大覆盖度
// ═══════════════════════════════════════════════════════════════
float Mask_Union(float a, float b)
{
    return max(a, b);
}

// ═══════════════════════════════════════════════════════════════
//  Mask_Intersection — min 软交集，重叠部分保留较小覆盖度
// ═══════════════════════════════════════════════════════════════
float Mask_Intersection(float a, float b)
{
    return min(a, b);
}

// ═══════════════════════════════════════════════════════════════
//  Mask_Invert — 覆盖度取反，输入先限制到 [0,1]
// ═══════════════════════════════════════════════════════════════
float Mask_Invert(float value)
{
    return 1.0 - saturate(value);
}

// ═══════════════════════════════════════════════════════════════
//  Mask_Difference — 饱和相减的软差集
// ═══════════════════════════════════════════════════════════════
float Mask_Difference(float a, float b)
{
    return saturate(a - b);
}

#endif // MASKFUNCTION_HLSL_INCLUDED
