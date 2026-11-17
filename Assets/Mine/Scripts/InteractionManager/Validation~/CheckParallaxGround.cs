using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Mine.Interaction;
using UObject = UnityEngine.Object;

// Run with: unityctl script execute -f "Assets/Mine/Scripts/InteractionManager/Validation~/CheckParallaxGround.cs"
//
// 验证视差映射（POM）在雪 / 沙地面上确实起作用，且关掉时回到纯法线效果：
//
//   A. `_ParallaxSteps = 0`（关）与 `= 16`（开）在同一台掠射相机下必须不同
//      —— 不同 = 采样点真的被推到了坑底可见处。
//   B. 同一份高度场、同一台相机，步数 8 与 16 的差应当很小（收敛），
//      差很大说明步长不够、结果没收敛。
//   C. 落盘 0 / 8 / 16 三张 PNG，供人工对比。
//
// 相机压到约 20° 掠射：法线互动+视差在这个角度差别最明显，正俯视几乎看不出来。
public class Script
{
    [Serializable]
    public sealed class Report
    {
        public string suite = "CheckParallaxGround";
        public int passed;
        public int failed;
        public List<string> details = new List<string>();
    }

    private const float Dt = 1f / 30f;
    private const int HoldSteps = 60;
    // 0 vs 16：整帧均值会被大片平地稀释（坑只占画面 1–2%），所以看**局部**证据：
    // 峰值差 + 明显变化的像素占比。整帧均值只用来判收敛，不用来判「有没有效果」。
    private const float MinPeakOn   = 0.10f;    // 最强的那个像素必须差这么多
    private const float MinChangedOn = 0.005f;  // ≥0.5% 的像素要有可见变化（|Δ| > 0.02）
    private const float VisibleDelta = 0.02f;
    private const float MaxDiffConv = 0.0040f;  // 8 vs 16：必须已收敛（整帧均值）

    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static object Field(object o, string name) => o.GetType().GetField(name, Private).GetValue(o);
    private static void SetField(object o, string name, object v) => o.GetType().GetField(name, Private).SetValue(o, v);
    private static object Call(object o, string name, params object[] args) =>
        o.GetType().GetMethod(name, Private).Invoke(o, args);

    private static Report _report;

    public static object Main()
    {
        var report = new Report();
        _report = report;

        var manager = UObject.FindFirstObjectByType<UniversalInteractionManager>();
        var sandTex = Shader.GetGlobalTexture("_InteractionSandTex") as RenderTexture;
        var snowTex = Shader.GetGlobalTexture("_InteractionSnowTex") as RenderTexture;
        if (manager == null || sandTex == null || snowTex == null)
        {
            Check("demo scene is loaded", false,
                  "manager=" + (manager != null) + ", sand=" + (sandTex != null) + ", snow=" + (snowTex != null));
            return report;
        }

        var probe = UObject.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .FirstOrDefault(t => t.name.Contains("Probe"));
        var drivers = UObject.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Where(m => m != null && m.GetType().Name == "InteractionProbeDriver").ToList();

        var patches = UObject.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .Where(r => r.sharedMaterial != null && r.sharedMaterial.shader != null &&
                        (r.sharedMaterial.shader.name == "InteractionManager/SandGround" ||
                         r.sharedMaterial.shader.name == "InteractionManager/SnowGround"))
            .ToList();
        if (patches.Count == 0)
        {
            Check("ground patches found", false, "no SandGround / SnowGround renderer");
            return report;
        }

        var sandPatch = patches.FirstOrDefault(r => r.sharedMaterial.shader.name == "InteractionManager/SandGround");
        if (sandPatch == null) sandPatch = patches[0];

        var target = (Transform)Field(manager, "_followTarget");
        bool preview = (bool)Field(manager, "_editorPreview");
        var startLast = (Vector3)Field(manager, "_lastAreaPos");
        var startProbe = probe != null ? probe.position : Vector3.zero;
        var driverState = drivers.Select(d => d.enabled).ToList();
        var hidden = new List<Renderer>();
        Camera camera = null;
        RenderTexture rt = null;
        Material instance = null;
        Material originalShared = null;

        try
        {
            foreach (var d in drivers) d.enabled = false;

            var b = sandPatch.bounds;
            var focus = new Vector3(b.center.x, 0f, b.center.z);

            var parking = new GameObject("~ParallaxCheckFollow");
            parking.hideFlags = HideFlags.HideAndDontSave;
            parking.transform.position = focus;

            SetField(manager, "_editorPreview", false);
            SetField(manager, "_followTarget", parking.transform);

            if (probe != null) hidden.AddRange(probe.GetComponentsInChildren<Renderer>());

            var camGO = new GameObject("~ParallaxCheckCam") { hideFlags = HideFlags.HideAndDontSave };
            camera = camGO.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2.2f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            // 约 20° 掠射：视差在这一档最明显。
            camera.transform.position = focus + new Vector3(0f, 1.2f, -3.2f);
            camera.transform.LookAt(focus, Vector3.up);

            rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = "~ParallaxCheckRT", hideFlags = HideFlags.HideAndDontSave };
            camera.targetTexture = rt;

