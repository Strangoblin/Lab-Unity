using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Compares single-HG lighting with the isotropic multiple-scattering fill.</summary>
/// <remarks>Run through unityctl; never saves scene or assets.</remarks>
public class Script
{
    const int Width = 960;
    const int Height = 540;
    const float G = 0.8f;
    const float SecondaryWeight = 1.0f;
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    static int assertions;

    static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    static double Hg(double g, double mu)
    {
        double denominator = Math.Pow(1.0 + g * g - 2.0 * g * mu, 1.5);
        return (1.0 - g * g) / (4.0 * Math.PI * denominator);
    }

    public static object Main()
    {
        string root = "tmp/sessions/2026-10-20-ssc-tiers/scattering-"
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
        GameObject lightOwner = null;
        RenderTexture target = null;
        Texture2D linearReadback = null;
        Texture2D colorReadback = null;
        string failure = null;
        try
        {
            Camera source = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : Camera.main;
            if (source == null) throw new Exception("SceneView or Main camera required");
            owner = new GameObject("SSC scattering validation") { hideFlags = HideFlags.HideAndDontSave };
            var camera = owner.AddComponent<Camera>();
            camera.CopyFrom(source);
            camera.enabled = false;
            camera.cameraType = CameraType.Game;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.aspect = (float)Width / Height;
            int occupied = 0;
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                occupied |= 1 << renderer.gameObject.layer;
            int lightLayer = 31;
            while (lightLayer >= 0 && (occupied & (1 << lightLayer)) != 0) lightLayer--;
            if (lightLayer < 0) throw new Exception("No unused light layer");
            camera.gameObject.layer = lightLayer;
            camera.cullingMask = 1 << lightLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = true;
            camera.allowDynamicResolution = false;
            camera.allowMSAA = false;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(0);
            cameraData.requiresDepthTexture = true;
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;

            lightOwner = new GameObject("SSC scattering isolated main light") { hideFlags = HideFlags.HideAndDontSave };
            lightOwner.layer = lightLayer;
            Light isolatedLight = lightOwner.AddComponent<Light>();
            isolatedLight.type = LightType.Directional;
            isolatedLight.shadows = LightShadows.None;
            if (RenderSettings.sun != null)
            {
                isolatedLight.transform.rotation = RenderSettings.sun.transform.rotation;
                isolatedLight.color = RenderSettings.sun.color;
                isolatedLight.intensity = RenderSettings.sun.intensity;
            }
            else
            {
                isolatedLight.transform.rotation = Quaternion.Euler(50, -30, 0);
                isolatedLight.color = Color.white;
                isolatedLight.intensity = 1;
            }

            feature.settings.speed = 0;
            feature.settings.jitter = 0;
            feature.settings.showSSC = false;
            feature.settings.phaseG = G;
            feature.settings.secondaryScattering = 0f;
            feature.Create();

            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            Check(target.Create(), "target creation");
            linearReadback = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
            colorReadback = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Check(RenderPipeline.SupportsRenderRequest(camera, request), "request unsupported");

            feature.SetActive(false);
            Color[] background = Capture(camera, request, linearReadback, colorReadback, Path.Combine(root, "background.png"));
            feature.SetActive(true);

            feature.settings.secondaryScattering = 0f; feature.Create();
            Color[] singleHg = Capture(camera, request, linearReadback, colorReadback, Path.Combine(root, "single-hg.png"));
            feature.settings.secondaryScattering = SecondaryWeight; feature.Create();
            Color[] dual = Capture(camera, request, linearReadback, colorReadback, Path.Combine(root, "dual-hg.png"));
            feature.settings.secondaryScattering = 0f; feature.Create();
            Color[] repeat = Capture(camera, request, linearReadback, colorReadback, Path.Combine(root, "single-hg-repeat.png"));

            double repeatMax = 0.0;
            var cloudLuma = new List<float>();
            for (int i = 0; i < singleHg.Length; i++)
            {
                repeatMax = Math.Max(repeatMax, MaxRgb(singleHg[i] - repeat[i]));
                if (MaxRgb(background[i]) > 1e-6f) throw new Exception("Isolated background is not black");
                float luma = Luma(singleHg[i]);
                if (luma > 1e-6f) cloudLuma.Add(luma);
                Check(Finite(singleHg[i]) && Finite(dual[i]), "finite output");
            }
            Check(cloudLuma.Count > 1000, "cloud positive control");
            Check(repeatMax <= 1e-6, "paired repeat exact");
            cloudLuma.Sort();
            float p95 = cloudLuma[(int)((cloudLuma.Count - 1) * 0.95f)];
            float darkLimit = p95 * 0.25f, brightLimit = p95 * 0.75f;
            double darkOld = 0.0, darkNew = 0.0, brightOld = 0.0, brightNew = 0.0;
            int darkCount = 0, brightCount = 0;
            for (int i = 0; i < singleHg.Length; i++)
            {
                float oldLuma = Luma(singleHg[i]);
                if (oldLuma <= 1e-6f) continue;
                float newLuma = Luma(dual[i]);
                if (oldLuma <= darkLimit) { darkOld += oldLuma; darkNew += newLuma; darkCount++; }
                if (oldLuma >= brightLimit) { brightOld += oldLuma; brightNew += newLuma; brightCount++; }
            }
            Check(darkCount > 100 && brightCount > 100, "dark and bright cloud regions");
            darkOld /= darkCount; darkNew /= darkCount; brightOld /= brightCount; brightNew /= brightCount;
            double darkGain = darkNew / Math.Max(darkOld, 1e-9);
            double brightGain = brightNew / Math.Max(brightOld, 1e-9);
            Check(darkGain > 10.0, "dark cloud gain=" + darkGain.ToString("R", Invariant) + " old=" + darkOld.ToString("R", Invariant) + " new=" + darkNew.ToString("R", Invariant) + " pixels=" + darkCount);
            Check(darkGain > brightGain, "fill lift darkGain=" + darkGain.ToString("R", Invariant) + " brightGain=" + brightGain.ToString("R", Invariant) + " darkOld=" + darkOld.ToString("R", Invariant) + " darkNew=" + darkNew.ToString("R", Invariant) + " brightOld=" + brightOld.ToString("R", Invariant) + " brightNew=" + brightNew.ToString("R", Invariant));

            var phaseCsv = new StringBuilder("mu,single_hg,dual_hg,gain\n");
            double peak = Hg(G, 1.0);
            foreach (double mu in new[] { 1.0, 0.75, 0.5, 0.25, 0.0, -0.5, -1.0 })
            {
                double single = Hg(G, mu) / peak;
                double filled = single + SecondaryWeight;
                phaseCsv.Append(mu.ToString("0.00", Invariant)).Append(',')
                    .Append(single.ToString("0.000000", Invariant)).Append(',')
                    .Append(filled.ToString("0.000000", Invariant)).Append(',')
                    .Append((filled / single).ToString("0.000000", Invariant)).Append('\n');
            }
            File.WriteAllText(Path.Combine(root, "phase.csv"), phaseCsv.ToString());
            File.WriteAllText(Path.Combine(root, "stats.csv"),
                "metric,value\n" +
                "cloud_pixels," + cloudLuma.Count + "\n" +
                "p95_single_hg," + p95.ToString("R", Invariant) + "\n" +
                "dark_pixels," + darkCount + "\n" +
                "dark_mean_single_hg," + darkOld.ToString("R", Invariant) + "\n" +
                "dark_mean_dual," + darkNew.ToString("R", Invariant) + "\n" +
                "dark_gain," + darkGain.ToString("R", Invariant) + "\n" +
                "bright_pixels," + brightCount + "\n" +
                "bright_mean_single_hg," + brightOld.ToString("R", Invariant) + "\n" +
                "bright_mean_dual," + brightNew.ToString("R", Invariant) + "\n" +
                "bright_gain," + brightGain.ToString("R", Invariant) + "\n" +
                "repeat_max_difference," + repeatMax.ToString("R", Invariant) + "\n" +
                "single_hg_sha256," + HashFile(Path.Combine(root, "single-hg.png")) + "\n" +
                "dual_hg_sha256," + HashFile(Path.Combine(root, "dual-hg.png")) + "\n");
            File.WriteAllText(Path.Combine(root, "summary.txt"),
                "device=" + SystemInfo.graphicsDeviceName + "\n" +
                "api=" + SystemInfo.graphicsDeviceType + "\n" +
                "g=" + G.ToString(Invariant) + " secondaryScattering=" + SecondaryWeight.ToString(Invariant) + "\n" +
                "assertions=" + assertions + "\npassed=true\n");
            return Path.GetFullPath(Path.Combine(root, "stats.csv"));
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
            if (linearReadback != null) UnityEngine.Object.DestroyImmediate(linearReadback);
            if (colorReadback != null) UnityEngine.Object.DestroyImmediate(colorReadback);
            if (lightOwner != null) UnityEngine.Object.DestroyImmediate(lightOwner);
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            bool restored = JsonUtility.ToJson(feature.settings) == settingsBefore && scene.isDirty == dirtyBefore;
            File.WriteAllText(Path.Combine(root, "summary.txt"),
                "device=" + SystemInfo.graphicsDeviceName + "\n" +
                "api=" + SystemInfo.graphicsDeviceType + "\n" +
                "g=" + G.ToString(Invariant) + " secondaryScattering=" + SecondaryWeight.ToString(Invariant) + "\n" +
                "assertions=" + assertions + "\n" +
                "passed=" + (failure == null && restored) + "\n" +
                "restored=" + restored + "\n" +
                "scene_dirty_before=" + dirtyBefore + "\n" +
                "scene_dirty_after=" + scene.isDirty + "\n" +
                (failure == null ? "" : "failure=" + failure + "\n"));
        }
    }

    static Color[] Capture(Camera camera, UniversalRenderPipeline.SingleCameraRequest request, Texture2D linear, Texture2D color, string png)
    {
        RenderPipeline.SubmitRenderRequest(camera, request);
        RenderTexture active = RenderTexture.active;
        RenderTexture source = request.destination;
        RenderTexture.active = source;
        linear.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
        linear.Apply(false, false);
        color.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
        color.Apply(false, false);
        RenderTexture.active = active;
        File.WriteAllBytes(png, color.EncodeToPNG());
        return linear.GetPixels();
    }

    static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
    static float MaxRgb(Color c) => Math.Max(Math.Abs(c.r), Math.Max(Math.Abs(c.g), Math.Abs(c.b)));
    static bool Finite(Color c) => !float.IsNaN(c.r) && !float.IsInfinity(c.r)
        && !float.IsNaN(c.g) && !float.IsInfinity(c.g)
        && !float.IsNaN(c.b) && !float.IsInfinity(c.b);
    static string HashFile(string path) => Hash(File.ReadAllBytes(path));
    static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
}
