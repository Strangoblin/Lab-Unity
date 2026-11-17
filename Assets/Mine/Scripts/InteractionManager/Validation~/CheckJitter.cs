using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Mine.Interaction;
using UObject = UnityEngine.Object;

// Run with: unityctl script execute Assets/Mine/Scripts/InteractionManager/Validation~/CheckJitter.cs
// 验证抖动的两个结构性前提：交互域中心落在纹素网格上、每步位移是精确整数个纹素，
// 以及「捕获位姿 == delta 位姿」这条时序约束没有被破坏。
public class Script
{
    [Serializable]
    public sealed class Report
    {
        public string suite = "CheckJitter";
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

    private static Vector3 GetCenter(object manager) =>
        (Vector3)manager.GetType().GetProperty("SnappedCenter", Private).GetValue(manager);

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

        var cam = (Camera)Field(manager, "_orthoCamera");
        var target = (Transform)Field(manager, "_followTarget");
        var origin = (RenderTexture)Field(manager, "_originRT");
        float areaSize = (float)Field(manager, "_areaSize");
        int resolution = (int)Field(manager, "_resolution");
        float texel = areaSize / Mathf.Max(resolution, 1);

        // 测试期间接管驱动：停掉自动移动，关掉编辑预览的自动步进。
        var drivers = UObject.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Where(m => m != null && m.GetType().Name == "InteractionProbeDriver").ToList();
        var driverState = drivers.Select(d => d.enabled).ToList();
        foreach (var d in drivers) d.enabled = false;

        bool preview = (bool)Field(manager, "_editorPreview");
        var startTarget = target != null ? target.position : Vector3.zero;
        var startLast = (Vector3)Field(manager, "_lastAreaPos");

        try
        {
            SetField(manager, "_editorPreview", false);

            Check("capture camera is driven manually, not by the render loop",
                cam != null && !cam.enabled, "orthoCamera.enabled = " + (cam != null && cam.enabled));

            // 把探针放在草地中心：域内完整，捕获不会被域边界裁掉。
            if (target != null) target.position = new Vector3(0f, startTarget.y, 0f);
            Call(manager, "SyncMatrices");
            Call(manager, "RenderOrigin");
            Call(manager, "Tick", 1f / 30f);

            int steps = 40;
            SetField(manager, "_lastAreaPos", GetCenter(manager));
            var centers = new List<Vector3>();
            var centroids = new List<float>();
            var probeX = new List<float>();
            var areaPos = new List<Vector3>();
            float maxGridResidual = 0f;
            float maxDeltaResidual = 0f;

            for (int i = 0; i < steps; i++)
            {
                // 慢速往返：单步位移 0..0.6 个纹素，正是旧实现里 round 在 0/±1 间跳的范围。
                float phase = i / (float)steps * Mathf.PI * 2f;
                float x = Mathf.Sin(phase) * texel * 12f;
                if (target != null) target.position = new Vector3(x, startTarget.y, 0f);

                Call(manager, "SyncMatrices");
                Call(manager, "RenderOrigin");
                Call(manager, "Tick", 1f / 30f);

                Vector3 center = GetCenter(manager);
                centers.Add(center);
                probeX.Add(x);
                areaPos.Add(Shader.GetGlobalVector("_InteractionAreaPos"));
                centroids.Add(CentroidX(origin));
            }

            for (int i = 0; i < centers.Count; i++)
            {
                float rx = Mathf.Abs(centers[i].x / texel - Mathf.Round(centers[i].x / texel));
                float rz = Mathf.Abs(centers[i].z / texel - Mathf.Round(centers[i].z / texel));
                maxGridResidual = Mathf.Max(maxGridResidual, rx, rz);

                if (i > 0)
                {
                    float dx = (centers[i].x - centers[i - 1].x) / texel;
                    float dz = (centers[i].z - centers[i - 1].z) / texel;
                    maxDeltaResidual = Mathf.Max(maxDeltaResidual,
                        Mathf.Abs(dx - Mathf.Round(dx)), Mathf.Abs(dz - Mathf.Round(dz)));
                }
            }

            Check("interaction area centre sits on the texel grid (residual < 1e-3 texel)",
                maxGridResidual < 1e-3f, "max residual = " + maxGridResidual.ToString("F5") + " texel");
            Check("per-step domain motion is an exact whole number of texels",
                maxDeltaResidual < 1e-3f, "max residual = " + maxDeltaResidual.ToString("F5") + " texel");

            bool areaPosMatches = areaPos.Count > 0 && areaPos.All(v =>
                Mathf.Abs(v.x - centers[areaPos.IndexOf(v)].x) < 1e-4f);
            Check("_InteractionAreaPos equals the snapped centre every step", areaPosMatches,
                "first = " + (areaPos.Count > 0 ? areaPos[0].ToString("F4") : "n/a"));

            float maxOrigin = 0f;
            var originData = Read(origin);
            for (int i = 0; i < originData.Length; i++) maxOrigin = Mathf.Max(maxOrigin, Mathf.Abs(originData[i]));
            Check("origin capture still renders with the camera component disabled",
                maxOrigin > 0.01f, "origin max = " + maxOrigin.ToString("F4") + " on " + originData.Length + " px");

            // 捕获是「相对交互域」的：探针像素 = (球世界坐标 − 域中心) / texel + 常数。
            // 域中心按整数纹素跳，所以质心天然是锯齿状的 —— 关键是每步都严格落在这条式子上：
            // 差一点就说明「渲染用的位姿」和「算 delta 用的位姿」不是同一次采样（旧实现的一步滞后）。
            int stepsCompared = 0, matched = 0;
            float maxCentroidError = 0f;
            for (int i = 1; i < centroids.Count; i++)
            {
                float expected = ((probeX[i] - probeX[i - 1]) - (centers[i].x - centers[i - 1].x)) / texel;
                float actual = centroids[i] - centroids[i - 1];
                stepsCompared++;
                if (Mathf.Abs(actual - expected) < 0.1f) matched++;
                maxCentroidError = Mathf.Max(maxCentroidError, Mathf.Abs(actual - expected));
            }
            Check("capture pose and delta pose are the same sample (centroid = (probe − centre)/texel)",
                matched == stepsCompared,
                matched + "/" + stepsCompared + " steps, max |error| " + maxCentroidError.ToString("F3") + " px");

            CheckGlobals(report, "grass", "_InteractionGrassTex", "_InteractionGrassValid");
            CheckGlobals(report, "snow", "_InteractionSnowTex", "_InteractionSnowValid");
            CheckGlobals(report, "sand", "_InteractionSandTex", "_InteractionSandValid");
        }
        finally
        {
            if (target != null) target.position = startTarget;
            SetField(manager, "_editorPreview", preview);
            SetField(manager, "_lastAreaPos", startLast);
            Call(manager, "SyncMatrices");
            for (int i = 0; i < drivers.Count; i++) if (drivers[i] != null) drivers[i].enabled = driverState[i];
            Application.logMessageReceived -= onLog;
        }