            // 用材质实例，别改到 .mat 资产。**必须先记下原材质**：
            // renderer.material 会把实例塞进渲染器的材质槽，销毁实例后那个槽就变成
            // 已销毁对象，后续所有检查都会报「找不到 SandGround 渲染器」。
            originalShared = sandPatch.sharedMaterial;
            instance = sandPatch.material;

            var p = new Vector3(focus.x, probe != null ? startProbe.y : 1f, focus.z);
            Step(manager, probe, p, HoldSteps);

            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "tmp", "sessions", "parallax-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(outDir);

            float[] off  = RenderAt(camera, rt, instance, 0,  hidden, outDir, "steps-0.png");
            float[] s8   = RenderAt(camera, rt, instance, 8,  hidden, outDir, "steps-8.png");
            float[] s16  = RenderAt(camera, rt, instance, 16, hidden, outDir, "steps-16.png");

            double dOn   = MeanDiff(off, s16);
            double dConv = MeanDiff(s8, s16);
            float  peakOn   = MaxDiff(off, s16);
            float  changedOn = ChangedFraction(off, s16, VisibleDelta);

            Check("parallax changes the picture (steps 0 → 16)",
                  peakOn >= MinPeakOn && changedOn >= MinChangedOn,
                  "max |Δ| = " + peakOn.ToString("F3") + " (need ≥ " + MinPeakOn.ToString("F2") +
                  "), changed " + (changedOn * 100f).ToString("F2") + "% of pixels (need ≥ " +
                  (MinChangedOn * 100f).ToString("F2") + "%), frame mean |Δ| = " + dOn.ToString("F4"));

            Check("parallax has converged by 8 steps", dConv <= MaxDiffConv,
                  "mean |Δ| 8 vs 16 = " + dConv.ToString("F4") + " (need ≤ " + MaxDiffConv.ToString("F4") + ")");

            report.details.Add("screenshots: " + outDir);
            report.details.Add("sand field after press — min " + ReadRT(sandTex).Min().ToString("F3") +
                               ", max " + ReadRT(sandTex).Max().ToString("F3"));

            UObject.DestroyImmediate(parking);
        }
        catch (Exception e)
        {
            Check("render pass", false, e.Message);
        }
        finally
        {
            foreach (var r in hidden) if (r != null) r.enabled = true;
            if (camera != null) { camera.targetTexture = null; UObject.DestroyImmediate(camera.gameObject); }
            if (rt != null) { rt.Release(); UObject.DestroyImmediate(rt); }
            if (sandPatch != null && originalShared != null) sandPatch.sharedMaterial = originalShared;
            if (instance != null) UObject.DestroyImmediate(instance);
            if (probe != null) probe.position = startProbe;
            SetField(manager, "_followTarget", target);
            SetField(manager, "_editorPreview", preview);
            SetField(manager, "_lastAreaPos", startLast);
            Call(manager, "SyncMatrices");
            for (int i = 0; i < drivers.Count; i++) if (drivers[i] != null) drivers[i].enabled = driverState[i];
        }

        return report;
    }

    private static float[] RenderAt(Camera camera, RenderTexture rt, Material mat, int steps,
                                    List<Renderer> probeRenderers, string dir, string file)
    {
        mat.SetFloat("_ParallaxSteps", steps);
        foreach (var r in probeRenderers) if (r != null) r.enabled = false;
        float[] pixels = Render(camera, rt);
        foreach (var r in probeRenderers) if (r != null) r.enabled = true;

        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        tex.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
        UObject.DestroyImmediate(tex);
        return pixels;
    }

    private static void Step(UniversalInteractionManager manager, Transform probe, Vector3 position, int steps)
    {
        if (probe != null) probe.position = position;
        for (int i = 0; i < steps; i++)
        {
            Call(manager, "SyncMatrices");
            Call(manager, "RenderOrigin");
            Call(manager, "Tick", Dt);
        }
    }

    private static double MeanDiff(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += Mathf.Abs(a[i] - b[i]);
        return sum / a.Length;
    }

    private static float MaxDiff(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        float max = 0;
        for (int i = 0; i < a.Length; i++) max = Mathf.Max(max, Mathf.Abs(a[i] - b[i]));
        return max;
    }

    private static float ChangedFraction(float[] a, float[] b, float threshold)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        int n = 0;
        for (int i = 0; i < a.Length; i++) if (Mathf.Abs(a[i] - b[i]) > threshold) n++;
        return (float)n / a.Length;
    }

    private static float[] Render(Camera camera, RenderTexture rt)
    {
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        tex.Apply();
        RenderTexture.active = previous;
        var pixels = tex.GetPixels();
        UObject.DestroyImmediate(tex);

        var luma = new float[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            luma[i] = pixels[i].r * 0.299f + pixels[i].g * 0.587f + pixels[i].b * 0.114f;
        return luma;
    }

    private static float[] ReadRT(RenderTexture rt)
    {
        if (rt == null) return Array.Empty<float>();
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RFloat, false, true);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        tex.Apply();
        RenderTexture.active = previous;
        var data = tex.GetRawTextureData<float>().ToArray();
        UObject.DestroyImmediate(tex);
        return data;
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (_report != null)
        {
            if (ok) _report.passed++; else _report.failed++;
            _report.details.Add((ok ? "[PASS] " : "[FAIL] ") + name + " — " + detail);
        }
        if (!ok) Debug.LogWarning("[CheckParallaxGround][FAIL] " + name + " — " + detail);
    }
}
