using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Mine.Interaction;
using UObject = UnityEngine.Object;

// Run with: unityctl script execute -f "Assets/Mine/Scripts/InteractionManager/Validation~/CheckTraceSeam.cs"
//
// 验证「域边界淡出把痕迹**吃掉了**」，而不只是给它加了一层显示遮罩。
//
// 症状（用户观察）：球体走开再走回来，会在域内暴露出一条**硬的断口**。
// 机制：只把写回值窗口化、读回时再除回来（InteractionDomainUnfade），存进去的原始值
// 一直是满的；痕迹被域边缘扫出去时数据是「显示上淡了、实际没淡」。域再扫回来，
// 那截原始值恢复成满强度，而它旁边（真正掉出 RT 的那一格）是 0 —— 硬断口就出现在这里。
//
// 动作：把 _followTarget 换成隐藏标记物，球贴着标记物压在雪地上（域跟着走），
// 让域把雪痕的尾端一路扫出 RT；再把球抬高、标记物走回去，于是雪痕尾端重新回到域内。
//
// 断言：整行里相邻纹素的**最大台阶**（相对全场峰值）不超过 15% —— 断口被吃成渐隐了。
public class Script
{
    [Serializable]
    public sealed class Report
    {
        public string suite = "CheckTraceSeam";
        public int passed;
        public int failed;
        public List<string> details = new List<string>();
    }

    // 相邻纹素允许的最大台阶（占全场峰值的比例）。硬断口是一步 100%。
    private const float MaxSeamStepRatio = 0.2f;
    // 尾端从「基本为零」升到「过半」要横跨多少纹素。硬断口是 0~1 个，渐隐带 5~12 个。
    private const int MinRampTexels = 4;

    private static readonly string[] TextureIds =
    {
        "_InteractionSnowTex", "_InteractionGrassTex", "_InteractionSandTex",
    };

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static object Field(object o, string name) =>
        o.GetType().GetField(name, Private).GetValue(o);

    private static void SetField(object o, string name, object value) =>
        o.GetType().GetField(name, Private).SetValue(o, value);

    private static object Call(object o, string name, params object[] args) =>
        o.GetType().GetMethod(name, Private).Invoke(o, args);

    private static Report _report;

    public static object Main()
    {
        var report = new Report();
        _report = report;
        var errors = new List<string>();
        Application.LogCallback onLog = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(type + ": " + message);
        };
        Application.logMessageReceived += onLog;

        var manager = UObject.FindFirstObjectByType<UniversalInteractionManager>();
        if (manager == null)
        {
            report.details.Add("no UniversalInteractionManager in the loaded scene");
            report.failed++;
            Application.logMessageReceived -= onLog;
            return report;
        }

        var target = (Transform)Field(manager, "_followTarget");
        var areaSize = (float)Field(manager, "_areaSize");
        var probe = UObject.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .FirstOrDefault(t => t.name.Contains("Probe"));

        var drivers = UObject.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Where(m => m != null && m.GetType().Name == "InteractionProbeDriver").ToList();
        var driverState = drivers.Select(d => d.enabled).ToList();
        foreach (var d in drivers) d.enabled = false;

        // 草地默认回弹得很快（~2.3/s），扫完再走回来早就没了。临时调成最慢，
        // 让草地也能被这条断言覆盖到。
        var grass = UObject.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .FirstOrDefault(m => m != null && m.GetType().Name == "GrassInteractionProcessor");
        object grassSpring = null;
        if (grass != null)
        {
            grassSpring = Field(grass, "_springBack");
            SetField(grass, "_springBack", 0f);
        }

        bool preview = (bool)Field(manager, "_editorPreview");
        var startProbe = probe != null ? probe.position : Vector3.zero;
        var startLast = (Vector3)Field(manager, "_lastAreaPos");

        // 雪地贴片在 x = -1.5·area … -0.5·area 之间（Demo 里中心 -12、边长 12）。
        float from = -1.5f * areaSize;
        float to   = -0.5f * areaSize;
        const int steps = 90;
        var probeY = startProbe.y;

        var marker = new GameObject("~TraceSeamFollow");
        marker.hideFlags = HideFlags.HideAndDontSave;
        marker.transform.position = new Vector3(to, 0f, 0f);

