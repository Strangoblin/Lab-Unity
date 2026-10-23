using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Synchronized request-wall timing for the three SSC quality tiers.</summary>
/// <remarks>Run through unityctl; never saves scene or assets.</remarks>
public class Script
{
    const int Width = 1920;
    const int Height = 1080;
    const int Warmup = 6;
    const int Samples = 12;
    const int Rounds = 3;
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static object Main()
    {
        string root = "tmp/sessions/2026-10-20-ssc-tiers/benchmark-"
            + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.CreateDirectory(root);
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        SSCFeature feature = rd.rendererFeatures.OfType<SSCFeature>().SingleOrDefault();
        if (feature == null || !feature.isActive) throw new Exception("Active SSC required");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        string settingsBefore = JsonUtility.ToJson(feature.settings);
        bool dirtyBefore = scene.isDirty;
        RenderTexture previous = RenderTexture.active;
        GameObject owner = null;
        RenderTexture target = null;
        Texture2D readback = null;
        string failure = null;
        var rows = new StringBuilder("round,order,quality,rays,shift,milliseconds\n");
        var samples = new Dictionary<SSCFeature.Quality, List<double>>();
        foreach (var quality in new[] { SSCFeature.Quality.Low, SSCFeature.Quality.Medium, SSCFeature.Quality.High })
            samples[quality] = new List<double>();
        try
        {
            Camera source = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : Camera.main;
            if (source == null) throw new Exception("SceneView or Main camera required");
            owner = new GameObject("SSC tier benchmark camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = owner.AddComponent<Camera>();
            camera.CopyFrom(source);
            camera.enabled = false;
            camera.cameraType = CameraType.Game;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.aspect = (float)Width / Height;
            camera.allowHDR = true;
            camera.allowDynamicResolution = false;
            camera.allowMSAA = false;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(0);
            cameraData.requiresDepthTexture = true;
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;

            feature.settings.speed = 0;
            feature.settings.jitter = 0;
            feature.settings.showSSC = false;
            feature.Create();
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            if (!target.Create()) throw new Exception("target creation failed");
            readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new Exception("request unsupported");
            var ordered = new[] { SSCFeature.Quality.Low, SSCFeature.Quality.Medium, SSCFeature.Quality.High };
            for (int round = 0; round < Rounds; round++)
            {
                var order = round % 2 == 0 ? ordered : ordered.Reverse().ToArray();
                for (int orderIndex = 0; orderIndex < order.Length; orderIndex++)
                {
                    var quality = order[orderIndex];
                    feature.settings.quality = quality;
                    feature.Create();
                    SSCFeature.Tier tier = feature.settings.Budget;
                    for (int warm = 0; warm < Warmup; warm++) Render(camera, request, readback);
                    for (int sample = 0; sample < Samples; sample++)
                    {
                        double milliseconds = Render(camera, request, readback);
                        samples[quality].Add(milliseconds);
                        rows.Append(round).Append(',').Append(orderIndex).Append(',')
                            .Append(quality).Append(',').Append(Rays(tier.rayCount)).Append(',')
                            .Append(tier.downsample).Append(',')
                            .Append(milliseconds.ToString("R", Invariant)).Append('\n');
                    }
                }
            }
            File.WriteAllText(Path.Combine(root, "samples.csv"), rows.ToString());
            var summary = new StringBuilder("quality,rays,shift,samples,min_ms,median_ms,p95_ms,max_ms\n");
            foreach (var quality in ordered)
            {
                var sorted = samples[quality].OrderBy(x => x).ToList();
                summary.Append(quality).Append(',').Append(Rays(SSCFeature.GetTier(quality).rayCount)).Append(',')
                    .Append(SSCFeature.GetTier(quality).downsample).Append(',').Append(sorted.Count).Append(',')
                    .Append(sorted.First().ToString("R", Invariant)).Append(',')
                    .Append(Median(sorted).ToString("R", Invariant)).Append(',')
                    .Append(sorted[(int)Math.Ceiling(sorted.Count * 0.95) - 1].ToString("R", Invariant)).Append(',')
                    .Append(sorted.Last().ToString("R", Invariant)).Append('\n');
            }
            File.WriteAllText(Path.Combine(root, "summary.csv"), summary.ToString());
            return Path.GetFullPath(Path.Combine(root, "summary.csv"));
        }
        catch (Exception error)
        {
            failure = error.ToString();
            throw;
        }
        finally
        {
            JsonUtility.FromJsonOverwrite(settingsBefore, feature.settings);
            feature.SetActive(true);
            feature.Create();
            RenderTexture.active = previous;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            bool restored = JsonUtility.ToJson(feature.settings) == settingsBefore && scene.isDirty == dirtyBefore;
            File.WriteAllText(Path.Combine(root, "summary.txt"),
                "device=" + SystemInfo.graphicsDeviceName + "\napi=" + SystemInfo.graphicsDeviceType + "\n" +
                "resolution=" + Width + "x" + Height + "\nwarmup=" + Warmup + "\nsamples_per_round=" + Samples +
                "\nrounds=" + Rounds + "\npassed=" + (failure == null && restored) + "\nrestored=" + restored +
                "\nscene_dirty_before=" + dirtyBefore + "\nscene_dirty_after=" + scene.isDirty + "\n" +
                (failure == null ? "" : "failure=" + failure + "\n"));
        }
    }

    static double Render(Camera camera, UniversalRenderPipeline.SingleCameraRequest request, Texture2D readback)
    {
        var watch = Stopwatch.StartNew();
        RenderPipeline.SubmitRenderRequest(camera, request);
        RenderTexture.active = request.destination;
        readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
        readback.Apply(false, false);
        watch.Stop();
        return watch.Elapsed.TotalMilliseconds;
    }

    static int Rays(SSCFeature.RayCount rays)
    {
        switch (rays)
        {
            case SSCFeature.RayCount.COUNT32: return 32;
            case SSCFeature.RayCount.COUNT128: return 128;
            case SSCFeature.RayCount.COUNT256: return 256;
            default: return 64;
        }
    }

    static double Median(List<double> sorted)
    {
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) * 0.5;
    }
}
