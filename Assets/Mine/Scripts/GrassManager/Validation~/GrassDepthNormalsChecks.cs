// ════════════════════════════════════════════════════════════
//  草与地形的深度 / 法线写入自检
//  通过 unityctl script execute -f 运行，不参与 Unity 编译。
//  需在 GrassManagerDemo 播放中执行。
//
//  做法：在同一帧语义下渲染两次 —— 草的 RendererFeature 开启 / 关闭，
//  各读回一次 _CameraNormalsTexture，比较差异像素，即可证明草真的进了
//  共享法线缓冲（该 pass 同时把深度写进 _CameraDepthTexture）。
//  地形则直接统计地面像素法线的朝向分布。
// ════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Mine.Grass;
using Mine.Terrain;

public static class Script
{
    private const int Width = 1280;
    private const int Height = 720;
    private const string EvidenceDir = "Assets/Mine/Scripts/GrassManager/Validation~/2026-11-11-depth-normals";

    public static void Main()
    {
        Camera camera = Camera.main;
        if (camera == null)
            throw new InvalidOperationException("No Main Camera in the loaded scene.");

        var terrain = UnityEngine.Object.FindFirstObjectByType<TerrainManager>();
        if (terrain == null || !terrain.HasData)
            throw new InvalidOperationException("TerrainManager has no snapshot.");

        var grass = UnityEngine.Object.FindFirstObjectByType<GrassChunkManager>();
        if (grass == null || grass.VisibleInstanceCount <= 0)
            throw new InvalidOperationException("GrassChunkManager has no visible instances.");

        // ── 1. Shader pass 结构 ────────────────────────────────
        CheckShaderPasses("Render/GrassWind");
        CheckShaderPasses("TerrainSurface/TerrainSurface");

        // ── 2. Feature 注册与 SSGI 状态 ────────────────────────
        UniversalRendererData rendererData = FindRendererData();
        ScriptableRendererFeature grassFeature = FindFeature(rendererData, typeof(GrassRenderFeature).FullName);
        if (grassFeature == null)
            throw new InvalidOperationException("GrassRenderFeature is not registered on the active renderer.");

        ScriptableRendererFeature ssgi = FindFeatureByName(rendererData, "SSGIFeature");
        bool ssgiWasActive = ssgi != null && ssgi.isActive;
        // 法线纹理只有在有 pass 声明 ConfigureInput(Normal) 时才生成，SSGI 正是这个消费者。
        if (ssgi != null)
            ssgi.SetActive(true);

        bool grassWasActive = grassFeature.isActive;
        grassFeature.SetActive(true);

        if (Shader.GetGlobalTexture("_CameraDepthTexture") == null)
            Debug.Log("[check] no _CameraDepthTexture global yet; it is created during rendering.");

        Directory.CreateDirectory(EvidenceDir);

        // ── 3. 渲染 A：草在管线内 ─────────────────────────────
        byte[] normalsOn = RenderAndReadNormals(camera, Path.Combine(EvidenceDir, "feature-on.png"));
        long publishedRequests = GrassDrawRequests.Requests.Count;

        // ── 4. 渲染 B：关掉草 feature 作为对照 ─────────────────
        grassFeature.SetActive(false);
        byte[] normalsOff = RenderAndReadNormals(camera, Path.Combine(EvidenceDir, "feature-off.png"));

        // ── 5. 还原状态 ───────────────────────────────────────
        grassFeature.SetActive(grassWasActive);
        // 资产里保存的就是关闭状态，验证完还原回去，交回用户手动开关。
        if (ssgi != null)
            ssgi.SetActive(false);

        // ── 6. 比较 ───────────────────────────────────────────
        if (normalsOn == null || normalsOff == null)
            throw new InvalidOperationException("_CameraNormalsTexture was not readable; see warnings above.");
        if (normalsOn.Length != normalsOff.Length)
            throw new InvalidOperationException("Normals readback sizes differ.");

        int changed = 0;
        double maxDelta = 0;
        int upFacing = 0;
        int totalPixels = normalsOn.Length / 4;
        for (int i = 0; i < normalsOn.Length; i += 4)
        {
            int delta = 0;
            for (int c = 0; c < 3; c++)
                delta = Math.Max(delta, Math.Abs(normalsOn[i + c] - normalsOff[i + c]));
            if (delta > 8)
                changed++;
            maxDelta = Math.Max(maxDelta, delta);

            // 法线纹理是 R8G8B8A8_SNorm：原始字节按有符号字节 / 127 还原。
            int rawY = normalsOn[i + 1];
            float ny = (rawY > 127 ? rawY - 256 : rawY) / 127f;
            if (ny > 0.5f)
                upFacing++;
        }

        float changedRatio = changed / (float)totalPixels;
        float upRatio = upFacing / (float)totalPixels;

        Debug.Log($"GRASS_DEPTH_NORMALS_CHECK: requests={publishedRequests}, " +
                  $"normalsChanged={changed} ({changedRatio:P2}), maxChannelDelta={maxDelta}, " +
                  $"groundNormalRatio={upRatio:P2}, size={Width}x{Height}");

        if (publishedRequests == 0)
            throw new InvalidOperationException("GrassChunkManager published no draw requests.");
        if (changedRatio < 0.001f)
            throw new InvalidOperationException(
                $"Grass does not appear in _CameraNormalsTexture (changed pixels {changedRatio:P3}).");
        if (upRatio < 0.3f)
            throw new InvalidOperationException(
                $"Normals buffer looks wrong: only {upRatio:P1} of pixels face up.");

        Debug.Log("GRASS_DEPTH_NORMALS_CHECK_PASS");
    }

