using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Mine.Interaction;
using UObject = UnityEngine.Object;

// Run with: unityctl script execute -f "Assets/Mine/Scripts/InteractionManager/Validation~/CheckDomainFade.cs"
//
// 验证「域边界淡出」是**数据级**约定（InteractionDomainEdge.hlsl）：
// 各子集在写回 RT 时自己把值收到 0，而不是等渲染侧去裁。
//
// 两条断言，任一条失败就说明痕迹还是「硬切」的：
//   A. RT 最外一圈纹素贴着 0（不超过全场峰值的 1%）—— 域外没有残留值能被 Clamp 采样拖出去。
//   B. 边界带内相邻纹素没有台阶 —— 收到 0 的过程是渐隐，不是一步掉下去。
//
// 做法：把 _followTarget 临时换成固定原点的空物体（域不动），探针沿 x 从 -0.4·area
// 拖到 +0.48·area，让痕迹一路铺到 RT 边上，然后读回每一张已发布的 _Interaction*Tex。
public class Script
{
    [Serializable]
    public sealed class Report
    {
        public string suite = "CheckDomainFade";
        public int passed;
        public int failed;
        public List<string> details = new List<string>();
    }

    // 边界带宽度（与 InteractionDomainEdge.hlsl 的 INTERACTION_DOMAIN_FADE 同一约定）
    private const float FadeWidth = 0.08f;
    // 相邻纹素允许的最大台阶占整场峰值的比例。硬切是一步 100%，渐隐远小于这个数。
    private const float MaxStepRatio = 0.25f;
    // 最外圈允许残留：窗口在纹素中心取到 smoothstep(0, 0.08, 0.5/256) ≈ 1.8e-3，
    // 所以「贴着 0」而不是「恰好 0」。超过峰值的 1% 就说明收束没生效。
    private const float MaxRingRatio = 0.01f;

    private static readonly string[] TextureIds =
    {
        "_InteractionGrassTex", "_InteractionSnowTex", "_InteractionSandTex", "_InteractionWaterTex",
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

        bool preview = (bool)Field(manager, "_editorPreview");
        var startProbe = probe != null ? probe.position : Vector3.zero;
        var startLast = (Vector3)Field(manager, "_lastAreaPos");

        // 域钉死在原点：探针才能走到 RT 边缘而不把域一起带走。
        var parking = new GameObject("~DomainFadeCheckFollow");
        parking.hideFlags = HideFlags.HideAndDontSave;
        parking.transform.position = Vector3.zero;

        try
        {
            SetField(manager, "_editorPreview", false);
            SetField(manager, "_followTarget", parking.transform);

            var p = startProbe;
            p.z = 0f;

            // 探针一路从左侧拖到靠近右边缘，痕迹铺满整行。
            float from = -0.4f * areaSize;
            float to   =  0.48f * areaSize;
            const int steps = 70;
            for (int i = 0; i <= steps; i++)
            {
                p.x = Mathf.Lerp(from, to, i / (float)steps);
                if (probe != null) probe.position = p;

                Call(manager, "SyncMatrices");
                if (!(bool)Call(manager, "RenderOrigin"))
                {
                    report.details.Add("RenderOrigin failed at step " + i);
                    report.failed++;
                    break;
                }
                Call(manager, "Tick", 1f / 30f);
            }

            report.details.Add("probe y = " + p.y.ToString("F3") + "  (穿透深度决定有没有痕迹)");

            int found = 0;
            var before = new Dictionary<string, float[]>();
            foreach (string id in TextureIds)
            {
                var texture = Shader.GetGlobalTexture(id) as RenderTexture;
                if (texture == null) continue;
                found++;
                var data = Read(texture);
                before[id] = RowTail(data, texture.width, texture.height);
                Analyse(report, id, texture, data, "after drag");
            }

            if (found == 0)
            {
                report.failed++;
                report.details.Add("[FAIL] no _Interaction*Tex global is published");
            }
            else
            {
                // ── 静置：探针挪到域外，空跑 3 秒 ──────────────────
                // 边界淡出必须是**空间**的：静置后痕迹整体变淡可以，但「带内被吃空、
                // 内沿剩一道坎」不行 —— 那就是软边自己变硬边。
                p.x = -0.49f * areaSize;
                if (probe != null) probe.position = p;
                for (int i = 0; i < 90; i++)
                {
                    Call(manager, "SyncMatrices");
                    Call(manager, "RenderOrigin");
                    Call(manager, "Tick", 1f / 30f);
                }

                foreach (string id in before.Keys)
                {
                    var texture = Shader.GetGlobalTexture(id) as RenderTexture;
                    if (texture == null) continue;
                    var data = Read(texture);
                    var after = RowTail(data, texture.width, texture.height);
                    Analyse(report, id, texture, data, "after 3 s idle");
                    CheckSoftEdgeSurvives(report, id, before[id], after);
                }
            }
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
        Check("no errors while dragging the probe to the domain edge", errors.Count == 0, string.Join(" | ", errors));
        return report;
    }

    private static void Analyse(Report report, string id, RenderTexture rt, float[] data, string phase)
    {
        int w = rt.width, h = rt.height;

        float maxAbs = 0f;
        int nonzero = 0;
        for (int i = 0; i < data.Length; i++)
        {
            float v = Mathf.Abs(data[i]);
            if (v > 1e-6f) nonzero++;
            maxAbs = Mathf.Max(maxAbs, v);
        }

        // ── A. 最外一圈恒为 0 ───────────────────────────────
        float ringMax = 0f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (x != 0 && x != w - 1 && y != 0 && y != h - 1) continue;
                ringMax = Mathf.Max(ringMax, Mathf.Abs(data[y * w + x]));
            }

        bool hasContent = maxAbs > 1e-3f;
        float ringRatio = maxAbs > 0f ? ringMax / maxAbs : ringMax;
        bool ringOk = ringRatio <= MaxRingRatio;
        report.details.Add((ringOk ? "[PASS] " : "[FAIL] ") + id + " border ring (" + phase + ") — " +
                           "max |v| = " + ringMax.ToString("F6") + " = " +
                           (ringRatio * 100f).ToString("F2") + "% of field max " + maxAbs.ToString("F4") +
                           ", nonzero " + nonzero +
                           " (limit " + (MaxRingRatio * 100f).ToString("F0") + "%)");
        if (ringOk) report.passed++; else report.failed++;

        if (!hasContent)
        {
            report.details.Add("[SKIP] " + id + " has no content (" + phase + "); drag/fade not exercised");
            return;
        }

        // ── B. 边界带内没有台阶 ─────────────────────────────
        // 只看有内容的那一行（过探针中心），沿 x 从「边界带起点」走到最外圈前一个纹素。
        int row = h / 2;
        int bandStart = Mathf.FloorToInt(w * (1f - FadeWidth));
        float maxStep = 0f;
        int stepAt = bandStart;
        for (int x = bandStart; x < w - 1; x++)
        {
            float step = Mathf.Abs(data[row * w + x + 1] - data[row * w + x]);
            if (step > maxStep) { maxStep = step; stepAt = x; }
        }

        float ratio = maxAbs > 0f ? maxStep / maxAbs : 0f;
        bool stepOk = ratio <= MaxStepRatio;
        report.details.Add((stepOk ? "[PASS] " : "[FAIL] ") + id + " edge band has no hard cut (" + phase +
                           ") — max step " + (ratio * 100f).ToString("F1") + "% of field max at x = " +
                           stepAt + " (limit " + (MaxStepRatio * 100f).ToString("F0") + "%)");
        if (stepOk) report.passed++; else report.failed++;
    }

