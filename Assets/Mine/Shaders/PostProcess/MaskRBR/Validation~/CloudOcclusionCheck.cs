using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

/// <summary>
/// Verifies the SSC → MaskRBR cloud occlusion handoff end to end.
/// Captures MaskRBR's debug mask under three states and checks the two attenuation
/// stages independently: SSC composite (1 - alpha) and the mask-stage multiplier.
/// No asset or scene is saved; settings and feature activation are restored.
/// </summary>
public class Script
{
    const int Width = 961;
    const int Height = 541;
    static int assertions;

    static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    public static object Main(string[] args)
    {
        if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            throw new ArgumentException("Pass the output directory for this cloud occlusion run.");
        string output = args[0];
        Directory.CreateDirectory(output);
        assertions = 0;
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        var ssc = rd.rendererFeatures.OfType<SSCFeature>().Single();
        var rbr = rd.rendererFeatures.OfType<MaskRBRFeature>().Single();
        string sscOriginal = JsonUtility.ToJson(ssc.settings);
        string rbrOriginal = JsonUtility.ToJson(rbr.settings);
        bool sscActive = ssc.isActive, rbrActive = rbr.isActive;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        bool dirty = scene.isDirty;

        GameObject owner = null;
        RenderTexture rt = null;
        Texture2D readback = null;
        RenderTexture previous = RenderTexture.active;
        string failure = null;
        var report = new StringBuilder();

        try
        {
            owner = new GameObject("SSC cloud occlusion check") { hideFlags = HideFlags.HideAndDontSave };
            var camera = owner.AddComponent<Camera>();
            camera.enabled = false;
            camera.cameraType = CameraType.Game;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.aspect = (float)Width / Height;
            camera.orthographic = false;
            camera.fieldOfView = 60;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 2000f;
            camera.allowHDR = true;
            camera.allowDynamicResolution = false;
            camera.allowMSAA = false;
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.2f, 0.3f, 0.4f, 1f);
            camera.transform.position = new Vector3(0f, 25f, -90f);
            camera.transform.LookAt(new Vector3(0f, 50f, 0f));

            var data = camera.GetUniversalAdditionalCameraData();
            data.SetRenderer(0);
            data.requiresDepthTexture = true;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;

            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            readback = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
            Check(rt.Create(), "render target created");

            // RBR: sky source, debug mask, cloud occlusion on. Debug mask ignores scene RGB,
            // so the only difference between captures is the mask value itself.
            rbr.settings.source = MaskRBRFeature.SourceMode.Sky;
            rbr.settings.debug = MaskRBRFeature.DebugMode.Mask;
            rbr.settings.intensity = 1f;
            rbr.settings.length = 0.2f;
            rbr.settings.occludeWithClouds = true;
            rbr.settings.passEvent = RenderPassEvent.AfterRenderingTransparents;
            rbr.SetActive(true);

            // SSC: stationary fields, black clouds so the composite contributes (1 - alpha) only.
            ssc.settings.speed = 0f;
            ssc.settings.jitter = 0f;
            ssc.settings.showSSC = false;
            ssc.settings.baseColorA = Color.black;
            ssc.settings.baseColorB = Color.black;
            ssc.settings.cloudBufferEvent = RenderPassEvent.AfterRenderingOpaques;
            ssc.settings.compositeEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            ssc.Create();

            ssc.SetActive(false);
            CheckCapture(camera, rt, readback, Path.Combine(output, "a-mask-noclouds.png"));
            float[] background = ReadPixels(rt, readback);
            Check(!SSCFeature.CloudOcclusionReady, "cloud handoff off when SSC inactive");

            ssc.SetActive(true);
            CheckCapture(camera, rt, readback, Path.Combine(output, "b-mask-clouds-occlude.png"));
            float[] occluded = ReadPixels(rt, readback);
            Check(SSCFeature.CloudOcclusionReady, "cloud handoff flagged after SSC ray march");

            rbr.settings.occludeWithClouds = false;
            CheckCapture(camera, rt, readback, Path.Combine(output, "c-mask-clouds-no-occlude.png"));
            float[] compositeOnly = ReadPixels(rt, readback);

            int solid = 0, dimmedByComposite = 0, dimmedByMask = 0, inverted = 0;
            float maxCompositeDrop = 0f, maxMaskDrop = 0f;
            for (int i = 0; i < background.Length; i += 4)
            {
                float a = background[i], b = compositeOnly[i], c = occluded[i];
                if (a > 0.002f) solid++;
                if (c > b + 0.002f || b > a + 0.002f) inverted++;
                if (b < a - 0.002f) { dimmedByComposite++; maxCompositeDrop = Mathf.Max(maxCompositeDrop, a - b); }
                if (c < b - 0.002f) { dimmedByMask++; maxMaskDrop = Mathf.Max(maxMaskDrop, b - c); }
            }

            report.Append("pixels=").Append(background.Length / 4).Append('\n');
            report.Append("maskPixels=").Append(solid).Append('\n');
            report.Append("dimmedBySSCComposite=").Append(dimmedByComposite).Append('\n');
            report.Append("dimmedByMaskStage=").Append(dimmedByMask).Append('\n');
            report.Append("invertedPixels=").Append(inverted).Append('\n');
            report.Append("maxCompositeDrop=").Append(maxCompositeDrop).Append('\n');
            report.Append("maxMaskDrop=").Append(maxMaskDrop).Append('\n');

            Check(solid > 1000, "sky mask is present");
            Check(dimmedByComposite > 1000, "SSC composite attenuates the mask");
            Check(dimmedByMask > 1000, "mask stage attenuates by cloud coverage");
            Check(inverted == 0, "attenuation is monotonic (no brightening)");
            Check(maxCompositeDrop > 0.01f && maxMaskDrop > 0.01f, "attenuation is measurable");

            // Order guard: both directions of the partial order must be reported.
            rbr.settings.occludeWithClouds = true;
            ssc.settings.cloudBufferEvent = RenderPassEvent.AfterRenderingTransparents + 1;
            string lateWarn = CaptureWarnings(() => CheckCapture(camera, rt, readback, Path.Combine(output, "d-late-buffer.png")));
            Check(lateWarn.Contains("Cloud occlusion is off"), "late cloud buffer warns: " + lateWarn);

            ssc.settings.cloudBufferEvent = RenderPassEvent.AfterRenderingOpaques;
            ssc.settings.compositeEvent = RenderPassEvent.AfterRenderingOpaques;
            string earlyWarn = CaptureWarnings(() => CheckCapture(camera, rt, readback, Path.Combine(output, "e-early-composite.png")));
            Check(earlyWarn.Contains("composites at"), "early composite warns: " + earlyWarn);

            string reportPath = Path.Combine(output, "cloud-occlusion.txt");
            File.WriteAllText(reportPath, report.ToString());
            return Path.GetFullPath(reportPath);
        }
        catch (Exception error)
        {
            failure = error.ToString();
            throw;
        }
        finally
        {
            JsonUtility.FromJsonOverwrite(sscOriginal, ssc.settings);
            JsonUtility.FromJsonOverwrite(rbrOriginal, rbr.settings);
            ssc.SetActive(sscActive);
            rbr.SetActive(rbrActive);
            ssc.Create();
            rbr.Create();
            RenderTexture.active = previous;
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            bool restored = sscOriginal == JsonUtility.ToJson(ssc.settings)
                && rbrOriginal == JsonUtility.ToJson(rbr.settings)
                && ssc.isActive == sscActive && rbr.isActive == rbrActive
                && scene.isDirty == dirty;
            File.WriteAllText(Path.Combine(output, "cloud-occlusion-report.json"), JsonUtility.ToJson(new Report
            {
                assertions = assertions,
                passed = failure == null && restored,
                restored = restored,
                failure = failure,
                sscActive = sscActive,
                rbrActive = rbrActive
            }, true));
        }
    }

    static string CaptureWarnings(Action render)
    {
        var logs = new System.Collections.Generic.List<string>();
        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Warning) logs.Add(message);
        }
        Application.logMessageReceived += OnLog;
        try { render(); }
        finally { Application.logMessageReceived -= OnLog; }
        return string.Join("\n", logs);
    }

    static void CheckCapture(Camera camera, RenderTexture rt, Texture2D readback, string outputPath)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        RenderTexture.active = rt;
        readback.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        readback.Apply(false, false);
        File.WriteAllBytes(outputPath, readback.EncodeToPNG());
    }

    static float[] ReadPixels(RenderTexture rt, Texture2D readback)
    {
        RenderTexture.active = rt;
        readback.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        readback.Apply(false, false);
        return readback.GetRawTextureData<float>().ToArray();
    }

    [Serializable]
    public class Report
    {
        public int assertions;
        public bool passed, restored, sscActive, rbrActive;
        public string failure;
    }
}
