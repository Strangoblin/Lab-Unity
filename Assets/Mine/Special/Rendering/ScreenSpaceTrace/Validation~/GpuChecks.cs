// ═══════════════════════════════════════════════════════════════
//  GpuChecks — SSR-framework samples, RAY regression and pyramid resources.
// ═══════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEditor;
using Mine.ScreenSpaceTrace;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

public class Script
{
    public static object Main() => TraceGpuChecks.Run();
    public static object NegativeControl() => TraceGpuChecks.NegativeControl();
}

public static class TraceGpuChecks
{
    private const string Root = "Assets/Mine/Special/Rendering/ScreenSpaceTrace/";
    private const float Near = 0.1f;
    private const float Far = 20f;
    private const float Thickness = 0.15f;
    private const int Samples = 96;
    private static readonly Vector4 Mapping = new(1, 1, 0, 0);
    private static readonly List<string> Cases = new();
    private static int _assertions;
    private static ComputeShader _trace;
    private static ComputeShader _build;
    private static Shader _consumer;
    private static bool _injectFault;
    private static string _output;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public Vector4 Origin;
        public Vector4 Direction;
        public Input(Vector3 origin, Vector3 direction, float maximum, float minimum = 0)
        {
            direction.Normalize();
            Origin = new Vector4(origin.x, origin.y, origin.z, minimum);
            Direction = new Vector4(direction.x, direction.y, direction.z, maximum);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Output
    {
        public Vector4 Geometry;
        public Vector4 Diagnostic;
    }

    public static object Run()
    {
        _assertions = 0;
        Cases.Clear();
        string output = "tmp/sessions/" + DateTime.Now.ToString("yyyy-MM-dd")
            + "-screen-trace-checks-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "/output/results.json";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        _output = output;
        try
        {
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(Root + "RuntimeFixture/TraceChecks.compute", ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(Root + "RuntimeFixture/TraceConsumers.shader", ImportAssetOptions.ForceUpdate);
            _trace = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "RuntimeFixture/TraceChecks.compute");
            _build = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "ScreenSpaceDepthPyramid.compute");
            _consumer = AssetDatabase.LoadAssetAtPath<Shader>(Root + "RuntimeFixture/TraceConsumers.shader");
            Require(_trace != null && _build != null && _consumer != null, "Missing imported GPU fixtures");
            _trace.SetInt("_TestInjectFault", _injectFault ? 1 : 0);
            foreach (ComputeShader compute in new[] { _trace, _build })
                foreach (var message in ShaderUtil.GetComputeShaderMessages(compute))
                    Require(message.severity.ToString() != "Error", compute.name + ": " + message.message);
            foreach (var message in ShaderUtil.GetShaderMessages(_consumer))
                Require(message.severity.ToString() != "Error", _consumer.name + ": " + message.message);
            foreach (Vector2Int size in new[] { new Vector2Int(13, 9), new(64, 32), new(65, 37), new(1, 17), new(17, 1), new(1, 1) })
                for (int representation = 0; representation < 5; representation++)
                    CheckCase(size.x, size.y, representation, false);
            CheckCase(13, 9, 0, true);
            CheckMapping();
            CheckDepthFields();
            CheckWorldSpaceDistance();
            CheckConsumers();
            CheckGraph();
            string json = "{\"passed\":true,\"device\":\"" + SystemInfo.graphicsDeviceType
                + "\",\"assertions\":" + _assertions + ",\"cases\":["
                + string.Join(",", Cases.Select(value => "\"" + value + "\"")) + "]}";
            File.WriteAllText(output, json);
            return new { passed = true, assertions = _assertions, cases = Cases.Count, output };
        }
        catch (Exception exception)
        {
            File.WriteAllText(output, "{\"passed\":false,\"negativeControl\":" + (_injectFault ? "true" : "false") + ",\"assertions\":" + _assertions
                + ",\"error\":\"" + exception.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"}");
            throw new Exception("GPU validation failed; evidence: " + output, exception);
        }
    }

    public static object NegativeControl()
    {
        _injectFault = true;
        try
        {
            Run();
            throw new InvalidOperationException("Injected DDA distance error was not detected");
        }
        catch (Exception exception) when (exception.InnerException != null
            && exception.InnerException.Message.Contains("distance backend 1"))
        {
            return new { detected = true, assertions = _assertions, output = _output, error = exception.InnerException.Message };
        }
        finally
        {
            _injectFault = false;
            if (_trace != null) _trace.SetInt("_TestInjectFault", 0);
        }
    }

    private static Vector4 ZParams(bool reversed) => reversed
        ? new Vector4(0, 0, 1f / Near - 1f / Far, 1f / Far)
        : new Vector4(0, 0, 1f / Far - 1f / Near, 1f / Near);

    private static Matrix4x4 Projection(bool ortho) => ortho
        ? Matrix4x4.Ortho(-4, 4, -3, 3, Near, Far)
        : Matrix4x4.Frustum(-Near, Near, -Near, Near, Near, Far);

