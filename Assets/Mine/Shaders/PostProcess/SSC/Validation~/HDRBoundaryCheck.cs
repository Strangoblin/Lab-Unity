using System;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class Script
{
    const int Width = 960, Height = 540, Shift = 2;
    const float LinearHDR = 1000f;
    const string BaselinePath = "Assets/Mine/Shaders/PostProcess/SSC/Validation~/Fixtures/SSC-before-optimization.shader.txt";
    const string ShaderPath = "Assets/Mine/Shaders/PostProcess/SSC/SSC.shader";
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    [Serializable] public class Difference
    {
        public int channelSamples;
        public double mae, p99, max, referenceMeanAbs, relativeMAE;
    }
    [Serializable] public class CaseResult
    {
        public string name, fixture, baselineHash, repeatedHash, optimizedHash, calibrationHash;
        public double centerMaxDifference, relativeMax;
        public bool centerGoal, wholeGoal;
        public Color actualUnoccludedCenter, baselineCenter, repeatedCenter, optimizedCenter;
        public bool baselineCenterZero, optimizedCenterZero, negativeControlExact, passed;
        public Difference fullRGB, center9x9RGB, negativeControlRGB;
    }
    [Serializable] public class Report
    {
        public string device, api, unity, scene, baselinePath, baselineHash, optimizedHash;
        public string settingsBefore, settingsAfter, failure, output;
        public string rawFormat = "Little-endian linear RGBA float32; ReadPixels bottom-up; no PNG/tonemapping";
        public string fixture = "960x540, 64 rays, shift2, speed/jitter0, black A/B, fixed pose, unused layer only; real noise and white-noise fixtures";
        public string centerCriterion = "Real: center diff<=.001. White sky: baseline/new exact0. White inside: baseline0 and optimized abs<=.0006. All: negative-control0, full max/calibratedHDR<=1e-4";
        public int width = Width, height = Height, testLayer;
        public float requestedLinearHDR = LinearHDR, suppliedBackgroundSRGB;
        public bool sceneDirtyBefore, sceneDirtyAfter, restored, passed;
        public CaseResult[] cases = new CaseResult[4];
    }
    public static object Main(string[] args)
    {
        string root = "tmp/sessions/2026-10-20-ssc-opt-c1/output/hdr-extreme-"
            + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.CreateDirectory(root);
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        SSCFeature feature = null;
        if (rd != null) foreach (var entry in rd.rendererFeatures) if (entry is SSCFeature) feature = (SSCFeature)entry;
        if (feature == null || feature.settings.mainTex2D == null) throw new Exception("SSC with current noise required");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        string originalSettings = JsonUtility.ToJson(feature.settings);
        Texture2D originalNoise = feature.settings.mainTex2D;
        bool originalActive = feature.isActive;
        var previousRT = RenderTexture.active;
        var report = new Report {
            device = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(),
            unity = Application.unityVersion, scene = scene.path, sceneDirtyBefore = scene.isDirty,
            baselinePath = BaselinePath, baselineHash = Hash(File.ReadAllBytes(BaselinePath)),
            optimizedHash = Hash(File.ReadAllBytes(ShaderPath)), settingsBefore = originalSettings,
            output = Path.GetFullPath(root), suppliedBackgroundSRGB = Mathf.LinearToGammaSpace(LinearHDR)
        };
        string csvPath = Path.Combine(root, "cases.csv");
        File.WriteAllText(csvPath, "fixture,case,calibration_r,calibration_g,calibration_b,baseline_r,baseline_g,baseline_b,repeat_r,repeat_g,repeat_b,optimized_r,optimized_g,optimized_b,center_max_diff,RGB_MAE,RGB_P99,RGB_max,RGB_reference_mean_abs,RGB_relative_MAE,RGB_relative_max,ROI9_MAE,ROI9_P99,ROI9_max,negative_MAE,negative_P99,negative_max,baseline_center_zero,optimized_center_zero,center_goal,whole_goal,negative_exact,passed,calibration_hash,baseline_hash,repeat_hash,optimized_hash\n");
        Shader baseline = null;
        GameObject owner = null;
        Material material = null;
        RenderTexture target = null;
        Texture2D readback = null;
        try
        {
            string beforeSource = File.ReadAllText(BaselinePath);
            const string declaration = "Shader \"PostProcess/SSC\"";
            if (!beforeSource.Contains(declaration)) throw new Exception("Baseline declaration missing");
            baseline = ShaderUtil.CreateShaderAsset(beforeSource.Replace(declaration, "Shader \"Hidden/SSC/HDR-Extreme-Baseline\""));
            baseline.hideFlags = HideFlags.HideAndDontSave;
            var optimized = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Compile64(baseline); Compile64(optimized);
            int occupied = 0;
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                occupied |= 1 << renderer.gameObject.layer;
            int layer = 31;
            while (layer >= 0 && (occupied & (1 << layer)) != 0) layer--;
            if (layer < 0) throw new Exception("No unused renderer layer");
            report.testLayer = layer;
            owner = new GameObject("SSC HDR extreme fixture") { hideFlags = HideFlags.HideAndDontSave };
            var camera = owner.AddComponent<Camera>();
            camera.enabled = false; camera.cameraType = CameraType.Game;
            camera.rect = new Rect(0, 0, 1, 1); camera.aspect = (float)Width / Height;
            camera.orthographic = false; camera.fieldOfView = 60; camera.nearClipPlane = 0.3f; camera.farClipPlane = 2000;
            camera.allowHDR = true; camera.allowDynamicResolution = false; camera.allowMSAA = false;
            camera.cullingMask = 1 << layer; camera.clearFlags = CameraClearFlags.SolidColor;
            float encoded = report.suppliedBackgroundSRGB;
            camera.backgroundColor = new Color(encoded, encoded, encoded, 1);
            camera.transform.position = new Vector3(0, 25, -90); camera.transform.LookAt(new Vector3(0, 50, 0));
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(0); cameraData.requiresDepthTexture = true;
            cameraData.renderPostProcessing = false; cameraData.antialiasing = AntialiasingMode.None;
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
            material.SetVector("_BaseColor", new Vector4(LinearHDR, LinearHDR, LinearHDR, 1));
            var ray = camera.ViewportPointToRay(new Vector3((Width / 2 + 0.5f) / Width, (Height / 2 + 0.5f) / Height, 0));
            Vector3 centerAt14 = ray.GetPoint((14f - ray.origin.z) / ray.direction.z);
            var thick = Geometry(owner, layer, material, "HDR coarse", new Vector3(-6, 52.8f, 10), new Vector3(4, 3, 0.3f));
            var thin = Geometry(owner, layer, material, "HDR center thin", centerAt14, new Vector3(0.12f, 1.2f, 0.3f));
            var rod = Geometry(owner, layer, material, "HDR rod", new Vector3(3, 53.2f, 12), new Vector3(0.45f, 2, 0.3f));
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                { antiAliasing = 1, useMipMap = false, autoGenerateMips = false, useDynamicScale = false };
            readback = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
            if (!target.Create()) throw new Exception("HDR target creation failed");
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new Exception("SingleCameraRequest unsupported");
            feature.settings.baseColorA = Color.black; feature.settings.baseColorB = Color.black;
            feature.settings.speed = 0; feature.settings.jitter = 0;
            feature.settings.height = 50; feature.settings.size = new Vector3(100, 10, 100);
            feature.settings.scale = 4; feature.settings.thickness = 0.8f;
            feature.settings.middle = 0.2f; feature.settings.threshold = 0.2f;
            for (int group = 0; group < 4; group++)
            {
                bool whiteNoise = group >= 2, geometry = (group % 2) == 1;
                feature.settings.mainTex2D = whiteNoise ? Texture2D.whiteTexture : originalNoise;
                thick.SetActive(geometry); thin.SetActive(geometry); rod.SetActive(geometry);
                string fixture = whiteNoise ? "white" : "real";
                var item = new CaseResult { fixture = fixture,
                    name = fixture + "-" + (geometry ? "inside-HDR-geometry" : "black-cloud-HDR-sky") };
                report.cases[group] = item;
                feature.SetActive(false);
                var unoccluded = Capture(camera, target, readback);
                item.actualUnoccludedCenter = unoccluded[(Height / 2) * Width + Width / 2];
                item.calibrationHash = SaveRaw(root, item.name + "-unoccluded", unoccluded);
                if (MaxRGB(item.actualUnoccludedCenter) < 100f) throw new Exception("HDR/center geometry calibration failed");
                Select(feature, baseline);
                var before = Capture(camera, target, readback);
                var repeated = Capture(camera, target, readback);
                Select(feature, optimized);
                var after = Capture(camera, target, readback);
                item.baselineHash = SaveRaw(root, item.name + "-baseline", before);
                item.repeatedHash = SaveRaw(root, item.name + "-baseline-repeat", repeated);
                item.optimizedHash = SaveRaw(root, item.name + "-optimized", after);
                item.fullRGB = Compare(before, after, -1);
                item.center9x9RGB = Compare(before, after, 4);
                item.negativeControlRGB = Compare(before, repeated, -1);
                int center = (Height / 2) * Width + Width / 2;
                item.baselineCenter = before[center]; item.repeatedCenter = repeated[center]; item.optimizedCenter = after[center];
                item.baselineCenterZero = IsZeroRGB(item.baselineCenter);
                item.optimizedCenterZero = IsZeroRGB(item.optimizedCenter);
                item.negativeControlExact = item.negativeControlRGB.max == 0;
                Color centerDelta = item.baselineCenter - item.optimizedCenter;
                item.centerMaxDifference = MaxRGB(centerDelta);
                item.relativeMax = item.fullRGB.max / Math.Max(1.0, MaxRGB(item.actualUnoccludedCenter));
                item.wholeGoal = item.relativeMax <= 1e-4;
                item.centerGoal = whiteNoise
                    ? item.baselineCenterZero && (geometry ? MaxRGB(item.optimizedCenter) <= 0.0006f : item.optimizedCenterZero)
                    : item.centerMaxDifference <= 0.001;
                item.passed = item.centerGoal && item.wholeGoal && item.negativeControlExact;
                WriteCaseCSV(csvPath, item);
            }
            report.passed = true;
            foreach (var item in report.cases) report.passed &= item.passed;
            return Path.GetFullPath(Path.Combine(root, "metadata.json"));
        }
        catch (Exception error) { report.failure = error.ToString(); throw; }
        finally
        {
            try
            {
                JsonUtility.FromJsonOverwrite(originalSettings, feature.settings);
                feature.SetActive(originalActive); feature.Create();
                report.settingsAfter = JsonUtility.ToJson(feature.settings);
                report.restored = report.settingsBefore == report.settingsAfter && feature.isActive == originalActive;
            }
            finally
            {
                RenderTexture.active = previousRT;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
                if (baseline != null) UnityEngine.Object.DestroyImmediate(baseline);
                report.sceneDirtyAfter = scene.isDirty;
                report.restored &= report.sceneDirtyBefore == report.sceneDirtyAfter && RenderTexture.active == previousRT;
                report.passed &= report.restored;
                File.WriteAllText(Path.Combine(root, "metadata.json"), JsonUtility.ToJson(report, true));
            }
        }
    }
    static GameObject Geometry(GameObject owner, int layer, Material material, string name, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.hideFlags = HideFlags.HideAndDontSave; go.layer = layer;
        go.transform.SetParent(owner.transform, false); go.transform.position = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material; go.SetActive(false); return go;
    }
    static void Compile64(Shader shader)
    {
        if (shader == null) throw new Exception("Shader missing");
        var material = new Material(shader);
        try
        {
            material.EnableKeyword("SSC_RAY_COUNT_64");
            for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity.ToString() == "Error") throw new Exception(shader.name + ": " + message.message);
        }
        finally { UnityEngine.Object.DestroyImmediate(material); }
    }
    static void Select(SSCFeature feature, Shader shader)
    {
        feature.settings.sscShader = shader; feature.settings.rayCount = SSCFeature.RayCount.COUNT64;
        feature.settings.downsample = Shift; feature.settings.showSSC = false; feature.SetActive(true); feature.Create();
    }
    static Color[] Capture(Camera camera, RenderTexture target, Texture2D readback)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
        readback.Apply(false, false); var pixels = readback.GetPixels();
        foreach (var color in pixels) for (int c = 0; c < 4; c++)
            if (float.IsNaN(color[c]) || float.IsInfinity(color[c])) throw new Exception("Nonfinite float-HDR output");
        return pixels;
    }
    static string SaveRaw(string root, string label, Color[] pixels)
    {
        var floats = new float[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++) for (int c = 0; c < 4; c++) floats[i * 4 + c] = pixels[i][c];
        var bytes = new byte[floats.Length * 4]; Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        File.WriteAllBytes(Path.Combine(root, label + ".rgba32f"), bytes); return Hash(bytes);
    }
    static Difference Compare(Color[] before, Color[] after, int radius)
    {
        int x0 = radius < 0 ? 0 : Width / 2 - radius, x1 = radius < 0 ? Width : Width / 2 + radius + 1;
        int y0 = radius < 0 ? 0 : Height / 2 - radius, y1 = radius < 0 ? Height : Height / 2 + radius + 1;
        var errors = new double[(x1 - x0) * (y1 - y0) * 3]; int n = 0; double sum = 0, reference = 0;
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) for (int c = 0; c < 3; c++)
        {
            float a = before[y * Width + x][c], b = after[y * Width + x][c];
            double error = Math.Abs((double)a - b); errors[n++] = error; sum += error; reference += Math.Abs(a);
        }
        Array.Sort(errors);
        return new Difference { channelSamples = n, mae = sum / n, max = errors[n - 1],
            p99 = errors[(int)Math.Ceiling(0.99 * n) - 1], referenceMeanAbs = reference / n,
            relativeMAE = sum / Math.Max(reference, 1e-12) };
    }
    static string Number(double value) { return value.ToString("R", Invariant); }
    static void WriteCaseCSV(string path, CaseResult item)
    {
        File.AppendAllText(path, string.Join(",", new[] {
            item.fixture, item.name,
            Number(item.actualUnoccludedCenter.r), Number(item.actualUnoccludedCenter.g), Number(item.actualUnoccludedCenter.b),
            Number(item.baselineCenter.r), Number(item.baselineCenter.g), Number(item.baselineCenter.b),
            Number(item.repeatedCenter.r), Number(item.repeatedCenter.g), Number(item.repeatedCenter.b),
            Number(item.optimizedCenter.r), Number(item.optimizedCenter.g), Number(item.optimizedCenter.b),
            Number(item.centerMaxDifference), Number(item.fullRGB.mae), Number(item.fullRGB.p99), Number(item.fullRGB.max),
            Number(item.fullRGB.referenceMeanAbs), Number(item.fullRGB.relativeMAE), Number(item.relativeMax),
            Number(item.center9x9RGB.mae), Number(item.center9x9RGB.p99), Number(item.center9x9RGB.max),
            Number(item.negativeControlRGB.mae), Number(item.negativeControlRGB.p99), Number(item.negativeControlRGB.max),
            item.baselineCenterZero.ToString(), item.optimizedCenterZero.ToString(),
            item.centerGoal.ToString(), item.wholeGoal.ToString(), item.negativeControlExact.ToString(), item.passed.ToString(),
            item.calibrationHash, item.baselineHash, item.repeatedHash, item.optimizedHash
        }) + "\n");
    }
    static bool IsZeroRGB(Color color) { return color.r == 0f && color.g == 0f && color.b == 0f; }
    static float MaxRGB(Color color) { return Mathf.Max(Mathf.Abs(color.r), Mathf.Abs(color.g), Mathf.Abs(color.b)); }
    static string Hash(byte[] bytes)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
}
