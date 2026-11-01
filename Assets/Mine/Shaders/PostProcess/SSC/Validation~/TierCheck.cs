using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

/// <summary>SSC quality migration and real RenderGraph budget wiring. No asset/scene saves.</summary>
public class Script
{
    const string Root = "tmp/sessions/2026-10-20-ssc-tiers/output";
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static int assertions;
    static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
    static SSCFeature.Quality Expected(int rays, int shift)
    {
        return shift >= 3 ? SSCFeature.Quality.Low
            : shift <= 1 ? SSCFeature.Quality.High
            : SSCFeature.Quality.Medium;
    }
    static int TierRays(int shift) => shift >= 3 ? (int)SSCFeature.RayCount.COUNT32 : shift <= 1 ? (int)SSCFeature.RayCount.COUNT128 : (int)SSCFeature.RayCount.COUNT64;
    static int TierShift(int shift) => shift >= 3 ? 3 : shift <= 1 ? 1 : 2;
    static void Budget(SSCFeature.Settings s, int rays, int shift, string label)
    {
        Check((int)s.Budget.rayCount == rays && s.Budget.downsample == shift, label + " budget");
    }
    public static object Main()
    {
        Directory.CreateDirectory(Root);
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        var f = rd.rendererFeatures.OfType<SSCFeature>().Single();
        string original = JsonUtility.ToJson(f.settings);
        bool originalActive = f.isActive;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        bool dirty = scene.isDirty;
        string failure = null;
        GameObject owner = null; RenderTexture rt = null; Texture2D readback = null;
        RenderTexture previous = RenderTexture.active;
        var rows = new StringBuilder("quality,rays,shift,width,height,keyword,sha256,cloudChangedPixels\n");
        try
        {
            var defaults = new SSCFeature.Settings();
            Check(defaults.quality == SSCFeature.Quality.Medium, "new default quality");
            Budget(defaults, 0, 2, "new default");
            defaults = JsonUtility.FromJson<SSCFeature.Settings>(JsonUtility.ToJson(defaults));
            Budget(defaults, 0, 2, "new default roundtrip");
            for (int rays = 0; rays < 3; rays++) for (int shift = 0; shift <= 4; shift++)
            {
                string legacy = "{\"_legacyDownsample\":" + shift + ",\"scale\":7.125}";
                var s = JsonUtility.FromJson<SSCFeature.Settings>(legacy);
                Check(s.quality == Expected(rays, shift), "old pair tier migration " + legacy);
                Check(s.scale == 7.125f, "artistic field preserved");
                Budget(s, TierRays(shift), TierShift(shift), "old pair migrated");
                string migrated = JsonUtility.ToJson(s);
                Check(migrated.Contains("\"_legacyDownsample\":-1"), "migration consumed");
                s.OnAfterDeserialize();
                Check(migrated == JsonUtility.ToJson(s), "migration idempotent");
                s = JsonUtility.FromJson<SSCFeature.Settings>(migrated);
                Budget(s, TierRays(shift), TierShift(shift), "migrated roundtrip");
                Check(s.quality == Expected(rays, shift), "quality roundtrip");
                s.quality = SSCFeature.Quality.High;
                s = JsonUtility.FromJson<SSCFeature.Settings>(JsonUtility.ToJson(s));
                Check(s.quality == SSCFeature.Quality.High, "explicit quality survives reload");
                Budget(s, 1, 1, "explicit quality budget");
                s.rayCount = (SSCFeature.RayCount)rays; s.downsample = shift;
                Budget(s, rays, shift, "old API ray then shift");
                s = new SSCFeature.Settings(); s.downsample = shift; s.rayCount = (SSCFeature.RayCount)rays;
                Budget(s, rays, shift, "old API shift then ray");
            }
            var live = JsonUtility.FromJson<SSCFeature.Settings>(File.ReadAllText("tmp/sessions/2026-10-20-ssc-tiers/input/live-settings.json"));
            Check(live.quality == SSCFeature.Quality.Medium, "live64/2 migrated to Medium");
            Budget(live, 0, 2, "live budget");
            // Check real Inspector visibility, including fields Unity hides from NextVisible.
            var serialized = new SerializedObject(f);
            var settings = serialized.FindProperty("settings");
            var qualityProperty = settings.FindPropertyRelative("quality");
            Check(qualityProperty != null, "quality serialized property");
            Check(qualityProperty.enumDisplayNames.SequenceEqual(new[] { "Low", "Medium", "High" }), "exactly three inspector choices");
            Check(string.IsNullOrEmpty(qualityProperty.tooltip), "no detailed tier tooltip");
            var child = settings.Copy(); int depth = child.depth;
            var visible = new StringBuilder();
            if (child.NextVisible(true)) do
            {
                if (child.depth <= depth) break;
                if (child.depth == depth + 1) visible.Append(child.name).Append(",");
            } while (child.NextVisible(false));
            Check(visible.ToString().Contains("quality,"), "quality visible");
            Check(!visible.ToString().Contains("downsample") && !visible.ToString().Contains("rayCount")
                && !visible.ToString().Contains("_custom") && !visible.ToString().Contains("_legacy"), "raw budgets hidden");

            int occupied = 0;
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                occupied |= 1 << renderer.gameObject.layer;
            int layer = 31; while (layer >= 0 && (occupied & (1 << layer)) != 0) layer--;
            Check(layer >= 0, "unused layer");
            owner = new GameObject("SSC tier validation") { hideFlags = HideFlags.HideAndDontSave };
            var camera = owner.AddComponent<Camera>();
            camera.enabled = false; camera.cameraType = CameraType.Game;
            camera.rect = new Rect(0, 0, 1, 1); camera.aspect = 961f / 541f;
            camera.orthographic = false; camera.fieldOfView = 60; camera.nearClipPlane = 0.3f; camera.farClipPlane = 2000;
            camera.allowHDR = true; camera.allowDynamicResolution = false; camera.allowMSAA = false;
            camera.cullingMask = 1 << layer; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.4f, .2f, .1f, 1);
            camera.transform.position = new Vector3(0, 25, -90); camera.transform.LookAt(new Vector3(0, 50, 0));
            var data = camera.GetUniversalAdditionalCameraData();
            data.SetRenderer(0); data.requiresDepthTexture = true; data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None;
            rt = new RenderTexture(961, 541, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            readback = new Texture2D(961, 541, TextureFormat.RGBAFloat, false, true);
            Check(rt.Create(), "render target created");
            Check(RenderPipeline.SupportsRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt }), "request supported");
            f.settings.speed = 0; f.settings.jitter = 0; f.settings.showSSC = false;
            f.SetActive(false);
            byte[] background = Capture(camera, rt, readback);
            f.SetActive(true);
            byte[] mediumPixels = null;
            var qualities = new[] { SSCFeature.Quality.Low, SSCFeature.Quality.Medium, SSCFeature.Quality.High };
            var rayCounts = new[] { SSCFeature.RayCount.COUNT32, SSCFeature.RayCount.COUNT64, SSCFeature.RayCount.COUNT128 };
            int[] steps = { 32, 64, 128 }, shifts = { 3, 2, 1 };
            for (int i = 0; i < qualities.Length; i++)
            {
                f.settings.SetBudget(rayCounts[i], shifts[i]);
                f.settings.quality = qualities[i]; f.Create();
                byte[] pixels = Capture(camera, rt, readback);
                var pass = typeof(SSCFeature).GetField("sscPass", Private).GetValue(f);
                var material = (Material)pass.GetType().GetField("_material", Private).GetValue(pass);
                Vector4 size = material.GetVector("_SSCTextureSize");
                int w = 961 >> shifts[i], h = 541 >> shifts[i];
                Check(size.z == w && size.w == h && Mathf.Abs(size.x - 1f / w) < 1e-4f && Mathf.Abs(size.y - 1f / h) < 1e-4f, "actual odd-size RT/uniform " + qualities[i] + " expected=" + w + "x" + h + " actual=" + size + " shiftUniform=" + material.GetFloat("_SSCDownsample"));
                Check(material.GetFloat("_SSCDownsample") == shifts[i], "actual shift uniform " + qualities[i]);
                string keyword = "SSC_RAY_COUNT_" + steps[i];
                Check(material.shaderKeywords.Count(k => k.StartsWith("SSC_RAY_COUNT_")) == 1 && material.IsKeywordEnabled(keyword), "actual keyword " + qualities[i]);
                Check(Mathf.Abs(material.GetFloat("_SecondaryScattering") - 1.0f) < 1e-4f, "secondary scattering uniform " + qualities[i]);
                int changed = 0; float[] floats = new float[pixels.Length / 4]; Buffer.BlockCopy(pixels, 0, floats, 0, pixels.Length);
                for (int p = 0; p < floats.Length; p++)
                {
                    Check(!float.IsNaN(floats[p]) && !float.IsInfinity(floats[p]), "finite render");
                    if ((p % 4) == 3) continue;
                    if (BitConverter.ToInt32(pixels, p * 4) != BitConverter.ToInt32(background, p * 4)) changed++;
                }
                Check(changed > 100, "cloud positive control " + qualities[i]);
                rows.Append(qualities[i]).Append(',').Append(steps[i]).Append(',').Append(shifts[i]).Append(',').Append(w).Append(',').Append(h).Append(',').Append(keyword).Append(',').Append(Hash(pixels)).Append(',').Append(changed).Append('\n');
                File.WriteAllBytes(Path.Combine(Root, qualities[i] + ".png"), readback.EncodeToPNG());
                if (qualities[i] == SSCFeature.Quality.Medium) mediumPixels = pixels;
            }
            f.settings.quality = (SSCFeature.Quality)999; f.Create();
            Check(mediumPixels.SequenceEqual(Capture(camera, rt, readback)), "unknown tier falls back to Medium");
            f.settings.SetBudget(SSCFeature.RayCount.COUNT256, 4); f.Create();
            Check(f.settings.Budget.rayCount == SSCFeature.RayCount.COUNT256 && f.settings.Budget.downsample == 4, "legacy validation budget preserved");
            f.settings.quality = SSCFeature.Quality.Medium; f.Create();
            Check(f.settings.Budget.rayCount == SSCFeature.RayCount.COUNT64 && f.settings.Budget.downsample == 2, "quality selection replaces legacy validation budget");
            File.WriteAllText(Path.Combine(Root, "inspector-fields.txt"), visible.ToString());
            return Path.GetFullPath(Path.Combine(Root, "report.json"));
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            JsonUtility.FromJsonOverwrite(original, f.settings); f.SetActive(originalActive); f.Create();
            RenderTexture.active = previous;
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            File.WriteAllText(Path.Combine(Root, "wiring.csv"), rows.ToString());
            bool restored = original == JsonUtility.ToJson(f.settings) && f.isActive == originalActive && scene.isDirty == dirty;
            File.WriteAllText(Path.Combine(Root, "report.json"), JsonUtility.ToJson(new Report {
                assertions = assertions, passed = failure == null && restored, restored = restored,
                sceneDirtyBefore = dirty, sceneDirtyAfter = scene.isDirty, failure = failure,
                settingsBefore = original, settingsAfter = JsonUtility.ToJson(f.settings),
                shaderSha256 = Hash(File.ReadAllBytes("Assets/Mine/Shaders/PostProcess/SSC/SSC.shader")),
                featureSha256 = Hash(File.ReadAllBytes("Assets/Mine/Shaders/PostProcess/SSC/SSCFeature.cs")),
                device = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString()
            }, true));
        }
    }
    static byte[] Capture(Camera camera, RenderTexture rt, Texture2D texture)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        RenderTexture.active = rt; texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); texture.Apply(false, false);
        return texture.GetRawTextureData<byte>().ToArray();
    }
    static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    [Serializable] public class Report
    {
        public int assertions; public bool passed, restored, sceneDirtyBefore, sceneDirtyAfter;
        public string failure, settingsBefore, settingsAfter, shaderSha256, featureSha256, device, api;
    }
}