    private static Texture2D Source(int width, int height, float[] depths, int representation)
    {
        bool linear = representation == 0;
        bool ortho = representation >= 3;
        bool reversed = representation == 2 || representation == 4;
        Vector4 zParams = ZParams(reversed);
        float[] raw = depths.Select(depth => linear ? depth : depth >= Far ? (reversed ? 0 : 1)
            : ortho ? (reversed ? 1 - (depth - Near) / (Far - Near) : (depth - Near) / (Far - Near))
            : (1f / depth - zParams.w) / zParams.z).ToArray();
        var texture = new Texture2D(width, height, TextureFormat.RFloat, false, true) { filterMode = FilterMode.Point };
        texture.SetPixelData(raw, 0);
        texture.Apply();
        return texture;
    }

    private static RenderTexture Target(int width, int height, bool mips, RenderTextureFormat format = RenderTextureFormat.RFloat)
    {
        var texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = true,
            useMipMap = mips,
            autoGenerateMips = false,
            filterMode = FilterMode.Point
        };
        Require(texture.Create(), "RenderTexture creation failed");
        return texture;
    }

    private static RenderTexture Build(Texture2D source, int width, int height, int representation, Vector4 mapping)
    {
        RenderTexture pyramid = Target(width, height, true);
        bool reversed = representation == 2 || representation == 4;
        _build.SetVector("_DepthParams", new Vector4(Near, Far, representation >= 3 ? 1 : 0, reversed ? 1 : 0));
        _build.SetVector("_SourceZBufferParams", ZParams(reversed));
        _build.SetVector("_SourceUvScaleBias", mapping);
        _build.SetInt("_SourceIsLinear", representation == 0 ? 1 : 0);
        int copy = _build.FindKernel("CopyDepth");
        _build.SetVector("_DestinationSize", new Vector4(width, height, 0, 0));
        _build.SetTexture(copy, "_SourceDepth", source);
        _build.SetTexture(copy, "_Destination", pyramid, 0);
        _build.Dispatch(copy, (width + 7) / 8, (height + 7) / 8, 1);
        int reduce = _build.FindKernel("ReduceDepth");
        for (int mip = 1; mip < pyramid.mipmapCount; mip++)
        {
            int mipWidth = Math.Max(1, width >> mip);
            int mipHeight = Math.Max(1, height >> mip);
            _build.SetVector("_DestinationSize", new Vector4(mipWidth, mipHeight, 0, 0));
            _build.SetInt("_SourceMip", mip - 1);
            _build.SetTexture(reduce, "_SourcePyramid", pyramid);
            _build.SetTexture(reduce, "_Destination", pyramid, mip);
            _build.Dispatch(reduce, (mipWidth + 7) / 8, (mipHeight + 7) / 8, 1);
        }
        return pyramid;
    }

    private static float[] Read(RenderTexture texture, int mip)
    {
        var readback = AsyncGPUReadback.Request(texture, mip);
        readback.WaitForCompletion();
        Require(!readback.hasError, "GPU readback failed");
        return readback.GetData<float>().ToArray();
    }

    private static float[] ReduceReference(float[] source, int width, int height, int targetWidth, int targetHeight)
    {
        float[] output = Enumerable.Repeat(Far, targetWidth * targetHeight).ToArray();
        for (int pixelY = 0; pixelY < targetHeight; pixelY++)
            for (int pixelX = 0; pixelX < targetWidth; pixelX++)
                for (int sourceY = (int)Math.Floor((double)pixelY * height / targetHeight);
                    sourceY < (int)Math.Ceiling((double)(pixelY + 1) * height / targetHeight); sourceY++)
                    for (int sourceX = (int)Math.Floor((double)pixelX * width / targetWidth);
                        sourceX < (int)Math.Ceiling((double)(pixelX + 1) * width / targetWidth); sourceX++)
                        output[pixelY * targetWidth + pixelX] = Math.Min(output[pixelY * targetWidth + pixelX], source[sourceY * width + sourceX]);
        return output;
    }

    private static void CheckPyramid(RenderTexture texture, float[] expected, int width, int height)
    {
        for (int mip = 0; mip < texture.mipmapCount; mip++)
        {
            int mipWidth = Math.Max(1, texture.width >> mip);
            int mipHeight = Math.Max(1, texture.height >> mip);
            if (mip > 0)
                expected = ReduceReference(expected, width, height, mipWidth, mipHeight);
            float[] actual = Read(texture, mip);
            Require(actual.Length == expected.Length, "Mip dimensions differ");
            for (int pixel = 0; pixel < expected.Length; pixel++)
                Close(actual[pixel], expected[pixel], "Pyramid mip " + mip + " pixel " + pixel, 0.001f);
            width = mipWidth;
            height = mipHeight;
        }
    }

    private static void SetContext(Action<string, Vector4> vector, Action<string, Matrix4x4> matrix,
        Action<string, int> integer, int width, int height, int representation, int mips, Vector4 mapping)
    {
        bool reversed = representation == 2 || representation == 4;
        matrix("_TestWorldToView", Matrix4x4.identity);
        matrix("_TestViewToClip", Projection(representation >= 3));
        vector("_TestUvScaleBias", new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
        vector("_TestDepthUvScaleBias", mapping);
        vector("_TestZBufferParams", ZParams(reversed));
        vector("_TestClip", new Vector4(Near, Far, 0, 0));
        vector("_TestSize", new Vector4(width, height, 1f / width, 1f / height));
        vector("_TestTextureSize", new Vector4(width, height, 1f / width, 1f / height));
        vector("_TestFlags", new Vector4(representation == 0 ? 1 : 0, representation >= 3 ? 1 : 0, reversed ? 1 : 0, mips));
        integer("_TestMaxSteps", Samples);
    }

    private static Input[] Rays()
    {
        var rays = new List<Input>
        {
            new(new Vector3(0, 0, -1), Vector3.back, 15),
            new(new Vector3(0, 0, -0.01f), Vector3.back, 15),
            new(new Vector3(0, 0, -1), Vector3.back, 15, 0.2f),
            new(new Vector3(0, 0, -1), Vector3.right, 15),
            new(new Vector3(0, 0, -1), Vector3.left, 15),
            new(new Vector3(0, 0, -1), Vector3.up, 15),
            new(new Vector3(0, 0, -1), Vector3.down, 15),
            new(new Vector3(-2, 0, -3), Vector3.right, 4),
            new(new Vector3(2, 0, -3), Vector3.left, 4),
            new(new Vector3(0, 0, -1), new Vector3(0.000001f, 0, -1), 15),
            new(new Vector3(50, 0, -1), Vector3.right, 15)
        };
        var random = new System.Random(8309);
        for (int index = 0; index < 96; index++)
            rays.Add(new Input(new Vector3((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1,
                index % 3 == 0 ? -8 : -1),
                new Vector3((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1,
                    index % 3 == 0 ? 0.6f : -0.6f), 30));
        return rays.ToArray();
    }

    private static Output[] Dispatch(Texture source, RenderTexture pyramid, Input[] rays, int representation)
    {
        using var inputs = new ComputeBuffer(rays.Length, 32);
        using var outputs = new ComputeBuffer(rays.Length * 4, 32);
        inputs.SetData(rays);
        int kernel = _trace.FindKernel(representation == 2 || representation == 4 ? "TraceReversed" : "Trace");
        _trace.SetBuffer(kernel, "_Inputs", inputs);
        _trace.SetBuffer(kernel, "_Outputs", outputs);
        _trace.SetTexture(kernel, "_Depth", source);
        _trace.SetTexture(kernel, "_Pyramid", pyramid);
        _trace.SetInt("_TestRayCount", rays.Length);
        _trace.Dispatch(kernel, 1, (rays.Length + 63) / 64, 1);
        var results = new Output[rays.Length * 4];
        outputs.GetData(results);
        return results;
    }

    private static bool Clip(double value, double slope, ref double low, ref double high)
    {
        if (Math.Abs(slope) < 1e-12)
            return value >= 0;
        double boundary = -value / slope;
        if (slope > 0) low = Math.Max(low, boundary);
        else high = Math.Min(high, boundary);
        return low <= high;
    }

    private static (bool hit, double distance) Oracle(Input ray, float[] depths, int width, int height, bool ortho, bool hiZ = false)
    {
        Vector3 origin = ray.Origin;
        Vector3 direction = ray.Direction;
        Matrix4x4 projection = Projection(ortho);
        Vector4 clipOrigin = projection * new Vector4(origin.x, origin.y, origin.z, 1);
        Vector4 clipDirection = projection * new Vector4(direction.x, direction.y, direction.z, 0);
        Vector3 start = origin + direction * ray.Origin.w;
        Vector4 startClip = projection * new Vector4(start.x, start.y, start.z, 1);
        Vector2 startUV = new(startClip.x / startClip.w * 0.5f + 0.5f, startClip.y / startClip.w * 0.5f + 0.5f);
        if (-start.z >= Near && -start.z <= Far && startUV.x > 0 && startUV.x < 1 && startUV.y > 0 && startUV.y < 1)
        {
            float depth = depths[(int)(startUV.y * height) * width + (int)(startUV.x * width)];
            if (depth < Far && -start.z >= depth)
                return (false, 0);
        }
        double minimum = ray.Origin.w;
        double maximum = ray.Direction.w;
        if (!Clip(-origin.z - Near, -direction.z, ref minimum, ref maximum)
            || !Clip(Far + origin.z, direction.z, ref minimum, ref maximum))
            return (false, 0);
        for (int axis = 0; axis < 2; axis++)
            if (!Clip(clipOrigin[axis] + clipOrigin.w, clipDirection[axis] + clipDirection.w, ref minimum, ref maximum)
                || !Clip(clipOrigin.w - clipOrigin[axis], clipDirection.w - clipDirection[axis], ref minimum, ref maximum))
                return (false, 0);
        if (minimum >= maximum) return (false, 0);
        double startW = clipOrigin.w + clipDirection.w * minimum;
        double endW = clipOrigin.w + clipDirection.w * maximum;
        double Distance(double progress) => (minimum * (1 - progress) / startW + maximum * progress / endW)
            / ((1 - progress) / startW + progress / endW);
        var levels = new List<(float[] depths, int width, int height)> { (depths, width, height) };
        if (hiZ)
            while (Math.Max(levels.Last().width, levels.Last().height) > 1)
            {
                var previous = levels.Last();
                int nextWidth = Math.Max(1, previous.width / 2);
                int nextHeight = Math.Max(1, previous.height / 2);
                float[] valid = previous.depths.Select(depth => float.IsFinite(depth) && depth > 0 && depth < Far ? depth : Far).ToArray();
                levels.Add((ReduceReference(valid, previous.width, previous.height, nextWidth, nextHeight), nextWidth, nextHeight));
            }
        (bool surface, double delta) Sample(double progress, int mip = 0)
        {
            double distance = Distance(progress);
            Vector3 position = origin + direction * (float)distance;
            Vector4 clip = projection * new Vector4(position.x, position.y, position.z, 1);
            var level = levels[mip];
            int pixelX = Math.Clamp((int)Math.Floor((clip.x / clip.w * 0.5 + 0.5) * level.width), 0, level.width - 1);
            int pixelY = Math.Clamp((int)Math.Floor((clip.y / clip.w * 0.5 + 0.5) * level.height), 0, level.height - 1);
            float scene = level.depths[pixelY * level.width + pixelX];
            return (float.IsFinite(scene) && scene > 0 && scene < Far, -position.z - scene);
        }
        var initial = Sample(0);
        bool previousInFront = initial.surface && initial.delta < 0;
        if (initial.surface && !previousInFront) return (false, 0);
        int mip = 0;
        double cursor = 0;
        for (int sample = 1; sample <= Samples; sample++)
        {
            double stride = hiZ ? Math.Min(Math.Pow(2, mip), Samples - cursor) : 1;
            double progress = (cursor + stride) / Samples;
            var current = Sample(progress, mip);
            if (current.surface && current.delta >= 0)
            {
                if (hiZ && mip > 0) { mip--; continue; }
                if (!previousInFront) return (false, 0);
                double low = cursor / Samples;
                double high = progress;
                for (int refine = 0; refine < 6; refine++)
                {
                    double middle = (low + high) * 0.5;
                    var probe = Sample(middle);
                    if (probe.surface && probe.delta >= 0) high = middle;
                    else low = middle;
                }
                var candidate = Sample(high);
                return (candidate.surface && candidate.delta >= 0 && candidate.delta <= Thickness, Distance(high));
            }
            previousInFront = current.surface && current.delta < 0;
            cursor += stride;
            if (cursor >= Samples) return (false, 0);
            if (hiZ) mip = Math.Min(mip + 1, levels.Count - 1);
        }
        return (false, 0);
    }

    private static void CheckOutput(Output result, Input ray, bool ortho, string label)
    {
        Require(result.Diagnostic.x >= 0 && result.Diagnostic.x <= 5, label + " invalid status");
        Require(result.Diagnostic.y <= Samples && result.Diagnostic.z <= Samples + 9, label + " unbounded queries");
        Require((result.Diagnostic.x == 0) == (result.Diagnostic.w == 1), label + " inconsistent HIT");
        if (result.Diagnostic.w == 0) return;
        Require(result.Geometry.z >= ray.Origin.w && result.Geometry.z <= ray.Direction.w, label + " distance range");
        Vector3 position = (Vector3)ray.Origin + (Vector3)ray.Direction * result.Geometry.z;
        Vector4 clip = Projection(ortho) * new Vector4(position.x, position.y, position.z, 1);
        Close(result.Geometry.x, clip.x / clip.w * 0.5f + 0.5f, label + " world U", 0.0002f);
        Close(result.Geometry.y, clip.y / clip.w * 0.5f + 0.5f, label + " world V", 0.0002f);
        Close(result.Geometry.w, -position.z, label + " world eye depth", 0.002f);
    }

    private static void CheckCase(int width, int height, int representation, bool flipped)
    {
        float[] depths = Enumerable.Repeat(5f, width * height).ToArray();
        depths[(height / 2) * width + width / 2] = 3;
        if (depths.Length > 4)
        {
            depths[0] = 1;
            depths[depths.Length - 1] = 2;
            for (int pixel = 1; pixel < depths.Length; pixel += 7) depths[pixel] = Far;
        }
        Vector4 mapping = flipped ? new Vector4(1, -1, 0, 1) : Mapping;
        float[] mapped = flipped ? Enumerable.Range(0, depths.Length).Select(pixel => depths[(height - 1 - pixel / width) * width + pixel % width]).ToArray() : depths;
        Texture2D source = Source(width, height, depths, representation);
        RenderTexture pyramid = null;
        try
        {
            pyramid = Build(source, width, height, representation, mapping);
            CheckPyramid(pyramid, mapped, width, height);
            SetContext(_trace.SetVector, _trace.SetMatrix, _trace.SetInt, width, height, representation, pyramid.mipmapCount, mapping);
            _trace.SetFloat("_TestThickness", Thickness);
            Input[] rays = Rays();
            Output[] actual = Dispatch(source, pyramid, rays, representation);
            for (int index = 0; index < rays.Length; index++)
            {
                var expected = Oracle(rays[index], mapped, width, height, representation >= 3);
                string label = width + "x" + height + " repr " + representation + " ray " + index;
                for (int backend = 1; backend < 3; backend++)
                {
                    Output result = actual[index * 4 + backend];
                    Require(result.Diagnostic.x != 4, label + " invalid backend " + backend);
                    if (backend == 1)
                    {
                        Require(result.Diagnostic.x != 3, label + " fixed samples must not report BUDGET");
                        Require((result.Diagnostic.w > 0) == expected.hit, label + " sample hit differs backend " + backend
                            + " expected " + expected + " actual " + result.Geometry.ToString("G9") + " diagnostic " + result.Diagnostic
                            + " origin " + rays[index].Origin.ToString("G9") + " direction " + rays[index].Direction.ToString("G9"));
                        if (expected.hit) Close(result.Geometry.z, (float)expected.distance, label + " distance backend " + backend, 0.005f);
                    }
                    else
                    {
                        var coarse = Oracle(rays[index], mapped, width, height, representation >= 3, true);
                        Require((result.Diagnostic.w > 0) == coarse.hit, label + " HiZ sample hit differs");
                        if (coarse.hit) Close(result.Geometry.z, (float)coarse.distance, label + " HiZ sample distance", 0.005f);
                    }
                    CheckOutput(result, rays[index], representation >= 3, label + " backend " + backend);
                }
                if (rays[index].Origin.w == 0 && !flipped && representation != 0)
                {
                    Output rayResult = actual[index * 4];
                    Output baseline = actual[index * 4 + 3];
                    Require(rayResult.Diagnostic.w == baseline.Diagnostic.w, label + " RAY baseline hit differs");
                    if (baseline.Diagnostic.w > 0)
                    {
                        Close(rayResult.Geometry.z, baseline.Geometry.z, label + " RAY baseline distance", 0.002f);
                        Close(rayResult.Geometry.x, baseline.Geometry.x, label + " RAY baseline U", 0.0001f);
                        Close(rayResult.Geometry.y, baseline.Geometry.y, label + " RAY baseline V", 0.0001f);
                    }
                }
            }
            _trace.SetInt("_TestMaxSteps", 1);
            Output[] exhausted = Dispatch(source, pyramid, new[] { new Input(new Vector3(0, 0, -1), new Vector3(0.8f, 0, -0.1f), 15) }, representation);
            Require(exhausted[1].Diagnostic.y <= 1 && exhausted[2].Diagnostic.y <= 1, "Budget is unbounded");
            Require(exhausted[1].Diagnostic.x != 3, "Uniform DDA must finish its sample schedule");
            if (width == 13 && height == 9 && representation == 0 && !flipped)
            {
                _trace.SetInt("_TestMaxSteps", 2);
                Output[] rollback = Dispatch(source, pyramid, new[] { new Input(new Vector3(0, 0, -1), Vector3.back, 15) }, representation);
                Require(rollback[2].Diagnostic.x == 3 && rollback[2].Diagnostic.y == 2, "HiZ rollback must consume its iteration budget");
            }
            SetContext(_trace.SetVector, _trace.SetMatrix, _trace.SetInt, width, height, representation, 0, mapping);
            Output[] missing = Dispatch(source, pyramid, new[] { rays[0] }, representation);
            Require(missing[2].Diagnostic.x == 5, "Missing pyramid must report UNAVAILABLE");
            Input invalid = rays[0];
            invalid.Direction = new Vector4(0, 0, 0, 5);
            Output[] invalidOutput = Dispatch(source, pyramid, new[] { invalid }, representation);
            for (int backend = 0; backend < 3; backend++) Require(invalidOutput[backend].Diagnostic.x == 4, "Invalid ray not rejected");
            Cases.Add(width + "x" + height + " representation " + representation + (flipped ? " flipped UV" : ""));
        }
        finally
        {
            if (pyramid != null) UnityEngine.Object.DestroyImmediate(pyramid);
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static void CheckConsumers()
    {
        Texture2D source = Source(13, 9, Enumerable.Repeat(5f, 117).ToArray(), 0);
        RenderTexture pyramid = Build(source, 13, 9, 0, Mapping);
        RenderTexture target = Target(1, 1, false, RenderTextureFormat.ARGBFloat);
        var material = new Material(_consumer);
        try
        {
            SetContext(material.SetVector, material.SetMatrix, material.SetInt, 13, 9, 0, pyramid.mipmapCount, Mapping);
            material.SetFloat("_TestThickness", Thickness);
            material.SetTexture("_ScreenTraceDepth", source);
            material.SetTexture("_ScreenTraceDepthPyramid", pyramid);
            material.SetVector("_TestOriginRange", new Vector4(0, 0, -1, 0));
            material.SetVector("_TestDirectionRange", new Vector4(0, 0, -1, 15));
            for (int backend = 0; backend < 3; backend++)
                for (int pass = 0; pass < 2; pass++)
                {
                    material.SetInt("_TestBackend", backend);
                    using var commands = new CommandBuffer();
                    commands.SetRenderTarget(target);
                    commands.ClearRenderTarget(false, true, Color.clear);
                    commands.SetGlobalVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                    commands.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3);
                    Graphics.ExecuteCommandBuffer(commands);
                    float[] result = Read(target, 0);
                    Close(result[pass == 0 ? 3 : 0], 1, "Fragment consumer did not hit");
                    Close(result[pass == 0 ? 2 : 1], 4, "Fragment consumer distance", 0.015f);
                }
            Cases.Add("Reflection-hit and shadow-occlusion fragments: all three backends");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(pyramid);
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static void CheckMapping()
    {
        float[] depths = Enumerable.Repeat(5f, 13 * 9).ToArray();
        depths[4 * 13 + 6] = 3;
        Vector4 mapping = new(7f / 13, 5f / 9, 3f / 13, 2f / 9);
        float[] mapped = Enumerable.Range(0, 35).Select(pixel => depths[(pixel / 7 + 2) * 13 + pixel % 7 + 3]).ToArray();
        Texture2D source = Source(13, 9, depths, 2);
        RenderTexture pyramid = Build(source, 7, 5, 2, mapping);
        try
        {
            CheckPyramid(pyramid, mapped, 7, 5);
            SetContext(_trace.SetVector, _trace.SetMatrix, _trace.SetInt, 7, 5, 2, pyramid.mipmapCount, mapping);
            _trace.SetVector("_TestTextureSize", new Vector4(13, 9, 1f / 13, 1f / 9));
            _trace.SetFloat("_TestThickness", Thickness);
            Input[] rays = Rays();
            Output[] actual = Dispatch(source, pyramid, rays, 2);
            for (int index = 0; index < rays.Length; index++)
            {
                var expected = Oracle(rays[index], mapped, 7, 5, false);
                for (int backend = 1; backend < 3; backend++)
                {
                    Output result = actual[index * 4 + backend];
                    CheckOutput(result, rays[index], false, "Cropped viewport " + index + " backend " + backend);
                    if (backend == 1)
                    {
                        Require((result.Diagnostic.w > 0) == expected.hit, "Cropped sample hit " + index);
                        if (expected.hit) Close(result.Geometry.z, (float)expected.distance, "Cropped sample world distance", 0.005f);
                    }
                    Require(result.Diagnostic.x != 4, "Cropped viewport invalid");
                }
            }
            Cases.Add("Active 7x5 viewport in physical 13x9 depth, reversed-Z");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(pyramid);
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static void CheckDepthFields()
    {
        foreach (int field in new[] { 0, 1, 2, 3 })
        {
            float[] depths = Enumerable.Repeat(field == 1 ? Far : 5f, 13 * 9).ToArray();
            if (field == 2) for (int pixel = 0; pixel < depths.Length; pixel++) if (pixel % 13 >= 6) depths[pixel] = 3;
            if (field == 3) { depths[0] = float.NaN; depths[1] = float.PositiveInfinity; depths[2] = -1; }
            Texture2D source = Source(13, 9, depths, 0);
            RenderTexture pyramid = Build(source, 13, 9, 0, Mapping);
            try
            {
                float[] expectedDepths = depths.Select(depth => float.IsFinite(depth) && depth > 0 && depth < Far ? depth : Far).ToArray();
                CheckPyramid(pyramid, expectedDepths, 13, 9);
                SetContext(_trace.SetVector, _trace.SetMatrix, _trace.SetInt, 13, 9, 0, pyramid.mipmapCount, Mapping);
                _trace.SetFloat("_TestThickness", Thickness);
                Input[] rays = Rays();
                Output[] actual = Dispatch(source, pyramid, rays, 0);
                for (int index = 0; index < rays.Length; index++)
                {
                    var expected = Oracle(rays[index], expectedDepths, 13, 9, false);
                    for (int backend = 1; backend < 3; backend++)
                    {
                        Output result = actual[index * 4 + backend];
                        CheckOutput(result, rays[index], false, "Depth field " + field + " ray " + index + " backend " + backend);
                        if (backend == 1)
                        {
                            Require((result.Diagnostic.w > 0) == expected.hit, "Depth field sample hit " + field + " ray " + index);
                            if (expected.hit) Close(result.Geometry.z, (float)expected.distance, "Depth field sample distance", 0.005f);
                        }
                        Require(result.Diagnostic.x != 4, "Depth field invalid");
                    }
                }
                _trace.SetFloat("_TestThickness", 0);
                Output[] exact = Dispatch(source, pyramid, new[] { rays[0] }, 0);
                if (field == 1) Require(exact[1].Diagnostic.w == 0 && exact[2].Diagnostic.w == 0, "Sky false hit");
                else for (int backend = 1; backend < 3; backend++)
                    if (exact[backend].Diagnostic.w > 0) Close(exact[backend].Geometry.z, field == 2 ? 2 : 4, "Zero-thickness accepted point", 0.002f);
                Cases.Add("Constant, sky, depth step, invalid depth field " + field + "; zero thickness");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pyramid);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }
    }

    private sealed class ExportPassData
    {
        public TextureHandle Source;
        public TextureHandle Destination;
        public int Mip;
        public int Width;
        public int Height;
        public ComputeShader Shader;
    }

    private static void CheckWorldSpaceDistance()
    {
        Texture2D source = Source(13, 9, Enumerable.Repeat(5f, 117).ToArray(), 0);
        RenderTexture pyramid = Build(source, 13, 9, 0, Mapping);
        try
        {
            Matrix4x4 viewToWorld = Matrix4x4.TRS(new Vector3(7, -3, 2), Quaternion.Euler(17, 41, 8), Vector3.one);
            Input[] viewRays = new[]
            {
                new Input(new Vector3(0, 0, -1), Vector3.back, 15),
                new Input(new Vector3(0, 0, -0.01f), Vector3.back, 15),
                new Input(new Vector3(0, 0, -1), Vector3.back, 15, 0.2f)
            };
            Input[] worldRays = viewRays.Select(ray => new Input(viewToWorld.MultiplyPoint3x4(ray.Origin),
                viewToWorld.MultiplyVector(ray.Direction), ray.Direction.w, ray.Origin.w)).ToArray();
            SetContext(_trace.SetVector, _trace.SetMatrix, _trace.SetInt, 13, 9, 0, pyramid.mipmapCount, Mapping);
            _trace.SetMatrix("_TestWorldToView", viewToWorld.inverse);
            _trace.SetFloat("_TestThickness", Thickness);
            Output[] actual = Dispatch(source, pyramid, worldRays, 0);
            for (int index = 0; index < worldRays.Length; index++)
                for (int backend = 0; backend < 3; backend++)
                {
                    Output result = actual[index * 4 + backend];
                    if (index == 1 && backend == 0) Require(result.Diagnostic.x == 2, "RAY baseline must reject before-near origin");
                    else
                    {
                        Require(result.Diagnostic.w == 1, "World-space transformed ray did not hit");
                        if (backend == 0)
                            Close(result.Geometry.z, 4, "RAY original world-origin distance", 0.01f);
                        else
                        {
                            var expected = Oracle(viewRays[index], Enumerable.Repeat(5f, 117).ToArray(), 13, 9, false, backend == 2);
                            Require(expected.hit, "Transformed sample reference must hit");
                            Close(result.Geometry.z, (float)expected.distance, "Original world-origin sampled distance after clipping", 0.005f);
                        }
                    }
                }
            Cases.Add("Rigid world/view transform; original distance preserved after near-plane and tMin clipping");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(pyramid);
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static void CheckGraph()
    {
        float[] depths = Enumerable.Repeat(5f, 13 * 9).ToArray();
        depths[116] = 1;
        Texture2D source = Source(13, 9, depths, 0);
        RenderTexture input = Target(13, 9, false);
        RenderTexture output = Target(13, 9, true);
        RenderTexture cameraTarget = Target(32, 32, false, RenderTextureFormat.ARGB32);
        RTHandle inputHandle = RTHandles.Alloc(input);
        RTHandle outputHandle = RTHandles.Alloc(output);
        var cameraObject = new GameObject("ScreenTraceValidationCamera") { hideFlags = HideFlags.HideAndDontSave };
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.cullingMask = 0;
        cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        Graphics.CopyTexture(source, input);
        int invocations = 0;
        var graph = new RenderGraph("ScreenTraceValidation");
        using var frame = new ContextContainer();
        Action<ScriptableRenderContext, Camera> callback = (context, currentCamera) =>
        {
            if (currentCamera != camera) return;
            invocations++;
            frame.Dispose();
            using var commands = new CommandBuffer();
            graph.BeginRecording(new RenderGraphParameters
            {
                commandBuffer = commands,
                scriptableRenderContext = context,
                currentFrameIndex = invocations,
                executionId = camera.GetInstanceID()
            });
            var request = new ScreenSpaceDepthPyramid.Request
            {
                Source = graph.ImportTexture(inputHandle), Shader = _build, Width = 13, Height = 9,
                SourceUvScaleBias = Mapping, ZBufferParams = ZParams(false), NearPlane = Near, FarPlane = Far,
                LinearDepth = true, SourceRevision = 7
            };
            Require(ScreenSpaceDepthPyramid.TryGet(graph, frame, request, out var first, out string reason), reason);
            Require(ScreenSpaceDepthPyramid.TryGet(graph, frame, request, out var shared, out reason), reason);
            Require(first.Texture.Equals(shared.Texture), "Same-stage consumers must share a pyramid");
            request.SourceRevision++;
            Require(ScreenSpaceDepthPyramid.TryGet(graph, frame, request, out var changed, out reason), reason);
            Require(!first.Texture.Equals(changed.Texture), "Source revision must invalidate cache");
            request.Width = 11;
            Require(ScreenSpaceDepthPyramid.TryGet(graph, frame, request, out var resized, out reason), reason);
            Require(!changed.Texture.Equals(resized.Texture) && resized.Width == 11, "Viewport must invalidate cache");
            request.Shader = null;
            Require(!ScreenSpaceDepthPyramid.TryGet(graph, frame, request, out _, out reason), "Missing shader accepted");
            request.Shader = _build;
            request.Source = graph.CreateTexture(new TextureDesc(4, 4)
            {
                format = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat,
                dimension = TextureDimension.Tex2DArray, slices = 2
            });
            Require(!ScreenSpaceDepthPyramid.TryGet(graph, frame, request, out _, out reason), "XR array accepted");
            request.Source = graph.CreateTexture(new TextureDesc(4, 4)
            {
                format = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat,
                dimension = TextureDimension.Tex2D, msaaSamples = MSAASamples.MSAA4x
            });
            Require(!ScreenSpaceDepthPyramid.TryGet(graph, frame, request, out _, out reason), "Unresolved MSAA accepted");
            TextureHandle destination = graph.ImportTexture(outputHandle);
            for (int mip = 0; mip < first.MipCount; mip++)
            {
                using var builder = graph.AddComputePass<ExportPassData>("Validate Pyramid Export", out var data);
                data.Source = first.Texture;
                data.Destination = destination;
                data.Mip = mip;
                data.Width = Math.Max(1, 13 >> mip);
                data.Height = Math.Max(1, 9 >> mip);
                data.Shader = _trace;
                builder.UseTexture(first.Texture, AccessFlags.Read);
                builder.UseTexture(destination, AccessFlags.ReadWrite);
                builder.SetRenderFunc((ExportPassData pass, ComputeGraphContext execution) =>
                {
                    int kernel = pass.Shader.FindKernel("CopyMip");
                    execution.cmd.SetComputeTextureParam(pass.Shader, kernel, "_Pyramid", pass.Source);
                    execution.cmd.SetComputeTextureParam(pass.Shader, kernel, "_Destination", pass.Destination, pass.Mip);
                    execution.cmd.SetComputeIntParam(pass.Shader, "_ExportMip", pass.Mip);
                    execution.cmd.SetComputeIntParams(pass.Shader, "_ExportSize", pass.Width, pass.Height);
                    execution.cmd.DispatchCompute(pass.Shader, kernel, (pass.Width + 7) / 8, (pass.Height + 7) / 8, 1);
                });
            }
            graph.EndRecordingAndExecute();
            context.ExecuteCommandBuffer(commands);
        };
        try
        {
            RenderPipelineManager.beginCameraRendering += callback;
            for (int iteration = 0; iteration < 2; iteration++)
            {
                var renderRequest = new UniversalRenderPipeline.SingleCameraRequest { destination = cameraTarget };
                Require(RenderPipeline.SupportsRenderRequest(camera, renderRequest), "Single-camera request unsupported");
                RenderPipeline.SubmitRenderRequest(camera, renderRequest);
                CheckPyramid(output, depths, 13, 9);
            }
            Require(invocations == 2, "RenderGraph callback did not run twice");
            Cases.Add("Real RenderGraph execution, lazy sharing, revision/viewport invalidation, recording-local lifetime");
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering -= callback;
            graph.Cleanup();
            inputHandle.Release();
            outputHandle.Release();
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(cameraTarget);
            UnityEngine.Object.DestroyImmediate(output);
            UnityEngine.Object.DestroyImmediate(input);
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static void Close(float actual, float expected, string message, float tolerance = 0.0001f)
        => Require(float.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance,
            message + " expected " + expected + " actual " + actual);

    private static void Require(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
