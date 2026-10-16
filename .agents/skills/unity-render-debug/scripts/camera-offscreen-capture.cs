using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// ═══════════════════════════════════════════════════════════════════════════════
//  相机显示 PNG — 实源：MaskBlur 的 SingleCameraRequest 离屏对照。
//  unityctl script execute 可运行完整文件；复制到会话 staging 后定制 Main。
//  调用方配置 camera.scene、Renderer、完整 rect、aspect、动态分辨率与 Debug。
// ═══════════════════════════════════════════════════════════════════════════════
public class Script
{
    public static object Main()
    {
        const int width = 640;
        const int height = 360;
        string task = DateTime.Now.ToString("yyyy-MM-dd") + "-render-debug-"
            + Guid.NewGuid().ToString("N").Substring(0, 8);
        string output = Path.Combine("tmp", "sessions", task, "output", "camera.png");
        return OffscreenCameraCapture.Capture(Camera.main, output, width, height);
    }
}

public static class OffscreenCameraCapture
{
    public sealed class Result
    {
        public string path;
        public string camera;
        public int width;
        public int height;
        public int bytes;
        public string format = "ARGB32 → RGBA32 PNG (display, not linear HDR)";
    }

    public static Result Capture(Camera camera, string outputPath, int width, int height)
    {
        if (camera == null)
            throw new ArgumentException("Select an existing, configured Game camera.", nameof(camera));
        if (camera.cameraType != CameraType.Game || camera.rect != new Rect(0f, 0f, 1f, 1f))
            throw new ArgumentException("Capture requires a Game camera with a full rect.", nameof(camera));
        if (camera.allowDynamicResolution)
            throw new ArgumentException("Configure the capture camera without dynamic resolution.", nameof(camera));
        if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)
            && data.renderType != CameraRenderType.Base)
            throw new ArgumentException("Capture requires a Base camera.", nameof(camera));
        if (width < 1 || height < 1 || width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
            throw new ArgumentOutOfRangeException(nameof(width), "Requested dimensions exceed supported texture sizes.");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Supply a writable PNG path.", nameof(outputPath));

        string fullPath = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must use the .png extension.", nameof(outputPath));
        if (File.Exists(fullPath))
            throw new IOException("Output already exists; choose another comparison filename: " + fullPath);

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture target = null;
        Texture2D image = null;
        try
        {
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "OffscreenCameraCapture",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                useDynamicScale = false
            };
            if (!target.Create())
                throw new InvalidOperationException("Could not create the capture RenderTexture.");
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException("The active pipeline does not support URP SingleCameraRequest.");
            RenderPipeline.SubmitRenderRequest(camera, request);

            image = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            image.Apply(false, false);
            byte[] png = image.EncodeToPNG();
            if (png == null || png.Length == 0)
                throw new InvalidOperationException("PNG encoding produced no data.");

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(png, 0, png.Length);
            return new Result
            {
                path = fullPath,
                camera = camera.name,
                width = target.width,
                height = target.height,
                bytes = png.Length
            };
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (image != null)
                UnityEngine.Object.DestroyImmediate(image);
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