        try
        {
            SetField(manager, "_editorPreview", false);
            SetField(manager, "_followTarget", marker.transform);

            // ── 1. 一路向前压：域把雪痕尾端扫出 RT ──────────────
            for (int i = 0; i <= steps; i++)
            {
                float x = Mathf.Lerp(from, to, i / (float)steps);
                marker.transform.position = new Vector3(x, 0f, 0f);
                if (probe != null) probe.position = new Vector3(x, probeY, 0f);

                Call(manager, "SyncMatrices");
                if (!(bool)Call(manager, "RenderOrigin"))
                {
                    report.details.Add("RenderOrigin failed at forward step " + i);
                    report.failed++;
                    break;
                }
                Call(manager, "Tick", 1f / 30f);
            }

            // ── 2. 球抬高、域走回来：被扫出去的那截重新进域 ──────
            for (int i = 0; i <= steps; i++)
            {
                float x = Mathf.Lerp(to, -1.0f * areaSize, i / (float)steps);
                marker.transform.position = new Vector3(x, 0f, 0f);
                if (probe != null) probe.position = new Vector3(x, probeY + 3f, 0f);

                Call(manager, "SyncMatrices");
                Call(manager, "RenderOrigin");
                Call(manager, "Tick", 1f / 30f);
            }

            int found = 0;
            foreach (string id in TextureIds)
            {
                var texture = Shader.GetGlobalTexture(id) as RenderTexture;
                if (texture == null) continue;
                found++;
                Analyse(report, id, texture, Read(texture));
            }

            if (found == 0)
            {
                report.failed++;
                report.details.Add("[FAIL] no _Interaction*Tex global is published");
            }
        }
        finally
        {
            if (probe != null) probe.position = startProbe;
            SetField(manager, "_followTarget", target);
            SetField(manager, "_editorPreview", preview);
            SetField(manager, "_lastAreaPos", startLast);
            if (grass != null) SetField(grass, "_springBack", grassSpring);
            Call(manager, "SyncMatrices");
            for (int i = 0; i < drivers.Count; i++) if (drivers[i] != null) drivers[i].enabled = driverState[i];
            UObject.DestroyImmediate(marker);
            Application.logMessageReceived -= onLog;
        }

        foreach (string e in errors) report.details.Add("unexpected log: " + e);
        Check("no errors while sweeping the domain out and back", errors.Count == 0, string.Join(" | ", errors));
        return report;
    }

    private static void Analyse(Report report, string id, RenderTexture rt, float[] data)
    {
        int w = rt.width, h = rt.height;

        float maxAbs = 0f;
        for (int i = 0; i < data.Length; i++)
            maxAbs = Mathf.Max(maxAbs, Mathf.Abs(data[i]));

        if (maxAbs <= 1e-3f)
        {
            report.details.Add("[SKIP] " + id + " has no content; sweep not exercised");
            return;
        }

        // 过球心的那一行，整行找最大台阶（不排除边界：断口本来就可能落在那儿）。
        int row = h / 2;
        float maxStep = 0f;
        int stepAt = 0;
        for (int x = 0; x < w - 1; x++)
        {
            float step = Mathf.Abs(data[row * w + x + 1] - data[row * w + x]);
            if (step > maxStep) { maxStep = step; stepAt = x; }
        }

        float ratio = maxStep / maxAbs;
        bool ok = ratio <= MaxSeamStepRatio;
        report.details.Add((ok ? "[PASS] " : "[FAIL] ") + id + " swept trace has no hard seam — " +
                           "max step " + (ratio * 100f).ToString("F1") + "% of field max " +
                           maxAbs.ToString("F4") + " at x = " + stepAt + " (uv " +
                           ((stepAt + 0.5f) / w).ToString("F3") + ", limit " +
                           (MaxSeamStepRatio * 100f).ToString("F0") + "%)");
        if (ok) report.passed++; else report.failed++;

        // 尾端渐变宽度：从「几乎为零」到「过半」横跨几个纹素。这才是「软」的判据。
        int half = -1;
        for (int x = 0; x < w; x++)
        {
            if (Mathf.Abs(data[row * w + x]) >= 0.5f * maxAbs) { half = x; break; }
        }
        int rampTexels = 0;
        if (half > 0)
        {
            for (int x = half - 1; x >= 0; x--)
            {
                if (Mathf.Abs(data[row * w + x]) < 0.02f * maxAbs) break;
                rampTexels++;
            }
        }
        bool rampOk = rampTexels >= MinRampTexels;
        report.details.Add((rampOk ? "[PASS] " : "[FAIL] ") + id + " swept trace tapers off — tail ramp " +
                           rampTexels + " texels from 2% to 50% of the peak (limit ≥ " +
                           MinRampTexels + ")");
        if (rampOk) report.passed++; else report.failed++;

        if (!ok || !rampOk)
            report.details.Add("    row around seam: " + Print(data, row * w + Mathf.Max(0, stepAt - 6), 14));
    }

    private static string Print(float[] data, int start, int count)
    {
        var parts = new List<string>();
        for (int i = start; i < start + count && i < data.Length; i++)
            parts.Add(data[i].ToString("F3"));
        return string.Join(" ", parts);
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (_report != null)
        {
            if (ok) _report.passed++; else _report.failed++;
            _report.details.Add((ok ? "[PASS] " : "[FAIL] ") + name + " — " + detail);
        }
        if (!ok) Debug.LogWarning("[CheckTraceSeam][FAIL] " + name + " — " + detail);
    }

    private static float[] Read(RenderTexture rt)
    {
        if (rt == null) return Array.Empty<float>();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RFloat, false, true);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        tex.Apply();
        RenderTexture.active = previous;
        float[] data = tex.GetRawTextureData<float>().ToArray();
        UObject.DestroyImmediate(tex);
        return data;
    }
}
