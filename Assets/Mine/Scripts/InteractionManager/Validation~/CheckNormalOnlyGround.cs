using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Mine.Interaction;
using UObject = UnityEngine.Object;

// Run with: unityctl script execute -f "Assets/Mine/Scripts/InteractionManager/Validation~/CheckNormalOnlyGround.cs"
//
// 验证「法线互动，不做网格变化」：
//
//   A. 两个地面 shader 编译无 error。
//   B. Forward 的 Vert 体里没有任何对 positionWS 的写入/采样
//      （静态判据：位移一旦回来了，这一条先炸，不用等看出画面不对）。
//   C. 交互仍然进到画面上：同一个相机，空场 vs 踩一脚两张图必须不同
//      （不同 = 法线/明暗确实由 _Interaction*Tex 驱动）。
//   D. 顺手落两张 PNG 到 tmp/sessions/，作为人工复核的证据。
//
// 依赖 Demo 场景（InteractionSurfacesDemo）已经打开。
public class Script
{
    [Serializable]
    public sealed class Report
    {
        public string suite = "CheckNormalOnlyGround";
        public int passed;
        public int failed;
        public List<string> details = new List<string>();
    }

    private const float Dt = 1f / 30f;
    private const int HoldSteps = 60;
    private const float MinImageDiff = 0.002f;   // 两次渲染的平均绝对差（0..1）

    private static readonly string[] GroundShaders =
    {
        "Assets/Mine/Scripts/InteractionManager/Snow/SnowGround.shader",
        "Assets/Mine/Scripts/InteractionManager/Sand/SandGround.shader",
    };

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

