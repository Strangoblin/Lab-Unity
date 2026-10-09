using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Mine.Weather;

public class Script
{
    const string WeatherRoot = "Assets/Mine/Shaders/PostProcess/Weather/";
    static readonly Vector4 IdentityScaleBias = new Vector4(1f, 1f, 0f, 0f);

    [Serializable]
    public sealed class CaseResult
    {
        public string Name;
        public int Width, Height, PixelCount, NonEmptyPixels, ChangedPixels, MismatchPixels;
        public bool Finite, Passed, ExpectedDifference;
        public float Time, MaxError, MaxNormalizedError, MinAlpha, MaxAlpha;
        public Vector4 MaxErrorRGBA;
    }

    [Serializable]
    public sealed class Report
    {
        public bool Passed;
        public int TotalCases, BaselineCases, ModuleCombinations, EndpointCases, ExpectedDifferencesDetected, Failures;
        public float MaxError, MaxNormalizedError;
        public string Error, Tolerance, Scope;
    }

    public static object Main()
    {
        var globals = new SavedGlobals();
        var owned = new List<UnityEngine.Object>();
        var cases = new List<CaseResult>();
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("A real graphics device is required.");

            var weatherShader = AssetDatabase.LoadAssetAtPath<Shader>(WeatherRoot + "Weather.shader");
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Noise2DRG.asset");
            if (weatherShader == null || !weatherShader.isSupported || noise == null)
                throw new InvalidOperationException("Missing supported Weather shader or actual Noise2DRG asset.");

            Type passType = typeof(WeatherFeature).GetNestedType("WeatherPass", BindingFlags.NonPublic);
            MethodInfo bind = passType == null ? null : passType.GetMethod("Bind", BindingFlags.Static | BindingFlags.NonPublic);
            if (bind == null)
                throw new InvalidOperationException("WeatherFeature.WeatherPass.Bind was not found.");

            string rainy = File.ReadAllText(Path.Combine(Application.dataPath, "Mine/Shaders/PostProcess/Rainy/Rainy.shader"));
            string blurBlock = @"(?m)^\s*if\s*\(mist\s*>\s*0\.0\)\s*\r?\n\s*color\s*=\s*lerp\(color,\s*RainyBlur\(uv,\s*lerp\(2\.0,\s*5\.0,\s*mist\)\),\s*mist\s*\*\s*0\.8\);\s*\r?\n";
            if (Regex.Matches(rainy, blurBlock).Count != 1)
                throw new InvalidOperationException("The exact Rain blur-only reference edit could not be established.");
            rainy = Regex.Replace(rainy, blurBlock, "");
            rainy = rainy.Replace("Shader \"PostProcess/Rainy\"", "Shader \"PostProcess/WeatherRainReference\"");
            Shader rainReference = ShaderUtil.CreateShaderAsset(rainy, true);
            if (rainReference != null) owned.Add(rainReference);
            if (rainReference == null || !rainReference.isSupported)
                throw new InvalidOperationException("The in-memory Rain reference shader is unsupported.");
            rainReference.hideFlags = HideFlags.HideAndDontSave;
            foreach (var message in ShaderUtil.GetShaderMessages(rainReference))
                if (message.severity.ToString() == "Error")
                    throw new InvalidOperationException("Rain reference compile error: " + message.message);

            string[] names = { "Rain", "Sand", "Snow" };
            string[] oldNames = { "Rainy", "Sandy", "Snowy" };
            var originals = new Material[3];
            var current = new Material[3];
            var parameters = new ResolvedWeatherSettings[3];
            for (int k = 0; k < 3; ++k)
            {
                var profile = AssetDatabase.LoadAssetAtPath<WeatherProfile>(WeatherRoot + "Presets/" + names[k] + ".asset");
                var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Mine/Shaders/PostProcess/" + oldNames[k] + "/Mine_PostProcess_" + oldNames[k] + ".mat");
                if (profile == null || sourceMaterial == null)
                    throw new InvalidOperationException("Missing actual profile or original material for " + names[k]);
                parameters[k] = profile.Resolve();
                originals[k] = new Material(k == 0 ? rainReference : sourceMaterial.shader);
                owned.Add(originals[k]);
                originals[k].CopyPropertiesFromMaterial(sourceMaterial);
                originals[k].hideFlags = HideFlags.HideAndDontSave;
                current[k] = new Material(weatherShader) { hideFlags = HideFlags.HideAndDontSave };
                owned.Add(current[k]);
                if (originals[k].passCount != 2 || current[k].FindPass("Particles") != 0
                    || current[k].FindPass("Atmosphere") != 1)
                    throw new InvalidOperationException("Unexpected pass layout for " + names[k]);
            }

