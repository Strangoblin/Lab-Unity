// ═══════════════════════════════════════════════════════════════
//  CameraChecks — actual URP camera tracing and production consumer regression.
// ═══════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Mine.RenderingDebug;

public class Script
{
    public static object Main() => SSGICameraChecks.Run(false);
    public static object NegativeControl() => SSGICameraChecks.Run(true);
    public static object HistoryNegativeControl() => SSGICameraChecks.HistoryNegativeControl();
    public static object Timing() => SSGICameraChecks.Timing();
    public static object TimingIsolatedSSGI() => SSGICameraChecks.Timing(true);
    public static object Jitter() => SSGICameraChecks.Jitter();
}

public static class SSGICameraChecks
{
    const string Root = "Assets/Mine/Shaders/PostProcess/SSGI/";
    const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int _assertions;
    static readonly List<string> Cases = new();
    static readonly List<string> Metrics = new();
    static string _output;

    sealed class RendererScope : IDisposable
    {
        readonly UniversalRenderPipelineAsset _pipeline;
        readonly FieldInfo _dataField;
        readonly FieldInfo _renderersField;
        readonly ScriptableRendererData[] _originalData;
        readonly ScriptableRenderer[] _originalRenderers;
        readonly ScriptableRendererFeature[][] _originalFeatures;
        readonly bool[][] _originalActive;
        readonly ScriptableRendererData[] _expandedData;
        readonly ScriptableRenderer[] _expandedRenderers;
        readonly ScriptableRendererData _clone;
        readonly List<ScriptableRendererFeature> _owned = new();
        public readonly int Index;

        public RendererScope()
        {
            _pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Require(_pipeline != null, "URP is required");
            _dataField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", InstanceFields);
            _renderersField = typeof(UniversalRenderPipelineAsset).GetField("m_Renderers", InstanceFields);
            _originalData = (ScriptableRendererData[])_dataField.GetValue(_pipeline);
            _originalRenderers = (ScriptableRenderer[])_renderersField.GetValue(_pipeline);
            _originalFeatures = _originalData.Select(data => data.rendererFeatures.ToArray()).ToArray();
            _originalActive = _originalFeatures.Select(features => features.Select(feature => feature.isActive).ToArray()).ToArray();
            Require(_originalData.Length == _originalRenderers.Length, "Renderer arrays must already be initialized");
            Index = _originalData.Length;
            _clone = UnityEngine.Object.Instantiate(_originalData[0]);
            _clone.name = "SSGI.Validation.DisposableRenderer";
            _clone.hideFlags = HideFlags.HideAndDontSave;
            _clone.rendererFeatures.Clear();
            _expandedData = new ScriptableRendererData[Index + 1];
            _expandedRenderers = new ScriptableRenderer[Index + 1];
            Array.Copy(_originalData, _expandedData, Index);
            Array.Copy(_originalRenderers, _expandedRenderers, Index);
            _expandedData[Index] = _clone;
            _dataField.SetValue(_pipeline, _expandedData);
            _renderersField.SetValue(_pipeline, _expandedRenderers);
        }

        public void Use(params ScriptableRendererFeature[] features)
        {
            _clone.rendererFeatures.Clear();
            foreach (ScriptableRendererFeature feature in features)
            {
                if (!_owned.Contains(feature)) _owned.Add(feature);
                feature.hideFlags = HideFlags.HideAndDontSave;
                _clone.rendererFeatures.Add(feature);
            }
            _clone.SetDirty();
            _pipeline.GetRenderer(Index);
        }

        public void Dispose()
        {
            bool unchanged = ReferenceEquals(_dataField.GetValue(_pipeline), _expandedData)
                && ReferenceEquals(_renderersField.GetValue(_pipeline), _expandedRenderers);
            if (!unchanged)
                throw new InvalidOperationException("Concurrent renderer-array change; refusing an unsafe restoration");
            _expandedRenderers[Index]?.Dispose();
            _dataField.SetValue(_pipeline, _originalData);
            _renderersField.SetValue(_pipeline, _originalRenderers);
            foreach (ScriptableRendererFeature feature in _owned)
            {
                feature.Dispose();
                UnityEngine.Object.DestroyImmediate(feature);
            }
            UnityEngine.Object.DestroyImmediate(_clone);
            for (int renderer = 0; renderer < _originalData.Length; renderer++)
            {
                Require(_originalData[renderer].rendererFeatures.SequenceEqual(_originalFeatures[renderer]),
                    "Original renderer Feature references changed");
                Require(_originalData[renderer].rendererFeatures.Select(feature => feature.isActive)
                    .SequenceEqual(_originalActive[renderer]), "Original renderer Feature Active states changed");
                Require(ReferenceEquals(_expandedRenderers[renderer], _originalRenderers[renderer]),
                    "Original renderer instance changed");
            }
        }
    }

    sealed class SceneScope : IDisposable
    {
        public readonly Scene Scene;
        public readonly Camera Camera;
        public readonly Material Material;
        public readonly List<GameObject> Geometry = new();
        public readonly Cubemap Sky;

