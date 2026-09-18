// ════════════════════════════════════════════════════════════
//  TRS — 2D UV 空间 TRS 与 3D 纯数学变换。
//  零 URP include、零 CBUFFER、零纹理；aspect 由调用方传入，库不持有全局。
//  2D 接口一律内建 aspect —— UV 空间在非方形屏幕上各向异性，直接做 R/S 会剪切。
//  面片约定为单位正方形 [0,1]^2（中心 0.5），与 Sprite / 粒子贴图一致。
//  点变换顺序 T * R * S：先绕 pivot 缩放，再旋转，最后平移。
// ════════════════════════════════════════════════════════════

#ifndef TRS_HLSL_INCLUDED
#define TRS_HLSL_INCLUDED

// ════════════════════════════════════════════════════════════
//  等比空间基元 — 旋转是正交变换，只在等比空间成立
// ════════════════════════════════════════════════════════════

/// <summary>绕原点旋转；position 必须已在等比空间 —— 在 UV 空间直接调用会剪切。</summary>
float2 TRS2D_RotateIsotropic(float2 position, float radiansAngle)
{
    float sine;
    float cosine;
    sincos(radiansAngle, sine, cosine);
    return float2(cosine * position.x - sine * position.y,
                  sine * position.x + cosine * position.y);
}

// ════════════════════════════════════════════════════════════
//  UV 各向异性桥接 — u 乘除 aspect，使两轴单位长度的像素数一致
// ════════════════════════════════════════════════════════════

/// <summary>UV → 等比空间；aspect 由调用方提供，全屏后处理取 _ScreenParams.x / _ScreenParams.y。</summary>
float2 TRS2D_UVToIsotropic(float2 uv, float aspect)
{
    return float2(uv.x * max(aspect, 1e-4), uv.y);
}

/// <summary>等比空间 → UV；与 TRS2D_UVToIsotropic 互逆。</summary>
float2 TRS2D_IsotropicToUV(float2 isotropic, float aspect)
{
    return float2(isotropic.x / max(aspect, 1e-4), isotropic.y);
}

// ════════════════════════════════════════════════════════════
//  UV 空间 TRS — aspect 内建；正逆两式的参数语义相反
//  · R/S 在等比空间执行，T 留在 UV 空间：translation 是位置而非距离，
//    故 (0,1)->(1,0) 这类「角到角」位移语义不受 aspect 影响
//  · 采样是逐像素反查，可见物体 = 正变换作用在单位正方形上：
//      { p | M⁻¹(p) ∈ [0,1]² } = M([0,1]²)
//  · 故逆变换（采样用）的参数描述物体姿态，正变换的参数描述查找坐标姿态
//  · 参数：pivot 为物体在 UV 空间的落点，translation 为 UV 空间位移，
//    scale 为等比空间尺寸（1.0 = 面片与屏幕等高）
// ════════════════════════════════════════════════════════════

/// <summary>正变换，把面片单位正方形的点映射到屏幕 UV；参数描述查找坐标姿态。</summary>
float2 TRS2D_TransformUV(float2 particleUV, float2 translation, float radiansAngle,
                         float2 scale, float2 pivot, float aspect)
{
    float2 local = TRS2D_RotateIsotropic((particleUV - 0.5) * scale, radiansAngle);
    float2 center = TRS2D_UVToIsotropic(pivot + translation, aspect);
    return TRS2D_IsotropicToUV(local + center, aspect);
}

/// <summary>逆变换，采样用；面片外返回 [0,1]^2 之外的值，调用方用 step 门控可见范围。</summary>
float2 TRS2D_InverseTransformUV(float2 uv, float2 translation, float radiansAngle,
                                float2 scale, float2 pivot, float aspect)
{
    float2 center = TRS2D_UVToIsotropic(pivot + translation, aspect);
    float2 local = TRS2D_RotateIsotropic(TRS2D_UVToIsotropic(uv, aspect) - center, -radiansAngle);
    return local / max(abs(scale), float2(1e-6, 1e-6)) * sign(scale) + 0.5;
}

// ════════════════════════════════════════════════════════════
//  3D 变换 — 物体局部 / 世界空间，不管相机；世界空间天然等比，无 aspect 概念
//  · 需要透视：先把点变到 camera space 再 TRS3D_ProjectCameraPoint，
//    不必为每个粒子构造 viewMatrix —— 这是「模拟 3D」路径
//  · 真实路径用 URP 的 TransformWorldToHClip / UNITY_MATRIX_VP，或由深度
//    重建 world position。共享库不持有 view / projection 全局，避免固化相机策略
//  · 后处理里没有可靠真实 3D 坐标时走模拟路径；要遮挡 / 视差 / 真实空间运动
//    必须额外重建 world position，并接受深度纹理与逆矩阵成本
// ════════════════════════════════════════════════════════════

/// <summary>四元数归一化；模长过小时按 1e-6 兜底。</summary>
float4 TRS3D_NormalizeQuaternion(float4 quaternion)
{
    return quaternion / max(length(quaternion), 1e-6);
}

/// <summary>四元数旋转点；输入四元数不必预先归一化。</summary>
float3 TRS3D_Rotate(float3 position, float4 quaternion)
{
    float4 q = TRS3D_NormalizeQuaternion(quaternion);
    float3 t = 2.0 * cross(q.xyz, position);
    return position + q.w * t + cross(q.xyz, t);
}

/// <summary>四元数逆旋转；与 TRS3D_Rotate 互逆。</summary>
float3 TRS3D_InverseRotate(float3 position, float4 quaternion)
{
    float4 q = TRS3D_NormalizeQuaternion(quaternion);
    return TRS3D_Rotate(position, float4(-q.xyz, q.w));
}

/// <summary>物体局部 / 世界空间的 T * R * S；不负责相机变换。</summary>
float3 TRS3D_TransformPoint(float3 position, float3 translation, float4 rotation,
                            float3 scale, float3 pivot)
{
    float3 local = (position - pivot) * scale;
    return TRS3D_Rotate(local, rotation) + pivot + translation;
}

/// <summary>TRS3D_TransformPoint 的逆。</summary>
float3 TRS3D_InverseTransformPoint(float3 position, float3 translation, float4 rotation,
                                   float3 scale, float3 pivot)
{
    float3 local = TRS3D_InverseRotate(position - pivot - translation, rotation);
    return local / max(abs(scale), float3(1e-6, 1e-6, 1e-6)) * sign(scale) + pivot;
}

/// <summary>camera-space 点 → [0,1] UV；深度过小时按 1e-4 兜底。</summary>
float2 TRS3D_ProjectCameraPoint(float3 cameraPoint, float focalLength,
                                float aspectRatio)
{
    float safeDepth = max(cameraPoint.z, 1e-4);
    float2 ndc;
    ndc.x = focalLength * cameraPoint.x / safeDepth / max(aspectRatio, 1e-4);
    ndc.y = focalLength * cameraPoint.y / safeDepth;
    return ndc * 0.5 + 0.5;
}

/// <summary>TRS3D_ProjectCameraPoint 的逆；depth 为 camera-space 深度。</summary>
float3 TRS3D_UnprojectCameraUV(float2 uv, float depth, float focalLength,
                               float aspectRatio)
{
    float2 ndc = uv * 2.0 - 1.0;
    float safeFocal = max(focalLength, 1e-4);
    return float3(ndc.x * depth * aspectRatio / safeFocal,
                  ndc.y * depth / safeFocal,
                  depth);
}

#endif // TRS_HLSL_INCLUDED