    /// <summary>过探针中心那一行、最外 24 个纹素。</summary>
    private static float[] RowTail(float[] data, int w, int h)
    {
        int start = Mathf.Max(0, w - 24);
        var tail = new float[w - start];
        Array.Copy(data, (h / 2) * w + start, tail, 0, tail.Length);
        return tail;
    }

    /// <summary>
    /// 静置后软边不能变硬：痕迹整体变淡是允许的，但「带内被吃空、内沿剩一道坎」是缺陷。
    /// 判据：淡出带里仍然要有足够多的纹素带值（台阶被分摊在多个纹素上），
    /// 而不是一步从内沿掉到 0。
    /// </summary>
    private static void CheckSoftEdgeSurvives(Report report, string id, float[] before, float[] after)
    {
        // before/after 都是最外 24 个纹素（bandStart ≈ 倒数第 20 个）。
        int n = after.Length;
        float peakBefore = 0f, peakAfter = 0f;
        for (int i = 0; i < n; i++)
        {
            peakBefore = Mathf.Max(peakBefore, Mathf.Abs(before[i]));
            peakAfter = Mathf.Max(peakAfter, Mathf.Abs(after[i]));
        }

        if (peakBefore < 1e-3f)
        {
            report.details.Add("[SKIP] " + id + " soft-edge stability — no content in the band before idling");
            return;
        }

        // 静置后带内已经没内容了（草地回弹干净、水面平息）—— 没有边可言，
        // 谈不上「软边变硬边」，直接跳过而不是判失败。
        if (peakAfter < 1e-3f)
        {
            report.details.Add("[SKIP] " + id + " soft-edge stability — band is empty after idling " +
                               "(peak " + peakAfter.ToString("F4") + "), nothing left to erode");
            return;
        }

        // 带内还有多少个纹素「明显带值」：软边是 8~15 个，硬边只有 0~2 个。
        float mark = peakAfter * 0.2f;
        int rampTexels = 0;
        for (int i = 0; i < n; i++) if (Mathf.Abs(after[i]) > mark) rampTexels++;
        bool ok = rampTexels >= 4;
        report.details.Add((ok ? "[PASS] " : "[FAIL] ") + id + " soft edge survives idling — " +
                           rampTexels + "/" + n + " texels in the band still above 20% of the band peak " +
                           "(band peak before " + peakBefore.ToString("F4") + " → after " +
                           peakAfter.ToString("F4") + "), limit ≥ 4");
        if (ok) report.passed++; else report.failed++;

        if (!ok)
            report.details.Add("    tail before: " + Print(before) + "\n    tail after : " + Print(after));
    }

    private static string Print(float[] a)
    {
        var parts = new string[a.Length];
        for (int i = 0; i < a.Length; i++) parts[i] = a[i].ToString("F3");
        return string.Join(" ", parts);
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (_report != null)
        {
            if (ok) _report.passed++; else _report.failed++;
            _report.details.Add((ok ? "[PASS] " : "[FAIL] ") + name + " — " + detail);
        }
        if (!ok) Debug.LogWarning("[CheckDomainFade][FAIL] " + name + " — " + detail);
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
