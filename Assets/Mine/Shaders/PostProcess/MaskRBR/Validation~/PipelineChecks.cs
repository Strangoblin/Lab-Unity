using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

/// <summary>Runs the production feature in an isolated preview scene and cloned URP configuration.</summary>
/// <remarks>Main receives an optional JSON file path and optional PNG output directory. No assets or existing scenes are saved.</remarks>
public class Script
{
    private const int Width = 640;
    private const int Height = 360;
    private const int SelectedLayer = 30;
    private const int OtherLayer = 29;
    private const float Tolerance = 0.002f;
    private const string Root = "Assets/Mine/Shaders/PostProcess/MaskRBR/";
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Color Sentinel = new Color(-17f, -13f, -11f, -7f);

    [Serializable]
    public sealed class CaseResult
    {
        public string name, contract;
        public bool passed, finite = true;
        public int pixelCount, changedPixels, nonzeroPixels, outsideMaskPixels;
        public float maxError, maxNormalizedError, maxRed, maxBlue, sumRays;
        public float[] boundOutputSize;
    }

    [Serializable]
    public sealed class Report
    {
        public bool passed, pipelineRestored, activeSceneUnchanged, originalRendererUnchanged, lightSettingsRestored;
        public int caseCount, failures, renderRequestCount, beginCameraCount;
        public string error, graphicsDevice, renderer, scope, graphBudget;
        public string[] renderErrors;
        public string pngDisplayMapping;
    }

