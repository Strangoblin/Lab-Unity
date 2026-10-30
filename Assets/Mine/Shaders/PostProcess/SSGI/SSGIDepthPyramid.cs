// ════════════════════════════════════════════════════════════════
//  SSGIDepthPyramid — request the shared per-camera trace depth hierarchy.
// ════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Mine.ScreenSpaceTrace;

internal static class SSGIDepthPyramid
{
    // Unified Diffuse/Specular and standalone Diffuse read opaque depth at this stage.
    public const int OpaqueStage = 0;
    // Standalone Specular executes after transparents and must not reuse an earlier stage.
    public const int AfterTransparentsStage = 1;

    public static bool TryGet(RenderGraph graph, ContextContainer frameData, UniversalResourceData resources,
        UniversalCameraData cameraData, ComputeShader shader, int sourceRevision,
        out TextureHandle pyramid, out int mipCount)
    {
        pyramid = TextureHandle.nullHandle;
        mipCount = 0;
        Camera camera = cameraData.camera;
        if (camera == null || shader == null || !resources.cameraDepthTexture.IsValid()
            || camera.rect != new Rect(0f, 0f, 1f, 1f))
            return false;
        TextureDesc depth = graph.GetTextureDesc(resources.cameraDepthTexture);
        int width = cameraData.cameraTargetDescriptor.width;
        int height = cameraData.cameraTargetDescriptor.height;
        // URP 17 binds _RTHandleScale = 1 in its camera globals; the shader also checks this.
        if (depth.sizeMode != TextureSizeMode.Explicit || depth.width != width || depth.height != height
            || depth.useDynamicScale || depth.useDynamicScaleExplicit || cameraData.cameraTargetDescriptor.useDynamicScale)
            return false;
        float near = camera.nearClipPlane;
        float far = camera.farClipPlane;
        float ratio = far / near;
        float x = SystemInfo.usesReversedZBuffer ? ratio - 1f : 1f - ratio;
        float y = SystemInfo.usesReversedZBuffer ? 1f : ratio;
        var request = new ScreenSpaceDepthPyramid.Request
        {
            Source = resources.cameraDepthTexture,
            Shader = shader,
            SourceRevision = sourceRevision,
            Width = width,
            Height = height,
            SourceUvScaleBias = new Vector4(1f, 1f, 0f, 0f),
            ZBufferParams = new Vector4(x, y, x / far, y / far),
            NearPlane = near,
            FarPlane = far,
            LinearDepth = false,
            Orthographic = camera.orthographic,
            ReversedZ = SystemInfo.usesReversedZBuffer
        };
        if (!ScreenSpaceDepthPyramid.TryGet(graph, frameData, request, out var result, out _)
            || result.Width != width || result.Height != height || result.MipCount < 1)
            return false;
        pyramid = result.Texture;
        mipCount = result.MipCount;
        return true;
    }
}
