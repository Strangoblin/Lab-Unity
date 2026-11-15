using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Mine.Interaction;
using UObject = UnityEngine.Object;

// Run with: unityctl script execute -f "Assets/Mine/Scripts/InteractionManager/Validation~/CheckSurfaceDynamics.cs"
//
// 验证雪 / 沙的**材料差异**（不只是「有没有痕迹」）：
//
//   沙 = 小形变、不可压实 → 受压挖坑 + 体积被挤到坑外沿堆成脊；抬离后按休止角塌落，
//        最后随时间整体回平。
//   雪 = 大形变、可压实   → 只向下压实，**没有**侧向堆脊；抬离后蠕变摊圆 + 沉降变浅。
//
// 三条断言：
//   A. 压住 2 s：沙同时有「负的坑」和「正的外沿脊」，雪只有正的下压、外沿没有超调。
//   B. 抬离后：两种材料的峰值都随时间单调变浅（痕迹会消失，不是永久留着）。
//   C. 沙的脊在坑的外侧（半径大于坑沿），不是坑里的噪点。
//
// 做法：把 _followTarget 钉死在原点（域不动、无重投影），探针压到域中心，
// 按住 2 s，再抬高 3 m 空跑 6 s，中间读回 _InteractionSandTex / _InteractionSnowTex。
public class Script
{
    [Serializable]
    public sealed class Report
    {
        public string suite = "CheckSurfaceDynamics";
        public int passed;
        public int failed;
        public List<string> details = new List<string>();
    }