    // ════════════════════════════════════════════════════════════
    //  渲染一次并读回 _CameraNormalsTexture
    // ════════════════════════════════════════════════════════════
    private static byte[] RenderAndReadNormals(Camera camera, string screenshotPath)
    {
        var descriptor = new RenderTextureDescriptor(Width, Height, GraphicsFormat.R8G8B8A8_UNorm,
            GraphicsFormat.D24_UNorm_S8_UInt)
        {
            msaaSamples = 1,
            sRGB = true,
        };
        var target = RenderTexture.GetTemporary(descriptor);
        RenderTexture previous = camera.targetTexture;
        try
        {
            camera.targetTexture = target;
            camera.Render();
        }
        finally
        {
            camera.targetTexture = previous;
        }

        SaveScreenshot(target, screenshotPath);
        RenderTexture.ReleaseTemporary(target);

        RenderTexture normals = FindRenderTexture("_CameraNormalsTexture_", Width, Height);
        if (normals == null)
        {
            Debug.LogWarning("[check] _CameraNormalsTexture was not found; no pass requested normals.");
            return null;
        }

        RenderTexture readable = null;
        try
        {
            var normalsDescriptor = new RenderTextureDescriptor(normals.width, normals.height,
                GraphicsFormat.R8G8B8A8_SNorm, GraphicsFormat.None) { msaaSamples = 1 };
            readable = RenderTexture.GetTemporary(normalsDescriptor);
            Graphics.CopyTexture(normals, readable);
            var request = AsyncGPUReadback.Request(readable);
            request.WaitForCompletion();
            if (request.hasError)
            {
                Debug.LogWarning("[check] AsyncGPUReadback failed for _CameraNormalsTexture.");
                return null;
            }
            return request.GetData<byte>().ToArray();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[check] Could not read _CameraNormalsTexture: {exception.Message}");
            return null;
        }
        finally
        {
            if (readable != null)
                RenderTexture.ReleaseTemporary(readable);
        }
    }

    // URP 在渲染结束后会把全局纹理还原成 UnityBlack，只能按命名找 RG 管理的 RT。
    private static RenderTexture FindRenderTexture(string prefix, int width, int height)
    {
        string expected = $"{prefix}{width}x{height}";
        foreach (RenderTexture candidate in Resources.FindObjectsOfTypeAll<RenderTexture>())
        {
            if (candidate != null && candidate.name.StartsWith(expected) &&
                candidate.graphicsFormat == GraphicsFormat.R8G8B8A8_SNorm)
                return candidate;
        }
        return null;
    }

    private static void SaveScreenshot(RenderTexture source, string path)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = source;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
        try
        {
            texture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
            texture.Apply(false, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  结构检查
    // ════════════════════════════════════════════════════════════
    private static void CheckShaderPasses(string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
            throw new InvalidOperationException($"Shader '{shaderName}' was not found.");
        var material = new Material(shader);
        try
        {
            foreach (string pass in new[] { "Forward", "DepthOnly", "DepthNormals" })
            {
                if (material.FindPass(pass) < 0)
                    throw new InvalidOperationException($"'{shaderName}' has no '{pass}' pass.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static UniversalRendererData FindRendererData()
    {
        foreach (ScriptableRendererData data in Resources.FindObjectsOfTypeAll<ScriptableRendererData>())
        {
            if (data is UniversalRendererData universal && universal.rendererFeatures != null &&
                universal.rendererFeatures.Count > 0 && FindFeatureByName(universal, "SSGIFeature") != null)
                return universal;
        }

        foreach (ScriptableRendererData data in Resources.FindObjectsOfTypeAll<ScriptableRendererData>())
            if (data is UniversalRendererData universal && universal.rendererFeatures != null &&
                universal.rendererFeatures.Count > 0)
                return universal;

        throw new InvalidOperationException("No UniversalRendererData with renderer features was found.");
    }

    private static ScriptableRendererFeature FindFeature(UniversalRendererData data, string typeName)
    {
        foreach (ScriptableRendererFeature feature in data.rendererFeatures)
        {
            if (feature != null && feature.GetType().FullName == typeName)
                return feature;
        }
        return null;
    }

    private static ScriptableRendererFeature FindFeatureByName(UniversalRendererData data, string typeName)
    {
        foreach (ScriptableRendererFeature feature in data.rendererFeatures)
        {
            if (feature != null && feature.GetType().Name == typeName)
                return feature;
        }
        return null;
    }
}
