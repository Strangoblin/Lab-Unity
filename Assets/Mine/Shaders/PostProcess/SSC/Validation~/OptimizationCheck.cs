using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

/// <summary>SSC float-HDR equivalence and synchronized request-wall A/B adapter.</summary>
/// <remarks>Run through unityctl script execute; never saves assets or scene.</remarks>
public class Script
{
    const string Session = "tmp/sessions/2026-10-20-ssc-opt-c1";
    const string BaselinePath = "Assets/Mine/Shaders/PostProcess/SSC/Validation~/Fixtures/SSC-before-optimization.shader.txt";
    const string ShaderPath = "Assets/Mine/Shaders/PostProcess/SSC/SSC.shader";
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    [Serializable] public class Report
    {
        public string mode, device, api, unity, scene, pose, settings, pipeline, metric, timeControl, geometry;
        public string baselineSourceSha256, optimizedSourceSha256, samplesSha256, metricsSha256, failure, baselinePath;
        public float renderScale;
        public string display = "Raw rgba32f is little-endian linear float RGBA, row order ReadPixels bottom-up; PNG linear-to-sRGB clipped without tonemapping";
        public int width, height, warmup = 8, samplesPerBlock = 12, rounds = 3, measuredSamples;
        public bool sceneDirtyBefore, sceneDirtyAfter, restored, visualColorsRestored = true;
    }
    public static object Main(string[] args)
    {
        args = args ?? new string[0];
        string mode = args.Length == 0 ? "visual" : args[0];
        bool captureMode = mode == "visual" || mode == "coverage";
        if (!captureMode && mode != "benchmark" && mode != "benchmark-edge") throw new ArgumentException("Use visual, coverage, benchmark, or benchmark-edge");
        string root = Session + "/output/" + mode + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.CreateDirectory(root);
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        SSCFeature f = null;
        foreach (var entry in rd.rendererFeatures) if (entry is SSCFeature) f = (SSCFeature)entry;
        if (f == null || !f.isActive) throw new Exception("Active SSC required");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        string originalSettings = JsonUtility.ToJson(f.settings);
        string baselinePath = args.Length > 1 ? args[1] : BaselinePath;
        string beforeSource = File.ReadAllText(baselinePath);
        string afterSource = File.ReadAllText(ShaderPath);
        var report = new Report {
            mode = mode, width = captureMode ? 960 : 1920, height = captureMode ? 540 : 1080,
            device = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(),
            unity = Application.unityVersion, scene = scene.path, sceneDirtyBefore = scene.isDirty,
            settings = originalSettings, pipeline = GraphicsSettings.currentRenderPipeline.name,
            renderScale = UniversalRenderPipeline.asset.renderScale, baselinePath = baselinePath,
            baselineSourceSha256 = Hash(File.ReadAllBytes(baselinePath)),
            optimizedSourceSha256 = Hash(File.ReadAllBytes(ShaderPath)),
            metric = "Linear ARGBHalf -> RGBAFloat errors; benchmark SubmitRenderRequest + ReadPixels(1x1), not pure GPU/FPS",
            timeControl = "SSC speed=0,jitter=0. View/benchmark other scene animation stays live; geometry uses unused layer + solid background. Each visual pair has baseline-repeat negative control"
        };
        Shader baseline = null, coverageOptimized = null;
        var go = new GameObject("SSC optimization camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        var reference = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : Camera.main;
        if (reference == null) { UnityEngine.Object.DestroyImmediate(go); throw new Exception("SceneView or Main camera required"); }
        camera.CopyFrom(reference); camera.transform.SetPositionAndRotation(reference.transform.position, reference.transform.rotation);
        camera.cameraType = CameraType.Game; camera.rect = new Rect(0, 0, 1, 1);
        camera.aspect = (float)report.width / report.height; camera.allowDynamicResolution = false; camera.allowHDR = true;
        var data = camera.GetUniversalAdditionalCameraData();
        data.SetRenderer(0); data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None; data.requiresDepthTexture = true;
        report.pose = camera.transform.position + " rotation=" + camera.transform.eulerAngles + " fov=" + camera.fieldOfView + " ortho=" + camera.orthographic;
        int occupied = 0;
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) occupied |= 1 << renderer.gameObject.layer;
        int testLayer = 31; while (testLayer > 0 && (occupied & (1 << testLayer)) != 0) testLayer--;
        if ((occupied & (1 << testLayer)) != 0) { UnityEngine.Object.DestroyImmediate(go); throw new Exception("No unused geometry-test layer"); }
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.hideFlags = HideFlags.HideAndDontSave; cube.layer = testLayer; cube.SetActive(false);
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
        material.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.2f, 1)); cube.GetComponent<Renderer>().sharedMaterial = material;
        var edgeRods = new GameObject[8];
        var previous = RenderTexture.active;
        var rt = new RenderTexture(report.width, report.height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(captureMode ? report.width : 1, captureMode ? report.height : 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            const string declaration = "Shader \"PostProcess/SSC\"";
            if (!beforeSource.Contains(declaration)) throw new Exception("Baseline shader declaration missing");
            if (mode == "coverage") { beforeSource = CoverageSource(beforeSource); coverageOptimized = ShaderUtil.CreateShaderAsset(CoverageSource(afterSource).Replace(declaration, "Shader \"Hidden/SSC/OptimizationCoverage\"")); report.metric += "; coverage encoded as RGB from Pass0 alpha, final alpha is not coverage"; }
            baseline = ShaderUtil.CreateShaderAsset(beforeSource.Replace(declaration, "Shader \"Hidden/SSC/OptimizationBaseline\""));
            var optimized = coverageOptimized != null ? coverageOptimized : AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Compile(baseline); Compile(optimized);
            f.settings.speed = 0; f.settings.jitter = 0; f.settings.showSSC = false;
            if (mode == "benchmark-edge") EdgeGeometry(f, camera, cube, material, edgeRods, report);
            rt.Create();
            if (!RenderPipeline.SupportsRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt }))
                throw new Exception("SingleCameraRequest unsupported");
            if (captureMode) Visual(root, f, camera, cube, rt, readback, baseline, optimized, report);
            else Benchmark(root, f, camera, rt, readback, baseline, optimized, report);
            return Path.GetFullPath(Path.Combine(root, "metadata.json"));
        }
        catch (Exception error) { report.failure = error.ToString(); throw; }
        finally
        {
            try
            {
                JsonUtility.FromJsonOverwrite(originalSettings, f.settings); f.Create();
                report.restored = JsonUtility.ToJson(f.settings) == originalSettings;
            }
            finally
            {
                RenderTexture.active = previous; rt.Release();
                UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(readback);
                for (int i = 1; i < edgeRods.Length; i++) if (edgeRods[i] != null) UnityEngine.Object.DestroyImmediate(edgeRods[i]);
                UnityEngine.Object.DestroyImmediate(cube); UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(go);
                if (baseline != null) UnityEngine.Object.DestroyImmediate(baseline);
                if (coverageOptimized != null) UnityEngine.Object.DestroyImmediate(coverageOptimized);
                report.sceneDirtyAfter = scene.isDirty;
                report.restored = report.restored && report.visualColorsRestored && report.sceneDirtyBefore == report.sceneDirtyAfter;
                string samples = Path.Combine(root, "samples.csv"), metrics = Path.Combine(root, "metrics.csv");
                if (File.Exists(samples)) report.samplesSha256 = Hash(File.ReadAllBytes(samples));
                if (File.Exists(metrics)) report.metricsSha256 = Hash(File.ReadAllBytes(metrics));
                File.WriteAllText(Path.Combine(root, "metadata.json"), JsonUtility.ToJson(report, true));
            }
        }
    }
    static string CoverageSource(string source)
    {
        const string entry = "#pragma fragment Frag_SSC"; int end = source.IndexOf("ENDHLSL");
        if (end < 0 || source.IndexOf(entry) < 0) throw new Exception("Coverage injection markers missing");
        return source.Insert(end, "half4 Frag_SSCCoverage(Varyings input):SV_Target { half4 c=Frag_SSC(input); return half4(c.a,c.a,c.a,1); }\n").Replace(entry, "#pragma fragment Frag_SSCCoverage");
    }
    static void Compile(Shader shader)
    {
        if (shader == null) throw new Exception("Shader missing");
        var material = new Material(shader);
        try
        {
            foreach (string key in new[] { "SSC_RAY_COUNT_32", "SSC_RAY_COUNT_64", "SSC_RAY_COUNT_128", "SSC_RAY_COUNT_256" })
            {
                material.shaderKeywords = new[] { key };
                for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);
            }
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity.ToString() == "Error") throw new Exception(shader.name + ": " + message.message);
        }
        finally { UnityEngine.Object.DestroyImmediate(material); }
    }
    static void Select(SSCFeature f, Shader shader, int shift, bool rays128 = false, bool debug = false)
    {
        f.settings.sscShader = shader; f.settings.downsample = shift;
        f.settings.rayCount = rays128 ? SSCFeature.RayCount.COUNT128 : SSCFeature.RayCount.COUNT64;
        f.settings.showSSC = debug; f.Create();
    }
    static Color[] Capture(Camera camera, RenderTexture rt, Texture2D texture)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        RenderTexture.active = rt; texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        texture.Apply(false, false); return texture.GetPixels();
    }
    static void Visual(string root, SSCFeature f, Camera camera, GameObject cube, RenderTexture rt, Texture2D texture, Shader baseline, Shader optimized, Report report)
    {
        report.geometry = report.mode == "coverage" ? "current SceneView,64 ray,shift0/1/2/3; Pass0 alpha encoded RGB" : "view=current SceneView; isolated front/thin z=-60 and inside=(4,50,0), FOV60/near0.3/far2000; bright-inside uses A=B=black and gamma-encoded background targeting linear RGB=1000; actual float background must be verified";
        string csv = Path.Combine(root, "metrics.csv");
        File.WriteAllText(csv, "comparison,case,shift,debug,channels,MAE,P99,max,reference_mean_abs,relative_MAE,baseline_raw_sha256,optimized_raw_sha256\n");
        Vector3 viewPosition = camera.transform.position; Quaternion viewRotation = camera.transform.rotation;
        int originalMask = camera.cullingMask; var originalClear = camera.clearFlags; Color originalBackground = camera.backgroundColor;
        Color originalA = f.settings.baseColorA, originalB = f.settings.baseColorB;
        try { foreach (string sceneCase in report.mode == "coverage" ? new[] { "coverage" } : new[] { "view", "front", "thin", "inside", "bright-inside" })
        {
            bool inside = sceneCase.EndsWith("inside"), view = sceneCase == "view" || sceneCase == "coverage";
            cube.SetActive(!view);
            camera.transform.SetPositionAndRotation(viewPosition, viewRotation);
            camera.cullingMask = originalMask; camera.clearFlags = originalClear; camera.backgroundColor = originalBackground;
            if (!view)
            {
                camera.orthographic = false; camera.farClipPlane = 2000; camera.nearClipPlane = 0.3f; camera.fieldOfView = 60;
                camera.cullingMask = 1 << cube.layer; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.125f, 0.25f, 0.5f, 1);
                camera.transform.position = new Vector3(0, 25, -90); camera.transform.LookAt(new Vector3(0, 50, 0));
                cube.transform.rotation = Quaternion.Euler(0, 0, -26);
                cube.transform.position = inside ? new Vector3(4, 50, 0) : new Vector3(2.18f, 32.09f, -60);
                cube.transform.localScale = inside ? new Vector3(3, 10, 3)
                    : new Vector3(sceneCase == "thin" ? 0.12f : 2.18f, 21.82f, sceneCase == "thin" ? 0.12f : 2.18f);
                float boxFront = -f.settings.size.z * 0.5f;
                float projectedExtent = cube.GetComponent<Renderer>().bounds.max.z;
                if (!inside && projectedExtent >= boxFront) throw new Exception("Foreground must be wholly before cloud box");
                if (sceneCase == "bright-inside") { f.settings.baseColorA = Color.black; f.settings.baseColorB = Color.black; float g = Mathf.LinearToGammaSpace(1000f); camera.backgroundColor = new Color(g, g, g, 1); }
            }
            foreach (int shift in report.mode == "coverage" ? new[] { 0, 1, 2, 3 } : view ? new[] { 2, 1 } : new[] { 2, 3, 0 })
            {
                foreach (bool debug in report.mode == "coverage" ? new[] { true } : view ? new[] { false, true } : new[] { false })
                {
                    string label = sceneCase + "-q" + shift + (debug ? "-cloudbuffer" : "-composite");
                    Select(f, baseline, shift, false, debug); var before = Capture(camera, rt, texture);
                    var repeated = Capture(camera, rt, texture);
                    Select(f, optimized, shift, false, debug); var after = Capture(camera, rt, texture);
                    string beforeHash = Save(root, label + "-baseline", before, rt.width, rt.height);
                    string repeatHash = Save(root, label + "-baseline-repeat", repeated, rt.width, rt.height);
                    string afterHash = Save(root, label + "-optimized", after, rt.width, rt.height);
                    Compare(csv, "baseline-repeat", sceneCase, shift, debug, before, repeated, beforeHash, repeatHash);
                    Compare(csv, "optimized", sceneCase, shift, debug, before, after, beforeHash, afterHash);
                }
            }
        } } finally { f.settings.baseColorA = originalA; f.settings.baseColorB = originalB; report.visualColorsRestored = f.settings.baseColorA == originalA && f.settings.baseColorB == originalB; }
    }
    static string Save(string root, string label, Color[] pixels, int width, int height)
    {
        var values = new float[pixels.Length * 4];
        var display = new Color[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            for (int channel = 0; channel < 4; channel++) values[i * 4 + channel] = pixels[i][channel];
            display[i] = pixels[i].gamma;
        }
        var raw = new byte[values.Length * 4]; Buffer.BlockCopy(values, 0, raw, 0, raw.Length);
        File.WriteAllBytes(Path.Combine(root, label + ".rgba32f"), raw);
        var png = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        try { png.SetPixels(display); png.Apply(); File.WriteAllBytes(Path.Combine(root, label + ".png"), png.EncodeToPNG()); }
        finally { UnityEngine.Object.DestroyImmediate(png); }
        return Hash(raw);
    }
    static void Compare(string csv, string comparison, string sceneCase, int shift, bool debug, Color[] before, Color[] after, string beforeHash, string afterHash)
    {
        foreach (bool alpha in new[] { false, true })
        {
            int channels = alpha ? 1 : 3; var differences = new float[before.Length * channels]; double total = 0, reference = 0;
            for (int i = 0; i < before.Length; i++)
                for (int j = 0; j < channels; j++)
                {
                    int channel = alpha ? 3 : j; float a = before[i][channel], b = after[i][channel];
                    if (float.IsNaN(a) || float.IsInfinity(a) || float.IsNaN(b) || float.IsInfinity(b)) throw new Exception("Nonfinite HDR output");
                    float difference = Mathf.Abs(a - b); differences[i * channels + j] = difference; total += difference; reference += Mathf.Abs(a);
                }
            Array.Sort(differences);
            File.AppendAllText(csv, comparison + "," + sceneCase + "," + shift + "," + debug + "," + (alpha ? "alpha" : "RGB") + ","
                + Number(total / differences.Length) + "," + Number(differences[(int)Math.Ceiling(0.99 * differences.Length) - 1]) + ","
                + Number(differences[differences.Length - 1]) + "," + Number(reference / differences.Length) + "," + Number(total / Math.Max(reference, 1e-12)) + "," + beforeHash + "," + afterHash + "\n");
        }
    }
    static void EdgeGeometry(SSCFeature f, Camera camera, GameObject cube, Material material, GameObject[] rods, Report report)
    {
        camera.orthographic = false; camera.fieldOfView = 60; camera.nearClipPlane = 0.3f; camera.farClipPlane = 2000;
        camera.transform.position = new Vector3(0, 25, -90); camera.transform.LookAt(new Vector3(0, 50, 0));
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.125f, 0.25f, 0.5f, 1);
        camera.cullingMask = 1 << cube.layer;
        for (int i = 0; i < rods.Length; i++)
        {
            rods[i] = i == 0 ? cube : GameObject.CreatePrimitive(PrimitiveType.Cube);
            rods[i].hideFlags = HideFlags.HideAndDontSave; rods[i].layer = cube.layer;
            rods[i].GetComponent<Renderer>().sharedMaterial = material; rods[i].SetActive(true);
            rods[i].transform.position = new Vector3(-8.75f + 2.5f * i, 32.09f, -60);
            rods[i].transform.rotation = Quaternion.Euler(0, 0, i % 2 == 0 ? -26 : 26);
            float width = 0.15f + 0.1f * i / (rods.Length - 1);
            rods[i].transform.localScale = new Vector3(width, 21.82f, width);
            if (rods[i].GetComponent<Renderer>().bounds.max.z >= -f.settings.size.z * 0.5f) throw new Exception("Edge rods overlap cloud box");
        }
        report.pose = camera.transform.position + " rotation=" + camera.transform.eulerAngles + " fov=60 near=0.3 far=2000 ortho=false";
        report.geometry = "8 rods; x=-8.75+2.5*i,y=32.09,z=-60; size=(0.15+0.1*i/7,21.82,same); alternating z rotation -26/+26; unused layer; solid background";
        report.timeControl = "SSC speed=0,jitter=0; isolated rods and solid background; original scene renderers excluded";
    }
    static void Benchmark(string root, SSCFeature f, Camera camera, RenderTexture rt, Texture2D pixel, Shader baseline, Shader optimized, Report report)
    {
        string csv = Path.Combine(root, "samples.csv");
        File.WriteAllText(csv, "round,block_order,mode,ray_count,downsample,sample,submit_ms,sync_ms\n");
        string[] modes = { "baseline-q64", "optimized-q64", "baseline-h64", "optimized-h64", "baseline-q128", "optimized-q128" };
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        var timer = new Stopwatch();
        for (int round = 0; round < 3; round++)
            for (int index = 0; index < modes.Length; index++)
            {
                string mode = modes[round % 2 == 0 ? index : modes.Length - 1 - index];
                bool rays128 = mode.EndsWith("128"); int shift = mode.Contains("-h") ? 1 : 2;
                Select(f, mode.StartsWith("baseline") ? baseline : optimized, shift, rays128);
                for (int warmup = 0; warmup < 8; warmup++)
                {
                    RenderPipeline.SubmitRenderRequest(camera, request); RenderTexture.active = rt;
                    pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                }
                var rows = new StringBuilder();
                for (int sample = 0; sample < 12; sample++)
                {
                    timer.Restart(); RenderPipeline.SubmitRenderRequest(camera, request);
                    double submit = timer.Elapsed.TotalMilliseconds;
                    RenderTexture.active = rt; pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                    timer.Stop(); double sync = timer.Elapsed.TotalMilliseconds;
                    rows.AppendLine(round + "," + index + "," + mode + "," + (rays128 ? 128 : 64) + "," + shift + "," + sample + "," + Number(submit) + "," + Number(sync));
                    report.measuredSamples++;
                }
                File.AppendAllText(csv, rows.ToString());
                File.WriteAllText(Path.Combine(root, "metadata.json"), JsonUtility.ToJson(report, true));
            }
        if (report.measuredSamples != 216) throw new Exception("Expected 216 benchmark samples");
    }
    static string Number(double value) { return value.ToString("R", Invariant); }
    static string Hash(byte[] bytes)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
}
