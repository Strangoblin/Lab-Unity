using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 列剖面探针：读取真实 Voronoi 贴图，按 SSC.shader 的当前数学在 CPU 上
// 复算每个 XZ 列的 coverage / 局部云顶 / 垂直填充比例，用来量化
// 「云体是否整层贯通」。一次性诊断脚本，结论并入 README 后删除。
public class Script
{
    const float WorldHalf = 250f;
    const int Grid = 400;
    const float Scale = 2f;
    const float SlabBottom = 40f;
    const float SlabTop = 60f;
    const float Thickness = 0.8f;
    const float Middle = 0.25f;

    static Color32[] pixels;
    static int width, height;

    public static object Main()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Voronoi.png");
        if (texture == null)
        {
            foreach (var guid in AssetDatabase.FindAssets("Voronoi t:Texture2D"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null) break;
            }
        }
        if (texture == null) throw new Exception("Voronoi texture missing");
        pixels = texture.GetPixels32();
        width = texture.width;
        height = texture.height;

        var lines = new List<string>();
        lines.Add("texture=" + texture.name + " " + width + "x" + height
                  + " colorSpace=" + texture.graphicsFormat);

        // 单列噪声统计（与贴图无关的取值域确认）
        var noiseSamples = new List<float>();
        for (int z = 0; z < Grid; z++)
        for (int x = 0; x < Grid; x++)
            noiseSamples.Add(DensityLOD(new Vector3(
                Mathf.Lerp(-WorldHalf, WorldHalf, x / (Grid - 1f)), 0f,
                Mathf.Lerp(-WorldHalf, WorldHalf, z / (Grid - 1f))), 4));
        noiseSamples.Sort();
        lines.Add("noise " + Pct(noiseSamples));

        foreach (float threshold in new[] { 0.20f, 0.44f, 0.47f, 0.62f })
        {
            lines.Add("");
            lines.Add("--- threshold " + threshold.ToString("0.00") + " ---");
            Evaluate(threshold, false, lines, "current lerp(1,cov,0.5)");
            Evaluate(threshold, true, lines, "proposed topScale=cov");
        }
        return lines;
    }

    static string Pct(List<float> values)
    {
        return "p05=" + Q(values, 0.05f).ToString("0.000")
             + " p25=" + Q(values, 0.25f).ToString("0.000")
             + " p50=" + Q(values, 0.50f).ToString("0.000")
             + " p75=" + Q(values, 0.75f).ToString("0.000")
             + " p95=" + Q(values, 0.95f).ToString("0.000")
             + " mean=" + Mean(values).ToString("0.000");
    }

    static void Evaluate(float threshold, bool directTop, List<string> lines, string label)
    {
        int columns = 0, holes = 0, reachTop = 0;
        var tops = new List<float>();
        double fillSum = 0, coverageSum = 0, densitySum = 0;
        var fill = new List<float>();

        for (int z = 0; z < Grid; z++)
        for (int x = 0; x < Grid; x++)
        {
            float wx = Mathf.Lerp(-WorldHalf, WorldHalf, x / (Grid - 1f));
            float wz = Mathf.Lerp(-WorldHalf, WorldHalf, z / (Grid - 1f));
            columns++;

            float boundary = BoundaryDensity(wx, wz);
            if (boundary <= 0f) { holes++; continue; }

            float noise = DensityLOD(new Vector3(wx, 0f, wz), 4);
            float coverage = Mathf.Clamp01((noise - threshold) / Mathf.Max(1e-4f, 1f - threshold));
            if (coverage <= 0f) { holes++; continue; }

            float topScale = directTop ? coverage : Mathf.Lerp(1f, coverage, 0.5f);
            coverageSum += coverage;
            topScale = Mathf.Max(topScale, 1e-4f);

            int filled = 0, total = 0;
            float peak = 0f;
            const int Steps = 100;
            for (int i = 0; i < Steps; i++)
            {
                float heightT = (i + 0.5f) / Steps;
                float h = Mathf.Clamp01(heightT / topScale);
                float d = (h <= Middle) ? (h / Mathf.Max(1e-5f, Middle))
                                        : ((1f - h) / Mathf.Max(1e-5f, 1f - Middle));
                d = Mathf.Pow(Mathf.Clamp01(d), Mathf.Max(0.00001f, 1f - Thickness));
                float density = coverage * d * boundary;
                total++;
                if (density > 1e-3f) filled++;
                peak = Mathf.Max(peak, density);
            }
            densitySum += peak;
            float fillRatio = filled / (float)total;
            fillSum += fillRatio;
            fill.Add(fillRatio);
            tops.Add(topScale);
            if (topScale > 0.9f) reachTop++;
        }

        tops.Sort();
        fill.Sort();
        int solid = columns - holes;
        lines.Add(label);
        lines.Add("  holes=" + (100f * holes / columns).ToString("0.0") + "% of columns");
        lines.Add("  topScale " + Pct(tops));
        lines.Add("  vertical fill " + Pct(fill)
                  + " (mean " + (solid > 0 ? (fillSum / solid).ToString("0.000") : "n/a") + ")");
        lines.Add("  mean coverage=" + (solid > 0 ? (coverageSum / solid).ToString("0.000") : "n/a")
                  + " mean peak density=" + (solid > 0 ? (densitySum / solid).ToString("0.000") : "n/a")
                  + " columns reaching slab top=" + (100f * reachTop / columns).ToString("0.0") + "%");
    }

    static float Q(List<float> values, float q)
    {
        if (values.Count == 0) return 0f;
        int index = Mathf.Clamp(Mathf.RoundToInt(q * (values.Count - 1)), 0, values.Count - 1);
        return values[index];
    }

    static float Mean(List<float> values)
    {
        double sum = 0;
        foreach (var v in values) sum += v;
        return values.Count > 0 ? (float)(sum / values.Count) : 0f;
    }

    static float BoundaryDensity(float wx, float wz)
    {
        float2Like p = new float2Like(wx / WorldHalf, wz / WorldHalf);
        float dist = (float)Math.Sqrt(p.x * p.x + p.y * p.y) - 1f;
        float coverage = Mathf.Clamp01(1f - dist);
        return SmoothStep(0f, 0.5f, coverage);
    }

    struct float2Like { public float x, y; public float2Like(float x, float y) { this.x = x; this.y = y; } }

    static float SmoothStep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    // SSC.shader Noise()：贴图是 sRGB，Linear 项目采样会被解码；这里用原始 8bit 数值
    // （等于编码还原后的值域），与 shader 中的 Noise 输出一致。
    static float Noise(Vector3 positionWS)
    {
        float u = positionWS.x / 256f;
        float v = positionWS.z / 256f;
        u -= Mathf.Floor(u);
        v -= Mathf.Floor(v);
        int px = Mathf.Clamp((int)(u * width), 0, width - 1);
        int py = Mathf.Clamp((int)(v * height), 0, height - 1);
        return pixels[py * width + px].r / 255f;
    }

    static float DensityLOD(Vector3 positionWS, int lod)
    {
        Vector3 pos = positionWS * Scale;
        float low = Noise(pos);
        float mid = 0f;
        float amp = 0.5f;
        float[] mul = { 2.02f, 2.23f, 2.41f, 2.62f };
        for (int i = 0; i < lod; i++)
        {
            pos *= mul[i];
            mid += amp * Noise(pos);
            amp *= 0.5f;
        }
        float weightSum = Mathf.Max(1f - Mathf.Pow(2f, -lod), 1e-4f);
        return low * 0.5f + (mid / weightSum) * 0.5f;
    }
}
