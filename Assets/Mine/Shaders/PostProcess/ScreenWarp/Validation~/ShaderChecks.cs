// ════════════════════════════════════════════════════════════
//  ScreenWarp Shader checks — Metal输出对照、HDR/Alpha与有效视口边界。
// ════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class Script
{
    const string Root = "Assets/Mine/Shaders/PostProcess/ScreenWarp/";
    const float Tolerance = 0.0015f;
    [Serializable] public sealed class Report
    {
        public bool passed;
        public string device;
        public int cases, pixels, failures, paddedCases, negativeControls;
        public float maxError;
        public List<string> messages = new List<string>();
    }

    public static object Main(string[] args)
    {
        var report = new Report { device = SystemInfo.graphicsDeviceType.ToString() };
        var savedRT = RenderTexture.active;
        Material material = null;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new Exception("A real graphics device is required.");
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "ScreenWarp.shader");
            if (shader == null || !shader.isSupported)
                throw new Exception("ScreenWarp shader missing or unsupported.");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (material.FindPass("ScreenWarp") != 0 || material.passCount != 1)
                throw new Exception("Expected exactly one ScreenWarp pass.");
            int[] widths = { 96, 54, 64 };
            int[] heights = { 54, 96, 64 };
            Vector2[] centers = { new Vector2(0.5f, 0.5f), new Vector2(0.15f, 0.8f) };
            float[] strengths = { 0f, 0.35f, 1f };
            float[] zooms = { 1f, 1.8f };
            for (int shape = 0; shape < widths.Length; ++shape)
            {
                for (int padded = 0; padded < 2; ++padded)
                {
                    using (var fixture = new Fixture(widths[shape], heights[shape], padded != 0))
                    {
                        for (int mode = 0; mode < 4; ++mode)
                        {
                            foreach (Vector2 center in centers)
                            foreach (float strength in strengths)
                            foreach (float zoom in zooms)
                            {
                                int border = (mode + shape) % 2;
                                Run(report, fixture, material, mode, strength, center, zoom, border, 0);
                                if (strength == 1f && zoom == 1f)
                                {
                                    Run(report, fixture, material, mode, strength, center, zoom, 1 - border, 0);
                                    Run(report, fixture, material, mode, strength, center, zoom, border, 1);
                                    Run(report, fixture, material, mode, strength, center, zoom, border, 2);
                                }
                            }
                        }
                        if (shape == 0 && padded == 0)
                        {
                            Color[] identity = fixture.Draw(material, 0, 0f, centers[0], 1f, 0, 0);
                            Color[] warp = fixture.Draw(material, 2, 1f, centers[0], 1f, 0, 0);
                            float difference = 0f;
                            for (int i = 0; i < identity.Length; ++i)
                                difference = Mathf.Max(difference, Error(identity[i], warp[i]));
                            if (difference < 0.05f)
                                throw new Exception("Negative control failed to detect disabled warp.");
                            ++report.negativeControls;
                        }
                    }
                }
            }
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity.ToString() == "Error")
                    throw new Exception("Shader error: " + message.message);
            report.passed = report.cases > 0 && report.failures == 0 && report.negativeControls == 1;
            string output = args != null && args.Length > 0 ? args[0]
                : Path.Combine("tmp", "sessions", DateTime.Now.ToString("yyyy-MM-dd")
                    + "-screenwarp-shader-" + Guid.NewGuid().ToString("N").Substring(0, 8), "output");
            Directory.CreateDirectory(output);
            File.WriteAllText(output + "/shader-checks.json", JsonUtility.ToJson(report, true));
            return report;
        }
        finally
        {
            RenderTexture.active = savedRT;
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    static void Run(Report report, Fixture fixture, Material material, int mode, float strength,
        Vector2 center, float zoom, int border, int debug)
    {
        Color[] actual = fixture.Draw(material, mode, strength, center, zoom, border, debug);
        float maxError = 0f;
        for (int y = 0; y < fixture.height; ++y)
        for (int x = 0; x < fixture.width; ++x)
        {
            Vector2 uv = new Vector2((x + 0.5f) / fixture.width, (y + 0.5f) / fixture.height);
            Vector2 mapped = Reference(uv, (float)fixture.width / fixture.height, mode, strength, center, zoom);
            bool inside = mapped.x >= 0f && mapped.x <= 1f && mapped.y >= 0f && mapped.y <= 1f;
            Color expected;
            if (debug == 1)
                expected = new Color(Mathf.Clamp01(mapped.x), Mathf.Clamp01(mapped.y), inside ? 1f : 0f, 1f);
            else if (debug == 2)
                expected = inside ? Color.green : Color.red;
            else if (!inside && border == 1)
                expected = Fixture.Border;
            else
            {
                float u = Mathf.Clamp(mapped.x, 0.5f / fixture.width, 1f - 0.5f / fixture.width);
                float v = Mathf.Clamp(mapped.y, 0.5f / fixture.height, 1f - 0.5f / fixture.height);
                expected = Fixture.Gradient(u, v);
            }
            maxError = Mathf.Max(maxError, Error(actual[y * fixture.width + x], expected));
        }
        ++report.cases;
        report.pixels += actual.Length;
        if (fixture.padded) ++report.paddedCases;
        report.maxError = Mathf.Max(report.maxError, maxError);
        if (maxError > Tolerance || float.IsNaN(maxError) || float.IsInfinity(maxError))
        {
            ++report.failures;
            if (report.messages.Count < 12)
                report.messages.Add("mode=" + mode + " strength=" + strength + " center=" + center
                    + " zoom=" + zoom + " border=" + border + " debug=" + debug + " padded="
                    + fixture.padded + " size=" + fixture.width + "x" + fixture.height + " error=" + maxError);
        }
    }

    static Vector2 Reference(Vector2 uv, double aspect, int mode, double strength, Vector2 center, double zoom)
    {
        if (strength == 0.0) return uv;
        double farX = Math.Max(center.x, 1.0 - center.x) * aspect;
        double farY = Math.Max(center.y, 1.0 - center.y);
        double normalization = Math.Sqrt(farX * farX + farY * farY);
        double x = (uv.x - center.x) * aspect / (normalization * zoom);
        double y = (uv.y - center.y) / (normalization * zoom);
        double r = Math.Sqrt(x * x + y * y);
        double sourceRadius = r;
        if (mode == 0) sourceRadius = r + 0.6 * r * r * r;
        if (mode == 1) sourceRadius = r / (1.0 + 0.6 * r * r);
        if (mode == 2) sourceRadius = Math.Tan(r * 1.2) / 1.2;
        double scale = r > 0.00001 ? sourceRadius / r : 1.0;
        if (mode == 3)
        {
            double hypotenuse = Math.Sqrt(x * x + 4.0);
            double cylinderFraction = (hypotenuse - x * x / hypotenuse) / hypotenuse;
            scale = cylinderFraction / (2.0 * cylinderFraction - 1.0);
        }
        double mappedX = center.x + x * scale * normalization / aspect;
        double mappedY = center.y + y * scale * normalization;
        return new Vector2((float)(uv.x + (mappedX - uv.x) * strength),
            (float)(uv.y + (mappedY - uv.y) * strength));
    }

    static float Error(Color a, Color b)
    {
        if (!Finite(a.r) || !Finite(a.g) || !Finite(a.b) || !Finite(a.a)) return float.PositiveInfinity;
        return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a));
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    sealed class Fixture : IDisposable
    {
        public static readonly Color Border = new Color(0.125f, 0.375f, 0.625f, 0.875f);
        public readonly int width, height;
        public readonly bool padded;
        readonly Texture2D input, readback;
        readonly RTHandle source;
        readonly RenderTexture target;
        readonly Vector4 scaleBias;

        public Fixture(int w, int h, bool pad)
        {
            width = w; height = h; padded = pad;
            int physicalWidth = pad ? w * 2 : w;
            int physicalHeight = pad ? h * 2 : h;
            input = new Texture2D(physicalWidth, physicalHeight, TextureFormat.RGBAFloat, false, true)
            { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Color[] colors = new Color[physicalWidth * physicalHeight];
            for (int y = 0; y < physicalHeight; ++y)
            for (int x = 0; x < physicalWidth; ++x)
                colors[y * physicalWidth + x] = x < w && y < h
                    ? Gradient((x + 0.5f) / w, (y + 0.5f) / h) : new Color(10f, 0f, 10f, 0f);
            input.SetPixels(colors); input.Apply(false, false);
            source = RTHandles.Alloc(input);
            scaleBias = new Vector4((float)w / physicalWidth, (float)h / physicalHeight, 0f, 0f);
            target = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
            target.Create();
            readback = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
        }

        public static Color Gradient(float u, float v) => new Color(u, v, 2f, 0.25f + 0.5f * u + 0.2f * v);

        public Color[] Draw(Material material, int mode, float strength, Vector2 center, float zoom, int border, int debug)
        {
            material.SetVector("_WarpParameters", new Vector4(mode, strength, zoom, border));
            material.SetVector("_WarpCenter", new Vector4(center.x, center.y, 0f, 0f));
            material.SetColor("_WarpBorderColor", Border);
            material.SetVector("_WarpSourceSize", new Vector4(width, height, 1f / width, 1f / height));
            material.SetFloat("_WarpDebug", debug);
            var command = new CommandBuffer { name = "ScreenWarp GPU validation" };
            try
            {
                command.SetRenderTarget(target);
                command.SetViewport(new Rect(0, 0, width, height));
                Blitter.BlitTexture(command, source, scaleBias, material, 0);
                Graphics.ExecuteCommandBuffer(command);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readback.Apply(false, false);
                return readback.GetPixels();
            }
            finally { command.Release(); }
        }

        public void Dispose()
        {
            source.Release(); target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(readback);
            UnityEngine.Object.DestroyImmediate(input);
        }
    }
}
