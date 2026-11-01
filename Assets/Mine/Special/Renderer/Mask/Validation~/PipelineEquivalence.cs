// ════════════════════════════════════════════════════════════════
//  Screen Mask Equivalence — isolated float baselines for both mask strategies.
// ════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

/// <summary>Before/after float-image checks for the original-material and solid-override mask consumers.</summary>
/// <remarks>Args: baseline|compare, outputDirectory, baselineDirectory. Never saves assets or existing scenes.</remarks>
public class Script
{
    private const int Width = 320;
    private const int Height = 180;
    private const int SelectedLayer = 30;
    private const int OtherLayer = 29;
    private const float Tolerance = 0.002f;
    private const string BlurRoot = "Assets/Mine/Shaders/PostProcess/MaskBlur/";
    private const string RBRRoot = "Assets/Mine/Shaders/PostProcess/MaskRBR/";
    private const string ToonPath = "Assets/Mine/Shaders/Render/PBRToon/PBRToon.shader";
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Color Sentinel = new Color(-17f, -13f, -11f, -7f);

    [Serializable]
    public sealed class CaseResult
    {
        public string name, contract, baselineFile;
        public bool passed, finite = true, controlPassed, alphaPreserved = true, comparisonPassed;
        public int pixelCount, nonzeroPixels, effectPixels, differingPixels;
        public float maxValue, maxError, maxNormalizedError, maxAlphaError;
    }

    [Serializable]
    public sealed class Report
    {
        public string phase, graphicsDevice, sourceRenderer, sourcePipeline, activeScenePath, scope, error;
        public int width = Width, height = Height, caseCount, failures, renderRequestCount, beginCameraCount;
        public bool passed, pipelineRestored, activeSceneUnchanged, originalRendererUnchanged, lightSettingsRestored, trackedGlobalsRestored;
        public string[] renderErrors;
    }

    private sealed class Fixture
    {
        public Camera camera;
        public Light light;
        public GameObject visible;
        public Material selected;
        public MaskBlurFeature blur;
        public MaskRBRFeature rbr;
        public Color[] scene;
        public string phase, outputDirectory, baselineDirectory;
        public Report report;
        public List<CaseResult> cases;
    }

