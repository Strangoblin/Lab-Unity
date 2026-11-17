using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Mine.Interaction;
using UObject = UnityEngine.Object;

// Run with: unityctl script execute -f "Assets/Mine/Scripts/InteractionManager/Validation~/CheckWaterFade.cs"
//
// 水面专用：验证边界吸收带（InteractionDomainEdge.hlsl）没有把波动方程弄坏。
//
// 动作：在交互域边缘（uv ≈ 0.92，正是 8% 吸收带的起点）把探针压下去再抬起来打一记冲量，
// 然后空跑 8 秒，逐帧采样 _InteractionWaterTex。
//
// 断言：
//   A. 最外一圈纹素全程贴着 0（≤ 峰值的 1%）—— 吸收带把波吃掉，没有 Dirichlet 墙残留。
//   B. 域内确实起波（峰值远大于 0）—— 冲量 / 重投影 / Verlet 都没被边界项压死。
//   C. 8 秒后场安静（≤ 峰值的 5%）—— 边界项没有反过来注入能量。
public class Script
{
    [Serializable]
    public sealed class Report
    {
        public string suite = "CheckWaterFade";
        public int passed;
        public int failed;
        public List<string> details = new List<string>();
    }

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static object Field(object o, string name) =>
        o.GetType().GetField(name, Private).GetValue(o);

    private static void SetField(object o, string name, object value) =>
        o.GetType().GetField(name, Private).SetValue(o, value);

    private static object Call(object o, string name, params object[] args) =>
        o.GetType().GetMethod(name, Private).Invoke(o, args);

    private static Report _report;
    private const float Dt = 1f / 30f;

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
        var water = UObject.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .FirstOrDefault(m => m != null && m.GetType().Name == "WaterInteractionProcessor");
        if (manager == null || water == null)
        {
            report.details.Add("need both UniversalInteractionManager and WaterInteractionProcessor");
            report.failed++;
            Application.logMessageReceived -= onLog;
            return report;
        }

        var probe = UObject.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .FirstOrDefault(t => t.name.Contains("Probe"));
        var target = (Transform)Field(manager, "_followTarget");
        float areaSize = (float)Field(manager, "_areaSize");

        var drivers = UObject.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Where(m => m != null && m.GetType().Name == "InteractionProbeDriver").ToList();
        var driverState = drivers.Select(d => d.enabled).ToList();
        foreach (var d in drivers) d.enabled = false;

        bool preview = (bool)Field(manager, "_editorPreview");
        var startProbe = probe != null ? probe.position : Vector3.zero;
        var startLast = (Vector3)Field(manager, "_lastAreaPos");

        var parking = new GameObject("~WaterFadeCheckFollow");
        parking.hideFlags = HideFlags.HideAndDontSave;
        parking.transform.position = Vector3.zero;

        try
        {
            SetField(manager, "_editorPreview", false);
            SetField(manager, "_followTarget", parking.transform);
            Call(manager, "SyncMatrices");
            SetField(manager, "_lastAreaPos",
                (Vector3)manager.GetType().GetProperty("SnappedCenter", Private).GetValue(manager));

            // 冲量打在吸收带起点附近：波出生就贴着边界，最考验吸收带。
            float x = areaSize * 0.42f;   // uv ≈ 0.92
            var p = startProbe;
            p.x = x; p.z = 0f; p.y = startProbe.y;

            // 先静止泡住，让 _InteractionOriginPrevTex 记录到深度（_Primed）。
            for (int i = 0; i < 10; i++)
            {
                Probe(probe, manager, p);
            }

            // 压下去 → 抬起来，8 帧一进一出。
            float pressY = -0.35f, liftY = 0.6f;
            for (int i = 0; i < 8; i++) { p.y = Mathf.Lerp(startProbe.y, pressY, (i + 1) / 8f); Probe(probe, manager, p); }
            for (int i = 0; i < 8; i++) { p.y = Mathf.Lerp(pressY, liftY, (i + 1) / 8f); Probe(probe, manager, p); }

            var waterRT = Shader.GetGlobalTexture("_InteractionWaterTex") as RenderTexture;
            if (waterRT == null)
            {
                report.failed++;
                report.details.Add("[FAIL] _InteractionWaterTex is not published");
                return report;
            }

            int w = waterRT.width, h = waterRT.height;
            float peak = 0f, ringMax = 0f, finalPeak = 0f;
            int peakFrame = -1, worstFrame = -1;
            float[] worstRow = null;
            float worstInterior = 0f;

            const int frames = 240;   // 8 s @30 Hz
            for (int f = 0; f < frames; f++)
            {
                Probe(probe, manager, p);   // 探针停在抬起来的位置，让波自己跑
                var data = Read(waterRT);
                float px = 0f, ring = 0f;
                for (int y = 0; y < h; y++)
                    for (int xx = 0; xx < w; xx++)
                    {
                        float v = Mathf.Abs(data[y * w + xx]);
                        px = Mathf.Max(px, v);
                        if (xx == 0 || xx == w - 1 || y == 0 || y == h - 1) ring = Mathf.Max(ring, v);
                    }
                if (ring > ringMax)
                {
                    ringMax = ring;
                    worstFrame = f;
                    worstInterior = px;
                    var row = new float[w];
                    Array.Copy(data, (h / 2) * w, row, 0, w);
                    worstRow = row;
                }
                if (px > peak) { peak = px; peakFrame = f; }
                if (f == frames - 1) finalPeak = px;
            }

            report.details.Add("peak " + peak.ToString("F4") + " at frame " + peakFrame +
                               ", final " + finalPeak.ToString("F4") +
                               ", border ring max " + ringMax.ToString("F6") + " (frame " + worstFrame +
                               ", interior " + worstInterior.ToString("F4") + ")");
            if (worstRow != null)
            {
                var tail = new List<string>();
                for (int i = w - 12; i < w; i++) tail.Add(worstRow[i].ToString("F4"));
                report.details.Add("worst row tail (" + (w - 12) + ".." + (w - 1) + "): " + string.Join(" ", tail));
            }
            report.details.Add("impulse at x = " + x.ToString("F2") + " (uv " +
                               ((x / areaSize) + 0.5f).ToString("F3") + "), area " + areaSize.ToString("F1"));

            Check("wave is actually generated inside the domain", peak > 0.05f, "peak = " + peak.ToString("F4"));
            Check("border ring stays at zero through the whole run",
                ringMax <= Mathf.Max(peak * 0.01f, 1e-4f),
                "ring max = " + ringMax.ToString("F6") + " = " +
                (peak > 0f ? (ringMax / peak * 100f).ToString("F2") : "n/a") + "% of peak");
            Check("field settles back to quiet (no energy injected back at the edge)",
                finalPeak <= Mathf.Max(peak * 0.05f, 1e-3f),
                "final " + finalPeak.ToString("F4") + " = " +
                (peak > 0f ? (finalPeak / peak * 100f).ToString("F2") : "n/a") + "% of peak");
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
        Check("no errors while stepping the water", errors.Count == 0, string.Join(" | ", errors));
        return report;
    }

    private static void Probe(Transform probe, object manager, Vector3 position)
    {
        if (probe != null) probe.position = position;
        Call(manager, "SyncMatrices");
        Call(manager, "RenderOrigin");
        Call(manager, "Tick", Dt);
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (_report != null)
        {
            if (ok) _report.passed++; else _report.failed++;
            _report.details.Add((ok ? "[PASS] " : "[FAIL] ") + name + " — " + detail);
        }
        if (!ok) Debug.LogWarning("[CheckWaterFade][FAIL] " + name + " — " + detail);
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
