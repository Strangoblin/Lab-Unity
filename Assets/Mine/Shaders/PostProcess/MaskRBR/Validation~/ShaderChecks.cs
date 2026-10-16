using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>GPU controls for the production scalar MaskRBR and main-light angular composite.</summary>
/// <remarks>Run with unityctl script execute -f. Main accepts an optional JSON output file path.</remarks>
public class Script
{
    const float Tolerance = 0.002f;
    [Serializable] public sealed class CaseResult
    {
        public string name;
        public bool passed, finite = true;
        public int pixelCount, mismatchPixels;
        public float maxNormalizedError;
        public float[] sampleActual, sampleExpected;
    }
    [Serializable] public sealed class Report
    {
        public bool passed, globalsRestored;
        public int caseCount, failures;
        public string error, graphicsDevice;
        public string scope = "Production Mask/Radial/Composite GPU passes with float inputs, R8/RHalf/ARGBHalf outputs and float readback. Scalar targets assert stored R only; Metal readback fills unused GBA with one. Composite asserts all RGBA. Every returned component finite. Independent analytic rays/HG and bilinear radial oracle; no RenderGraph, XR or performance claim.";
    }

    // ════════════════════════════════════════════════════════════
    //  Entry — real shader variants and disposable GPU fixtures
    // ════════════════════════════════════════════════════════════
    public static object Main(string[] args)
    {
        var report = new Report { graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName };
        var cases = new List<CaseResult>();
        var globals = new SavedGlobals();
        Material material = null;
        try
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Mine/Shaders/PostProcess/MaskRBR/MaskRBR.shader");
            Require(shader != null && shader.isSupported && shader.passCount == 3,
                "Production three-pass shader is missing or unsupported.");
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                Require(message.severity.ToString() != "Error", message.message);
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Require(material.FindPass("Mask") == 0 && material.FindPass("Radial") == 1 && material.FindPass("Composite") == 2,
                "Production pass indices do not match Mask/Radial/Composite.");
            using (var f = new Fixture(129, 65))
            {
                CheckMasks(material, f, cases);
                CheckKernel(material, f, cases);
                CheckPhaseAndComposite(material, f, cases);
            }
            using (var f = new Fixture(65, 129)) CheckKernelAspect(material, f, cases);
        }
        catch (Exception e) { report.error = e.ToString(); }
        finally
        {
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            globals.Restore();
            report.globalsRestored = globals.Matches();
        }
        foreach (var c in cases) if (!c.passed) report.failures++;
        report.caseCount = cases.Count;
        report.passed = report.error == null && report.failures == 0 && report.caseCount > 0 && report.globalsRestored;
        string summary = JsonUtility.ToJson(report);
        var json = new StringBuilder(summary.Substring(0, summary.Length - 1)).Append(",\"cases\":[");
        for (int i = 0; i < cases.Count; i++) { if (i > 0) json.Append(','); json.Append(JsonUtility.ToJson(cases[i])); }
        json.Append("]}");
        if (args != null && args.Length > 0 && !string.IsNullOrEmpty(args[0]))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
            File.WriteAllText(args[0], json.ToString());
        }
        return json.ToString();
    }

    // ════════════════════════════════════════════════════════════
    //  Masks and kernels — scalar coverage, constant gain, aspect and boundaries
    // ════════════════════════════════════════════════════════════
    static void CheckMasks(Material m, Fixture f, List<CaseResult> cases)
    {
        f.Set(f.Objects, (x, y) => new Color(x < f.Width / 2 ? 1f : 0f, 0f, 0f, 1f));
        f.SetDepth((x, y) => y >= f.Height / 2 ? FarDepth : 0.5f);
        for (int mode = 0; mode < 3; mode++)
        {
            m.SetFloat("_MaskRBRSourceMode", mode);
            int selectedMode = mode;
            CompareScalar(cases, "Mask.UnionMode" + mode, f.Draw(m, 0, f.Objects), f.Pixels((x, y) =>
            {
                float sky = y >= f.Height / 2 ? 1f : 0f, obj = x < f.Width / 2 ? 1f : 0f;
                return new Color(selectedMode == 0 ? sky : selectedMode == 1 ? obj : Mathf.Max(sky, obj), 0f, 0f, 1f);
            }));
        }
    }

    static void SetParameters(Material m, Fixture f, float length, float falloff = 2f, float g = 0.7f, float angle = 60f, bool available = true)
    {
        m.SetVector("_MaskRBRRadialParameters", new Vector4(length, falloff, 0f, 0f));
        m.SetVector("_MaskRBRPhaseParameters", new Vector4(g, Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Cos(angle * 0.8f * Mathf.Deg2Rad), available ? 1f : 0f));
        m.SetVector("_MaskRBROutputSize", new Vector4(f.Width, f.Height, 0f, 0f));
        m.SetFloat("_MaskRBRIntensity", 0.4f);
        m.SetFloat("_MaskRBRDebugMode", 0f);
    }
    static void Quality(Material m, int quality)
    {
        CoreUtils.SetKeyword(m, "_MASKRBR_QUALITY_LOW", quality == 0);
        CoreUtils.SetKeyword(m, "_MASKRBR_QUALITY_HIGH", quality == 2);
    }
    static void CheckKernel(Material m, Fixture f, List<CaseResult> cases)
    {
        f.LightDirection = Vector3.forward;
        f.Set(f.Mask, (x, y) => Color.white);
        for (int q = 0; q < 3; q++)
        {
            Quality(m, q);
            foreach (float falloff in new[] { 0f, 2f, 8f })
            {
                SetParameters(m, f, 0.4f, falloff);
                CompareScalar(cases, "Kernel.UnitGain.Q" + q + ".F" + falloff, f.Draw(m, 1, f.Mask),
                    f.Pixels((x, y) => new Color(1f, 0f, 0f, 1f)));
            }
            f.Set(f.Mask, (x, y) => Color.clear);
            CompareScalar(cases, "Kernel.Empty.Q" + q, f.Draw(m, 1, f.Mask), f.Pixels((x, y) => new Color(0f, 0f, 0f, 1f)));
            f.Set(f.Mask, (x, y) => Color.white);
        }
        CheckKernelAspect(m, f, cases);
        f.Set(f.Mask, (x, y) => Color.white);
        f.LightDirection = new Vector3(3f, 0.2f, 1f).normalized;
        for (int q = 0; q < 3; q++)
        {
            Quality(m, q); SetParameters(m, f, 1f, 0f);
            Color[] actual = f.Draw(m, 1, f.Mask);
            CompareScalar(cases, "Kernel.ZeroOutside.Q" + q, actual, KernelOracle(f, q, 1f, 0f));
            Difference(cases, "Control.ClampBorderRejected.Q" + q, actual, f.Pixels((x, y) => new Color(1f, 0f, 0f, 1f)));
        }
        f.LightDirection = Vector3.forward;
        f.Set(f.Mask, (x, y) => new Color(((x * 11 + y * 7) % 17) / 16f, 0f, 0f, 1f));
        Quality(m, 1); SetParameters(m, f, 0.4f);
        Color[] fixedTaps = f.Draw(m, 1, f.Mask);
        m.SetVector("_MaskRBRRadialParameters", new Vector4(0.4f, 2f, 1f, 0f));
        Color[] first = f.Draw(m, 1, f.Mask, 1f);
        CompareScalar(cases, "Jitter.TimeIndependent", first, f.Draw(m, 1, f.Mask, 100f));
        Difference(cases, "Control.JitterChangesSamples", first, fixedTaps);
        SetParameters(m, f, 0.4f, available: false);
        CompareScalar(cases, "Focus.Absent", f.Draw(m, 1, f.Mask), f.Pixels((x, y) => new Color(0f, 0f, 0f, 1f)));
        SetParameters(m, f, 0.4f); f.LightDirection = Vector3.back;
        CompareScalar(cases, "Focus.Behind", f.Draw(m, 1, f.Mask), f.Pixels((x, y) => new Color(0f, 0f, 0f, 1f)));
        f.LightDirection = Vector3.forward; f.Orthographic = true;
        CompareScalar(cases, "Focus.Orthographic", f.Draw(m, 1, f.Mask), f.Pixels((x, y) => new Color(0f, 0f, 0f, 1f)));
        f.Orthographic = false;
    }
    static void CheckKernelAspect(Material m, Fixture f, List<CaseResult> cases)
    {
        f.LightDirection = new Vector3(0.35f, 0.2f, 1f).normalized;
        f.Set(f.Mask, (x, y) => new Color((x + y * 0.25f) / (f.Width + f.Height * 0.25f), 0f, 0f, 1f));
        for (int q = 0; q < 3; q++)
        {
            Quality(m, q); SetParameters(m, f, 0.2f);
            CompareScalar(cases, "Kernel.AnalyticRamp." + f.Width + "x" + f.Height + ".Q" + q,
                f.Draw(m, 1, f.Mask), KernelOracle(f, q, 0.2f, 2f));
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Angular composite — independent camera rays/HG and main-light scaling
    // ════════════════════════════════════════════════════════════
    static void CheckPhaseAndComposite(Material m, Fixture f, List<CaseResult> cases)
    {
        f.Set(f.Rays, (x, y) => Color.white);
        f.Set(f.Scene, (x, y) => new Color(0.2f, 0.1f, 0.3f, 0.37f));
        f.LightDirection = Vector3.forward;
        foreach (float g in new[] { 0f, 0.7f, 0.95f })
        {
            SetParameters(m, f, 0f, g: g, angle: 25f); m.SetFloat("_MaskRBRDebugMode", 3f);
            Color[] actual = f.Draw(m, 2, f.Scene);
            Compare(cases, "Phase.HGAndCone.G" + g, actual, PhaseOracle(f, g, 25f));
            int center = (f.Height / 2) * f.Width + f.Width / 2;
            Compare(cases, "Phase.PeakOne.G" + g, new[] { actual[center] }, new[] { new Color(1f, 1f, 1f, 0.37f) });
        }
        f.LightDirection = new Vector3(0.35f, 0.2f, 1f).normalized;
        SetParameters(m, f, 0f, g: 0.7f, angle: 30f); m.SetFloat("_MaskRBRDebugMode", 3f);
        Color[] offCenter = f.Draw(m, 2, f.Scene);
        Compare(cases, "Phase.OffCenterViewSign", offCenter, PhaseOracle(f, 0.7f, 30f));
        Vector3 originalDirection = f.LightDirection;
        f.LightDirection = new Vector3(originalDirection.x, -originalDirection.y, originalDirection.z);
        Difference(cases, "Control.YMirrorRejected", offCenter, PhaseOracle(f, 0.7f, 30f));
        f.LightDirection = originalDirection;
        f.Set(f.Scene, (x, y) => new Color(20f, 5f, 0f, 0.37f));
        Compare(cases, "Phase.SceneRGBIndependent", f.Draw(m, 2, f.Scene), offCenter);
        f.Set(f.Scene, (x, y) => new Color(0.2f, 0.1f, 0.3f, 0.37f));
        m.SetFloat("_MaskRBRDebugMode", 0f);
        foreach (float intensity in new[] { 0f, 0.4f, 0.8f })
        {
            m.SetFloat("_MaskRBRIntensity", intensity);
            f.LightColor = new Vector4(2f, 0.5f, 1.5f, 1f);
            Compare(cases, "Composite.LinearStrengthAndAlpha." + intensity, f.Draw(m, 2, f.Scene),
                f.Pixels((x, y) =>
                {
                    float emission = Envelope(f.Ray(x, y), f.LightDirection, 0.7f, 30f) * intensity;
                    return new Color(0.2f + 2f * emission, 0.1f + 0.5f * emission, 0.3f + 1.5f * emission, 0.37f);
                }));
        }
        f.LightColor = new Vector4(4f, 1f, 3f, 1f); m.SetFloat("_MaskRBRIntensity", 0.4f);
        Compare(cases, "Composite.DoubleLightColor", f.Draw(m, 2, f.Scene), f.Pixels((x, y) =>
        {
            float e = Envelope(f.Ray(x, y), f.LightDirection, 0.7f, 30f) * 0.8f;
            return new Color(0.2f + 2f * e, 0.1f + 0.5f * e, 0.3f + 1.5f * e, 0.37f);
        }));
        SetParameters(m, f, 0f, available: false); m.SetFloat("_MaskRBRDebugMode", 3f);
        Compare(cases, "Phase.AbsentLight", f.Draw(m, 2, f.Scene), f.Pixels((x, y) => new Color(0f, 0f, 0f, 0.37f)));
        m.SetFloat("_MaskRBRDebugMode", 1f); f.Set(f.Mask, (x, y) => new Color(0.5f, 0f, 0f, 1f));
        Compare(cases, "Debug.MaskWithoutLight", f.Draw(m, 2, f.Scene), f.Pixels((x, y) => new Color(0.5f, 0.5f, 0.5f, 0.37f)));
    }

    // ════════════════════════════════════════════════════════════
    //  CPU oracles — camera geometry and double-precision normalized phase
    // ════════════════════════════════════════════════════════════
    static float Envelope(Vector3 ray, Vector3 light, float g, float angle)
    {
        double mu = Math.Max(-1.0, Math.Min(1.0, Vector3.Dot(ray, light.normalized)));
        double outer = Math.Cos(angle * Math.PI / 180), inner = Math.Cos(angle * 0.8 * Math.PI / 180);
        double t = Math.Max(0, Math.Min(1, (mu - outer) / (inner - outer)));
        return (float)(Math.Pow(((1 - g) * (1 - g)) / (1 + g * g - 2 * g * mu), 1.5) * t * t * (3 - 2 * t));
    }
    static Color[] PhaseOracle(Fixture f, float g, float angle)
    {
        return f.Pixels((x, y) => { float v = Envelope(f.Ray(x, y), f.LightDirection, g, angle); return new Color(v, v, v, 0.37f); });
    }
    static Color[] KernelOracle(Fixture f, int quality, float length, float falloff)
    {
        int taps = quality == 0 ? 16 : quality == 2 ? 48 : 24;
        float tan = Mathf.Tan(30f * Mathf.Deg2Rad), aspect = (float)f.Width / f.Height;
        Vector3 light = f.LightDirection.normalized;
        Vector2 focus = new Vector2(0.5f + light.x / light.z / (2f * tan * aspect), 0.5f + light.y / light.z / (2f * tan));
        float fade = Mathf.Clamp01(light.z / 0.1f);
        return f.Pixels((x, y) =>
        {
            Vector2 uv = new Vector2((x + 0.5f) / f.Width, (y + 0.5f) / f.Height);
            Vector2 delta = focus - uv;
            float distance = new Vector2(delta.x * aspect, delta.y).magnitude;
            Vector2 span = delta * Mathf.Clamp01(length / Mathf.Max(distance, 0.000001f));
            double sum = 0, weights = 0;
            for (int i = 0; i < taps; i++)
            {
                float t = (float)i / (taps - 1); double w = Math.Pow(2, -falloff * t);
                sum += f.Sample(uv + span * t) * w; weights += w;
            }
            return new Color((float)(sum / weights) * fade, 0f, 0f, 1f);
        });
    }
    static void CompareScalar(List<CaseResult> cases, string name, Color[] actual, Color[] expected)
    {
        Compare(cases, name, actual, expected, 1);
    }
    static void Compare(List<CaseResult> cases, string name, Color[] actual, Color[] expected, int channels = 4)
    {
        Require(actual.Length == expected.Length, "Dimensions differ: " + name);
        var c = new CaseResult { name = name, pixelCount = actual.Length };
        for (int i = 0; i < actual.Length; i++)
        {
            bool mismatch = false;
            for (int channel = 0; channel < 4; channel++)
            {
                c.finite &= Finite(actual[i][channel]);
                if (channel >= channels) continue;
                float error = Mathf.Abs(actual[i][channel] - expected[i][channel]) / (1 + Mathf.Abs(expected[i][channel]));
                c.maxNormalizedError = Mathf.Max(c.maxNormalizedError, error); mismatch |= error > Tolerance;
            }
            if (mismatch) c.mismatchPixels++;
        }
        c.passed = c.finite && c.mismatchPixels == 0; cases.Add(c);
        if (!c.passed && actual.Length > 0)
        {
            c.sampleActual = new[] { actual[0].r, actual[0].g, actual[0].b, actual[0].a };
            c.sampleExpected = new[] { expected[0].r, expected[0].g, expected[0].b, expected[0].a };
        }
    }
    static void Difference(List<CaseResult> cases, string name, Color[] actual, Color[] rejected)
    {
        Compare(cases, name, actual, rejected, 1);
        CaseResult c = cases[cases.Count - 1]; c.passed = c.finite && c.maxNormalizedError > 0.001f;
    }
    static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static float FarDepth { get { return SystemInfo.usesReversedZBuffer ? 0f : 1f; } }

    // ════════════════════════════════════════════════════════════
    //  Fixture — native matrices after target binding and float readback
    // ════════════════════════════════════════════════════════════
    sealed class Fixture : IDisposable
    {
        public readonly int Width, Height;
        public readonly Texture2D Scene, Mask, Objects, Rays, Depth;
        public Vector3 LightDirection = Vector3.forward;
        public Vector4 LightColor = Vector4.one;
        public bool Orthographic;
        readonly List<Texture2D> owned = new List<Texture2D>();
        readonly CommandBuffer command = new CommandBuffer();
        Color[] maskPixels;
        public Fixture(int width, int height)
        {
            Width = width; Height = height;
            Scene = Make(TextureFormat.RGBAFloat); Mask = Make(TextureFormat.RGBAFloat);
            Objects = Make(TextureFormat.RGBAFloat); Rays = Make(TextureFormat.RGBAFloat); Depth = Make(TextureFormat.RFloat);
            Set(Scene, (x, y) => Color.white); Set(Mask, (x, y) => Color.white);
            Set(Objects, (x, y) => Color.clear); Set(Rays, (x, y) => Color.white); SetDepth((x, y) => FarDepth);
        }
        Texture2D Make(TextureFormat format)
        {
            var t = new Texture2D(Width, Height, format, false, true) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            owned.Add(t); return t;
        }
        public Color[] Pixels(Func<int, int, Color> fn)
        {
            var p = new Color[Width * Height];
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) p[y * Width + x] = fn(x, y);
            return p;
        }
        public void Set(Texture2D t, Func<int, int, Color> fn)
        {
            Color[] pixels = Pixels(fn);
            if (t == Mask) maskPixels = pixels;
            t.SetPixels(pixels); t.Apply(false, false);
        }
        public void SetDepth(Func<int, int, float> fn)
        {
            var p = new float[Width * Height];
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) p[y * Width + x] = fn(x, y);
            Depth.SetPixelData(p, 0); Depth.Apply(false, false);
        }
        public Vector3 Ray(int x, int y)
        {
            float tan = Mathf.Tan(30f * Mathf.Deg2Rad);
            return new Vector3((2f * (x + 0.5f) / Width - 1) * tan * Width / Height,
                (2f * (y + 0.5f) / Height - 1) * tan, 1f).normalized;
        }
        public float Sample(Vector2 uv)
        {
            if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) return 0;
            float x = uv.x * Width - 0.5f, y = uv.y * Height - 0.5f;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            int x0 = Mathf.Clamp(ix, 0, Width - 1), x1 = Mathf.Clamp(ix + 1, 0, Width - 1);
            int y0 = Mathf.Clamp(iy, 0, Height - 1), y1 = Mathf.Clamp(iy + 1, 0, Height - 1);
            return Mathf.Lerp(Mathf.Lerp(maskPixels[y0 * Width + x0].r, maskPixels[y0 * Width + x1].r, x - ix),
                Mathf.Lerp(maskPixels[y1 * Width + x0].r, maskPixels[y1 * Width + x1].r, x - ix), y - iy);
        }
        public Color[] Draw(Material material, int pass, Texture2D source, float time = 1f)
        {
            var previous = RenderTexture.active;
            RenderTexture target = null; Texture2D readback = null; RTHandle handle = null;
            try
            {
                var format = pass == 0 ? RenderTextureFormat.R8 : pass == 1 ? RenderTextureFormat.RHalf : RenderTextureFormat.ARGBHalf;
                target = new RenderTexture(Width, Height, 0, format, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
                Require(target.Create(), "Required production render format unavailable: " + format);
                readback = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
                handle = RTHandles.Alloc(source); command.Clear();
                command.SetGlobalVector("_Time", new Vector4(time / 20, time, time * 2, time * 3));
                command.SetGlobalVector("_TimeParameters", new Vector4(time, Mathf.Sin(time), Mathf.Cos(time), 0));
                command.SetGlobalVector("_RTHandleScale", Vector4.one);
                command.SetGlobalVector("unity_OrthoParams", new Vector4(1, 1, 0, Orthographic ? 1 : 0));
                command.SetGlobalVector("_MainLightPosition", new Vector4(LightDirection.x, LightDirection.y, LightDirection.z, 0));
                command.SetGlobalVector("_MainLightColor", LightColor);
                command.SetGlobalVector("_WorldSpaceCameraPos", Vector4.zero);
                command.SetGlobalVector("_CameraDepthTexture_TexelSize", new Vector4(1f / Width, 1f / Height, Width, Height));
                Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -1));
                Matrix4x4 cpuP = Orthographic ? Matrix4x4.Ortho(-1, 1, -1, 1, 0.3f, 250f)
                    : Matrix4x4.Perspective(60f, (float)Width / Height, 0.3f, 250f);
                Matrix4x4 gpuP = GL.GetGPUProjectionMatrix(cpuP, true);
                command.SetGlobalTexture("_CameraDepthTexture", Depth);
                command.SetGlobalTexture("_MaskRBRObjectMaskTexture", Objects);
                command.SetGlobalTexture("_MaskRBRMaskTexture", Mask);
                command.SetGlobalTexture("_MaskRBRRayTexture", Rays);
                command.SetRenderTarget(target); command.SetViewProjectionMatrices(view, cpuP);
                command.SetGlobalMatrix("unity_MatrixInvVP", (gpuP * view).inverse);
                command.SetGlobalMatrix("unity_MatrixInvV", view.inverse);
                command.SetGlobalVector("_ProjectionParams", new Vector4(SystemInfo.graphicsUVStartsAtTop ? -1 : 1, 0.3f, 250f, 1f / 250f));
                command.SetViewport(new Rect(0, 0, Width, Height)); command.ClearRenderTarget(false, true, Color.magenta);
                Blitter.BlitTexture(command, handle, new Vector4(1, 1, 0, 0), material, pass);
                Graphics.ExecuteCommandBuffer(command); RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false); readback.Apply(false, false);
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                if (handle != null) handle.Release();
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            }
        }
        public void Dispose() { command.Release(); foreach (var t in owned) UnityEngine.Object.DestroyImmediate(t); }
    }

    sealed class SavedGlobals
    {
        readonly string[] vectors = { "_Time", "_TimeParameters", "_RTHandleScale", "unity_OrthoParams", "_MainLightPosition", "_MainLightColor", "_WorldSpaceCameraPos", "_ProjectionParams", "_CameraDepthTexture_TexelSize" };
        readonly string[] matrices = { "unity_MatrixV", "unity_MatrixVP", "glstate_matrix_projection", "unity_MatrixInvVP", "unity_MatrixInvV" };
        readonly string[] textures = { "_CameraDepthTexture", "_MaskRBRObjectMaskTexture", "_MaskRBRMaskTexture", "_MaskRBRRayTexture" };
        readonly Vector4[] v; readonly Matrix4x4[] m; readonly Texture[] t;
        public SavedGlobals()
        {
            v = new Vector4[vectors.Length]; m = new Matrix4x4[matrices.Length]; t = new Texture[textures.Length];
            for (int i = 0; i < v.Length; i++) v[i] = Shader.GetGlobalVector(vectors[i]);
            for (int i = 0; i < m.Length; i++) m[i] = Shader.GetGlobalMatrix(matrices[i]);
            for (int i = 0; i < t.Length; i++) t[i] = Shader.GetGlobalTexture(textures[i]);
        }
        public void Restore()
        {
            using (var cmd = new CommandBuffer()) { cmd.SetViewProjectionMatrices(m[0], m[2]); Graphics.ExecuteCommandBuffer(cmd); }
            for (int i = 0; i < v.Length; i++) Shader.SetGlobalVector(vectors[i], v[i]);
            for (int i = 0; i < m.Length; i++) Shader.SetGlobalMatrix(matrices[i], m[i]);
            for (int i = 0; i < t.Length; i++) Shader.SetGlobalTexture(textures[i], t[i]);
        }
        public bool Matches()
        {
            for (int i = 0; i < v.Length; i++) if (Shader.GetGlobalVector(vectors[i]) != v[i]) return false;
            for (int i = 0; i < m.Length; i++) if (Shader.GetGlobalMatrix(matrices[i]) != m[i]) return false;
            for (int i = 0; i < t.Length; i++) if (Shader.GetGlobalTexture(textures[i]) != t[i]) return false;
            return true;
        }
    }
}