        foreach (string e in errors) report.details.Add("unexpected log: " + e);
        Check("no errors while stepping the domain", errors.Count == 0, string.Join(" | ", errors));
        return report;
    }

    private static void CheckGlobals(Report report, string label, string texId, string validId)
    {
        var texture = Shader.GetGlobalTexture(texId) as RenderTexture;
        float valid = Shader.GetGlobalFloat(validId);
        if (texture == null)
        {
            report.failed++;
            report.details.Add("[FAIL] " + label + " global texture is missing");
            return;
        }
        var data = Read(texture);
        float max = 0f, min = 0f;
        int nonzero = 0;
        for (int i = 0; i < data.Length; i++)
        {
            max = Mathf.Max(max, data[i]);
            min = Mathf.Min(min, data[i]);
            if (Mathf.Abs(data[i]) > 1e-4f) nonzero++;
        }
        bool ok = valid > 0.5f && nonzero > 0;
        if (ok) report.passed++; else report.failed++;
        report.details.Add((ok ? "[PASS] " : "[FAIL] ") + label +
            ": valid " + valid + ", max " + max.ToString("F3") + ", min " + min.ToString("F3") +
            ", nonzero " + nonzero);
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (_report != null)
        {
            if (ok) _report.passed++; else _report.failed++;
            _report.details.Add((ok ? "[PASS] " : "[FAIL] ") + name + " — " + detail);
        }
        if (!ok) Debug.LogWarning("[CheckJitter][FAIL] " + name + " — " + detail);
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

    private static float CentroidX(RenderTexture rt)
    {
        if (rt == null) return 0f;
        float[] data = Read(rt);
        float sum = 0f, weighted = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float v = Mathf.Abs(data[i]);
            if (v < 1e-4f) continue;
            sum += v;
            weighted += (i % rt.width) * v;
        }
        return sum > 0f ? weighted / sum : 0f;
    }
}
