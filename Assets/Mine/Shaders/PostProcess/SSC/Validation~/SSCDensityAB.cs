using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// 同会话 A/B：把一份旧的 SSC 源码编译成 Hidden shader，与当前 SSC.shader
// 交替渲染同一相机，逐浮点比较原始云缓冲与合成结果。
// 用途：证明纯收敛改动（去 branch、合并 heightDensity）不改变输出。
// baseline 默认 /tmp/ssc_head.shader；可传入任意保存的 shader 源码路径。
// 相机 cullingMask=0 + 纯色背景，排除场景材质动画造成的非确定性。
public class Script
{
    const string ShaderPath = "Assets/Mine/Shaders/PostProcess/SSC/SSC.shader";
    const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    const int Width = 960;
    const int Height = 540;

    public static object Main(string[] args)
    {
        string baselinePath = args != null && args.Length > 0 ? args[0] : "/tmp/ssc_head.shader";
        if (!File.Exists(baselinePath)) throw new Exception("Baseline source missing: " + baselinePath);
        const string declaration = "Shader \"PostProcess/SSC\"";
        string beforeSource = File.ReadAllText(baselinePath);
        if (!beforeSource.Contains(declaration)) throw new Exception("Baseline declaration missing");

        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData == null) throw new Exception("Renderer missing: " + RendererPath);
        SSCFeature feature = null;
        foreach (var entry in rendererData.rendererFeatures)
            if (entry is SSCFeature) feature = (SSCFeature)entry;
        if (feature == null || !feature.isActive) throw new Exception("Active SSC required");

        var scene = SceneManager.GetActiveScene();
        string originalSettings = JsonUtility.ToJson(feature.settings);
        bool dirtyBefore = scene.isDirty;
        string root = "tmp/sessions/" + DateTime.UtcNow.ToString("yyyy-MM-dd") + "-ssc-density-ab-"
                    + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.CreateDirectory(root);

        var report = new List<string>();
        var go = new GameObject("SSC density A/B camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        RenderTexture rt = null;
        Texture2D readback = null;
        Shader baseline = null;
        try
        {
            var position = new Vector3(0f, 25f, -90f);
            camera.transform.SetPositionAndRotation(
                position, Quaternion.LookRotation(new Vector3(0f, 50f, 0f) - position));
            camera.cameraType = CameraType.Game;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 2000f;
            camera.aspect = (float)Width / Height;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.allowHDR = true;
            camera.allowDynamicResolution = false;
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.20f, 0.25f, 0.35f, 1f);
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(0);
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
            cameraData.requiresDepthTexture = true;

            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                 { hideFlags = HideFlags.HideAndDontSave };
            readback = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true)
                       { hideFlags = HideFlags.HideAndDontSave };

            baseline = ShaderUtil.CreateShaderAsset(
                beforeSource.Replace(declaration, "Shader \"Hidden/SSC/DensityABBaseline\""));
            if (baseline == null) throw new Exception("Baseline shader compile failed");
            Shader current = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (current == null) throw new Exception("Current shader missing: " + ShaderPath);

            feature.settings.speed = 0f;
            feature.settings.jitter = 0f;

            report.Add("baseline=" + baselinePath + " sha256=" + Hash(File.ReadAllBytes(baselinePath)));
            report.Add("current=" + ShaderPath + " sha256=" + Hash(File.ReadAllBytes(ShaderPath)));
            report.Add("camera=" + position + " lookAt=(0,50,0) fov=60 " + Width + "x" + Height
                       + " ARGBHalf cullingMask=0 solidBG");
            report.Add("note=bufferOnly=true reads the raw cloud buffer (RGB premultiplied, A coverage)");

            foreach (int shift in new[] { 0, 2 })
            {
                foreach (bool bufferOnly in new[] { false, true })
                {
                    feature.settings.downsample = shift;
                    feature.settings.showSSC = bufferOnly;

                    Select(feature, baseline);
                    Capture(camera, rt, readback);
                    Select(feature, current);
                    Capture(camera, rt, readback);

                    Select(feature, baseline);
                    float[] a1 = Capture(camera, rt, readback);
                    Select(feature, current);
                    float[] b1 = Capture(camera, rt, readback);
                    Select(feature, baseline);
                    float[] a2 = Capture(camera, rt, readback);
                    Select(feature, current);
                    float[] b2 = Capture(camera, rt, readback);

                    float maxA, rmsA, changedA, maxB, rmsB, changedB, maxAB, rmsAB, changedAB;
                    Diff(a1, a2, out maxA, out rmsA, out changedA);
                    Diff(b1, b2, out maxB, out rmsB, out changedB);
                    Diff(a1, b1, out maxAB, out rmsAB, out changedAB);

                    string tag = "shift=" + shift + " bufferOnly=" + bufferOnly;
                    report.Add(string.Format(
                        "{0} control-baseline    max={1:E3} rms={2:E3} changed={3:P4}", tag, maxA, rmsA, changedA));
                    report.Add(string.Format(
                        "{0} control-current     max={1:E3} rms={2:E3} changed={3:P4}", tag, maxB, rmsB, changedB));
                    report.Add(string.Format(
                        "{0} baseline-vs-current max={1:E3} rms={2:E3} changed={3:P4} {4}",
                        tag, maxAB, rmsAB, changedAB, maxAB == 0f ? "IDENTICAL" : "DIFFERS"));
                    report.Add(string.Format(
                        "{0} mean RGB a=({1:F5},{2:F5},{3:F5}) b=({4:F5},{5:F5},{6:F5}) meanA a={7:F5} b={8:F5}",
                        tag, MeanChannel(a1, 0), MeanChannel(a1, 1), MeanChannel(a1, 2),
                        MeanChannel(b1, 0), MeanChannel(b1, 1), MeanChannel(b1, 2),
                        MeanChannel(a1, 3), MeanChannel(b1, 3)));
                }
            }

            File.WriteAllLines(Path.Combine(root, "ab.txt"), report);
            report.Add("output=" + root);
        }
        finally
        {
            JsonUtility.FromJsonOverwrite(originalSettings, feature.settings);
            feature.Create();
            if (baseline != null) UnityEngine.Object.DestroyImmediate(baseline);
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            UnityEngine.Object.DestroyImmediate(go);
        }
        return report;
    }

    static void Select(SSCFeature feature, Shader shader)
    {
        feature.settings.sscShader = shader;
        feature.Create();
    }

    static float[] Capture(Camera camera, RenderTexture rt, Texture2D readback)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        RenderTexture.active = rt;
        readback.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        readback.Apply(false, false);
        RenderTexture.active = null;
        return readback.GetRawTextureData<float>().ToArray();
    }

    static void Diff(float[] a, float[] b, out float max, out float rms, out float changed)
    {
        if (a == null || b == null || a.Length != b.Length) throw new Exception("Sample length mismatch");
        double sum = 0.0;
        int differing = 0;
        max = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            if (!float.IsFinite(a[i]) || !float.IsFinite(b[i]))
                throw new Exception("Non-finite sample at index " + i);
            float d = Mathf.Abs(a[i] - b[i]);
            if (d > max) max = d;
            if (d > 0f) differing++;
            sum += (double)d * d;
        }
        rms = (float)Math.Sqrt(sum / a.Length);
        changed = (float)differing / a.Length;
    }

    static double MeanChannel(float[] pixels, int channel)
    {
        double total = 0.0;
        for (int i = channel; i < pixels.Length; i += 4) total += pixels[i];
        return total / (pixels.Length / 4.0);
    }

    static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    static string HashFloat(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Hash(bytes);
    }
}
