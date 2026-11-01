using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Mine.HDRMaps;
using Object = UnityEngine.Object;

// unityctl script execute <this-file> <output-directory>
// The oracle is local mathematical code, never the production mapping functions.
// All textures/materials are nonpersistent. No scene, importer or asset is changed.
public class Script
{
    [Serializable] public sealed class Report
    {
        public string suite = "HDRMapChecks", graphicsDevice;
        public int passed, failed, oraclePixels, roundtripPixels, cubePixels, negativeControls;
        public List<string> details = new List<string>();
    }
    private static readonly List<Object> Owned = new List<Object>();
    private static readonly Color HDR = new Color(4f, 2f, 0.5f, 0.25f);
    private const float SourceAlpha = 0.375f;

    public static object Main(string[] args)
    {
        return Run(args != null && args.Length > 0 ? args[0] : null);
    }

    public static string Run(string outputDirectory)
    {
        var report = new Report { graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName };
        var errors = new List<string>();
        RenderTexture originalActive = RenderTexture.active;
        bool originalSRGBWrite = GL.sRGBWrite;
        Application.LogCallback listener = (message, trace, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(type + ": " + message);
        };
        Application.logMessageReceived += listener;
        try
        {
            Check(report, "device.real_gpu_and_hdr_formats", () =>
            {
                Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Real GPU required.");
                Require(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf), "ARGBHalf support.");
                Require(SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf), "RGBAHalf support.");
            });
            Texture2D source = DirectionSource(512);
            Color[] originalSource = source.GetPixels();
            Check(report, "source.independent_six_axis_contract", () =>
            {
                Near(new Color(.5f, .5f, 1f, SourceAlpha), ColorOf(Vector3.forward), 0f, "+Z");
                Near(new Color(.5f, 1f, .5f, SourceAlpha), ColorOf(Vector3.up), 0f, "+Y");
                Near(new Color(1f, .5f, .5f, SourceAlpha), ColorOf(Vector3.right), 0f, "+X");
            });

            for (int pi = 0; pi < 4; pi++)
            {
                HDRMapProjection projection = (HDRMapProjection)pi;
                foreach (int resolution in new[] { 65, 128 })
                {
                    Check(report, "direct." + projection + ".resolution=" + resolution, () =>
                    {
                        Texture2D output = Keep(HDRMapConverter.Convert(source, HDRMapProjection.Equirectangular,
                            projection, resolution));
                        CheckMetadata(output, projection, resolution);
                        CompareOracle(output, projection, HDRMapCoverage.Panorama, .008f, report, false);
                        if ((projection == HDRMapProjection.Octahedral || projection == HDRMapProjection.EqualAreaOctahedral)
                            && resolution % 2 != 0)
                            Near(ColorOf(Vector3.up), output.GetPixel(resolution / 2, resolution / 2), .002f, "odd central texel is +Y");
                    });
                }
                Check(report, "coverage.interior." + projection, () =>
                {
                    Texture2D output = Keep(HDRMapConverter.Convert(source, HDRMapProjection.Equirectangular,
                        projection, 65, HDRMapCoverage.Interior));
                    CompareOracle(output, projection, HDRMapCoverage.Interior, .008f, report, false);
                });
                Check(report, "roundtrip." + projection + ".to_equirectangular", () =>
                {
                    Texture2D encoded = Keep(HDRMapConverter.Convert(source, HDRMapProjection.Equirectangular, projection, 129));
                    Texture2D restored = Keep(HDRMapConverter.Convert(encoded, projection,
                        HDRMapProjection.Equirectangular, 128));
                    CompareOracle(restored, HDRMapProjection.Equirectangular, HDRMapCoverage.Panorama,
                        projection == HDRMapProjection.Hemisphere ? .035f : .025f, report,
                        projection == HDRMapProjection.Hemisphere);
                });
            }

            Check(report, "source.unreadable_linear_hdr_and_alpha_survive_all_projections", () =>
            {
                Texture2D unreadable = ConstantSource(32, HDR);
                unreadable.Apply(false, true);
                Require(!unreadable.isReadable, "fixture must actually be unreadable");
                for (int pi = 0; pi < 4; pi++)
                {
                    HDRMapProjection projection = (HDRMapProjection)pi;
                    Texture2D output = Keep(HDRMapConverter.Convert(unreadable, HDRMapProjection.Equirectangular, projection, 33));
                    for (int y = 0; y < output.height; y++)
                    for (int x = 0; x < output.width; x++)
                    {
                        bool valid;
                        Decode((x + .5f) / output.width, (y + .5f) / output.height, projection, out valid);
                        Near(valid ? HDR : Color.clear, output.GetPixel(x, y), .003f, "HDR alpha pixel " + projection + " " + x + "," + y);
                    }
                }
            });