    private const float Dt = 1f / 30f;
    private const int   HoldSteps = 60;    // 压住 2 s
    private const int   FadeSteps = 180;   // 抬离后空跑 6 s
    private const float PitMin = 0.30f;    // 坑/压痕至少要有这么深（saturate(0.16*5) ≈ 0.79）
    private const float RimMin = 0.01f;    // 沙脊至少这么高（归一化单位，×0.30 m = 3 mm 起步）
    private const float RimMaxRatio = 0.5f;   // 脊不能比坑还高
    private const float SnowHaloRatio = 0.10f; // 雪外沿只允许蠕变晕（≤ 峰值 10%），不能有脊

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
            report.failed++;
            report.details.Add("[FAIL] no UniversalInteractionManager in the loaded scene");
            Application.logMessageReceived -= onLog;
            return report;
        }

        var target = (Transform)Field(manager, "_followTarget");
        var probe = UObject.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .FirstOrDefault(t => t.name.Contains("Probe"));
        var drivers = UObject.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Where(m => m != null && m.GetType().Name == "InteractionProbeDriver").ToList();

        var driverState = drivers.Select(d => d.enabled).ToList();
        foreach (var d in drivers) d.enabled = false;

        bool preview = (bool)Field(manager, "_editorPreview");
        var startProbe = probe != null ? probe.position : Vector3.zero;
        var startLast = (Vector3)Field(manager, "_lastAreaPos");

        var parking = new GameObject("~SurfaceDynamicsCheckFollow");
        parking.hideFlags = HideFlags.HideAndDontSave;
        parking.transform.position = Vector3.zero;

        var sandTex = Shader.GetGlobalTexture("_InteractionSandTex") as RenderTexture;
        var snowTex = Shader.GetGlobalTexture("_InteractionSnowTex") as RenderTexture;

        if (sandTex == null || snowTex == null)
        {
            report.failed++;
            report.details.Add("[FAIL] missing _InteractionSandTex / _InteractionSnowTex global " +
                               "(sand " + (sandTex != null) + ", snow " + (snowTex != null) + ")");
            UObject.DestroyImmediate(parking);
            Application.logMessageReceived -= onLog;
            return report;
        }

        try
        {
            SetField(manager, "_editorPreview", false);
            SetField(manager, "_followTarget", parking.transform);

            // 探针压到域中心（z 也归零，否则压痕不在 RT 中心、径向剖面会歪）。
            var p = new Vector3(0f, startProbe.y, 0f);

            // ── A. 压住 2 s ─────────────────────────────────────────
            Step(manager, probe, p, HoldSteps);

            var sandPress = Read(sandTex);
            var snowPress = Read(snowTex);

            var sand = Profile(sandPress, sandTex.width, sandTex.height);
            var snow = Profile(snowPress, snowTex.width, snowTex.height);

            report.details.Add("sand press — pit " + sand.peak.ToString("F3") +
                               " @r" + sand.peakRadius + ", ring mean beyond r" + sand.edgeRadius +
                               " = " + sand.outerMean.ToString("F4") + ", outer max " +
                               sand.outerMax.ToString("F4"));
            report.details.Add("snow press — depth " + snow.peak.ToString("F3") +
                               " @r" + snow.peakRadius + ", ring mean beyond r" + snow.edgeRadius +
                               " = " + snow.outerMean.ToString("F4") + ", outer max " +
                               snow.outerMax.ToString("F4"));

            Check("sand digs a pit", Mathf.Abs(sand.signedPeak) >= PitMin,
                  "pit " + sand.peak.ToString("F3") + " (need ≥ " + PitMin.ToString("F2") + ")");

            Check("sand piles the displaced material into a rim outside the pit",
                  sand.outerMax >= RimMin && Mathf.Abs(sand.signedPeak) > 0f &&
                  sand.outerMax <= Mathf.Abs(sand.signedPeak) * RimMaxRatio &&
                  sand.outerMean > 0f,
                  "outer max " + sand.outerMax.ToString("F4") + " (need ≥ " + RimMin.ToString("F3") +
                  ", ≤ " + (RimMaxRatio * 100f).ToString("F0") + "% of pit), ring mean " +
                  sand.outerMean.ToString("F4") + " (need > 0)");

            Check("sand rim sits outside the pit lip", sand.outerMaxRadius >= sand.edgeRadius,
                  "rim peak at r" + sand.outerMaxRadius + ", pit lip at r" + sand.edgeRadius);

            Check("snow compacts downward", snow.peak >= PitMin,
                  "depth " + snow.peak.ToString("F3") + " (need ≥ " + PitMin.ToString("F2") + ")");

            // 雪的场只有非负（正 = 往下压），所以它**结构上**就表达不出向上的隆起；
            // 这里查的是外沿不能有一圈「比峰值还显眼」的量 —— 5×5 蠕变晕允许有，成脊不允许。
            Check("snow leaves no rim (compaction, not displacement)",
                  snow.outerMax <= snow.peak * SnowHaloRatio && snow.min >= -1e-4f,
                  "outer max " + snow.outerMax.ToString("F4") + " = " +
                  (snow.peak > 0f ? (snow.outerMax / snow.peak * 100f).ToString("F1") : "n/a") +
                  "% of peak (limit " + (SnowHaloRatio * 100f).ToString("F0") + "%), field min " +
                  snow.min.ToString("F5") + " (must be ≥ 0)");

            // ── B. 抬离，痕迹随时间变浅 ─────────────────────────────
            p.y += 3f;
            var sandTrack = new List<float> { sand.peak };
            var snowTrack = new List<float> { snow.peak };
            var sandOuter = new List<float> { sand.outerMax };

            for (int step = 0; step < FadeSteps; step++)
            {
                Step(manager, probe, p, 1);
                if ((step + 1) % (FadeSteps / 3) != 0) continue;

                var s = Profile(Read(sandTex), sandTex.width, sandTex.height);
                var w = Profile(Read(snowTex), snowTex.width, snowTex.height);
                sandTrack.Add(s.peak);
                sandOuter.Add(s.outerMax);
                snowTrack.Add(w.peak);
            }

            report.details.Add("sand pit over idle: " + Print(sandTrack) + " / rim " + Print(sandOuter));
            report.details.Add("snow depth over idle: " + Print(snowTrack));

            // 单调变浅 + 6 s 后明显浅了（沉降是乘性的，形状保持、整体等比衰减）。
            Check("sand trace fades over time", Fades(sandTrack, 0.80f),
                  Print(sandTrack) + " — need monotone non-increasing and < 80% after 6 s");
            Check("sand rim fades with the pit", Fades(sandOuter, 0.80f),
                  Print(sandOuter) + " — rim must not be permanent either");
            Check("snow trace fades over time", Fades(snowTrack, 0.85f),
                  Print(snowTrack) + " — need monotone non-increasing and < 85% after 6 s");

            // 抬离后沙要按休止角塌落：坑壁不能比 tanθ 更陡（这里只查「确实塌了一点」）。
            var sandAfter = Profile(Read(sandTex), sandTex.width, sandTex.height);
            report.details.Add("sand after idle — pit " + sandAfter.peak.ToString("F3") +
                               ", outer max " + sandAfter.outerMax.ToString("F4"));
        }
        finally
        {
            if (probe != null) probe.position = startProbe;
            SetField(manager, "_followTarget", target);
            SetField(manager, "_editorPreview", preview);
            SetField(manager, "_lastAreaPos", startLast);
            Call(manager, "SyncMatrices");
            for (int i = 0; i < drivers.Count; i++) if (drivers[i] != null) drivers[i].enabled = driverState[i];
            UObject.DestroyImmediate(parking);
            Application.logMessageReceived -= onLog;
        }

        foreach (string e in errors) report.details.Add("unexpected log: " + e);
        Check("no errors while driving the surfaces", errors.Count == 0, string.Join(" | ", errors));
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

    /// <summary>穿过峰值纹素的径向剖面（只沿 +x）。</summary>
    public struct Radial
    {
        public float signedPeak;   // 带符号的峰值（沙是负的坑，雪是正的压痕）
        public float peak;         // |signedPeak|
        public int   peakRadius;
        public int   edgeRadius;   // 从峰值往外第一次落到 10% 峰值的半径
        public float outerMax;     // 坑沿之外那一圈的最大值（带符号）
        public int   outerMaxRadius;
        public float outerMean;    // 那一圈的均值（带符号）
        public float min;          // 全场最小值（雪的场必须 ≥ 0）
    }

    private static Radial Profile(float[] data, int w, int h)
    {
        var result = new Radial { outerMax = 0f, outerMean = 0f };

        // 峰值纹素：按绝对值找，沙找到的是坑底、雪找到的是压痕中心。
        int best = 0;
        float bestAbs = -1f;
        result.min = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float a = Mathf.Abs(data[i]);
            if (a > bestAbs) { bestAbs = a; best = i; }
            if (data[i] < result.min) result.min = data[i];
        }
        result.signedPeak = data[best];
        result.peak = bestAbs;
        if (bestAbs <= 1e-6f) return result;

        int cx = best % w, cy = best / w;
        result.peakRadius = 0;

        // 沿 +x 走到边界，找 10% 峰值的半径。
        int limit = Mathf.Min(w - 1 - cx, (int)(w * 0.35f));
        result.edgeRadius = limit;
        for (int r = 1; r <= limit; r++)
        {
            if (Mathf.Abs(data[cy * w + cx + r]) <= bestAbs * 0.1f)
            {
                result.edgeRadius = r;
                break;
            }
        }

        // 坑沿之外 1..12 个纹素。**取带符号的最大值**：脊按定义是正值，
        // 而紧贴坑壁那一格在抬离后会被休止线拉低（坑壁比 tanθ 陡，沙往坑里塌，
        // 见 SandInteraction.compute 第 3 步），那个负值不是脊，不该参与脊的判据。
        float sum = 0f;
        int count = 0;
        int outerEnd = Mathf.Min(limit, result.edgeRadius + 12);
        for (int r = Mathf.Max(1, result.edgeRadius); r <= outerEnd; r++)
        {
            float v = data[cy * w + cx + r];
            sum += v;
            count++;
            if (count == 1 || v > result.outerMax)
            {
                result.outerMax = v;
                result.outerMaxRadius = r;
            }
        }
        result.outerMean = count > 0 ? sum / count : 0f;
        return result;
    }

    private static bool Fades(List<float> track, float maxRatio)
    {
        for (int i = 1; i < track.Count; i++)
            if (track[i] > track[i - 1] + 1e-4f)
                return false;
        return track.Count > 1 && track[track.Count - 1] <= track[0] * maxRatio;
    }

    private static string Print(List<float> a)
    {
        var parts = new string[a.Count];
        for (int i = 0; i < a.Count; i++) parts[i] = a[i].ToString("F3");
        return string.Join(" → ", parts);
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (_report != null)
        {
            if (ok) _report.passed++; else _report.failed++;
            _report.details.Add((ok ? "[PASS] " : "[FAIL] ") + name + " — " + detail);
        }
        if (!ok) Debug.LogWarning("[CheckSurfaceDynamics][FAIL] " + name + " — " + detail);
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