    // ════════════════════════════════════════════════════════════
    //  Entry — isolated Game/Base camera and temporary pipeline/renderer clones
    // ════════════════════════════════════════════════════════════
    public static object Main(string[] args)
    {
        var report = new Report
        {
            graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName,
            scope = "Actual URP SingleCameraRequest with main directional light, production MaskRBRFeature/MaskRBR shaders, scalar receiver HG/cone and radial CPU oracles, visible and fully occluded opaque geometry in a disposable preview scene, cloned renderer and pipeline. Existing scene feature order, XR and performance are outside this fixture.",
            graphBudget = "Not measured: URP suppresses RenderGraph debug data during render requests; compiled graph budget needs a normal camera-frame capture.",
            pngDisplayMapping = "Optional full 640x360 PNG: linear HDR RGB uses c/(1+c), then linear-to-sRGB; numeric assertions use linear RGBAFloat readback."
        };
        var cases = new List<CaseResult>();
        var errors = new List<string>();
        var originalQualityPipeline = QualitySettings.renderPipeline;
        var originalDefaultPipeline = GraphicsSettings.defaultRenderPipeline;
        Scene originalActiveScene = SceneManager.GetActiveScene();
        RenderTexture originalRT = RenderTexture.active;
        bool originalLinearLight = GraphicsSettings.lightsUseLinearIntensity;
        bool originalColorTemperature = GraphicsSettings.lightsUseColorTemperature;
        UniversalRenderPipelineAsset sourcePipeline = null, pipeline = null;
        UniversalRendererData sourceRenderer = null, renderer = null;
        MaskRBRFeature feature = null;
        int[] originalFeatureIds = null;
        Scene preview = default(Scene);
        var owned = new List<UnityEngine.Object>();
        var originalMaskGlobals = new Texture[4];
        string[] maskGlobals = { "_CameraDepthTexture", "_MaskRBRObjectMaskTexture", "_MaskRBRMaskTexture", "_MaskRBRRayTexture" };
        for (int i = 0; i < maskGlobals.Length; i++) originalMaskGlobals[i] = Shader.GetGlobalTexture(maskGlobals[i]);
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
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "A real graphics device is required.");
            Require(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                && SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat), "Float render/readback formats are required.");
            sourcePipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Require(sourcePipeline != null, "The current pipeline must be URP.");
            FieldInfo rendererList = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", PrivateInstance);
            FieldInfo defaultIndex = typeof(UniversalRenderPipelineAsset).GetField("m_DefaultRendererIndex", PrivateInstance);
            Require(rendererList != null && defaultIndex != null, "URP renderer-list fields are unavailable.");
            var originalRenderers = (ScriptableRendererData[])rendererList.GetValue(sourcePipeline);
            sourceRenderer = originalRenderers[(int)defaultIndex.GetValue(sourcePipeline)] as UniversalRendererData;
            Require(sourceRenderer != null, "The default renderer must be a UniversalRendererData.");
            originalFeatureIds = FeatureIds(sourceRenderer);
            renderer = UnityEngine.Object.Instantiate(sourceRenderer);
            renderer.name = "MaskRBR disposable renderer clone";
            renderer.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(renderer);
            renderer.rendererFeatures.Clear();
            renderer.opaqueLayerMask = (1 << SelectedLayer) | (1 << OtherLayer);
            renderer.intermediateTextureMode = IntermediateTextureMode.Always;
            feature = ScriptableObject.CreateInstance<MaskRBRFeature>();
            feature.name = "MaskRBR disposable validation feature";
            feature.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(feature);
            feature.settings.shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "MaskRBR.shader");
            feature.settings.maskShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "MaskRBRMask.shader");
            Require(feature.settings.shader != null && feature.settings.maskShader != null,
                "The explicit production shader references are missing.");
            feature.settings.source = MaskRBRFeature.SourceMode.Objects;
            feature.settings.layerMask = 1 << SelectedLayer;
            feature.settings.quality = MaskRBRFeature.QualityMode.High;
            feature.settings.length = 0.3f;
            feature.settings.falloff = 1f;
            feature.settings.jitterStrength = 0f;
            feature.settings.halfAngle = 60f;
            feature.settings.anisotropy = 0.7f;
            feature.settings.intensity = 0.4f;
            feature.Create();
            renderer.rendererFeatures.Add(feature);
            renderer.SetDirty();
            pipeline = UnityEngine.Object.Instantiate(sourcePipeline);
            pipeline.name = "MaskRBR disposable URP clone";
            pipeline.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(pipeline);
            rendererList.SetValue(pipeline, new ScriptableRendererData[] { renderer });
            defaultIndex.SetValue(pipeline, 0);
            pipeline.renderScale = 1f;
            pipeline.msaaSampleCount = 1;
            pipeline.supportsHDR = true;
            FieldInfo mainLightMode = typeof(UniversalRenderPipelineAsset).GetField("m_MainLightRenderingMode", PrivateInstance);
            Require(mainLightMode != null, "URP main-light mode field is unavailable.");
            mainLightMode.SetValue(pipeline, LightRenderingMode.PerPixel);
            report.renderer = sourceRenderer.name + " (temporary clone; only MaskRBR feature)";

            preview = EditorSceneManager.NewPreviewScene();
            Camera camera = MakeCamera(preview, owned);
            Material red = MakeUnlit(new Color(4f, 0f, 0f, 1f), owned);
            Material blue = MakeUnlit(new Color(0f, 0f, 8f, 1f), owned);
            MakeCube(preview, "Visible selected red source", new Vector3(-0.7f, 0f, 3f), new Vector3(0.65f, 1.0f, 0.4f), SelectedLayer, red, owned);
            Vector3 hiddenCenter = new Vector3(0.75f, 0f, 4f);
            MakeCube(preview, "Fully hidden selected red source", hiddenCenter, new Vector3(0.7f, 0.7f, 0.3f), SelectedLayer, red, owned);
            MakeCube(preview, "Unselected blue occluder", new Vector3(0.75f, 0f, 2.6f), new Vector3(1.2f, 1.2f, 0.4f), OtherLayer, blue, owned);
            Light light = MakeLight(preview, owned);
            Vector3 lightDirection = new Vector3(-0.7f, 0.3f, 3f).normalized;
            light.transform.rotation = Quaternion.LookRotation(-lightDirection, Vector3.up);
            light.color = Color.white;
            light.intensity = 2f;
            light.useColorTemperature = false;
            QualitySettings.renderPipeline = pipeline;

            RunCases(feature, pipeline, camera, light, lightDirection, red, blue, hiddenCenter, cases, report, args);
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
            for (int i = 0; i < maskGlobals.Length; i++) Shader.SetGlobalTexture(maskGlobals[i], originalMaskGlobals[i]);
            Shader.SetGlobalVector("_MainLightPosition", originalLightPosition);
            Shader.SetGlobalVector("_MainLightColor", originalLightColor);
            GraphicsSettings.lightsUseLinearIntensity = originalLinearLight;
            GraphicsSettings.lightsUseColorTemperature = originalColorTemperature;
            report.pipelineRestored = QualitySettings.renderPipeline == originalQualityPipeline
                && GraphicsSettings.defaultRenderPipeline == originalDefaultPipeline;
            report.activeSceneUnchanged = SceneManager.GetActiveScene() == originalActiveScene;
            report.originalRendererUnchanged = sourceRenderer == null || EqualIds(FeatureIds(sourceRenderer), originalFeatureIds);
            report.lightSettingsRestored = GraphicsSettings.lightsUseLinearIntensity == originalLinearLight
                && GraphicsSettings.lightsUseColorTemperature == originalColorTemperature;
        }
        foreach (CaseResult item in cases) if (!item.passed) report.failures++;
        report.caseCount = cases.Count;
        report.renderErrors = errors.ToArray();
        report.passed = report.error == null && report.caseCount > 0 && report.failures == 0
            && report.pipelineRestored && report.activeSceneUnchanged && report.originalRendererUnchanged
            && report.lightSettingsRestored && errors.Count == 0;
        string json = Serialize(report, cases);
        string output = args != null && args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("MASKRBR_VALIDATION_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, json);
        }
        return json;
    }

    // ════════════════════════════════════════════════════════════
    //  Cases — visible masks, independent scalar rays and real main-light emission
    // ════════════════════════════════════════════════════════════
    private static void RunCases(MaskRBRFeature feature, UniversalRenderPipelineAsset pipeline, Camera camera,
        Light light, Vector3 lightDirection, Material selected, Material unselected, Vector3 hiddenCenter,
        List<CaseResult> cases, Report report, string[] args)
    {
        feature.SetActive(false);
        Color[] baseline = Render(camera, report);
        float minAlpha = 1f, maxAlpha = 0f;
        foreach (Color pixel in baseline) { minAlpha = Mathf.Min(minAlpha, pixel.a); maxAlpha = Mathf.Max(maxAlpha, pixel.a); }
        cases.Add(new CaseResult
        {
            name = "Control.NonuniformSceneAlpha", contract = "Float baseline contains fractional background alpha and opaque geometry alpha",
            passed = minAlpha > 0f && minAlpha < 0.9f && maxAlpha > 0.99f, finite = Finite(minAlpha) && Finite(maxAlpha)
        });
        feature.SetActive(true);
        feature.settings.debug = MaskRBRFeature.DebugMode.Off;
        feature.settings.intensity = 0f;
        AddCompare(cases, "Skip.IntensityZero", "Zero effect intensity preserves real pipeline RGBA", Render(camera, report), baseline);
        feature.settings.intensity = 0.4f;
        feature.settings.length = 0f;
        AddCompare(cases, "Skip.LengthZero", "Off debug and zero length preserve real pipeline RGBA", Render(camera, report), baseline);
        feature.settings.length = 0.3f;

        feature.settings.debug = MaskRBRFeature.DebugMode.Mask;
        Color[] objectMask = Render(camera, report);
        var objects = InspectScalar("Mask.VisibleOpaqueAndOcclusion",
            "Selected visible geometry enters the mask; unselected blue occluder and fully hidden selected object do not", objectMask);
        Vector3 hiddenUV = camera.WorldToViewportPoint(hiddenCenter);
        int hiddenPixel = Pixel(hiddenUV.x, hiddenUV.y);
        Vector3 focusUV = camera.WorldToViewportPoint(camera.transform.position + lightDirection);
        int focusPixel = Pixel(focusUV.x, focusUV.y);
        objects.maxError = Mathf.Abs(objectMask[hiddenPixel].r);
        objects.passed &= objects.nonzeroPixels > 200 && objects.nonzeroPixels < objectMask.Length / 3
            && objects.maxError < 0.01f && baseline[hiddenPixel].b > 1f && objectMask[focusPixel].r > 0.99f;
        cases.Add(objects);

        feature.settings.source = MaskRBRFeature.SourceMode.Sky;
        Color[] skyMask = Render(camera, report);
        var sky = InspectScalar("Mask.SkyDepth", "Far-plane clear background enters sky mask while solid foreground is excluded", skyMask);
        sky.passed &= sky.nonzeroPixels > skyMask.Length / 2 && sky.nonzeroPixels < skyMask.Length - 200
            && skyMask[focusPixel].r < 0.01f && skyMask[hiddenPixel].r < 0.01f;
        cases.Add(sky);
        feature.settings.source = MaskRBRFeature.SourceMode.SkyAndObjects;
        Color[] union = Render(camera, report);
        var unionExpected = new Color[union.Length];
        for (int i = 0; i < union.Length; i++)
        {
            float value = Mathf.Max(skyMask[i].r, objectMask[i].r);
            unionExpected[i] = new Color(value, value, value, baseline[i].a);
        }
        AddCompare(cases, "Mask.UnionAndAlpha", "Combined mask is the actual sky/object union and preserves original scene alpha", union, unionExpected);

        feature.settings.source = MaskRBRFeature.SourceMode.Objects;
        feature.settings.debug = MaskRBRFeature.DebugMode.Rays;
        Color[] highRays = null;
        for (int quality = 0; quality < 3; quality++)
        {
            feature.settings.quality = (MaskRBRFeature.QualityMode)quality;
            Color[] rays = Render(camera, report);
            var item = InspectScalar("Rays.Quality" + quality,
                "Actual fixed quality variant emits bounded grayscale scalar rays beyond the selected silhouette", rays);
            for (int i = 0; i < rays.Length; i++)
                if (objectMask[i].r < 0.01f && rays[i].r > 0.005f) item.outsideMaskPixels++;
            item.passed &= item.maxRed > 0.1f && item.outsideMaskPixels > 20;
            VerifyBindings(feature, item, quality);
            cases.Add(item);
            if (quality == 2) highRays = rays;
        }
        AddCompare(cases, "Rays.OffCenterDirectionOracle",
            "Independent Camera viewport direction, bilinear mask taps and HG/cone predict actual off-center main-light scalar rays",
            highRays, RayOracle(camera, lightDirection, objectMask, baseline, feature.settings, 48));

        Vector4 boundDirection = Shader.GetGlobalVector("_MainLightPosition");
        Vector4 boundLightColor = Shader.GetGlobalVector("_MainLightColor");
        Color expectedLightColor = ActiveLightColor(light);
        var lightBinding = new CaseResult
        {
            name = "MainLight.DirectionAndLinearBinding",
            contract = "URP binds the real directional light toward the selected object, with real active-space color and intensity",
            finite = Finite(boundDirection.x) && Finite(boundDirection.y) && Finite(boundDirection.z)
                && Finite(boundLightColor.x) && Finite(boundLightColor.y) && Finite(boundLightColor.z)
        };
        lightBinding.maxError = Mathf.Max(Vector3.Distance(new Vector3(boundDirection.x, boundDirection.y, boundDirection.z), lightDirection),
            Mathf.Abs(boundDirection.w));
        lightBinding.maxNormalizedError = ColorError(new Color(boundLightColor.x, boundLightColor.y, boundLightColor.z), expectedLightColor);
        lightBinding.passed = lightBinding.finite && lightBinding.maxError < Tolerance && lightBinding.maxNormalizedError < Tolerance;
        cases.Add(lightBinding);

        feature.settings.debug = MaskRBRFeature.DebugMode.Off;
        Color[] whiteComposite = Render(camera, report);
        AddCompare(cases, "Composite.MainLightHDRAndAlpha",
            "Production graph adds scalar rays times actual white main-light intensity and effect strength, retaining original alpha",
            whiteComposite, CompositeOracle(baseline, highRays, expectedLightColor, feature.settings.intensity));

        light.color = new Color(0.25f, 0.5f, 1f, 1f);
        feature.settings.debug = MaskRBRFeature.DebugMode.Rays;
        AddCompare(cases, "Rays.LightColorIndependent",
            "Changing real directional-light RGB does not color or alter the scalar ray texture", Render(camera, report), highRays);
        feature.settings.debug = MaskRBRFeature.DebugMode.Off;
        Color[] coloredComposite = Render(camera, report);
        AddCompare(cases, "Composite.LinearMainLightColor",
            "Each emitted RGB channel inherits URP linear main-light color rather than scene RGB or white clipping",
            coloredComposite, CompositeOracle(baseline, highRays, ActiveLightColor(light), feature.settings.intensity));
        light.intensity = 4f;
        feature.settings.debug = MaskRBRFeature.DebugMode.Rays;
        AddCompare(cases, "Rays.LightIntensityIndependent",
            "Doubling the real main-light intensity leaves its scalar angular/radial coverage unchanged", Render(camera, report), highRays);
        feature.settings.debug = MaskRBRFeature.DebugMode.Off;
        AddCompare(cases, "Composite.LightIntensityLinear",
            "Doubling real main-light intensity doubles RGB emission above baseline", Render(camera, report),
            CompositeOracle(baseline, highRays, ActiveLightColor(light), feature.settings.intensity));
        feature.settings.intensity = 0.2f;
        AddCompare(cases, "Composite.EffectIntensityLinear",
            "Halving effect strength cancels doubled main-light intensity and preserves every original alpha",
            Render(camera, report), coloredComposite);
        feature.settings.intensity = 0.4f;
        light.intensity = 2f;
        light.color = Color.white;

        selected.SetColor("_BaseColor", new Color(0f, 11f, 0.5f, 1f));
        unselected.SetColor("_BaseColor", new Color(16f, 1f, 0f, 1f));
        Color originalBackground = camera.backgroundColor;
        camera.backgroundColor = new Color(2f, 0f, 5f, originalBackground.a);
        feature.settings.debug = MaskRBRFeature.DebugMode.Rays;
        AddCompare(cases, "Rays.SceneRGBIndependent",
            "Changing selected material, occluder material and background HDR RGB does not alter scalar rays",
            Render(camera, report), highRays);
        selected.SetColor("_BaseColor", new Color(4f, 0f, 0f, 1f));
        unselected.SetColor("_BaseColor", new Color(0f, 0f, 8f, 1f));
        camera.backgroundColor = originalBackground;

        feature.settings.source = MaskRBRFeature.SourceMode.SkyAndObjects;
        feature.settings.length = 0f;
        feature.settings.anisotropy = 0f;
        feature.settings.halfAngle = 90f;
        AddCompare(cases, "Phase.IsotropicZeroLength",
            "Zero-length debug still applies phase; g=0 and a full angular window retain one-tap union mask with peak one",
            Render(camera, report), union);
        feature.settings.anisotropy = 0.7f;
        feature.settings.halfAngle = 35f;
        Color[] phaseRays = Render(camera, report);
        AddCompare(cases, "Phase.OffCenterHGAndConeOracle",
            "Independent camera rays and peak-normalized HG/cone predict zero-length actual receiver emission and view/light sign",
            phaseRays, RayOracle(camera, lightDirection, union, baseline, feature.settings, 48));
        var monotonic = InspectScalar("Phase.AngularConcentration",
            "Receiver emission decreases at increasing angular separation while off-center source peak remains near one", phaseRays);
        int near = Pixel(focusUV.x - 0.07f, focusUV.y), far = Pixel(focusUV.x - 0.18f, focusUV.y);
        monotonic.passed &= union[near].r > 0.99f && union[far].r > 0.99f
            && phaseRays[focusPixel].r > 0.98f && phaseRays[focusPixel].r > phaseRays[near].r
            && phaseRays[near].r > phaseRays[far].r + 0.02f;
        cases.Add(monotonic);
        feature.settings.anisotropy = 0.95f;
        feature.settings.halfAngle = 12f;
        Color[] narrow = Render(camera, report);
        AddCompare(cases, "Phase.G95NarrowConeOracle",
            "Upper anisotropy endpoint remains finite and bounded, with peak one and exact zero outside a narrow angular window",
            narrow, RayOracle(camera, lightDirection, union, baseline, feature.settings, 48));
        var cone = InspectScalar("Phase.ExactWindowAndG95Concentration",
            "Selected pixels outside the angle window emit zero; upper g concentrates energy while retaining a nonzero source peak", narrow);
        int outsideWindow = 0;
        for (int i = 0; i < narrow.Length; i++)
        {
            Vector3 direction = camera.ViewportPointToRay(new Vector3(((i % Width) + 0.5f) / Width,
                ((i / Width) + 0.5f) / Height, 0f)).direction.normalized;
            if (union[i].r > 0.99f && Vector3.Dot(direction, lightDirection) < Mathf.Cos(12f * Mathf.Deg2Rad) - 0.001f)
            {
                outsideWindow++;
                cone.maxError = Mathf.Max(cone.maxError, Mathf.Abs(narrow[i].r));
            }
        }
        cone.passed &= outsideWindow > narrow.Length / 3 && cone.maxError < 0.00001f
            && narrow[focusPixel].r > 0.95f && cone.sumRays < monotonic.sumRays * 0.25f;
        cases.Add(cone);
        feature.settings.anisotropy = 0.7f;
        feature.settings.length = 0.3f;
        feature.settings.source = MaskRBRFeature.SourceMode.Objects;
        feature.settings.quality = MaskRBRFeature.QualityMode.Low;
        Color[] lowNarrow = Render(camera, report);
        var lowWindow = InspectScalar("Phase.LowUpsampleWindow",
            "Low radial texture is upsampled before final-resolution angular emission; receiver pixels outside the cone stay zero", lowNarrow);
        int lowOutside = 0;
        for (int i = 0; i < lowNarrow.Length; i++)
        {
            Vector3 direction = camera.ViewportPointToRay(new Vector3(((i % Width) + 0.5f) / Width,
                ((i / Width) + 0.5f) / Height, 0f)).direction.normalized;
            if (Vector3.Dot(direction, lightDirection) < Mathf.Cos(12f * Mathf.Deg2Rad) - 0.001f)
            {
                lowOutside++;
                lowWindow.maxError = Mathf.Max(lowWindow.maxError, Mathf.Abs(lowNarrow[i].r));
            }
        }
        lowWindow.passed &= lowOutside > lowNarrow.Length / 3 && lowWindow.maxError < 0.00001f && lowWindow.maxRed > 0.1f;
        cases.Add(lowWindow);
        feature.settings.quality = MaskRBRFeature.QualityMode.High;
        feature.settings.halfAngle = 60f;

        light.enabled = false;
        AddZero(cases, "MainLight.Absent", "Absent real main light emits no scalar rays", Render(camera, report));
        feature.settings.debug = MaskRBRFeature.DebugMode.Mask;
        AddCompare(cases, "Debug.MaskWithoutMainLight", "Mask inspection remains available when no directional main light exists",
            Render(camera, report), objectMask);
        feature.settings.debug = MaskRBRFeature.DebugMode.Rays;
        light.enabled = true;
        light.transform.rotation = Quaternion.LookRotation(lightDirection, Vector3.up);
        AddZero(cases, "MainLight.Behind", "Behind-camera real main directional light emits no scalar rays", Render(camera, report));
        light.transform.rotation = Quaternion.LookRotation(-lightDirection, Vector3.up);
        camera.orthographic = true;
        camera.orthographicSize = 1.5f;
        AddZero(cases, "MainLight.Orthographic", "Orthographic camera has no finite directional radial focus and emits no rays",
            Render(camera, report));
        camera.orthographic = false;

        feature.settings.jitterStrength = 1f;
        Color[] jittered = Render(camera, report);
        AddCompare(cases, "Jitter.StableAcrossRequests", "Spatial hash jitter remains identical across successive real camera requests",
            Render(camera, report), jittered);
        var jitterControl = InspectScalar("Control.JitterChangesTaps", "Nonzero spatial jitter changes actual boundary ray samples", jittered);
        for (int i = 0; i < jittered.Length; i++)
            jitterControl.maxNormalizedError = Mathf.Max(jitterControl.maxNormalizedError,
                Mathf.Abs(jittered[i].r - highRays[i].r) / (1f + Mathf.Abs(highRays[i].r)));
        jitterControl.passed &= jitterControl.maxNormalizedError > 0.0005f;
        cases.Add(jitterControl);
        feature.settings.jitterStrength = 0f;

        float[] renderScales = { 0.1f, 2f };
        for (int i = 0; i < renderScales.Length; i++)
        {
            pipeline.renderScale = renderScales[i];
            feature.settings.quality = i == 0 ? MaskRBRFeature.QualityMode.Low : MaskRBRFeature.QualityMode.High;
            Color[] scaledRays = Render(camera, report);
            var scaled = InspectScalar("Rays.RenderScaleEndpoint" + renderScales[i].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                "Render-scale endpoint retains finite grayscale rays and binds length to final request dimensions rather than intermediate size", scaledRays);
            scaled.passed &= scaled.nonzeroPixels > 20 && scaled.maxRed > 0.05f;
            VerifyBindings(feature, scaled, i == 0 ? 0 : 2);
            cases.Add(scaled);
        }
        pipeline.renderScale = 1f;
        feature.settings.quality = MaskRBRFeature.QualityMode.High;

        if (args != null && args.Length > 1 && !string.IsNullOrEmpty(args[1]))
        {
            Directory.CreateDirectory(args[1]);
            WritePNG(Path.Combine(args[1], "maskrbr-baseline.png"), baseline);
            WritePNG(Path.Combine(args[1], "maskrbr-mask.png"), objectMask, true);
            WritePNG(Path.Combine(args[1], "maskrbr-rays.png"), highRays, true);
            WritePNG(Path.Combine(args[1], "maskrbr-composite.png"), coloredComposite);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Fixture construction and float capture — no SaveAssets or existing camera changes
    // ════════════════════════════════════════════════════════════
    private static GameObject MakeObject(Scene preview, string name, List<UnityEngine.Object> owned)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        owned.Add(go);
        SceneManager.MoveGameObjectToScene(go, preview);
        return go;
    }

    private static Camera MakeCamera(Scene preview, List<UnityEngine.Object> owned)
    {
        var camera = MakeObject(preview, "MaskRBR isolated Game camera", owned).AddComponent<Camera>();
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

    private static Material MakeUnlit(Color color, List<UnityEngine.Object> owned)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        Require(shader != null && shader.isSupported, "URP Unlit shader is missing or unsupported.");
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        material.SetColor("_BaseColor", color);
        owned.Add(material);
        return material;
    }

    private static void MakeCube(Scene preview, string name, Vector3 position, Vector3 scale, int layer,
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
    }

    private static Light MakeLight(Scene preview, List<UnityEngine.Object> owned)
    {
        var light = MakeObject(preview, "MaskRBR main directional light", owned).AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.shadows = LightShadows.None;
        light.cullingMask = (1 << SelectedLayer) | (1 << OtherLayer);
        return light;
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
                name = "MaskRBR isolated float capture", hideFlags = HideFlags.HideAndDontSave,
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

    // ════════════════════════════════════════════════════════════
    //  Assertions and output — numerical data are independent of display PNG mapping
    // ════════════════════════════════════════════════════════════
    private static Color ActiveLightColor(Light light)
    {
        Color color = QualitySettings.activeColorSpace == ColorSpace.Linear ? light.color.linear : light.color;
        return new Color(color.r * light.intensity, color.g * light.intensity, color.b * light.intensity, 1f);
    }

    private static Color[] CompositeOracle(Color[] scene, Color[] rays, Color lightColor, float intensity)
    {
        var expected = new Color[scene.Length];
        for (int i = 0; i < expected.Length; i++)
            expected[i] = new Color(scene[i].r + lightColor.r * rays[i].r * intensity,
                scene[i].g + lightColor.g * rays[i].r * intensity,
                scene[i].b + lightColor.b * rays[i].r * intensity, scene[i].a);
        return expected;
    }

    private static Color[] RayOracle(Camera camera, Vector3 lightDirection, Color[] mask, Color[] scene,
        MaskRBRFeature.Settings settings, int taps)
    {
        var expected = new Color[mask.Length];
        Vector3 focus = camera.WorldToViewportPoint(camera.transform.position + lightDirection);
        float aspect = (float)Width / Height;
        float focusFade = Mathf.Clamp01(Vector3.Dot(camera.transform.forward, lightDirection) / 0.1f);
        for (int i = 0; i < expected.Length; i++)
        {
            Vector2 uv = new Vector2(((i % Width) + 0.5f) / Width, ((i / Width) + 0.5f) / Height);
            Vector3 direction = camera.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f)).direction.normalized;
            float angular = ReceiverEnvelope(direction, lightDirection, settings.anisotropy, settings.halfAngle);
            float value;
            if (settings.length <= 0f) value = SampleMask(mask, uv);
            else
            {
                Vector2 towardFocus = new Vector2(focus.x, focus.y) - uv;
                float distance = new Vector2(towardFocus.x * aspect, towardFocus.y).magnitude;
                Vector2 span = towardFocus * Mathf.Clamp01(settings.length / Mathf.Max(distance, 0.000001f));
                double sum = 0.0, totalWeight = 0.0;
                for (int tap = 0; tap < taps; tap++)
                {
                    float t = (float)tap / (taps - 1);
                    double weight = Math.Pow(2.0, -settings.falloff * t);
                    sum += SampleMask(mask, uv + span * t) * weight;
                    totalWeight += weight;
                }
                value = (float)(sum / totalWeight);
            }
            value *= angular * focusFade;
            expected[i] = new Color(value, value, value, scene[i].a);
        }
        return expected;
    }

    private static float ReceiverEnvelope(Vector3 cameraRay, Vector3 lightDirection, float g, float halfAngle)
    {
        double mu = Math.Max(-1.0, Math.Min(1.0, Vector3.Dot(cameraRay, lightDirection)));
        double outer = Math.Cos(halfAngle * Math.PI / 180.0), inner = Math.Cos(halfAngle * 0.8 * Math.PI / 180.0);
        double t = Math.Max(0.0, Math.Min(1.0, (mu - outer) / (inner - outer)));
        double cone = t * t * (3.0 - 2.0 * t);
        double peakDenominator = (1.0 - g) * (1.0 - g);
        double denominator = 1.0 + g * g - 2.0 * g * mu;
        return (float)(Math.Pow(peakDenominator / Math.Max(denominator, 0.00000001), 1.5) * cone);
    }

    private static float SampleMask(Color[] mask, Vector2 uv)
    {
        if (uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f) return 0f;
        float x = uv.x * Width - 0.5f, y = uv.y * Height - 0.5f;
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float tx = x - ix, ty = y - iy;
        int x0 = Mathf.Clamp(ix, 0, Width - 1), x1 = Mathf.Clamp(ix + 1, 0, Width - 1);
        int y0 = Mathf.Clamp(iy, 0, Height - 1), y1 = Mathf.Clamp(iy + 1, 0, Height - 1);
        return Mathf.Lerp(Mathf.Lerp(mask[y0 * Width + x0].r, mask[y0 * Width + x1].r, tx),
            Mathf.Lerp(mask[y1 * Width + x0].r, mask[y1 * Width + x1].r, tx), ty);
    }

    private static float ColorError(Color actual, Color expected)
    {
        float error = 0f;
        for (int channel = 0; channel < 3; channel++)
            error = Mathf.Max(error, Mathf.Abs(actual[channel] - expected[channel]) / (1f + Mathf.Abs(expected[channel])));
        return error;
    }

    private static CaseResult Inspect(string name, string contract, Color[] pixels)
    {
        var item = new CaseResult { name = name, contract = contract, pixelCount = pixels.Length };
        foreach (Color value in pixels)
        {
            for (int channel = 0; channel < 4; channel++) item.finite &= Finite(value[channel]);
            if (Mathf.Max(Mathf.Max(Mathf.Abs(value.r), Mathf.Abs(value.g)), Mathf.Abs(value.b)) > 0.005f) item.nonzeroPixels++;
            item.maxRed = Mathf.Max(item.maxRed, value.r);
            item.maxBlue = Mathf.Max(item.maxBlue, value.b);
            item.sumRays += value.r;
        }
        item.passed = item.finite;
        return item;
    }

    private static CaseResult InspectScalar(string name, string contract, Color[] pixels)
    {
        var item = Inspect(name, contract, pixels);
        foreach (Color value in pixels)
            item.passed &= value.r >= -Tolerance && value.r <= 1f + Tolerance
                && Mathf.Abs(value.r - value.g) < Tolerance && Mathf.Abs(value.r - value.b) < Tolerance;
        return item;
    }

    private static void AddZero(List<CaseResult> cases, string name, string contract, Color[] pixels)
    {
        var item = Inspect(name, contract, pixels);
        item.passed = item.finite && item.nonzeroPixels == 0;
        cases.Add(item);
    }

    private static void AddCompare(List<CaseResult> cases, string name, string contract, Color[] actual, Color[] expected)
    {
        Require(actual.Length == expected.Length, "Mismatched comparison dimensions: " + name);
        var item = Inspect(name, contract, actual);
        for (int i = 0; i < actual.Length; i++)
        {
            bool changed = false;
            for (int channel = 0; channel < 4; channel++)
            {
                float error = Mathf.Abs(actual[i][channel] - expected[i][channel]);
                float normalized = error / (1f + Mathf.Abs(expected[i][channel]));
                item.maxError = Mathf.Max(item.maxError, error);
                item.maxNormalizedError = Mathf.Max(item.maxNormalizedError, normalized);
                changed |= normalized > Tolerance;
            }
            if (changed) item.changedPixels++;
        }
        item.passed = item.finite && item.changedPixels == 0;
        cases.Add(item);
    }

    private static void VerifyBindings(MaskRBRFeature feature, CaseResult item, int quality)
    {
        object pass = typeof(MaskRBRFeature).GetField("_pass", PrivateInstance).GetValue(feature);
        var renderPass = pass as ScriptableRenderPass;
        Require(renderPass != null, "Feature pass is missing.");
        var material = (Material)pass.GetType().GetField("_material", PrivateInstance).GetValue(pass);
        Require(material != null, "Feature runtime material is missing.");
        Vector4 size = material.GetVector("_MaskRBROutputSize");
        item.boundOutputSize = new[] { size.x, size.y, size.z, size.w };
        item.passed &= (renderPass.input & ScriptableRenderPassInput.Depth) != 0
            && renderPass.renderPassEvent == RenderPassEvent.AfterRenderingSkybox
            && Mathf.Abs(size.x - Width) < 0.01f && Mathf.Abs(size.y - Height) < 0.01f
            && material.IsKeywordEnabled("_MASKRBR_QUALITY_LOW") == (quality == 0)
            && material.IsKeywordEnabled("_MASKRBR_QUALITY_HIGH") == (quality == 2);
    }

    private static int Pixel(float u, float v)
    {
        return Mathf.Clamp((int)(v * Height), 0, Height - 1) * Width + Mathf.Clamp((int)(u * Width), 0, Width - 1);
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

    private static void WritePNG(string path, Color[] linearHDR, bool mask = false)
    {
        Require(!File.Exists(path), "Refusing to overwrite an existing PNG: " + path);
        Texture2D texture = null;
        try
        {
            texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var display = new Color[linearHDR.Length];
            for (int i = 0; i < display.Length; i++)
            {
                Color value = linearHDR[i];
                if (mask) display[i] = new Color(value.r, value.g, value.b, 1f);
                else
                {
                    float r = Mathf.Max(value.r, 0f), g = Mathf.Max(value.g, 0f), b = Mathf.Max(value.b, 0f);
                    display[i] = new Color(Mathf.LinearToGammaSpace(r / (1f + r)), Mathf.LinearToGammaSpace(g / (1f + g)),
                        Mathf.LinearToGammaSpace(b / (1f + b)), 1f);
                }
            }
            texture.SetPixels(display);
            texture.Apply(false, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); }
    }

    private static string Serialize(Report report, List<CaseResult> cases)
    {
        string summary = JsonUtility.ToJson(report);
        var json = new StringBuilder(summary.Substring(0, summary.Length - 1));
        json.Append(",\"cases\":[");
        for (int i = 0; i < cases.Count; i++)
        {
            if (i > 0) json.Append(',');
            json.Append(JsonUtility.ToJson(cases[i]));
        }
        json.Append("]}");
        return json.ToString();
    }
}