        public SceneScope(int renderer)
        {
            Scene = EditorSceneManager.NewPreviewScene();
            var cameraObject = new GameObject("SSGI.Validation.Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, Scene);
            Camera = cameraObject.AddComponent<Camera>();
            Camera.scene = Scene;
            Camera.enabled = false;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 20f;
            Camera.fieldOfView = 60f;
            Camera.rect = new Rect(0, 0, 1, 1);
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.black;
            Camera.allowHDR = true;
            Camera.allowMSAA = false;
            Camera.allowDynamicResolution = false;
            Camera.useOcclusionCulling = false;
            UniversalAdditionalCameraData cameraData = Camera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(renderer);
            cameraData.renderPostProcessing = false;
            cameraData.renderType = CameraRenderType.Base;
            cameraData.antialiasing = AntialiasingMode.None;
            cameraData.volumeLayerMask = 0;
            Material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Material.SetColor("_BaseColor", new Color(0.5f, 0.3f, 0.2f));
            AddCube(new Vector3(0, 0, 5), new Vector3(40, 30, 1));
            var lightObject = new GameObject("SSGI.Validation.Light");
            SceneManager.MoveGameObjectToScene(lightObject, Scene);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2f;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(25, -20, 0);
            Sky = new Cubemap(8, TextureFormat.RGBAFloat, true);
            foreach (CubemapFace face in Enum.GetValues(typeof(CubemapFace)))
                if (face != CubemapFace.Unknown)
                    Sky.SetPixels(Enumerable.Repeat(new Color(0.2f, 0.4f, 0.6f, 1), 64).ToArray(), face);
            Sky.Apply(true, false);
        }

        public GameObject AddCube(Vector3 position, Vector3 scale)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(cube, Scene);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = Material;
            Geometry.Add(cube);
            return cube;
        }

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(Sky);
            UnityEngine.Object.DestroyImmediate(Material);
            EditorSceneManager.ClosePreviewScene(Scene);
        }
    }

    public static object Run(bool injectFault)
    {
        _assertions = 0;
        Cases.Clear();
        Metrics.Clear();
        string task = DateTime.Now.ToString("yyyy-MM-dd") + "-ssgi-camera-"
            + Guid.NewGuid().ToString("N").Substring(0, 8);
        _output = "tmp/sessions/" + task + "/output";
        Directory.CreateDirectory(_output);
        bool sceneDirty = SceneManager.GetActiveScene().isDirty;
        Material adapter = null;
        try
        {
            foreach (string shaderPath in new[] { "DiffuseGI/DiffuseGI.shader", "SpecularGI/SpecularGI.shader",
                "AO/AO.shader", "SSGITemporal.shader", "SSGIComposite.shader", "RuntimeFixture/Adapter.shader",
                "RuntimeFixture/BaselineDiffuse.shader", "RuntimeFixture/BaselineSpecular.shader" })
            {
                AssetDatabase.ImportAsset(Root + shaderPath, ImportAssetOptions.ForceUpdate);
                Shader shader = Load(shaderPath);
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Require(message.severity.ToString() != "Error", shaderPath + ": " + message.message);
            }
            using (var renderer = new RendererScope())
            using (var scene = new SceneScope(renderer.Index))
            {
                adapter = new Material(Load("RuntimeFixture/Adapter.shader"));
                var probe = ScriptableObject.CreateInstance<DebugOutputFeature>();
                probe.settings.debug = true;
                probe.settings.requireDepth = true;
                probe.settings.debugMaterial = adapter;
                renderer.Use(probe);
                CheckAdapter(scene, adapter, injectFault);
                if (!injectFault)
                {
                    scene.AddCube(new Vector3(0.9f, -0.1f, 2.8f), new Vector3(0.7f, 1.2f, 0.7f));
                    scene.AddCube(new Vector3(0, -1.2f, 3), new Vector3(10, 0.15f, 8));
                    CheckConsumers(renderer, scene);
                    CheckHistory(renderer, scene);
                }
            }
            Require(SceneManager.GetActiveScene().isDirty == sceneDirty, "Active scene dirty state changed");
            if (injectFault)
                throw new InvalidOperationException("Injected camera distance fault was not detected");
            WriteResult(true, false, null);
            return new { passed = true, assertions = _assertions, cases = Cases.Count, output = _output };
        }
        catch (Exception exception)
        {
            bool detected = injectFault && exception.Message.StartsWith("DDA world distance");
            WriteResult(false, detected, exception.Message);
            if (detected)
                return new { detected = true, assertions = _assertions, error = exception.Message, output = _output };
            throw new Exception("SSGI camera checks failed; " + _output, exception);
        }
        finally
        {
            if (adapter != null) UnityEngine.Object.DestroyImmediate(adapter);
        }
    }


    public static object Jitter()
    {
        _assertions = 0;
        _output = "tmp/sessions/2026-10-13-diffuse-jitter-r1/output";
        Directory.CreateDirectory(_output);
        bool originalDirty = SceneManager.GetActiveScene().isDirty;
        var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        var data = (ScriptableRendererData[])typeof(UniversalRenderPipelineAsset)
            .GetField("m_RendererDataList", InstanceFields).GetValue(pipeline);
        SSGIFeature source = data[0].rendererFeatures.OfType<SSGIFeature>().Single();
        string originalSettings = EditorJsonUtility.ToJson(source);
        Require(!Application.isPlaying && source.isActive, "Active SSGI in Edit Mode required");
        Require(data[0].rendererFeatures.All(item => !item.isActive || item == source), "Another active Feature requires coordination");
        Require(source.settings.performance.diffuseGI == SSGIQuality.Medium, "Expected Medium Diffuse");
        Scene preview = default;
        GameObject cameraObject = null;
        var results = new List<object>();
        string errorMessage = null;
        Camera original = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : Camera.main;
        Require(original != null, "A source camera is required");
        Vector2Int size = new(original.pixelWidth, original.pixelHeight);
        try
        {
            using (var renderer = new RendererScope())
            {
                SSGIFeature feature = UnityEngine.Object.Instantiate(source);
                renderer.Use(feature);
                preview = EditorSceneManager.NewPreviewScene();
                cameraObject = new GameObject("DiffuseGI.Jitter.DisposableCamera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(original);
                camera.cameraType = CameraType.Game;
                camera.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                camera.scene = SceneManager.GetActiveScene();
                camera.enabled = false;
                camera.targetTexture = null;
                camera.allowDynamicResolution = false;
                camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = (float)size.x / size.y;
                UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.SetRenderer(renderer.Index);
                cameraData.renderType = CameraRenderType.Base;
                cameraData.renderPostProcessing = false;
                cameraData.antialiasing = AntialiasingMode.None;
                results.Add(JitterBlock(camera, feature, size, SSGITraceBackend.DDA, 0f, false, 0.15f));
                results.Add(JitterBlock(camera, feature, size, SSGITraceBackend.DDA, 0f, true, 0.15f));
                results.Add(JitterBlock(camera, feature, size, SSGITraceBackend.Ray, 0f, false, 0.15f));
                results.Add(JitterBlock(camera, feature, size, SSGITraceBackend.Ray, 0f, true, 0.15f));
                results.Add(JitterBlock(camera, feature, size, SSGITraceBackend.DDA, 0.5f, false, 0.15f));
                results.Add(JitterBlock(camera, feature, size, SSGITraceBackend.Ray, 0.5f, false, 0.15f));
                results.Add(JitterBlock(camera, feature, size, SSGITraceBackend.DDA, 0.9f, false, 0.15f));
                results.Add(JitterBlock(camera, feature, size, SSGITraceBackend.DDA, 0.5f, false, 0.5f));
            }
        }
        catch (Exception error) { errorMessage = error.ToString(); }
        finally
        {
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
        bool restored = originalSettings == EditorJsonUtility.ToJson(source)
            && originalDirty == SceneManager.GetActiveScene().isDirty;
        var report = new
        {
            passed = errorMessage == null && restored && results.Count == 8,
            sourceCamera = original.name,
            sourceCameraType = original.cameraType.ToString(),
            size = new { width = size.x, height = size.y }, pose = original.transform.position.ToString(),
            sourceSettings = originalSettings, results, restored, error = errorMessage,
            metric = "Linear diffuse history color readback; static copied pose; no transparent/postprocessing overlay",
            device = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType
        };
        File.WriteAllText(_output + "/jitter.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        Require(report.passed, "Jitter diagnostic failed: " + errorMessage);
        return new { report.passed, cases = results.Count, output = _output + "/jitter.json" };
    }

    static object JitterBlock(Camera camera, SSGIFeature feature, Vector2Int size,
        SSGITraceBackend backend, float blend, bool frozen, float sigma)
    {
        feature.settings.diffuseGI.traceBackend = backend;
        feature.settings.diffuseGI.depthSigma = sigma;
        feature.settings.temporal.diffuseGI = blend;
        feature.settings.debug = SSGIFeature.DebugMode.DiffuseGI;
        feature.Create();
        object pass = Field(feature, "_pass");
        object temporal = Field(pass, "_temporal");
        var histories = (IDictionary)Field(temporal, "_histories");
        var material = (Material)Field(pass, "_diffuseMaterial");
        var temporalMaterial = ((Material[])Field(temporal, "_signalMaterials"))[1];
        var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(size.x, size.y, TextureFormat.RGBAFloat, false, true);
        RenderTexture originalActive = RenderTexture.active;
        int pixelCount = size.x * size.y;
        var sums = new double[pixelCount];
        var squares = new double[pixelCount];
        Color[] previous = null;
        bool[] mask = null;
        double squaredDifference = 0;
        var frames = new List<int>();
        var valid = new List<float>();
        var shaderTimes = new List<float[]>();
        const int warmup = 32;
        const int samples = 16;
        int originalFrame = Time.frameCount;
        try
        {
            Require(target.Create(), "Jitter target creation");
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "Jitter render request support");
            for (int sample = -warmup; sample < samples; ++sample)
            {
                if (frozen && histories.Contains(camera.GetInstanceID()))
                {
                    object previousHistory = histories[camera.GetInstanceID()];
                    previousHistory.GetType().GetField("frameIndex", InstanceFields).SetValue(previousHistory, 0);
                }
                RenderPipeline.SubmitRenderRequest(camera, request);
                if (sample < 0) continue;
                object history = histories[camera.GetInstanceID()];
                int side = ((int[])Field(history, "lastColorSide"))[1];
                RTHandle signal = ((RTHandle[,])Field(history, "color"))[1, side];
                Require(signal != null && signal.rt.width == size.x && signal.rt.height == size.y, "Full actual diffuse signal");
                if (mask == null)
                {
                    RenderTexture.active = ((RTHandle[])Field(history, "depth"))[side].rt;
                    readback.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0, false);
                    mask = readback.GetPixels().Select(color => color.r > 0f && color.r < camera.farClipPlane).ToArray();
                }
                RenderTexture.active = signal.rt;
                readback.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0, false);
                Color[] colors = readback.GetPixels();
                Finite(colors, "Jitter actual history signal");
                for (int pixel = 0; pixel < pixelCount; ++pixel)
                {
                    double luminance = JitterLuminance(colors[pixel]);
                    sums[pixel] += luminance;
                    squares[pixel] += luminance * luminance;
                    if (previous != null && mask[pixel])
                    {
                        double difference = luminance - JitterLuminance(previous[pixel]);
                        squaredDifference += difference * difference;
                    }
                }
                previous = colors;
                frames.Add(material.GetInt("_DiffuseGIFrameIndex"));
                valid.Add(temporalMaterial.GetFloat("_SSGIHistoryValid"));
                Vector4 shaderTime = Shader.GetGlobalVector("_Time");
                shaderTimes.Add(new[] { shaderTime.x, shaderTime.y, shaderTime.z, shaderTime.w });
                Close(material.GetInt("_GIRayCount"), 6, 0, "Actual Medium ray count");
                Close(material.GetInt("_GIStepCount"), 48, 0, "Actual Medium step count");
                Close(temporalMaterial.GetFloat("_SSGITemporalBlend"), blend, 0.00001f, "Actual requested temporal blend");
                Require(material.IsKeywordEnabled("SSGI_TRACE_RAY") == (backend == SSGITraceBackend.Ray), "Actual jitter backend");
            }
            int surfacePixels = mask.Count(surface => surface);
            Require(surfacePixels > 0 && frames.Count == samples, "Nonempty surface and frame matrix");
            double mean = 0;
            double variance = 0;
            int activePixels = 0;
            for (int pixel = 0; pixel < pixelCount; ++pixel)
            {
                if (!mask[pixel]) continue;
                double average = sums[pixel] / samples;
                mean += average;
                variance += Math.Max(0, squares[pixel] / samples - average * average);
                if (average > 0.02) activePixels++;
            }
            Require(activePixels > 0, "Nonempty meaningful diffuse signal");
            Require(frames.SequenceEqual(frozen ? Enumerable.Repeat(0, samples) : Enumerable.Range(warmup, samples)), "Actual frozen/advancing frame sequence");
            mean /= surfacePixels;
            double frameRms = Math.Sqrt(squaredDifference / surfacePixels / (samples - 1));
            var stats = new { backend = backend.ToString(), blend, frozen, sigma, warmup, samples, surfacePixels, activePixels,
                meanLuminance = mean, frameRms, relativeFrameRms = frameRms / Math.Max(mean, 0.000001),
                rmsPixelStdDev = Math.Sqrt(variance / surfacePixels), frames, historyValid = valid,
                shaderTimes, editorFrameStart = originalFrame, editorFrameEnd = Time.frameCount };
            File.WriteAllText(_output + "/" + backend + "-" + blend.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "-" + frozen + "-" + sigma.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json",
                Newtonsoft.Json.JsonConvert.SerializeObject(stats, Newtonsoft.Json.Formatting.Indented));
            return stats;
        }
        finally
        {
            RenderTexture.active = originalActive;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(readback);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    static double JitterLuminance(Color color) => color.r * 0.2126 + color.g * 0.7152 + color.b * 0.0722;

    [Serializable]
    sealed class TimingReport
    {
        public bool passed;
        public string metric = "GPU-synchronized render-request wall milliseconds; not GPU timestamps";
        public string device;
        public string scene;
        public string settings;
        public bool isolatedSSGI;
        public string originalFeatures;
        public bool sceneAndSettingsRestored;
        public int samplesPerBlock = 12;
        public int warmupPerBlock = 8;
        public int rounds = 3;
        public List<TimingBlock> blocks = new();
        public string error;
    }

    [Serializable]
    sealed class TimingBlock
    {
        public string variant;
        public int width;
        public int height;
        public int round;
        public int diffuseBudget;
        public int specularBudget;
        public int firstRayFrame;
        public double[] submitMilliseconds;
        public double[] synchronizedMilliseconds;
    }

    public static object Timing(bool isolatedSSGI = false)
    {
        _assertions = 0;
        string task = DateTime.Now.ToString("yyyy-MM-dd") + "-ssgi-timing-"
            + Guid.NewGuid().ToString("N").Substring(0, 8);
        _output = "tmp/sessions/" + task + "/output";
        Directory.CreateDirectory(_output);
        var report = new TimingReport
        {
            device = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
            scene = SceneManager.GetActiveScene().path,
            isolatedSSGI = isolatedSSGI
        };
        bool sceneDirty = SceneManager.GetActiveScene().isDirty;
        Scene preview = default;
        GameObject cameraObject = null;
        SSGIFeature source = null;
        try
        {
            Camera original = Camera.main;
            Require(original != null && !Application.isPlaying, "Timing requires the actual Main Camera in Edit Mode");
            var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            var allData = (ScriptableRendererData[])typeof(UniversalRenderPipelineAsset)
                .GetField("m_RendererDataList", InstanceFields).GetValue(pipeline);
            UniversalAdditionalCameraData originalData = original.GetUniversalAdditionalCameraData();
            int originalIndex = (int)typeof(UniversalAdditionalCameraData)
                .GetField("m_RendererIndex", InstanceFields).GetValue(originalData);
            if (originalIndex < 0)
                originalIndex = (int)typeof(UniversalRenderPipelineAsset)
                    .GetField("m_DefaultRendererIndex", InstanceFields).GetValue(pipeline);
            Require(originalIndex == 0, "Existing renderer scope clones index zero; refusing a different source");
            source = allData[originalIndex].rendererFeatures.OfType<SSGIFeature>().SingleOrDefault();
            Require(source != null, "The source renderer must contain exactly one SSGI Feature");
            report.settings = EditorJsonUtility.ToJson(source);
            report.originalFeatures = Newtonsoft.Json.JsonConvert.SerializeObject(allData[originalIndex].rendererFeatures
                .Select(item => new { name = item.name, type = item.GetType().FullName, active = item.isActive }));
            if (!isolatedSSGI)
            {
                Require(source.isActive, "SSGI is inactive; use the explicit isolated-SSGI timing mode or leave the original configuration unchanged");
                Require(allData[originalIndex].rendererFeatures.All(item => !item.isActive || item == source),
                    "Timing clone would omit another active Feature; use explicit isolated-SSGI mode");
            }
            Require(source.settings.performance.diffuseGI == SSGIQuality.Medium
                && source.settings.performance.specularGI == SSGIQuality.Medium,
                "Timing comparisons require the observed Medium configuration");
            var sizes = new[] { new Vector2Int(original.pixelWidth, original.pixelHeight), new Vector2Int(1920, 1080) }.Distinct();
            foreach (string path in new[] { "DiffuseGI/DiffuseGI.shader", "SpecularGI/SpecularGI.shader" })
            {
                foreach (var message in ShaderUtil.GetShaderMessages(Load(path)))
                    Require(message.severity.ToString() != "Error", path + ": " + message.message);
            }
            using (var renderer = new RendererScope())
            {
                var feature = UnityEngine.Object.Instantiate(source);
                if (isolatedSSGI) feature.SetActive(true);
                renderer.Use(feature);
                preview = EditorSceneManager.NewPreviewScene();
                cameraObject = new GameObject("SSGI.Timing.DisposableCamera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(original);
                camera.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                camera.scene = original.scene;
                camera.enabled = false;
                camera.targetTexture = null;
                camera.allowDynamicResolution = false;
                UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(originalData), cameraData);
                cameraData.SetRenderer(renderer.Index);
                cameraData.renderType = CameraRenderType.Base;
                foreach (Vector2Int size in sizes)
                    TimeResolution(camera, feature, source, size, report);
            }
        }
        catch (Exception error) { report.error = error.ToString(); }
        finally
        {
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            report.sceneAndSettingsRestored = sceneDirty == SceneManager.GetActiveScene().isDirty
                && source != null && report.settings == EditorJsonUtility.ToJson(source);
            report.passed = report.error == null && report.sceneAndSettingsRestored && report.blocks.Count >= 24;
            File.WriteAllText(_output + "/timing.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        }
        Require(report.passed, "Timing failed; see " + _output + "/timing.json: " + report.error);
        return new { passed = report.passed, reportPath = _output + "/timing.json", blocks = report.blocks.Count };
    }

    static void TimeResolution(Camera camera, SSGIFeature feature, SSGIFeature source,
        Vector2Int size, TimingReport report)
    {
        var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            antiAliasing = 1, useMipMap = false, useDynamicScale = false
        };
        var pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            Require(target.Create(), "Timing target creation");
            camera.aspect = (float)size.x / size.y;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "Timing request support");
            for (int round = 0; round < report.rounds; round++)
            {
                for (int sequence = 0; sequence < 4; sequence++)
                {
                    int variant = (sequence + round) % 4;
                    feature.settings.diffuseGI.shader = source.settings.diffuseGI.shader;
                    feature.settings.specularGI.shader = source.settings.specularGI.shader;
                    feature.settings.diffuseGI.traceBackend = variant == 0 || variant == 2 ? SSGITraceBackend.Ray : SSGITraceBackend.DDA;
                    feature.settings.specularGI.traceBackend = variant == 0 || variant == 3 ? SSGITraceBackend.Ray : SSGITraceBackend.DDA;
                    feature.Create();
                    for (int warmup = 0; warmup < report.warmupPerBlock; warmup++)
                    {
                        RenderPipeline.SubmitRenderRequest(camera, request);
                        RenderTexture.active = target;
                        pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                    }
                    Material diffuse = (Material)Field(Field(feature, "_pass"), "_diffuseMaterial");
                    Material specular = (Material)Field(Field(Field(feature, "_pass"), "_specularPass"), "_material");
                    Require(diffuse.IsKeywordEnabled("SSGI_TRACE_RAY") == (feature.settings.diffuseGI.traceBackend == SSGITraceBackend.Ray)
                        && specular.IsKeywordEnabled("SSGI_TRACE_RAY") == (feature.settings.specularGI.traceBackend == SSGITraceBackend.Ray),
                        "Actual backend keyword differs from timing case");
                    Require(diffuse.shader == feature.settings.diffuseGI.shader && specular.shader == feature.settings.specularGI.shader,
                        "Actual timing shader differs");
                    Require(diffuse.GetInt("_GIRayCount") == 6, "Medium ray count changed");
                    Require(diffuse.GetInt("_GIStepCount") == 48 && specular.GetVector("_TraceParams").w == 64,
                        "Actual timing budget differs from the configured Medium tier");
                    var block = new TimingBlock
                    {
                        variant = new[] { "Ray-Ray", "DDA48-DDA64", "Ray-DDA64", "DDA48-Ray" }[variant],
                        width = size.x, height = size.y, round = round,
                        diffuseBudget = 48,
                        specularBudget = 64,
                        firstRayFrame = -1,
                        submitMilliseconds = new double[report.samplesPerBlock],
                        synchronizedMilliseconds = new double[report.samplesPerBlock]
                    };
                    var stopwatch = new System.Diagnostics.Stopwatch();
                    for (int sample = 0; sample < report.samplesPerBlock; sample++)
                    {
                        stopwatch.Restart();
                        RenderPipeline.SubmitRenderRequest(camera, request);
                        block.submitMilliseconds[sample] = stopwatch.Elapsed.TotalMilliseconds;
                        RenderTexture.active = target;
                        pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                        stopwatch.Stop();
                        block.synchronizedMilliseconds[sample] = stopwatch.Elapsed.TotalMilliseconds;
                        if (sample == 0) block.firstRayFrame = diffuse.GetInt("_DiffuseGIFrameIndex");
                    }
                    report.blocks.Add(block);
                    if (round == 0 && (variant == 0 || variant == 1))
                        Finite(Capture(camera, size, block.variant + "-" + size.x + "x" + size.y + ".png"), "Timing image finite");
                }
            }
        }
        finally
        {
            RenderTexture.active = previousActive;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(pixel);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    static void CheckAdapter(SceneScope scene, Material material, bool injectFault)
    {
        Camera camera = scene.Camera;
        foreach (Vector2Int size in new[] { new Vector2Int(189, 101), new(321, 179), new(97, 61) })
        foreach (bool orthographic in new[] { false, true })
        {
            camera.orthographic = orthographic;
            camera.orthographicSize = 3f;
            camera.aspect = (float)size.x / size.y;
            material.SetInt("_TestMode", 2);
            Color layout = Center(Capture(camera, size));
            Close(layout.r, size.x, 0.1f, "Active depth width");
            Close(layout.g, size.y, 0.1f, "Active depth height");
            Require(layout.b >= layout.r && layout.a >= layout.g, "Physical depth covers active grid");
            Metrics.Add("{\"name\":\"depth-layout-" + size.x + "x" + size.y + "-ortho-" + orthographic
                + "\",\"activeWidth\":" + layout.r + ",\"activeHeight\":" + layout.g
                + ",\"physicalWidth\":" + layout.b + ",\"physicalHeight\":" + layout.a + "}");
            material.SetInt("_TestMode", 7);
            Color depth = Center(Capture(camera, size));
            Close(depth.r, 4.5f, 0.01f, "Actual camera plane depth");
            Close(depth.b, 1, 0.001f, "Actual surface validity");
            foreach (Vector3 direction in new[] { new Vector3(0, 0, 1), new(0.2f, 0.12f, 1), new(-0.3f, -0.16f, 1) })
            {
                Vector3 origin = new(0, 0, 2);
                Vector3 unit = direction.normalized;
                material.SetVector("_TestOrigin", origin);
                material.SetVector("_TestDirection", unit);
                material.SetVector("_TestParams", new Vector4(8, 0.15f, 2048, 0));
                material.SetInt("_TestMode", 0);
                material.SetInt("_TestInjectFault", injectFault ? 1 : 0);
                CoreUtils.SetKeyword(material, "SSGI_TRACE_RAY", false);
                Color dda = Center(Capture(camera, size));
                Close(dda.a, 0, 0.001f, "DDA plane HIT");
                float expected = (4.5f - origin.z) / unit.z;
                Close(dda.r, expected, 0.008f, "DDA world distance");
                Vector3 viewport = camera.WorldToViewportPoint(origin + unit * expected);
                Close(dda.g, viewport.x, 0.003f, "DDA hit U");
                Close(dda.b, viewport.y, 0.003f, "DDA hit V");
                foreach (int steps in new[] { 24, 48, 64, 32, 96 })
                {
                    material.SetVector("_TestParams", new Vector4(8, 0.15f, steps, 0));
                    CoreUtils.SetKeyword(material, "SSGI_TRACE_RAY", true);
                    Color ray = Center(Capture(camera, size));
                    material.SetInt("_TestMode", 1);
                    Color baseline = Center(Capture(camera, size));
                    Close(ray.a, baseline.a, 0.001f, "RAY baseline hit");
                    Close(ray.r, baseline.r, 0.008f, "RAY baseline distance");
                    Close(ray.g, baseline.g, 0.003f, "RAY baseline U");
                    Close(ray.b, baseline.b, 0.003f, "RAY baseline V");
                    material.SetInt("_TestMode", 0);
                }
            }
            Cases.Add("actual-camera-" + size + "-ortho-" + orthographic);
        }
        camera.orthographic = false;
        material.SetInt("_TestMode", 0);
        material.SetVector("_TestOrigin", new Vector3(0, 0, 2));
        material.SetVector("_TestDirection", new Vector3(0.6f, 0, 1).normalized);
        material.SetVector("_TestParams", new Vector4(8, 0.15f, 1, 0));
        CoreUtils.SetKeyword(material, "SSGI_TRACE_RAY", false);
        Color budget = Center(Capture(camera, new Vector2Int(189, 101)));
        Require(budget.a != 3, "Fixed-sample DDA must complete, not report grid BUDGET");
        Cases.Add("actual-camera-single-sample-dda");
        material.SetVector("_TestParams", new Vector4(15, 0.15f, 2048, 0));
        foreach (Vector3 origin in new[] { new Vector3(0, 0, 0.02f), new(-10, 0, 2) })
        {
            Vector3 direction = origin.x == 0 ? Vector3.forward : new Vector3(4, 0, 1).normalized;
            material.SetVector("_TestOrigin", origin);
            material.SetVector("_TestDirection", direction);
            Color clipped = Center(Capture(camera, new Vector2Int(189, 101)));
            Close(clipped.a, 0, 0.001f, "Clipped camera ray HIT");
            Close(clipped.r, (4.5f - origin.z) / direction.z, 0.015f, "Clipped distance relative to original origin");
        }
        Cases.Add("actual-camera-near-and-screen-clipping");
        float pixelWorldWidth = 2f * 3.5f * Mathf.Tan(30f * Mathf.Deg2Rad) / 101;
        GameObject thin = scene.AddCube(new Vector3(1.2f, 0, 3.5f), new Vector3(pixelWorldWidth * 0.4f, 0.5f, 0.02f));
        try
        {
            material.SetVector("_TestOrigin", new Vector3(0, 0, 2));
            material.SetVector("_TestDirection", new Vector3(0.8f, 0, 1).normalized);
            material.SetVector("_TestParams", new Vector4(8, 0.02f, 2048, 0));
            Color thinHit = Center(Capture(camera, new Vector2Int(189, 101)));
            Close(thinHit.a, 0, 0.001f, "Thin camera geometry HIT");
            Close(thinHit.r, 1.49f * Mathf.Sqrt(1.64f), 0.02f, "Thin camera geometry first-hit distance");
            material.SetVector("_TestParams", new Vector4(8, 0.02f, 8, 0));
            Color sparseDda = Center(Capture(camera, new Vector2Int(189, 101)));
            Require(sparseDda.a != 0 || sparseDda.r > thinHit.r + 0.5f,
                "Sparse DDA must demonstrate the retired complete-coverage guarantee");
            CoreUtils.SetKeyword(material, "SSGI_TRACE_RAY", true);
            Color sparseRay = Center(Capture(camera, new Vector2Int(189, 101)));
            Require(sparseRay.a != 0 || sparseRay.r > thinHit.r + 0.5f,
                "Sparse RAY control must miss the thin foreground, even if it hits the backdrop");
            Cases.Add("actual-camera-dense-versus-sparse-dda-ray-sampling");
        }
        finally { UnityEngine.Object.DestroyImmediate(thin); }
    }

    static void CheckConsumers(RendererScope renderer, SceneScope scene)
    {
        var size = new Vector2Int(189, 101);
        var diffuse = ScriptableObject.CreateInstance<DiffuseGIFeature>();
        diffuse.controls.receiverAlbedo = Color.white;
        diffuse.controls.debug = DiffuseGIFeature.DebugMode.Indirect;
        var specular = ScriptableObject.CreateInstance<SpecularGIFeature>();
        specular.settings.skyCubemap = scene.Sky;
        specular.controls.debug = SpecularGIFeature.Settings.DebugMode.Spatial;
        var integrated = ScriptableObject.CreateInstance<SSGIFeature>();
        integrated.settings.compositeShader = Load("SSGIComposite.shader");
        integrated.settings.ao.shader = Load("AO/AO.shader");
        integrated.settings.diffuseGI.shader = Load("DiffuseGI/DiffuseGI.shader");
        integrated.settings.specularGI.shader = Load("SpecularGI/SpecularGI.shader");
        integrated.settings.specularGI.skyCubemap = scene.Sky;
        integrated.settings.intensity.ao = 0;
        foreach (SSGIQuality quality in Enum.GetValues(typeof(SSGIQuality)))
        {
            diffuse.controls.performance = quality;
            specular.controls.performance = quality;
            integrated.settings.performance.diffuseGI = quality;
            integrated.settings.performance.specularGI = quality;
            diffuse.settings.shader = Load("RuntimeFixture/BaselineDiffuse.shader");
            diffuse.settings.traceBackend = SSGITraceBackend.Ray;
            renderer.Use(diffuse);
            Color[] baselineDiffuse = Capture(scene.Camera, size);
            diffuse.settings.shader = Load("DiffuseGI/DiffuseGI.shader");
            diffuse.Create();
            Color[] rayDiffuse = Capture(scene.Camera, size);
            Compare(rayDiffuse, baselineDiffuse, 0.012f, "Diffuse RAY production regression " + quality);
            Require(Energy(rayDiffuse) > 0.00001, "Diffuse regression input must actually gather radiance");
            specular.settings.shader = Load("RuntimeFixture/BaselineSpecular.shader");
            specular.settings.traceBackend = SSGITraceBackend.Ray;
            renderer.Use(specular);
            Color[] baselineSpecular = Capture(scene.Camera, size);
            specular.settings.shader = Load("SpecularGI/SpecularGI.shader");
            specular.Create();
            Color[] raySpecular = Capture(scene.Camera, size);
            Compare(raySpecular, baselineSpecular, 0.012f, "Specular RAY production regression " + quality);
            foreach (SSGITraceBackend backend in Enum.GetValues(typeof(SSGITraceBackend)))
            {
                diffuse.settings.traceBackend = backend;
                renderer.Use(diffuse);
                Color[] standaloneDiffuse = Capture(scene.Camera, size,
                    quality == SSGIQuality.Medium ? "diffuse-" + backend + ".png" : null);
                Finite(standaloneDiffuse, "Standalone diffuse " + backend + quality);
                Require(Energy(standaloneDiffuse) > 0.00001, "Diffuse radiance " + backend + quality);
                Material diffuseMaterial = (Material)Field(Field(diffuse, "_pass"), "_material");
                int tierIndex = (int)quality;
                int expectedDiffuseSteps = new[] { 24, 48, 64 }[tierIndex];
                Close(diffuseMaterial.GetInt("_GIStepCount"), expectedDiffuseSteps, 0, "Diffuse effective budget");
                Close(diffuseMaterial.GetInt("_GIRayCount"), new[] { 4, 6, 8 }[tierIndex], 0, "Diffuse ray count unchanged");
                Require(diffuseMaterial.IsKeywordEnabled("SSGI_TRACE_RAY") == (backend == SSGITraceBackend.Ray), "Diffuse actual backend keyword");
                specular.settings.traceBackend = backend;
                renderer.Use(specular);
                Color[] standaloneSpecular = Capture(scene.Camera, size,
                    quality == SSGIQuality.Medium ? "specular-" + backend + ".png" : null);
                Finite(standaloneSpecular, "Standalone specular " + backend + quality);
                Require(Energy(standaloneSpecular) > 0.01, "Specular radiance " + backend + quality);
                Material specularMaterial = (Material)Field(Field(specular, "_pass"), "_material");
                int expectedSpecularSteps = new[] { 32, 64, 96 }[tierIndex];
                Close(specularMaterial.GetVector("_TraceParams").w, expectedSpecularSteps, 0, "Specular effective budget");
                Require(specularMaterial.IsKeywordEnabled("SSGI_TRACE_RAY") == (backend == SSGITraceBackend.Ray), "Specular actual backend keyword");
                integrated.settings.diffuseGI.traceBackend = backend;
                integrated.settings.specularGI.traceBackend = backend;
                integrated.settings.debug = SSGIFeature.DebugMode.DiffuseGI;
                renderer.Use(integrated);
                Color[] combinedDiffuse = Capture(scene.Camera, size);
                Compare(combinedDiffuse, standaloneDiffuse, 0.012f, "Integrated / standalone diffuse " + backend + quality);
                integrated.settings.debug = SSGIFeature.DebugMode.SpecularGI;
                Color[] combinedSpecular = Capture(scene.Camera, size);
                Compare(combinedSpecular, standaloneSpecular, 0.012f, "Integrated / standalone specular " + backend + quality);
                Cases.Add("consumers-" + backend + "-" + quality);
            }
            Cases.Add("production-ray-baseline-" + quality);
        }
        integrated.settings.intensity.ao = 1;
        integrated.settings.debug = SSGIFeature.DebugMode.AO;
        renderer.Use(integrated);
        integrated.settings.diffuseGI.traceBackend = SSGITraceBackend.Ray;
        integrated.settings.specularGI.traceBackend = SSGITraceBackend.Ray;
        Color[] rayAO = Capture(scene.Camera, size);
        integrated.settings.diffuseGI.traceBackend = SSGITraceBackend.DDA;
        integrated.settings.specularGI.traceBackend = SSGITraceBackend.DDA;
        Color[] ddaAO = Capture(scene.Camera, size);
        Compare(ddaAO, rayAO, 0.00001f, "AO unaffected by GI backend");
        Cases.Add("ao-backend-isolation");
        var displaySize = new Vector2Int(640, 360);
        diffuse.controls.performance = SSGIQuality.Medium;
        specular.controls.performance = SSGIQuality.Medium;
        foreach (SSGITraceBackend backend in Enum.GetValues(typeof(SSGITraceBackend)))
        {
            diffuse.settings.traceBackend = backend;
            renderer.Use(diffuse);
            Color[] diffuseDisplay = Capture(scene.Camera, displaySize, "diffuse-medium-640-" + backend + ".png");
            Finite(diffuseDisplay, "Medium 640 diffuse " + backend);
            specular.settings.traceBackend = backend;
            renderer.Use(specular);
            Color[] specularDisplay = Capture(scene.Camera, displaySize, "specular-medium-640-" + backend + ".png");
            Finite(specularDisplay, "Medium 640 specular " + backend);
            Cases.Add("medium-640-camera-" + backend);
        }
    }

    static void CheckHistory(RendererScope renderer, SceneScope scene)
    {
        var feature = ScriptableObject.CreateInstance<SSGIFeature>();
        feature.settings.compositeShader = Load("SSGIComposite.shader");
        feature.settings.temporalShader = Load("SSGITemporal.shader");
        feature.settings.ao.shader = Load("AO/AO.shader");
        feature.settings.diffuseGI.shader = Load("DiffuseGI/DiffuseGI.shader");
        feature.settings.specularGI.shader = Load("SpecularGI/SpecularGI.shader");
        feature.settings.specularGI.skyCubemap = scene.Sky;
        feature.settings.performance.ao = SSGIQuality.Low;
        feature.settings.performance.diffuseGI = SSGIQuality.Low;
        feature.settings.performance.specularGI = SSGIQuality.Low;
        renderer.Use(feature);
        var size = new Vector2Int(189, 101);
        Capture(scene.Camera, size);
        Capture(scene.Camera, size);
        Flags(feature, 1, 1, 1, "History warmup");
        object temporal = Field(Field(feature, "_pass"), "_temporal");
        IDictionary histories = (IDictionary)Field(temporal, "_histories");
        object history = histories[scene.Camera.GetInstanceID()];
        var colors = ((RTHandle[,])Field(history, "color")).Cast<RTHandle>().ToArray();
        var depths = ((RTHandle[])Field(history, "depth")).ToArray();
        var materials = (Material[])Field(temporal, "_signalMaterials");
        Require(materials.Distinct().Count() == 3, "Separate signal materials");
        CheckKeyEquality(history);

        Action<Action, int, int, int, string> change = (mutate, ao, diffuse, specular, label) =>
        {
            mutate();
            Capture(scene.Camera, size);
            Flags(feature, ao, diffuse, specular, label);
            RTHandle[] nextColors = ((RTHandle[,])Field(history, "color")).Cast<RTHandle>().ToArray();
            RTHandle[] nextDepths = (RTHandle[])Field(history, "depth");
            Require(colors.SequenceEqual(nextColors) && depths.SequenceEqual(nextDepths), label + " must not reallocate histories");
            Capture(scene.Camera, size);
            Flags(feature, 1, 1, 1, label + " recovered");
            Cases.Add(label);
        };
        change(() => feature.settings.diffuseGI.traceBackend = SSGITraceBackend.Ray, 1, 0, 1, "diffuse-backend-ray");
        change(() => feature.settings.diffuseGI.traceBackend = SSGITraceBackend.DDA, 1, 0, 1, "diffuse-backend-dda");
        change(() => feature.settings.specularGI.traceBackend = SSGITraceBackend.Ray, 1, 1, 0, "specular-backend-ray");
        change(() => feature.settings.specularGI.traceBackend = SSGITraceBackend.DDA, 1, 1, 0, "specular-backend-dda");
        change(() => feature.settings.diffuseGI.maxDistance = 4, 1, 0, 1, "diffuse-distance");
        change(() => feature.settings.diffuseGI.thickness = 0.2f, 1, 0, 1, "diffuse-thickness");
        change(() => feature.settings.diffuseGI.normalBias = 0.04f, 1, 0, 1, "diffuse-bias");
        change(() => feature.settings.diffuseGI.depthSigma = 0.2f, 1, 0, 1, "diffuse-filter");
        change(() => feature.settings.performance.diffuseGI = SSGIQuality.Medium, 1, 0, 1, "diffuse-quality");
        change(() => feature.settings.specularGI.roughness = 0.2f, 1, 1, 0, "specular-roughness");
        change(() => feature.settings.performance.specularGI = SSGIQuality.Medium, 1, 1, 0, "specular-quality");
        change(() => feature.settings.specularGI.skyMaxMip = 5, 1, 1, 0, "specular-sky-mip");
        change(() => feature.settings.specularGI.skyCubemap = null, 1, 1, 0, "specular-sky-resource");
        change(() => feature.settings.intensity.diffuseGI = 0.5f, 1, 1, 1, "composite-intensity-does-not-invalidate");
        change(() => feature.settings.temporal.diffuseGI = 0.2f, 1, 1, 1, "blend-does-not-invalidate");
        var secondObject = new GameObject("SSGI.Validation.SecondCamera");
        SceneManager.MoveGameObjectToScene(secondObject, scene.Scene);
        try
        {
            Camera second = secondObject.AddComponent<Camera>();
            second.CopyFrom(scene.Camera);
            second.scene = scene.Scene;
            second.enabled = false;
            second.transform.position = new Vector3(0.2f, 0, 0);
            UniversalAdditionalCameraData additional = second.GetUniversalAdditionalCameraData();
            additional.SetRenderer(renderer.Index);
            additional.renderPostProcessing = false;
            Capture(second, size);
            Flags(feature, 0, 0, 0, "New camera has no eligible history");
            Capture(second, size);
            Flags(feature, 1, 1, 1, "Second camera warmup");
            Require(histories.Count == 2, "Two independent camera histories");
            Capture(scene.Camera, size);
            Flags(feature, 1, 1, 1, "Original camera history survives second camera");
            Cases.Add("history-multi-camera-isolation");
        }
        finally { UnityEngine.Object.DestroyImmediate(secondObject); }
        Capture(scene.Camera, new Vector2Int(193, 103));
        Flags(feature, 0, 0, 0, "Camera resize invalidates all histories");
        Cases.Add("history-camera-resize");
    }

    static void CheckKeyEquality(object history)
    {
        Type keyType = ((Array)Field(history, "keys")).GetType().GetElementType();
        MethodInfo equals = keyType.GetMethod("Equals", new[] { keyType });
        object[] arguments = { SSGITraceBackend.DDA, new Vector4(1, 2, 3, 4),
            new Vector4(5, 6, 7, 8), new Vector4(9, 10, 11, 12), 13, 14 };
        object baseline = Activator.CreateInstance(keyType, arguments);
        object same = Activator.CreateInstance(keyType, arguments);
        Require((bool)equals.Invoke(baseline, new[] { same }), "Equal effective history keys");
        for (int index = 0; index < arguments.Length; index++)
        {
            object[] changed = (object[])arguments.Clone();
            if (index == 0) changed[index] = SSGITraceBackend.Ray;
            else if (index < 4)
            {
                Vector4 vector = (Vector4)changed[index];
                vector.x += 0.000001f;
                changed[index] = vector;
            }
            else changed[index] = (int)changed[index] + 1;
            object other = Activator.CreateInstance(keyType, changed);
            Require(!(bool)equals.Invoke(baseline, new[] { other }), "Exact history key field " + index);
        }
        Cases.Add("exact-history-key-equality");
    }

    public static object HistoryNegativeControl()
    {
        _assertions = 0;
        Cases.Clear();
        Metrics.Clear();
        _output = "tmp/sessions/" + DateTime.Now.ToString("yyyy-MM-dd")
            + "-ssgi-history-control-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "/output";
        Directory.CreateDirectory(_output);
        try
        {
            using (var renderer = new RendererScope())
            using (var scene = new SceneScope(renderer.Index))
            {
                var feature = ScriptableObject.CreateInstance<SSGIFeature>();
                feature.settings.compositeShader = Load("SSGIComposite.shader");
                feature.settings.temporalShader = Load("SSGITemporal.shader");
                feature.settings.ao.shader = Load("AO/AO.shader");
                feature.settings.diffuseGI.shader = Load("DiffuseGI/DiffuseGI.shader");
                feature.settings.specularGI.shader = Load("SpecularGI/SpecularGI.shader");
                feature.settings.performance.diffuseGI = SSGIQuality.Low;
                renderer.Use(feature);
                var size = new Vector2Int(189, 101);
                Capture(scene.Camera, size);
                Capture(scene.Camera, size);
                Flags(feature, 1, 1, 1, "Control warmup");
                object temporal = Field(Field(feature, "_pass"), "_temporal");
                object history = ((IDictionary)Field(temporal, "_histories"))[scene.Camera.GetInstanceID()];
                Array keys = (Array)Field(history, "keys");
                object poisoned = keys.GetValue(1);
                poisoned.GetType().GetField("_backend", InstanceFields).SetValue(poisoned, SSGITraceBackend.Ray);
                poisoned.GetType().GetField("_sampling", InstanceFields).SetValue(poisoned, new Vector4(8, 4, 24, 0));
                keys.SetValue(poisoned, 1);
                feature.settings.diffuseGI.traceBackend = SSGITraceBackend.Ray;
                Capture(scene.Camera, size);
                Flags(feature, 1, 0, 1, "Injected wrong-backend history acceptance");
            }
            throw new InvalidOperationException("Poisoned history contract was not detected");
        }
        catch (Exception exception)
        {
            bool detected = exception.Message.StartsWith("Injected wrong-backend history acceptance channel 1");
            WriteResult(false, detected, exception.Message);
            if (!detected) throw;
            return new { detected, assertions = _assertions, error = exception.Message, output = _output };
        }
    }

    static object Field(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, InstanceFields);
        Require(field != null, "Missing reflected field " + name);
        return field.GetValue(target);
    }

    static void Flags(SSGIFeature feature, int ao, int diffuse, int specular, string label)
    {
        object temporal = Field(Field(feature, "_pass"), "_temporal");
        Material[] materials = (Material[])Field(temporal, "_signalMaterials");
        int[] expected = { ao, diffuse, specular };
        for (int channel = 0; channel < 3; channel++)
            Close(materials[channel].GetFloat("_SSGIHistoryValid"), expected[channel], 0.001f, label + " channel " + channel);
    }

    static double Energy(Color[] colors) => colors.Average(color => (double)color.r + color.g + color.b);

    static void Finite(Color[] colors, string label)
    {
        foreach (Color color in colors)
            Require(!float.IsNaN(color.r) && !float.IsInfinity(color.r)
                && !float.IsNaN(color.g) && !float.IsInfinity(color.g)
                && !float.IsNaN(color.b) && !float.IsInfinity(color.b)
                && !float.IsNaN(color.a) && !float.IsInfinity(color.a), label + " finite output");
    }

    static void Compare(Color[] actual, Color[] expected, float tolerance, string label)
    {
        Require(actual.Length == expected.Length && actual.Length > 0, label + " nonempty matching readbacks");
        float maximum = 0;
        for (int pixel = 0; pixel < actual.Length; pixel++)
        {
            maximum = Mathf.Max(maximum, Mathf.Abs(actual[pixel].r - expected[pixel].r),
                Mathf.Abs(actual[pixel].g - expected[pixel].g), Mathf.Abs(actual[pixel].b - expected[pixel].b));
        }
        Require(maximum <= tolerance, label + " maximum RGB difference " + maximum);
        Metrics.Add("{\"name\":\"" + label + "\",\"maxRgbDifference\":"
            + maximum.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}");
    }

    static Color[] Capture(Camera camera, Vector2Int size, string png = null)
    {
        camera.aspect = (float)size.x / size.y;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            antiAliasing = 1, useMipMap = false, useDynamicScale = false
        };
        Texture2D readback = null;
        try
        {
            Require(target.Create(), "Capture target creation");
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "SingleCameraRequest support");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            readback = new Texture2D(size.x, size.y, TextureFormat.RGBAFloat, false, true);
            readback.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0, false);
            readback.Apply(false, false);
            Color[] colors = readback.GetPixels();
            Require(colors.Length == size.x * size.y, "Full camera readback");
            if (png != null)
            {
                var display = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false, true);
                try
                {
                    display.SetPixels(colors.Select(color => new Color(Mathf.LinearToGammaSpace(color.r),
                        Mathf.LinearToGammaSpace(color.g), Mathf.LinearToGammaSpace(color.b), 1)).ToArray());
                    display.Apply();
                    File.WriteAllBytes(_output + "/" + png, display.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(display); }
            }
            return colors;
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    static Shader Load(string relative)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + relative);
        Require(shader != null, "Missing shader " + relative);
        return shader;
    }

    static Color Center(Color[] colors) => colors[colors.Length / 2];

    static void Require(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    static void Close(float actual, float expected, float tolerance, string message)
    {
        Require(!float.IsNaN(actual) && !float.IsInfinity(actual)
            && Mathf.Abs(actual - expected) <= tolerance, message + ": expected " + expected + ", actual " + actual);
    }

    static void WriteResult(bool passed, bool detected, string error)
    {
        File.WriteAllText(_output + "/results.json", "{\"passed\":" + (passed ? "true" : "false")
            + ",\"negativeControlDetected\":" + (detected ? "true" : "false")
            + ",\"device\":\"" + SystemInfo.graphicsDeviceType + "\",\"assertions\":" + _assertions
            + ",\"cases\":[" + string.Join(",", Cases.Select(value => "\"" + value + "\""))
            + "],\"metrics\":[" + string.Join(",", Metrics) + "],\"error\":\""
            + (error ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"}");
    }
}