        // ── A. 编译 ─────────────────────────────────────────────
        foreach (string path in GroundShaders)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                Check(Path.GetFileName(path) + " loads", false, "missing asset");
                continue;
            }
            var messages = ShaderUtil.GetShaderMessages(shader);
            var errors = messages
                .Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                .ToList();
            Check(Path.GetFileName(path) + " compiles without error", errors.Count == 0,
                  errors.Count == 0 ? messages.Length + " message(s), 0 error"
                                    : string.Join(" | ", errors.Select(e => e.message)));
        }

        // ── B. Vert 不动顶点（静态判据） ─────────────────────────
        foreach (string path in GroundShaders)
        {
            string source;
            try { source = File.ReadAllText(path); }
            catch (Exception e) { Check("read " + Path.GetFileName(path), false, e.Message); continue; }

            string body = ExtractMethodBody(source, "Varyings Vert(Attributes v)");
            if (body == null)
            {
                Check(Path.GetFileName(path) + " has a forward Vert", false, "Varyings Vert(Attributes v) not found");
                continue;
            }

            // (?<![.\w]) 排除 o.positionWS = ... 这类纯输出赋值，只抓顶点自身的写入。
            var writes = Regex.Matches(body, @"(?<![.\w])positionWS\s*(\.[xyz]\s*)?(\+=|-=|\*=|/=|=(?!=))");
            // 允许唯一一处声明：float3 positionWS = TransformObjectToWorld(...)
            var realWrites = writes.Cast<Match>()
                .Where(m => !Regex.IsMatch(body.Substring(Mathf.Max(0, m.Index - 40), 40), @"float3\s+$"))
                .ToList();
            bool samplesField = body.Contains("SampleSnowImprintWS") || body.Contains("SampleSandHeightWS");

            Check(Path.GetFileName(path) + " forward Vert does not move the mesh",
                  realWrites.Count == 0 && !samplesField,
                  realWrites.Count == 0 && !samplesField
                      ? "no positionWS write, no height-field sample in Vert"
                      : realWrites.Count + " write(s) to positionWS, samples field: " + samplesField);
        }

        // ── C/D. 画面：同一台相机，空场 vs 踩一脚 ────────────────
        var manager = UObject.FindFirstObjectByType<UniversalInteractionManager>();
        var sandTex = Shader.GetGlobalTexture("_InteractionSandTex") as RenderTexture;
        if (manager == null || sandTex == null)
        {
            Check("demo scene is loaded", false,
                  "manager=" + (manager != null) + ", _InteractionSandTex=" + (sandTex != null));
            return report;
        }

        var probe = UObject.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .FirstOrDefault(t => t.name.Contains("Probe"));
        var drivers = UObject.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Where(m => m != null && m.GetType().Name == "InteractionProbeDriver").ToList();

        var patch = UObject.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.shader != null &&
                                 r.sharedMaterial.shader.name == "InteractionManager/SandGround");
        if (patch == null)
        {
            Check("sand patch found", false, "no renderer using InteractionManager/SandGround");
            return report;
        }

        var target = (Transform)Field(manager, "_followTarget");
        bool preview = (bool)Field(manager, "_editorPreview");
        var startLast = (Vector3)Field(manager, "_lastAreaPos");
        var startProbe = probe != null ? probe.position : Vector3.zero;
        var driverState = drivers.Select(d => d.enabled).ToList();
        var hidden = new List<Renderer>();
        Camera camera = null;
        RenderTexture rt = null;
        string outDir = null;

        try
        {
            foreach (var d in drivers) d.enabled = false;

            // 探针只在自己拍图时藏起来；Step 期间必须可见，否则捕获相机拍不到它，
            // 穿透恒为 0（这条踩过：origin tex 全程 0.000，以为是域没搬过去）。
            var probeRoot = probe != null ? probe : null;
            if (probeRoot != null) hidden.AddRange(probeRoot.GetComponentsInChildren<Renderer>());

            // 压痕必须落在**沙地那块**上：域跟着 _followTarget 走，探针也压在同一处。
            var b = patch.bounds;
            var focus = new Vector3(b.center.x, 0f, b.center.z);

            var parking = new GameObject("~NormalOnlyCheckFollow");
            parking.hideFlags = HideFlags.HideAndDontSave;
            parking.transform.position = focus;

            SetField(manager, "_editorPreview", false);
            SetField(manager, "_followTarget", parking.transform);

            // 让相机正对沙地，正交、俯视。
            var camGO = new GameObject("~NormalOnlyCheckCam") { hideFlags = HideFlags.HideAndDontSave };
            camera = camGO.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2f;   // 压痕半径约 1 m，视野再大就会被背景稀释掉（1.2% 像素）
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            // 斜俯视：法线互动的效果全在明暗上，正俯视配顶光几乎看不出来（实测 mean|Δ| 只有 0.0012）。
            camera.transform.position = focus + new Vector3(0f, 3.5f, -2.5f);
            camera.transform.LookAt(focus, Vector3.up);

            rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = "~NormalOnlyCheckRT", hideFlags = HideFlags.HideAndDontSave };
            camera.targetTexture = rt;

            outDir = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                "tmp", "sessions", "normal-only-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(outDir);

            float[] flat = RenderWithoutProbe(camera, rt, hidden);
            SaveImage(rt, Path.Combine(outDir, "flat.png"));

            var p = new Vector3(focus.x, probe != null ? startProbe.y : 1f, focus.z);
            Step(manager, probe, p, HoldSteps);
            float[] pressed = RenderWithoutProbe(camera, rt, hidden);
            SaveImage(rt, Path.Combine(outDir, "pressed.png"));

            var field = ReadRT(sandTex);
            if (field.Length > 0)
                report.details.Add("sand field after press — min " + field.Min().ToString("F3") +
                                   ", max " + field.Max().ToString("F3"));

            var origin = Shader.GetGlobalTexture("_InteractionOriginTex") as RenderTexture;
            var originData = ReadRT(origin);
            report.details.Add("focus " + focus + ", probe " + p + ", patch bounds " + b.size +
                               ", origin tex " + (origin != null ? origin.width + "x" + origin.height : "null") +
                               (originData.Length > 0 ? " max " + originData.Max().ToString("F3") : ""));

            double diff = 0;
            int changed = 0;
            for (int i = 0; i < flat.Length; i++)
            {
                float d = Mathf.Abs(pressed[i] - flat[i]);
                diff += d;
                if (d > 0.01f) changed++;
            }
            diff /= Mathf.Max(1, flat.Length);

            Check("the footprint still reaches the picture",
                  diff >= MinImageDiff,
                  "mean |Δ| between flat and pressed = " + diff.ToString("F4") +
                  " (need ≥ " + MinImageDiff.ToString("F4") + "), changed px " +
                  (100f * changed / Mathf.Max(1, flat.Length)).ToString("F1") + "%, luma " +
                  flat.Average(v => (double)v).ToString("F3") + " → " +
                  pressed.Average(v => (double)v).ToString("F3") +
                  " (全黑说明相机没渲到东西)");

            report.details.Add("screenshots: " + outDir);

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
            if (probe != null) probe.position = startProbe;
            SetField(manager, "_followTarget", target);
            SetField(manager, "_editorPreview", preview);
            SetField(manager, "_lastAreaPos", startLast);
            Call(manager, "SyncMatrices");
            for (int i = 0; i < drivers.Count; i++) if (drivers[i] != null) drivers[i].enabled = driverState[i];
        }

        return report;
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

    // ── 提取方法体（按花括号配对） ──────────────────────────────
    private static string ExtractMethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return null;
        int brace = source.IndexOf('{', start);
        if (brace < 0) return null;
        int depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source.Substring(brace + 1, i - brace - 1);
            }
        }
        return null;
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

    private static float[] RenderWithoutProbe(Camera camera, RenderTexture rt, List<Renderer> probeRenderers)
    {
        foreach (var r in probeRenderers) if (r != null) r.enabled = false;
        float[] pixels = Render(camera, rt);
        foreach (var r in probeRenderers) if (r != null) r.enabled = true;
        return pixels;
    }

    private static void SaveImage(RenderTexture rt, string path)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        tex.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UObject.DestroyImmediate(tex);
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (_report != null)
        {
            if (ok) _report.passed++; else _report.failed++;
            _report.details.Add((ok ? "[PASS] " : "[FAIL] ") + name + " — " + detail);
        }
        if (!ok) Debug.LogWarning("[CheckNormalOnlyGround][FAIL] " + name + " — " + detail);
    }
}