    public static object Main(string[] args)
    {
        var report = new Report
        {
            graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName,
            scope = "320x180 linear RGBAFloat SingleCameraRequest images from production MaskBlur/PBRToon and MaskRBR. Isolated preview scene, cloned renderer and URP; masks retain material weights, opacity occlusion, layer selection, sky union. Deterministic blur kernels and radial receiver outputs. XR, dynamic resolution, MSAA and performance are outside this fixture. Baselines are base64-wrapped lossless gzip little-endian float RGBA, bottom-left-first rows, no display mapping."
        };
        var cases = new List<CaseResult>();
        var errors = new List<string>();
        var originalQualityPipeline = QualitySettings.renderPipeline;
        var originalDefaultPipeline = GraphicsSettings.defaultRenderPipeline;
        Scene originalScene = SceneManager.GetActiveScene();
        RenderTexture originalRT = RenderTexture.active;
        bool originalLinear = GraphicsSettings.lightsUseLinearIntensity;
        bool originalTemperature = GraphicsSettings.lightsUseColorTemperature;
        Scene preview = default(Scene);
        UniversalRendererData sourceRenderer = null;
        int[] originalFeatureIds = null;
        string originalRendererJson = null;
        bool originalSceneDirty = originalScene.isDirty;
        var owned = new List<UnityEngine.Object>();
        string[] globalNames = { "_CameraDepthTexture", "_MaskBlurMaskTexture", "_MaskBlurTexture",
            "_MaskRBRObjectMaskTexture", "_MaskRBRMaskTexture", "_MaskRBRRayTexture" };
        var originalGlobals = new Texture[globalNames.Length];
        for (int i = 0; i < globalNames.Length; i++) originalGlobals[i] = Shader.GetGlobalTexture(globalNames[i]);
        Vector4 originalLightPosition = Shader.GetGlobalVector("_MainLightPosition");
        Vector4 originalLightColor = Shader.GetGlobalVector("_MainLightColor");
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
        };
        Action<ScriptableRenderContext, Camera> begin = (context, camera) => report.beginCameraCount++;
        Application.logMessageReceived += capture;
        RenderPipelineManager.beginCameraRendering += begin;
        try
        {
            Require(args != null && args.Length == 3, "Expected args: baseline|compare outputDirectory baselineDirectory.");
            string phase = args[0];
            Require(phase == "baseline" || phase == "compare", "Phase must be baseline or compare.");
            report.phase = phase;
            string outputDirectory = Path.GetFullPath(args[1]);
            string baselineDirectory = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(outputDirectory);
            if (phase == "baseline") Directory.CreateDirectory(baselineDirectory);
            else
            {
                Require(Directory.Exists(baselineDirectory), "Compare requires an existing baseline directory.");
                foreach (string name in ExpectedCaseNames())
                {
                    string file = Path.Combine(baselineDirectory, name + ".rgba-f32.bin.gz.b64");
                    Require(File.Exists(file), "Compare requires a complete baseline before rendering: " + file);
                    ReadRaw(file);
                }
            }
            Require(!File.Exists(Path.Combine(outputDirectory, "report.json")), "Use an unused output directory.");
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "A real graphics device is required.");
            Require(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                && SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat), "Float render/readback formats are required.");
            var sourcePipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Require(sourcePipeline != null, "The current pipeline must be URP.");
            FieldInfo rendererList = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", PrivateInstance);
            FieldInfo defaultIndex = typeof(UniversalRenderPipelineAsset).GetField("m_DefaultRendererIndex", PrivateInstance);
            Require(rendererList != null && defaultIndex != null, "URP renderer-list fields are unavailable.");
            var originalRenderers = (ScriptableRendererData[])rendererList.GetValue(sourcePipeline);
            sourceRenderer = originalRenderers[(int)defaultIndex.GetValue(sourcePipeline)] as UniversalRendererData;
            Require(sourceRenderer != null, "The default renderer must be a UniversalRendererData.");
            originalFeatureIds = FeatureIds(sourceRenderer);
            originalRendererJson = EditorJsonUtility.ToJson(sourceRenderer);
            report.sourceRenderer = AssetDatabase.GetAssetPath(sourceRenderer);
            report.sourcePipeline = AssetDatabase.GetAssetPath(sourcePipeline);
            report.activeScenePath = originalScene.path;
            var renderer = UnityEngine.Object.Instantiate(sourceRenderer);
            renderer.name = "Shared mask disposable renderer clone";
            renderer.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(renderer);
            renderer.rendererFeatures.Clear();
            renderer.opaqueLayerMask = (1 << SelectedLayer) | (1 << OtherLayer);
            renderer.intermediateTextureMode = IntermediateTextureMode.Always;

            var blur = ScriptableObject.CreateInstance<MaskBlurFeature>();
            blur.name = "Shared mask disposable original-material consumer";
            blur.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(blur);
            blur.settings.shader = AssetDatabase.LoadAssetAtPath<Shader>(BlurRoot + "MaskBlur.shader");
            Require(blur.settings.shader != null, "The explicit production MaskBlur shader is missing.");
            blur.settings.layerMask = 1 << SelectedLayer;
            blur.settings.quality = MaskBlurFeature.QualityMode.High;
            blur.settings.kernel = MaskBlurFeature.KernelMode.Gaussian;
            blur.settings.radiusMode = MaskBlurFeature.RadiusMode.Pixels;
            blur.settings.blurRadius = 4f;
            blur.settings.jitterStrength = 0.7f;
            blur.settings.intensity = 0.5f;
            blur.Create();
            blur.SetActive(false);
            renderer.rendererFeatures.Add(blur);

            var rbr = ScriptableObject.CreateInstance<MaskRBRFeature>();
            rbr.name = "Shared mask disposable solid-override consumer";
            rbr.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(rbr);
            rbr.settings.shader = AssetDatabase.LoadAssetAtPath<Shader>(RBRRoot + "MaskRBR.shader");
            rbr.settings.maskShader = AssetDatabase.LoadAssetAtPath<Shader>(RBRRoot + "MaskRBRMask.shader");
            Require(rbr.settings.shader != null && rbr.settings.maskShader != null,
                "The explicit production MaskRBR shaders are missing.");
            rbr.settings.source = MaskRBRFeature.SourceMode.Objects;
            rbr.settings.layerMask = 1 << SelectedLayer;
            rbr.settings.quality = MaskRBRFeature.QualityMode.High;
            rbr.settings.length = 0.3f;
            rbr.settings.falloff = 1f;
            rbr.settings.jitterStrength = 0f;
            rbr.settings.halfAngle = 60f;
            rbr.settings.anisotropy = 0.7f;
            rbr.settings.intensity = 0.4f;
            rbr.Create();
            rbr.SetActive(false);
            renderer.rendererFeatures.Add(rbr);
            renderer.SetDirty();

            var pipeline = UnityEngine.Object.Instantiate(sourcePipeline);
            pipeline.name = "Shared mask disposable URP clone";
            pipeline.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(pipeline);
            rendererList.SetValue(pipeline, new ScriptableRendererData[] { renderer });
            defaultIndex.SetValue(pipeline, 0);
            pipeline.renderScale = 1f;
            pipeline.msaaSampleCount = 1;
            pipeline.supportsHDR = true;
            FieldInfo mainMode = typeof(UniversalRenderPipelineAsset).GetField("m_MainLightRenderingMode", PrivateInstance);
            Require(mainMode != null, "URP main-light mode field is unavailable.");
            mainMode.SetValue(pipeline, LightRenderingMode.PerPixel);

            preview = EditorSceneManager.NewPreviewScene();
            Camera camera = MakeCamera(preview, owned);
            Material selected = MakeToon(owned);
            Material unselected = MakeUnlit(new Color(0.1f, 0.2f, 2f, 1f), owned);
            GameObject visible = MakeCube(preview, "Visible original-material source", new Vector3(-0.7f, 0f, 3f),
                new Vector3(0.65f, 1f, 0.4f), SelectedLayer, selected, owned);
            MakeCube(preview, "Fully hidden selected source", new Vector3(0.75f, 0f, 4f),
                new Vector3(0.7f, 0.7f, 0.3f), SelectedLayer, selected, owned);
            MakeCube(preview, "Unselected foreground occluder", new Vector3(0.75f, 0f, 2.6f),
                new Vector3(1.2f, 1.2f, 0.4f), OtherLayer, unselected, owned);
            Light light = MakeLight(preview, owned);
            SetForwardLight(light);
            GraphicsSettings.lightsUseLinearIntensity = true;
            GraphicsSettings.lightsUseColorTemperature = false;
            QualitySettings.renderPipeline = pipeline;
            var fixture = new Fixture { camera = camera, light = light, visible = visible, selected = selected,
                blur = blur, rbr = rbr, phase = phase, outputDirectory = outputDirectory,
                baselineDirectory = baselineDirectory, report = report, cases = cases };
            RunCases(fixture);
            Require(errors.Count == 0, "Render errors: " + string.Join(" | ", errors.ToArray()));
        }
        catch (Exception exception) { report.error = exception.ToString(); }
        finally
        {
            QualitySettings.renderPipeline = originalQualityPipeline;
            GraphicsSettings.defaultRenderPipeline = originalDefaultPipeline;
            RenderTexture.active = originalRT;
            Application.logMessageReceived -= capture;
            RenderPipelineManager.beginCameraRendering -= begin;
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            for (int i = 0; i < globalNames.Length; i++) Shader.SetGlobalTexture(globalNames[i], originalGlobals[i]);
            Shader.SetGlobalVector("_MainLightPosition", originalLightPosition);
            Shader.SetGlobalVector("_MainLightColor", originalLightColor);
            report.trackedGlobalsRestored = Shader.GetGlobalVector("_MainLightPosition") == originalLightPosition
                && Shader.GetGlobalVector("_MainLightColor") == originalLightColor;
            for (int i = 0; i < globalNames.Length; i++)
                report.trackedGlobalsRestored &= Shader.GetGlobalTexture(globalNames[i]) == originalGlobals[i];
            GraphicsSettings.lightsUseLinearIntensity = originalLinear;
            GraphicsSettings.lightsUseColorTemperature = originalTemperature;
            report.pipelineRestored = QualitySettings.renderPipeline == originalQualityPipeline
                && GraphicsSettings.defaultRenderPipeline == originalDefaultPipeline;
            report.activeSceneUnchanged = SceneManager.GetActiveScene() == originalScene && originalScene.isDirty == originalSceneDirty;
            report.originalRendererUnchanged = sourceRenderer == null ||
                (EqualIds(FeatureIds(sourceRenderer), originalFeatureIds) && EditorJsonUtility.ToJson(sourceRenderer) == originalRendererJson);
            report.lightSettingsRestored = GraphicsSettings.lightsUseLinearIntensity == originalLinear
                && GraphicsSettings.lightsUseColorTemperature == originalTemperature;
        }
        foreach (CaseResult item in cases) if (!item.passed) report.failures++;
        report.caseCount = cases.Count;
        report.renderErrors = errors.ToArray();
        report.passed = report.error == null && report.caseCount == 22 && report.failures == 0
            && report.pipelineRestored && report.activeSceneUnchanged && report.originalRendererUnchanged
            && report.lightSettingsRestored && report.trackedGlobalsRestored && errors.Count == 0;
        string json = Serialize(report, cases);
        if (args != null && args.Length >= 2)
        {
            string directory = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "report.json");
            if (!File.Exists(path)) File.WriteAllText(path, json);
        }
        return json;
    }

    private static void RunCases(Fixture f)
    {
        f.scene = Render(f.camera, f.report);
        Add(f, "Scene.Reference", "Both features inactive; fractional background and opaque geometry alpha", f.scene,
            pixels => pixels[0].a > 0.2f && pixels[0].a < 0.7f && pixels[Pixel(f.camera, new Vector3(-0.7f, 0f, 3f))].a > 0.99f);
        int selectedPixel = Pixel(f.camera, new Vector3(-0.7f, 0f, 3f));
        int occludedPixel = Pixel(f.camera, new Vector3(0.75f, 0f, 4f));
        f.blur.SetActive(true);
        f.blur.settings.debug = MaskBlurFeature.DebugMode.Mask;
        f.selected.SetFloat("_MaskBlurWeight", 1f);
        Color[] originalMask = Render(f.camera, f.report);
        Add(f, "Blur.Mask.Weight1", "Visible PBRToon material mask reaches one; completely occluded selected object stays zero", originalMask,
            pixels => Scalar(pixels) && pixels[selectedPixel].r > 0.99f && pixels[occludedPixel].r < 0.001f && CountNonzero(pixels) > 100);
        f.selected.SetFloat("_MaskBlurWeight", 0.35f);
        Add(f, "Blur.Mask.Weight035", "R8 coverage retains fractional material weight", Render(f.camera, f.report),
            pixels => Scalar(pixels) && pixels[selectedPixel].r > 0.34f && pixels[selectedPixel].r < 0.36f && pixels[occludedPixel].r < 0.001f);
        f.selected.SetFloat("_MaskBlurWeight", 0f);
        Add(f, "Blur.Mask.Weight0", "Zero material weight leaves an empty source", Render(f.camera, f.report), Zero);
        f.selected.SetFloat("_MaskBlurWeight", 1f);
        f.visible.SetActive(false);
        Color[] hiddenScene = CaptureInactive(f);
        Add(f, "Blur.Mask.FullyOccluded", "Only fully occluded selected geometry remains; mask is empty", Render(f.camera, f.report), Zero, hiddenScene);
        f.visible.SetActive(true);
        f.blur.settings.layerMask = 0;
        Add(f, "Blur.Mask.ExcludedLayers", "An empty selection excludes both selected meshes", Render(f.camera, f.report), Zero);
        f.blur.settings.layerMask = 1 << SelectedLayer;
        f.blur.settings.debug = MaskBlurFeature.DebugMode.Off;
        for (int kernel = 0; kernel < 2; kernel++)
        {
            f.blur.settings.kernel = (MaskBlurFeature.KernelMode)kernel;
            for (int quality = 0; quality < 3; quality++)
            {
                f.blur.settings.quality = (MaskBlurFeature.QualityMode)quality;
                Add(f, "Blur.Composite." + f.blur.settings.kernel + "." + f.blur.settings.quality,
                    "Non-empty selected HDR emission is diffused; alpha remains scene alpha", Render(f.camera, f.report),
                    pixels => CountEffects(pixels, f.scene) > 100 && CountOutsideEffects(pixels, f.scene, originalMask) > 20);
            }
        }
        f.blur.settings.blurRadius = 0f;
        Add(f, "Blur.Composite.Radius0", "Zero radius retains selected color gain without outside diffusion", Render(f.camera, f.report),
            pixels => CountEffects(pixels, f.scene) > 100 && CountOutsideEffects(pixels, f.scene, originalMask) == 0);
        f.blur.SetActive(false);
        f.rbr.SetActive(true);
        f.rbr.settings.debug = MaskRBRFeature.DebugMode.Mask;
        Color[] objectMask = null;
        for (int source = 0; source < 3; source++)
        {
            f.rbr.settings.source = (MaskRBRFeature.SourceMode)source;
            Color[] pixels = Render(f.camera, f.report);
            if (source == 1) objectMask = pixels;
            int mode = source;
            Add(f, "RBR.Mask." + f.rbr.settings.source, "Visible solid-override object/sky coverage and foreground occlusion", pixels,
                values => Scalar(values) && values[occludedPixel].r < 0.001f
                    && (mode == 0 ? values[0].r > 0.99f && values[selectedPixel].r < 0.001f
                        : mode == 1 ? values[0].r < 0.001f && values[selectedPixel].r > 0.99f
                        : values[0].r > 0.99f && values[selectedPixel].r > 0.99f));
        }
        f.rbr.settings.source = MaskRBRFeature.SourceMode.Objects;
        f.rbr.settings.debug = MaskRBRFeature.DebugMode.Rays;
        Color[] rays = Render(f.camera, f.report);
        Add(f, "RBR.Rays.OffCenterMainLight", "Forward off-center main light produces non-empty radial source spreading", rays,
            pixels => Scalar(pixels) && CountNonzero(pixels) > 100 && CountOutsideNonzero(pixels, objectMask) > 20);
        f.rbr.settings.debug = MaskRBRFeature.DebugMode.Off;
        Add(f, "RBR.Composite.OffCenterMainLight", "Colored real main-light rays contribute outside the original source contour", Render(f.camera, f.report),
            pixels => CountEffects(pixels, f.scene) > 100 && CountOutsideEffects(pixels, f.scene, objectMask) > 20);
        f.rbr.settings.debug = MaskRBRFeature.DebugMode.Rays;
        f.light.enabled = false;
        Color[] missingScene = CaptureInactive(f);
        Add(f, "RBR.Rays.MissingMainLight", "No actual main directional light yields zero rays", Render(f.camera, f.report), Zero, missingScene);
        f.light.enabled = true;
        f.light.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        Color[] behindScene = CaptureInactive(f);
        Add(f, "RBR.Rays.BehindMainLight", "Backward main light yields zero rays", Render(f.camera, f.report), Zero, behindScene);
        SetForwardLight(f.light);
        f.rbr.settings.layerMask = 0;
        f.rbr.settings.debug = MaskRBRFeature.DebugMode.Mask;
        Add(f, "RBR.Mask.EmptySelection", "Empty object selection produces zero coverage", Render(f.camera, f.report), Zero);
        f.rbr.settings.debug = MaskRBRFeature.DebugMode.Rays;
        Add(f, "RBR.Rays.EmptySelection", "Empty object source produces zero radial emission", Render(f.camera, f.report), Zero);
    }

    private static string[] ExpectedCaseNames()
    {
        var names = new List<string> { "Scene.Reference", "Blur.Mask.Weight1", "Blur.Mask.Weight035",
            "Blur.Mask.Weight0", "Blur.Mask.FullyOccluded", "Blur.Mask.ExcludedLayers" };
        for (int kernel = 0; kernel < 2; kernel++)
            for (int quality = 0; quality < 3; quality++)
                names.Add("Blur.Composite." + (MaskBlurFeature.KernelMode)kernel + "." + (MaskBlurFeature.QualityMode)quality);
        names.AddRange(new[] { "Blur.Composite.Radius0", "RBR.Mask.Sky", "RBR.Mask.Objects", "RBR.Mask.SkyAndObjects",
            "RBR.Rays.OffCenterMainLight", "RBR.Composite.OffCenterMainLight", "RBR.Rays.MissingMainLight",
            "RBR.Rays.BehindMainLight", "RBR.Mask.EmptySelection", "RBR.Rays.EmptySelection" });
        return names.ToArray();
    }

    private static Color[] CaptureInactive(Fixture f)
    {
        bool blurActive = f.blur.isActive, rbrActive = f.rbr.isActive;
        try { f.blur.SetActive(false); f.rbr.SetActive(false); return Render(f.camera, f.report); }
        finally { f.blur.SetActive(blurActive); f.rbr.SetActive(rbrActive); }
    }

    private static void Add(Fixture f, string name, string contract, Color[] actual, Func<Color[], bool> control, Color[] scene = null)
    {
        var item = new CaseResult { name = name, contract = contract, pixelCount = actual.Length,
            baselineFile = name + ".rgba-f32.bin.gz.b64" };
        scene = scene ?? f.scene;
        foreach (Color pixel in actual)
        {
            for (int c = 0; c < 4; c++) item.finite &= Finite(pixel[c]);
            item.maxValue = Mathf.Max(item.maxValue, Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)));
        }
        item.nonzeroPixels = CountNonzero(actual);
        item.effectPixels = CountEffects(actual, scene);
        for (int i = 0; i < actual.Length; i++)
            item.maxAlphaError = Mathf.Max(item.maxAlphaError, Mathf.Abs(actual[i].a - scene[i].a));
        item.alphaPreserved = item.maxAlphaError <= Tolerance;
        item.controlPassed = item.finite && control(actual) && item.alphaPreserved;
        string path = Path.Combine(f.baselineDirectory, item.baselineFile);
        if (f.phase == "baseline")
        {
            Require(!File.Exists(path), "Refusing to overwrite a baseline: " + path);
            WriteRaw(path, actual);
            item.comparisonPassed = true;
        }
        else
        {
            Require(File.Exists(path), "Missing baseline: " + path);
            Color[] expected = ReadRaw(path);
            Require(expected.Length == actual.Length, "Mismatched baseline dimensions: " + name);
            for (int i = 0; i < actual.Length; i++)
            {
                bool different = false;
                for (int c = 0; c < 4; c++)
                {
                    Require(Finite(expected[i][c]), "Non-finite baseline: " + name);
                    float error = Mathf.Abs(actual[i][c] - expected[i][c]);
                    float normalized = error / (1f + Mathf.Abs(expected[i][c]));
                    item.maxError = Mathf.Max(item.maxError, error);
                    item.maxNormalizedError = Mathf.Max(item.maxNormalizedError, normalized);
                    different |= normalized > Tolerance;
                }
                if (different) item.differingPixels++;
            }
            item.comparisonPassed = item.differingPixels == 0;
            if (!item.comparisonPassed) WriteRaw(Path.Combine(f.outputDirectory, name + ".actual.rgba-f32.bin.gz.b64"), actual);
        }
        item.passed = item.controlPassed && item.comparisonPassed;
        f.cases.Add(item);
    }

    private static void WriteRaw(string path, Color[] pixels)
    {
        using (var memory = new MemoryStream())
        {
            using (var compressed = new GZipStream(memory, System.IO.Compression.CompressionLevel.Optimal, true))
            using (var writer = new BinaryWriter(compressed))
                foreach (Color pixel in pixels)
                    for (int c = 0; c < 4; c++) writer.Write(pixel[c]);
            using (var file = new StreamWriter(File.Open(path, FileMode.CreateNew, FileAccess.Write)))
                file.Write(Convert.ToBase64String(memory.ToArray()));
        }
    }

    private static Color[] ReadRaw(string path)
    {
        byte[] raw;
        using (var file = new MemoryStream(Convert.FromBase64String(File.ReadAllText(path))))
        using (var compressed = new GZipStream(file, CompressionMode.Decompress))
        using (var memory = new MemoryStream())
        {
            compressed.CopyTo(memory);
            raw = memory.ToArray();
        }
        Require(raw.Length == Width * Height * 16L, "Unexpected decompressed baseline byte count: " + path);
        var pixels = new Color[Width * Height];
        using (var reader = new BinaryReader(new MemoryStream(raw)))
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return pixels;
    }

    private static bool Scalar(Color[] pixels)
    {
        foreach (Color value in pixels)
            if (value.r < -Tolerance || value.r > 1f + Tolerance || Mathf.Abs(value.r - value.g) > Tolerance || Mathf.Abs(value.r - value.b) > Tolerance) return false;
        return true;
    }
    private static bool Zero(Color[] pixels) { return CountNonzero(pixels) == 0; }
    private static int CountNonzero(Color[] pixels)
    {
        int count = 0;
        foreach (Color value in pixels) if (Mathf.Max(Mathf.Abs(value.r), Mathf.Max(Mathf.Abs(value.g), Mathf.Abs(value.b))) > 0.005f) count++;
        return count;
    }
    private static int CountEffects(Color[] actual, Color[] scene)
    {
        int count = 0;
        for (int i = 0; i < actual.Length; i++) if (RGBDifference(actual[i], scene[i]) > 0.005f) count++;
        return count;
    }
    private static int CountOutsideEffects(Color[] actual, Color[] scene, Color[] mask)
    {
        int count = 0;
        for (int i = 0; i < actual.Length; i++) if (mask[i].r < 0.001f && RGBDifference(actual[i], scene[i]) > 0.005f) count++;
        return count;
    }
    private static int CountOutsideNonzero(Color[] actual, Color[] mask)
    {
        int count = 0;
        for (int i = 0; i < actual.Length; i++) if (mask[i].r < 0.001f && actual[i].r > 0.005f) count++;
        return count;
    }
    private static float RGBDifference(Color a, Color b)
    {
        return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
    }
    private static int Pixel(Camera camera, Vector3 point)
    {
        Vector3 uv = camera.WorldToViewportPoint(point);
        return Mathf.Clamp((int)(uv.y * Height), 0, Height - 1) * Width + Mathf.Clamp((int)(uv.x * Width), 0, Width - 1);
    }

    private static GameObject MakeObject(Scene preview, string name, List<UnityEngine.Object> owned)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        owned.Add(go);
        SceneManager.MoveGameObjectToScene(go, preview);
        return go;
    }
    private static Camera MakeCamera(Scene preview, List<UnityEngine.Object> owned)
    {
        var camera = MakeObject(preview, "Shared mask isolated Game camera", owned).AddComponent<Camera>();
        camera.scene = preview;
        camera.enabled = false;
        camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.1f, 0.2f, 0.37f);
        camera.cullingMask = (1 << SelectedLayer) | (1 << OtherLayer);
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 20f;
        camera.fieldOfView = 60f;
        camera.aspect = (float)Width / Height;
        camera.rect = new Rect(0f, 0f, 1f, 1f);
        camera.allowHDR = true;
        camera.allowMSAA = false;
        camera.allowDynamicResolution = false;
        var data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        data.SetRenderer(0);
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.antialiasing = AntialiasingMode.None;
        data.volumeLayerMask = 0;
        return camera;
    }
    private static Material MakeToon(List<UnityEngine.Object> owned)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ToonPath);
        Require(shader != null && shader.isSupported, "Production PBRToon shader is missing or unsupported.");
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        owned.Add(material);
        material.SetColor("_BaseColor", new Color(0.8f, 0.1f, 0.05f, 1f));
        material.SetFloat("_Brightness", 0f);
        material.SetFloat("_SSSWeight", 0f);
        material.SetFloat("_FillLightStrength", 0f);
        material.SetFloat("_RimStrength", 0f);
        material.SetFloat("_EnableOutline", 0f);
        material.SetFloat("_OutlineScale", 0f);
        material.DisableKeyword("ENABLE_OUTLINE");
        material.SetFloat("_MaskBlurWeight", 1f);
        var emission = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
        emission.SetPixel(0, 0, new Color(2f, 0.2f, 0.1f, 1f));
        emission.Apply(false, true);
        owned.Add(emission);
        material.SetTexture("_EmissionTex", emission);
        return material;
    }
    private static Material MakeUnlit(Color color, List<UnityEngine.Object> owned)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        Require(shader != null && shader.isSupported, "URP Unlit shader is missing or unsupported.");
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        material.SetColor("_BaseColor", color);
        owned.Add(material);
        return material;
    }
    private static GameObject MakeCube(Scene preview, string name, Vector3 position, Vector3 scale, int layer,
        Material material, List<UnityEngine.Object> owned)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.hideFlags = HideFlags.HideAndDontSave;
        owned.Add(go);
        SceneManager.MoveGameObjectToScene(go, preview);
        go.layer = layer;
        go.transform.SetPositionAndRotation(position, Quaternion.identity);
        go.transform.localScale = scale;
        var collider = go.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }
    private static Light MakeLight(Scene preview, List<UnityEngine.Object> owned)
    {
        var light = MakeObject(preview, "Shared mask main directional light", owned).AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.25f, 0.6f, 1f, 1f);
        light.intensity = 2f;
        light.useColorTemperature = false;
        light.shadows = LightShadows.None;
        light.cullingMask = (1 << SelectedLayer) | (1 << OtherLayer);
        return light;
    }
    private static void SetForwardLight(Light light)
    {
        light.transform.rotation = Quaternion.LookRotation(-new Vector3(-0.7f, 0.3f, 3f).normalized, Vector3.up);
    }
    private static Color[] Render(Camera camera, Report report)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture target = null;
        Texture2D readback = null;
        try
        {
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                name = "Shared mask isolated float capture", hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1, useMipMap = false, autoGenerateMips = false
            };
            Require(target.Create(), "Could not create a float render-request target.");
            RenderTexture.active = target;
            GL.Clear(true, true, Sentinel);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "URP SingleCameraRequest is unsupported.");
            int beginBefore = report.beginCameraCount;
            RenderPipeline.SubmitRenderRequest(camera, request);
            report.renderRequestCount++;
            Require(report.beginCameraCount > beginBefore, "The request did not invoke real camera rendering callbacks.");
            Require(camera.targetTexture == null, "The request failed to restore camera.targetTexture.");
            RenderTexture.active = target;
            readback = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            readback.Apply(false, false);
            Color[] pixels = readback.GetPixels();
            Require(pixels.Length == Width * Height, "Float readback dimensions are incorrect.");
            Require(Mathf.Abs(pixels[0].r - Sentinel.r) > 0.01f, "Render request left the target at the sentinel.");
            return pixels;
        }
        finally
        {
            RenderTexture.active = previous;
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        }
    }
    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static int[] FeatureIds(UniversalRendererData renderer)
    {
        var ids = new int[renderer.rendererFeatures.Count];
        for (int i = 0; i < ids.Length; i++) ids[i] = renderer.rendererFeatures[i] == null ? 0 : renderer.rendererFeatures[i].GetInstanceID();
        return ids;
    }
    private static bool EqualIds(int[] a, int[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    private static string Serialize(Report report, List<CaseResult> cases)
    {
        string summary = JsonUtility.ToJson(report);
        var json = new StringBuilder(summary.Substring(0, summary.Length - 1));
        json.Append(",\"cases\":[");
        for (int i = 0; i < cases.Count; i++) { if (i > 0) json.Append(','); json.Append(JsonUtility.ToJson(cases[i])); }
        json.Append("]}");
        return json.ToString();
    }
}