            var zeroNoise = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            owned.Add(zeroNoise);
            var zeros = new Color[64];
            for (int i = 0; i < zeros.Length; ++i) zeros[i] = new Color(0f, 0f, 0f, 1f);
            zeroNoise.SetPixels(zeros);
            zeroNoise.Apply(false, false);

            int[] widths = { 192, 108 };
            int[] heights = { 108, 192 };
            float[] times = { 0.37f, 1.43f, 4.2f };
            for (int shape = 0; shape < 2; ++shape)
            {
                using (var fixture = new Fixture(widths[shape], heights[shape]))
                {
                    for (int k = 0; k < 3; ++k)
                    {
                        foreach (float time in times)
                        {
                            Bind(bind, current[k], noise, parameters[k], fixture.Size, time);
                            Color[][] pair = fixture.RenderPair(originals[k], current[k], time);
                            cases.Add(Compare("AB." + names[k] + "." + fixture.Width + "x" + fixture.Height,
                                fixture, pair[1], pair[0], time, false));
                        }
                    }

                    if (shape == 0)
                    {
                        for (int k = 0; k < 3; ++k)
                        {
                            for (int mask = 0; mask < 16; ++mask)
                            {
                                ResolvedWeatherSettings p = parameters[k];
                                if ((mask & 1) == 0) p.ParticleOpacity = 0f;
                                if ((mask & 2) == 0) p.LensStrength = 0f;
                                if ((mask & 4) == 0) p.FogStrength = 0f;
                                if ((mask & 8) == 0) p.ToneStrength = 0f;
                                Bind(bind, current[k], noise, p, fixture.Size, times[1]);
                                Color[] rendered = fixture.RenderWeather(current[k], times[1]);
                                CaseResult result = Compare("Modules." + names[k] + ".mask" + mask,
                                    fixture, rendered, mask == 0 ? fixture.InputPixels : rendered, times[1], false);
                                cases.Add(result);
                            }
                        }

                        ResolvedWeatherSettings negative = parameters[1];
                        negative.ToneStrength = 0f;
                        if (parameters[1].ToneStrength <= 0f)
                            throw new InvalidOperationException("Tone negative control requires an active actual Sand tone.");
                        Bind(bind, current[1], noise, negative, fixture.Size, times[1]);
                        Color[][] negativePair = fixture.RenderPair(originals[1], current[1], times[1]);
                        cases.Add(Compare("NegativeControl.Sand.ToneOff", fixture,
                            negativePair[1], negativePair[0], times[1], true));

                        ResolvedWeatherSettings snowZero = parameters[2];
                        snowZero.ParticleOpacity = 0f;
                        snowZero.LensStrength = 1f;
                        snowZero.EdgeBaseWidth = 0f;
                        snowZero.EdgeNoiseWidth = 1f;
                        snowZero.FogStrength = 0f;
                        snowZero.ToneStrength = 0f;
                        Bind(bind, current[2], zeroNoise, snowZero, fixture.Size, times[1]);
                        cases.Add(Compare("SnowZeroNoise.DerivedWidthZero", fixture,
                            fixture.RenderWeather(current[2], times[1]), fixture.InputPixels, times[1], false));

                        snowZero.EdgeBaseWidth = 0.2f;
                        Bind(bind, current[2], zeroNoise, snowZero, fixture.Size, times[1]);
                        cases.Add(Compare("SnowZeroNoise.PositiveWidthControl", fixture,
                            fixture.RenderWeather(current[2], times[1]), fixture.InputPixels, times[1], true));

                        for (int k = 0; k < 3; ++k)
                        {
                            ResolvedWeatherSettings particles = ParticlesOnly(parameters[k]);
                            for (int randomness = 0; randomness <= 1; ++randomness)
                            {
                                ResolvedWeatherSettings p = particles;
                                p.Randomness = randomness;
                                Bind(bind, current[k], noise, p, fixture.Size, times[1]);
                                cases.Add(CheckFinite("Endpoint." + names[k] + ".Randomness" + randomness,
                                    fixture, current[k], times[1]));
                            }
                            for (int cycle = 0; cycle <= 2; ++cycle)
                            {
                                ResolvedWeatherSettings p = particles;
                                p.CycleRate = cycle;
                                Bind(bind, current[k], noise, p, fixture.Size, times[1]);
                                cases.Add(CheckFinite("Endpoint." + names[k] + ".CycleRate" + cycle,
                                    fixture, current[k], times[1]));
                            }
                            ResolvedWeatherSettings frozen = particles;
                            frozen.CycleRate = 0f;
                            Bind(bind, current[k], noise, frozen, fixture.Size, times[0]);
                            Color[] frozenFirst = fixture.RenderWeather(current[k], times[0]);
                            Bind(bind, current[k], noise, frozen, fixture.Size, times[2]);
                            cases.Add(Compare("Endpoint." + names[k] + ".CycleRateZeroFrozen", fixture,
                                fixture.RenderWeather(current[k], times[2]), frozenFirst, times[2], false));

                            if (k != 1)
                                for (int threshold = 0; threshold <= 1; ++threshold)
                                {
                                    ResolvedWeatherSettings p = particles;
                                    p.ParticleShape = threshold;
                                    Bind(bind, current[k], noise, p, fixture.Size, times[1]);
                                    Color[] rendered = fixture.RenderWeather(current[k], times[1]);
                                    cases.Add(Compare("Endpoint." + names[k] + ".ParticleShape" + threshold,
                                        fixture, rendered, threshold == 0 ? fixture.InputPixels : rendered, times[1], false));
                                }

                            ResolvedWeatherSettings strengths = parameters[k];
                            strengths.ParticleOpacity = 1f;
                            strengths.LensStrength = 1f;
                            strengths.FogStrength = 1f;
                            strengths.ToneStrength = 1f;
                            Bind(bind, current[k], noise, strengths, fixture.Size, times[1]);
                            cases.Add(CheckFinite("Endpoint." + names[k] + ".StrengthsOne", fixture, current[k], times[1]));
                        }

                        for (int refraction = 0; refraction <= 1; ++refraction)
                        {
                            ResolvedWeatherSettings p = ParticlesOnly(parameters[0]);
                            p.RefractionScale = refraction;
                            Bind(bind, current[0], noise, p, fixture.Size, times[1]);
                            Color[] rendered = fixture.RenderWeather(current[0], times[1]);
                            cases.Add(Compare("Endpoint.Rain.RefractionScale" + refraction, fixture,
                                rendered, refraction == 0 ? fixture.InputPixels : rendered, times[1], false));
                        }

                        for (int baseEndpoint = 0; baseEndpoint <= 1; ++baseEndpoint)
                            for (int noiseEndpoint = 0; noiseEndpoint <= 1; ++noiseEndpoint)
                            {
                                ResolvedWeatherSettings p = parameters[2];
                                p.ParticleOpacity = 0f;
                                p.LensStrength = 0f;
                                p.ToneStrength = 0f;
                                p.FogStrength = 1f;
                                p.FogBaseDistance = baseEndpoint == 0 ? WeatherProfile.MinimumFogDistance : 10000f;
                                p.FogNoiseDistance = noiseEndpoint == 0 ? 0f : 10000f;
                                Bind(bind, current[2], noise, p, fixture.Size, times[1]);
                                cases.Add(CheckFinite("Endpoint.Snow.FogBase" + baseEndpoint + ".FogNoise" + noiseEndpoint,
                                    fixture, current[2], times[1]));
                            }
                    }
                }
            }