            Check(report, "cube.input_converts_six_distinct_hdr_faces", () => CheckCube(report));
            Check(report, "negative_control.wrong_projection_is_visibly_wrong", () =>
            {
                Texture2D oct = Keep(HDRMapConverter.Convert(source, HDRMapProjection.Equirectangular, HDRMapProjection.Octahedral, 128));
                Texture2D wrong = Keep(HDRMapConverter.Convert(oct, HDRMapProjection.Equirectangular, HDRMapProjection.Equirectangular, 65));
                float sum = 0f;
                Color[] actual = wrong.GetPixels();
                for (int y = 0; y < wrong.height; y++)
                for (int x = 0; x < wrong.width; x++)
                {
                    bool valid;
                    Vector3 d = Decode((x + .5f) / wrong.width, (y + .5f) / wrong.height, HDRMapProjection.Equirectangular, out valid);
                    sum += RGBError(ColorOf(d), actual[y * wrong.width + x]);
                }
                float mean = sum / actual.Length;
                Require(mean > .15f, "Wrong-encoding control must differ substantially; MAE=" + mean);
                report.details.Add("metric.wrong_encoding_mean_rgb_error=" + mean.ToString("R"));
                report.negativeControls++;
            });

            CheckArguments(report, source);
            Check(report, "source.original_pixels_and_sampler_unchanged", () =>
            {
                Color[] pixels = source.GetPixels();
                Require(pixels.Length == originalSource.Length, "source size unchanged");
                for (int i = 0; i < pixels.Length; i++) Near(originalSource[i], pixels[i], 0f, "source pixel " + i);
                Require(source.wrapModeU == TextureWrapMode.Repeat && source.wrapModeV == TextureWrapMode.Clamp,
                    "source sampler unchanged");
            });
            Check(report, "shader.no_compiler_errors", () =>
            {
                Shader shader = Shader.Find("Hidden/Mine/HDRMapConvert");
                Require(shader != null && shader.isSupported, "conversion shader supported");
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Require(message.severity.ToString() != "Error", message.message + " @ " + message.file + ":" + message.line);
            });
        }
        catch (Exception exception) { Failure(report, "fixture.unexpected_exception", exception); }
        finally
        {
            for (int i = Owned.Count - 1; i >= 0; i--)
                if (Owned[i] != null) Object.DestroyImmediate(Owned[i]);
            Owned.Clear();
            Application.logMessageReceived -= listener;
        }
        Check(report, "cleanup.render_state_restored", () =>
        {
            Require(RenderTexture.active == originalActive, "RenderTexture.active restored on all paths.");
            Require(GL.sRGBWrite == originalSRGBWrite, "GL.sRGBWrite restored on all paths.");
        });
        Check(report, "unity.no_unexpected_error_logs", () => Require(errors.Count == 0, string.Join(" | ", errors.ToArray())));
        Check(report, "suite.required_work_executed", () =>
        {
            Require(report.oraclePixels > 75000, "All projection pixels were compared.");
            Require(report.roundtripPixels > 25000, "All roundtrip layouts were compared.");
            Require(report.cubePixels > 10000, "Cubemap checks executed.");
            Require(report.negativeControls == 1, "Wrong-encoding GPU control executed.");
        });
        string json = JsonUtility.ToJson(report, true);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "conversion-report.json"), json);
        }
        if (report.failed != 0) throw new InvalidOperationException(json);
        return json;
    }

    private static void CheckMetadata(Texture2D output, HDRMapProjection projection, int resolution)
    {
        Require(output.width == (projection == HDRMapProjection.Equirectangular ? resolution * 2 : resolution)
            && output.height == resolution, "exact requested output dimensions");
        Require(output.format == TextureFormat.RGBAHalf, "RGBAHalf output");
        Require(output.mipmapCount == 1, "mip0 only; no reflection prefiltering");
        Require(output.isReadable, "caller can save returned output");
        Require(!UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(output.graphicsFormat), "linear output");
        Require(output.wrapModeV == TextureWrapMode.Clamp, "V clamp");
        Require(output.wrapModeU == (projection == HDRMapProjection.Equirectangular ? TextureWrapMode.Repeat : TextureWrapMode.Clamp), "projection U wrap");
    }

    private static void CompareOracle(Texture2D output, HDRMapProjection projection, HDRMapCoverage coverage,
        float tolerance, Report report, bool hemisphereSource)
    {
        Color[] pixels = output.GetPixels();
        float maximum = 0f;
        int compared = 0;
        for (int y = 0; y < output.height; y++)
        for (int x = 0; x < output.width; x++)
        {
            bool valid;
            Vector3 d = Decode((x + .5f) / output.width, (y + .5f) / output.height, projection, out valid);
            if (coverage == HDRMapCoverage.Interior && Mathf.Abs(d.z) < 1e-5f) continue;
            if (hemisphereSource && d.z >= 0f && d.z < .2f) continue; // Orthographic disk boundary is singular, not smooth.
            bool covered = valid && (coverage == HDRMapCoverage.Panorama || d.z >= 0f)
                && (!hemisphereSource || d.z >= 0f);
            Color expected = covered ? ColorOf(d) : Color.clear;
            Color actual = pixels[y * output.width + x];
            float error = RGBAError(expected, actual);
            maximum = Mathf.Max(maximum, error);
            if (error > tolerance)
                throw new InvalidOperationException("Oracle mismatch at " + projection + " pixel=" + x + "," + y
                    + " direction=" + d + " expected=" + expected + " actual=" + actual + " error=" + error + " tolerance=" + tolerance);
            compared++;
        }
        if (hemisphereSource || tolerance > .01f) report.roundtripPixels += compared;
        else report.oraclePixels += compared;
        report.details.Add("metric." + projection + "." + coverage + "." + output.width + "x" + output.height
            + ".max_rgba_error=" + maximum.ToString("R") + ".pixels=" + compared);
    }

    // Independently expressed geometric construction. Y is the octahedral pole;
    // equal-area folds a diamond then maps radial disk area linearly to 1-|Y|.
    private static Vector3 Decode(float u, float v, HDRMapProjection projection, out bool valid)
    {
        valid = true;
        double a = u * 2.0 - 1.0, b = v * 2.0 - 1.0;
        if (projection == HDRMapProjection.Equirectangular)
        {
            double latitude = (v - .5) * Math.PI, longitude = (u - .5) * Math.PI * 2;
            return new Vector3((float)(Math.Sin(longitude) * Math.Cos(latitude)), (float)Math.Sin(latitude),
                (float)(Math.Cos(longitude) * Math.Cos(latitude)));
        }
        if (projection == HDRMapProjection.Hemisphere)
        {
            double squared = a * a + b * b;
            valid = squared <= 1.0;
            return new Vector3((float)a, (float)b, (float)Math.Sqrt(Math.Max(0.0, 1.0 - squared)));
        }
        double diamond = 1.0 - Math.Abs(a) - Math.Abs(b);
        if (projection == HDRMapProjection.Octahedral)
        {
            double x = a, z = b;
            if (diamond < 0.0)
            {
                x = (1.0 - Math.Abs(b)) * (a < 0 ? -1 : 1);
                z = (1.0 - Math.Abs(a)) * (b < 0 ? -1 : 1);
            }
            return new Vector3((float)x, (float)diamond, (float)z).normalized;
        }
        double xAbsolute = Math.Abs(a), zAbsolute = Math.Abs(b);
        if (diamond < 0.0)
        {
            xAbsolute = 1.0 - Math.Abs(b);
            zAbsolute = 1.0 - Math.Abs(a);
        }
        double radius = xAbsolute + zAbsolute;
        double angle = radius == 0 ? 0 : zAbsolute / radius * Math.PI * .5;
        double y = 1.0 - radius * radius;
        double horizontal = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
        return new Vector3((float)(horizontal * Math.Cos(angle) * (a < 0 ? -1 : 1)),
            (float)(diamond < 0 ? -y : y), (float)(horizontal * Math.Sin(angle) * (b < 0 ? -1 : 1)));
    }

    private static void CheckCube(Report report)
    {
        var cube = Keep(new Cubemap(64, TextureFormat.RGBAHalf, false));
        Color[] colors = { new Color(4, .1f, .2f, .3f), new Color(.1f, 3, .2f, .4f),
            new Color(.1f, .2f, 2, .5f), new Color(2, 2, .1f, .6f),
            new Color(2, .1f, 2, .7f), new Color(.1f, 2, 2, .8f) };
        for (int face = 0; face < 6; face++)
        {
            var pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = colors[face];
            cube.SetPixels(pixels, (CubemapFace)face);
        }
        cube.Apply(false, false);
        Texture2D output = Keep(HDRMapConverter.ConvertFromCubemap(cube, HDRMapProjection.Equirectangular, 129));
        for (int y = 0; y < output.height; y++)
        for (int x = 0; x < output.width; x++)
        {
            bool valid;
            Vector3 d = Decode((x + .5f) / output.width, (y + .5f) / output.height, HDRMapProjection.Equirectangular, out valid);
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
            float largest = Mathf.Max(ax, Mathf.Max(ay, az));
            float second = ax + ay + az - largest - Mathf.Min(ax, Mathf.Min(ay, az));
            if (largest - second < .10f) continue; // Hardware cubemap face-edge filtering is intentionally excluded.
            int face = ax > ay && ax > az ? (d.x >= 0 ? 0 : 1) : ay > az ? (d.y >= 0 ? 2 : 3) : (d.z >= 0 ? 4 : 5);
            Near(colors[face], output.GetPixel(x, y), .006f, "cube face " + face + " at " + x + "," + y);
            report.cubePixels++;
        }
    }

    private static void CheckArguments(Report report, Texture2D source)
    {
        Check(report, "arguments.null_source", () => Reject(() => HDRMapConverter.Convert(null, 0, 0, 32)));
        Check(report, "arguments.null_cube", () => Reject(() => HDRMapConverter.ConvertFromCubemap(null, 0, 32)));
        Check(report, "arguments.texture2d_is_not_cube", () => Reject(() => HDRMapConverter.ConvertFromCubemap(source, 0, 32)));
        Check(report, "arguments.cube_is_not_texture2d", () =>
        {
            Cubemap cube = Keep(new Cubemap(16, TextureFormat.RGBAHalf, false));
            Reject(() => HDRMapConverter.Convert(cube, 0, 0, 32));
        });
        foreach (int resolution in new[] { -1, 0, 15, 4097, int.MaxValue })
            Check(report, "arguments.invalid_resolution=" + resolution,
                () => Reject(() => HDRMapConverter.Convert(source, 0, 0, resolution)));
        Check(report, "arguments.invalid_source_projection", () => Reject(() => HDRMapConverter.Convert(source, (HDRMapProjection)99, 0, 32)));
        Check(report, "arguments.invalid_target_projection", () => Reject(() => HDRMapConverter.Convert(source, 0, (HDRMapProjection)(-1), 32)));
        Check(report, "arguments.invalid_coverage", () => Reject(() => HDRMapConverter.Convert(source, 0, 0, 32, (HDRMapCoverage)99)));
    }

    private static Texture2D DirectionSource(int height)
    {
        Texture2D source = Keep(new Texture2D(height * 2, height, TextureFormat.RGBAFloat, false, true));
        var pixels = new Color[source.width * source.height];
        for (int y = 0; y < source.height; y++)
        for (int x = 0; x < source.width; x++)
        {
            bool valid;
            pixels[y * source.width + x] = ColorOf(Decode((x + .5f) / source.width, (y + .5f) / source.height,
                HDRMapProjection.Equirectangular, out valid));
        }
        source.SetPixels(pixels);
        source.Apply(false, false);
        source.wrapModeU = TextureWrapMode.Repeat;
        source.wrapModeV = TextureWrapMode.Clamp;
        return source;
    }
    private static Texture2D ConstantSource(int height, Color color)
    {
        Texture2D source = Keep(new Texture2D(height * 2, height, TextureFormat.RGBAHalf, false, true));
        var pixels = new Color[source.width * source.height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        source.SetPixels(pixels);
        source.Apply(false, false);
        return source;
    }
    private static Color ColorOf(Vector3 d) { return new Color(d.x * .5f + .5f, d.y * .5f + .5f, d.z * .5f + .5f, SourceAlpha); }
    private static T Keep<T>(T value) where T : Object { value.hideFlags = HideFlags.HideAndDontSave; Owned.Add(value); return value; }
    private static float RGBError(Color a, Color b) { return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b))); }
    private static float RGBAError(Color a, Color b) { return Mathf.Max(RGBError(a, b), Mathf.Abs(a.a - b.a)); }
    private static void Near(Color expected, Color actual, float tolerance, string message)
    {
        Require(!float.IsNaN(actual.r) && !float.IsNaN(actual.g) && !float.IsNaN(actual.b) && !float.IsNaN(actual.a), "nonfinite " + message);
        Require(RGBAError(expected, actual) <= tolerance, message + " expected=" + expected + " actual=" + actual + " error=" + RGBAError(expected, actual));
    }
    private static void Reject(Func<Texture2D> operation)
    {
        Texture2D result = null;
        bool rejected = false;
        try { result = operation(); }
        catch (ArgumentException) { rejected = true; }
        finally { if (result != null) Object.DestroyImmediate(result); }
        Require(rejected, "Invalid API arguments must throw ArgumentException; silently clamping is invalid.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(Report report, string name, Action operation)
    {
        try { operation(); report.passed++; report.details.Add("PASS " + name); }
        catch (Exception exception) { Failure(report, name, exception); }
    }
    private static void Failure(Report report, string name, Exception exception)
    {
        report.failed++;
        report.details.Add("FAIL " + name + ": " + exception.Message);
    }
}