            int failures = 0, expectedDifferencesDetected = 0, endpointCases = 0;
            float maxError = 0f, maxNormalizedError = 0f;
            foreach (CaseResult result in cases)
            {
                if (!result.Passed) ++failures;
                if (result.Name.StartsWith("Endpoint.", StringComparison.Ordinal)) ++endpointCases;
                if (result.ExpectedDifference && result.MismatchPixels > 0) ++expectedDifferencesDetected;
                if (!result.ExpectedDifference)
                {
                    maxError = Mathf.Max(maxError, result.MaxError);
                    maxNormalizedError = Mathf.Max(maxNormalizedError, result.MaxNormalizedError);
                }
            }
            return Serialize(new Report
            {
                Passed = failures == 0,
                TotalCases = cases.Count,
                BaselineCases = 18,
                ModuleCombinations = 48,
                EndpointCases = endpointCases,
                ExpectedDifferencesDetected = expectedDifferencesDetected,
                Failures = failures,
                MaxError = maxError,
                MaxNormalizedError = maxNormalizedError,
                Tolerance = "abs(new-ref) <= 1e-3*(1+abs(ref)), all RGBA pixels finite",
                Scope = "GPU equations with actual presets/noise and reflected production Bind; scaleBias identity; synthetic perspective depth; no RenderGraph scheduling or visual/performance claim"
            }, cases);
        }
        catch (Exception exception)
        {
            return Serialize(new Report
            {
                Passed = false, Error = exception.ToString(), TotalCases = cases.Count
            }, cases);
        }
        finally
        {
            globals.Restore();
            for (int i = owned.Count - 1; i >= 0; --i)
                if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
        }
    }

    static string Serialize(Report report, List<CaseResult> cases)
    {
        string summary = JsonUtility.ToJson(report);
        if (string.IsNullOrEmpty(summary) || summary[summary.Length - 1] != '}')
            throw new InvalidOperationException("Validation summary did not serialize to a JSON object.");
        var json = new StringBuilder(summary.Substring(0, summary.Length - 1));
        json.Append(",\"Cases\":[");
        for (int i = 0; i < cases.Count; ++i)
        {
            if (i > 0) json.Append(',');
            string item = JsonUtility.ToJson(cases[i]);
            if (!item.Contains("\"PixelCount\"") || !item.Contains("\"NonEmptyPixels\""))
                throw new InvalidOperationException("Validation case fields did not serialize: " + cases[i].Name);
            json.Append(item);
        }
        json.Append("]}");
        return json.ToString();
    }

    static void Bind(MethodInfo bind, Material material, Texture2D noise,
        ResolvedWeatherSettings parameters, Vector4 size, float time)
    {
        bind.Invoke(null, new object[] { material, noise, parameters, WeatherDebugMode.Off, size, time });
    }

    static ResolvedWeatherSettings ParticlesOnly(ResolvedWeatherSettings parameters)
    {
        parameters.LensStrength = 0f;
        parameters.FogStrength = 0f;
        parameters.ToneStrength = 0f;
        return parameters;
    }

    static CaseResult CheckFinite(string name, Fixture fixture, Material material, float time)
    {
        Color[] rendered = fixture.RenderWeather(material, time);
        return Compare(name, fixture, rendered, rendered, time, false);
    }

    static CaseResult Compare(string name, Fixture fixture, Color[] output, Color[] reference,
        float time, bool expectedDifference)
    {
        var result = new CaseResult
        {
            Name = name, Width = fixture.Width, Height = fixture.Height,
            PixelCount = output.Length, Time = time, Finite = true,
            ExpectedDifference = expectedDifference,
            MinAlpha = float.PositiveInfinity, MaxAlpha = float.NegativeInfinity
        };
        for (int i = 0; i < output.Length; ++i)
        {
            bool mismatch = false, changed = false;
            float nonempty = 0f;
            for (int channel = 0; channel < 4; ++channel)
            {
                float value = output[i][channel], target = reference[i][channel];
                if (float.IsNaN(value) || float.IsInfinity(value) || float.IsNaN(target) || float.IsInfinity(target))
                {
                    result.Finite = false;
                    mismatch = true;
                    continue;
                }
                float error = Mathf.Abs(value - target);
                float normalized = error / (1f + Mathf.Abs(target));
                result.MaxError = Mathf.Max(result.MaxError, error);
                result.MaxNormalizedError = Mathf.Max(result.MaxNormalizedError, normalized);
                Vector4 rgba = result.MaxErrorRGBA;
                rgba[channel] = Mathf.Max(rgba[channel], error);
                result.MaxErrorRGBA = rgba;
                mismatch |= error > 1e-3f * (1f + Mathf.Abs(target));
                changed |= Mathf.Abs(value - fixture.InputPixels[i][channel])
                    > 1e-3f * (1f + Mathf.Abs(fixture.InputPixels[i][channel]));
                nonempty += Mathf.Abs(value);
            }
            if (mismatch) ++result.MismatchPixels;
            if (changed) ++result.ChangedPixels;
            if (nonempty > 1e-6f) ++result.NonEmptyPixels;
            result.MinAlpha = Mathf.Min(result.MinAlpha, output[i].a);
            result.MaxAlpha = Mathf.Max(result.MaxAlpha, output[i].a);
        }
        result.Passed = result.Finite && result.PixelCount == fixture.Width * fixture.Height
            && result.NonEmptyPixels == result.PixelCount
            && (!name.StartsWith("AB.", StringComparison.Ordinal) || result.ChangedPixels > 0)
            && (expectedDifference ? result.MismatchPixels > 0 : result.MismatchPixels == 0);
        return result;
    }

    sealed class SavedGlobals
    {
        readonly string[] vectorNames = { "_Time", "_ScreenParams", "_ScaledScreenParams", "_ZBufferParams",
            "_WorldSpaceCameraPos", "unity_OrthoParams", "_RTHandleScale", "_CameraDepthTexture_TexelSize" };
        readonly string[] matrixNames = { "unity_MatrixV", "unity_MatrixInvVP" };
        readonly Vector4[] vectors;
        readonly Matrix4x4[] matrices;
        readonly Texture depth;
        readonly RenderTexture active;

        public SavedGlobals()
        {
            vectors = new Vector4[vectorNames.Length];
            matrices = new Matrix4x4[matrixNames.Length];
            for (int i = 0; i < vectorNames.Length; ++i) vectors[i] = Shader.GetGlobalVector(vectorNames[i]);
            for (int i = 0; i < matrixNames.Length; ++i) matrices[i] = Shader.GetGlobalMatrix(matrixNames[i]);
            depth = Shader.GetGlobalTexture("_CameraDepthTexture");
            active = RenderTexture.active;
        }

        public void Restore()
        {
            for (int i = 0; i < vectorNames.Length; ++i) Shader.SetGlobalVector(vectorNames[i], vectors[i]);
            for (int i = 0; i < matrixNames.Length; ++i) Shader.SetGlobalMatrix(matrixNames[i], matrices[i]);
            Shader.SetGlobalTexture("_CameraDepthTexture", depth);
            RenderTexture.active = active;
        }
    }

    sealed class Fixture : IDisposable
    {
        public readonly int Width, Height;
        public readonly Color[] InputPixels;
        public Vector4 Size { get { return new Vector4(Width, Height, 1f / Width, 1f / Height); } }
        readonly Texture2D input, depth, readback;
        readonly RenderTexture firstA, outputA, firstB, outputB;
        readonly RTHandle sourceHandle, firstHandleA, firstHandleB;
        readonly CommandBuffer command;
        readonly Vector4 zBuffer;
        readonly Matrix4x4 view, inverseVP;

        public Fixture(int width, int height)
        {
            Width = width; Height = height;
            try
            {
            const float near = 0.3f, far = 250f;
            float z0 = 1f - far / near, z1 = far / near;
            zBuffer = new Vector4(z0, z1, z0 / far, z1 / far);
            if (SystemInfo.usesReversedZBuffer)
                zBuffer = new Vector4(-zBuffer.x, zBuffer.y + zBuffer.x,
                    -zBuffer.z, zBuffer.w + zBuffer.z);
            view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f))
                * Matrix4x4.TRS(new Vector3(0f, 2f, 0f), Quaternion.identity, Vector3.one).inverse;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60f, (float)width / height, near, far), true);
            inverseVP = (projection * view).inverse;

            InputPixels = new Color[width * height];
            var depths = new float[InputPixels.Length];
            for (int y = 0; y < height; ++y)
                for (int x = 0; x < width; ++x)
                {
                    int i = y * width + x;
                    float u = (x + 0.5f) / width, v = (y + 0.5f) / height;
                    float checker = ((x / 7 + y / 5) & 1) == 0 ? 0.1f : 0.35f;
                    InputPixels[i] = new Color(0.12f + 1.2f * u + checker,
                        0.08f + 0.9f * v, 0.15f + 0.8f * (1f - u) * v, 0.12f + 0.75f * u);
                    float eyeDepth = 4f + 170f * u * v;
                    depths[i] = (1f / eyeDepth - zBuffer.w) / zBuffer.z;
                }
            input = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            input.SetPixels(InputPixels); input.Apply(false, false);
            depth = new Texture2D(width, height, TextureFormat.RFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            depth.SetPixelData(depths, 0); depth.Apply(false, false);
            readback = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave };
            firstA = CreateTarget("Weather.Check.Reference.Particles");
            outputA = CreateTarget("Weather.Check.Reference.Atmosphere");
            firstB = CreateTarget("Weather.Check.Current.Particles");
            outputB = CreateTarget("Weather.Check.Current.Atmosphere");
            sourceHandle = RTHandles.Alloc(input);
            firstHandleA = RTHandles.Alloc(firstA);
            firstHandleB = RTHandles.Alloc(firstB);
            command = new CommandBuffer { name = "Weather GPU equation validation" };
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        RenderTexture CreateTarget(string name)
        {
            var target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            { name = name, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!target.Create())
            {
                UnityEngine.Object.DestroyImmediate(target);
                throw new InvalidOperationException("Could not create floating point validation target.");
            }
            return target;
        }

        void Begin(float time)
        {
            command.Clear();
            Vector4 screen = new Vector4(Width, Height, 1f + 1f / Width, 1f + 1f / Height);
            command.SetGlobalVector("_Time", new Vector4(time / 20f, time, time * 2f, time * 3f));
            command.SetGlobalVector("_ScreenParams", screen);
            command.SetGlobalVector("_ScaledScreenParams", screen);
            command.SetGlobalVector("_ZBufferParams", zBuffer);
            command.SetGlobalVector("_WorldSpaceCameraPos", new Vector4(0f, 2f, 0f, 1f));
            command.SetGlobalVector("unity_OrthoParams", Vector4.zero);
            command.SetGlobalVector("_RTHandleScale", Vector4.one);
            command.SetGlobalVector("_CameraDepthTexture_TexelSize", new Vector4(1f / Width, 1f / Height, Width, Height));
            command.SetGlobalTexture("_CameraDepthTexture", depth);
            command.SetGlobalMatrix("unity_MatrixV", view);
            command.SetGlobalMatrix("unity_MatrixInvVP", inverseVP);
        }

        void Draw(Material material, RTHandle source, RenderTexture target, int pass)
        {
            command.SetRenderTarget(target);
            command.SetViewport(new Rect(0, 0, Width, Height));
            command.ClearRenderTarget(false, true, Color.clear);
            Blitter.BlitTexture(command, source, IdentityScaleBias, material, pass);
        }

        public Color[][] RenderPair(Material reference, Material current, float time)
        {
            Begin(time);
            Draw(reference, sourceHandle, firstA, 0);
            Draw(reference, firstHandleA, outputA, 1);
            Draw(current, sourceHandle, firstB, 0);
            Draw(current, firstHandleB, outputB, 1);
            Graphics.ExecuteCommandBuffer(command);
            return new Color[][] { Read(outputA), Read(outputB) };
        }

        public Color[] RenderWeather(Material material, float time)
        {
            Begin(time);
            Draw(material, sourceHandle, firstB, 0);
            Draw(material, firstHandleB, outputB, 1);
            Graphics.ExecuteCommandBuffer(command);
            return Read(outputB);
        }

        Color[] Read(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                readback.Apply(false, false);
                return readback.GetPixels();
            }
            finally { RenderTexture.active = previous; }
        }

        public void Dispose()
        {
            if (command != null) command.Release();
            if (sourceHandle != null) sourceHandle.Release();
            if (firstHandleA != null) firstHandleA.Release();
            if (firstHandleB != null) firstHandleB.Release();
            if (firstA != null) firstA.Release();
            if (outputA != null) outputA.Release();
            if (firstB != null) firstB.Release();
            if (outputB != null) outputB.Release();
            UnityEngine.Object.DestroyImmediate(firstA); UnityEngine.Object.DestroyImmediate(outputA);
            UnityEngine.Object.DestroyImmediate(firstB); UnityEngine.Object.DestroyImmediate(outputB);
            UnityEngine.Object.DestroyImmediate(input); UnityEngine.Object.DestroyImmediate(depth);
            UnityEngine.Object.DestroyImmediate(readback);
        }
    }
}
