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
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Mine.RenderingDebug;

public partial class Script
{
    public static object Main() => SSGICameraChecks.Run(false);
    public static object NegativeControl() => SSGICameraChecks.Run(true);
    public static object HistoryNegativeControl() => SSGICameraChecks.HistoryNegativeControl();
    public static object SpatialNegativeControl() => SSGICameraChecks.Run(false, true);
    public static object MultiscaleNegativeControl() => SSGICameraChecks.MultiscaleNegativeControl();
    public static object SequenceComparison() => SSGICameraChecks.SequenceComparison();
    public static object MultiscaleComparison() => SSGICameraChecks.MultiscaleComparison();
    public static object Timing() => SSGICameraChecks.Timing();
    public static object TimingIsolatedSSGI() => SSGICameraChecks.Timing(true);
}

public static partial class SSGICameraChecks
{
    const string Root = "Assets/Mine/Shaders/PostProcess/SSGI/";
    const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int _assertions;
    static readonly List<string> Cases = new();
    static readonly List<string> Metrics = new();
    static string _output;

    sealed class TraceExportFeature : ScriptableRendererFeature
    {
        public Material material;
        TraceExportPass _pass;

        sealed class TraceExportPass : ScriptableRenderPass
        {
            sealed class Data { public Material material; public TextureHandle source; }
            readonly Material _material;
            public TraceExportPass(Material material)
            {
                _material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                TextureDesc descriptor = graph.GetTextureDesc(resources.activeColorTexture);
                descriptor.name = "SSGI.Validation.RawTrace";
                TextureHandle output = graph.CreateTexture(descriptor);
                using (var builder = graph.AddRasterRenderPass<Data>("SSGI.Validation.RawTrace", out var data))
                {
                    data.material = _material;
                    data.source = resources.activeColorTexture;
                    builder.UseTexture(data.source, AccessFlags.Read);
                    builder.UseGlobalTexture(Shader.PropertyToID("_GITraceTexture"), AccessFlags.Read);
                    builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                    builder.SetRenderFunc((Data pass, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, pass.source, new Vector4(1, 1, 0, 0), pass.material, 0));
                }
                resources.cameraColor = output;
            }
        }

        public override void Create() => _pass = new TraceExportPass(material);
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) => renderer.EnqueuePass(_pass);
    }

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

    public static object Run(bool injectFault, bool injectSpatialFault = false)
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
                "RuntimeFixture/BaselineDiffuse.shader", "RuntimeFixture/BaselineSpecular.shader",
                "RuntimeFixture/SpatialChecks.shader" })
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
                    CheckSpatial(renderer, scene, injectSpatialFault);
                    scene.AddCube(new Vector3(0.9f, -0.1f, 2.8f), new Vector3(0.7f, 1.2f, 0.7f));
                    scene.AddCube(new Vector3(0, -1.2f, 3), new Vector3(10, 0.15f, 8));
                    CheckConsumers(renderer, scene);
                    CheckHistory(renderer, scene);
                }
            }
            Require(SceneManager.GetActiveScene().isDirty == sceneDirty, "Active scene dirty state changed");
            if (injectFault)
                throw new InvalidOperationException("Injected camera distance fault was not detected");
            if (injectSpatialFault)
                throw new InvalidOperationException("Injected spatial filter fault was not detected");
            WriteResult(true, false, null);
            return new { passed = true, assertions = _assertions, cases = Cases.Count, output = _output };
        }
        catch (Exception exception)
        {
            bool detected = injectFault && exception.Message.StartsWith("DDA world distance")
                || injectSpatialFault && exception.Message.StartsWith("Spatial reference");
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

    // ═══════════════════════════════════════════════════════════════
    //  SpatialChecks.fragment — insert inside SSGICameraChecks.
    //  Drives the isolated SpatialChecks.shader through DebugOutputFeature and
    //  compares every GPU pixel against an independent CPU weighted reference.
    // ═══════════════════════════════════════════════════════════════

    const int SpatialWidth = 9;
    const int SpatialHeight = 7;
    const float SpatialInverseSigma = 2f;
    const float SpatialNormalPower = 32f;
    const float SpatialNormalValidity = 0.25f;
    const float SpatialFallbackFloor = 0.0001f;
    const float SpatialTolerance = 2e-5f;

    static void CheckSpatial(RendererScope renderer, SceneScope scene, bool injectFault)
    {
        Material probeMaterial = new Material(Load("RuntimeFixture/SpatialChecks.shader"));
        var probe = ScriptableObject.CreateInstance<DebugOutputFeature>();
        try
        {
            probe.settings.debug = true;
            probe.settings.requireDepth = true;
            probe.settings.debugMaterial = probeMaterial;
            renderer.Use(probe);
            probeMaterial.SetInt("_SpatialFault", injectFault ? 1 : 0);
            probeMaterial.SetVector("_TestSize", new Vector4(1f / SpatialWidth, 1f / SpatialHeight,
                SpatialWidth, SpatialHeight));
            CheckSpatialConstant(scene, probeMaterial);
            CheckSpatialImpulse(scene, probeMaterial);
            CheckSpatialStepDepth(scene, probeMaterial);
            CheckSpatialNormalDiscontinuity(scene, probeMaterial);
            CheckSpatialInvalidNormal(scene, probeMaterial);
            CheckSpatialSky(scene, probeMaterial);
            CheckSpatialBoundary(scene, probeMaterial);
            CheckSpatialRadius(scene, probeMaterial);
            CheckSpatialMultiscale(scene, probeMaterial, false);
        Color[] parity = new Color[SpatialWidth * SpatialHeight];
        for (int pixelY = 0; pixelY < SpatialHeight; ++pixelY)
        for (int pixelX = 0; pixelX < SpatialWidth; ++pixelX)
        {
            float value = (pixelX + pixelY) % 2;
            parity[SpatialIndex(pixelX, pixelY)] = new Color(value, 1f - value, 0.5f, 0.25f + value * 0.5f);
        }
        RunSpatialCase(scene, probeMaterial, "fractional-parity", parity, BuildUniformDepth(4f),
            BuildUniformNormal(Vector3.forward), new[] { 1.5f }, new[] { true });
            RunSpatialCase(scene, probeMaterial, "fallback",
                BuildUniformSource(new Color(8f, 3f, 1f, 1f)), BuildUniformDepth(4f),
                BuildUniformNormal(Vector3.zero), new[] { 1f }, new[] { false },
                new Color(0.25f, 0.5f, 0.75f, 0.125f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probeMaterial);
        }
    }

    static void CheckSpatialConstant(SceneScope scene, Material probeMaterial)
    {
        RunSpatialCase(scene, probeMaterial, "constant",
            BuildUniformSource(new Color(3f, 1.5f, 0.75f, 1f)),
            BuildUniformDepth(4f),
            BuildUniformNormal(Vector3.forward),
            new[] { 1f },
            new[] { true });
    }

    static void CheckSpatialImpulse(SceneScope scene, Material probeMaterial)
    {
        Color[] source = BuildUniformSource(new Color(0.125f, 0.25f, 0.5f, 1f));
        source[SpatialIndex(4, 3)] = new Color(12f, 6f, 3f, 0.5f);
        RunSpatialCase(scene, probeMaterial, "impulse",
            source,
            BuildUniformDepth(4f),
            BuildUniformNormal(Vector3.forward),
            new[] { 1f },
            new[] { true });
    }

    static void CheckSpatialStepDepth(SceneScope scene, Material probeMaterial)
    {
        float[] depth = BuildUniformDepth(4f);
        Color[] source = new Color[SpatialWidth * SpatialHeight];
        for (int pixelY = 0; pixelY < SpatialHeight; pixelY++)
        {
            for (int pixelX = 0; pixelX < SpatialWidth; pixelX++)
            {
                int index = SpatialIndex(pixelX, pixelY);
                depth[index] = pixelX <= 2 ? 2f : (pixelX == 3 ? 3f : 7f);
                source[index] = new Color(0.5f + pixelX * 0.25f, 0.5f + pixelY * 0.25f, 1f, 1f);
            }
        }
        RunSpatialCase(scene, probeMaterial, "step-depth",
            source,
            depth,
            BuildUniformNormal(Vector3.forward),
            new[] { 2f },
            new[] { true });
    }

    static void CheckSpatialNormalDiscontinuity(SceneScope scene, Material probeMaterial)
    {
        Vector3[] normal = BuildUniformNormal(Vector3.forward);
        Color[] source = BuildUniformSource(new Color(3f, 0.25f, 0f, 0.25f));
        for (int pixelY = 0; pixelY < SpatialHeight; pixelY++)
            for (int pixelX = 4; pixelX < SpatialWidth; pixelX++)
            {
                normal[SpatialIndex(pixelX, pixelY)] = Vector3.right;
                source[SpatialIndex(pixelX, pixelY)] = new Color(0f, 1f, 4f, 0.75f);
            }
        RunSpatialCase(scene, probeMaterial, "normal-discontinuity",
            source,
            BuildUniformDepth(5f),
            normal,
            new[] { 2f },
            new[] { true });
    }

    static void CheckSpatialInvalidNormal(SceneScope scene, Material probeMaterial)
    {
        Vector3[] normal = BuildUniformNormal(Vector3.forward);
        normal[SpatialIndex(2, 2)] = new Vector3(0f, 0f, 0.4f);
        Color[] source = BuildUniformSource(new Color(1f, 2f, 3f, 1f));
        source[SpatialIndex(2, 2)] = new Color(12f, 0f, 0f, 0.5f);
        for (int pixelY = 0; pixelY < SpatialHeight; pixelY++)
            for (int pixelX = 6; pixelX < SpatialWidth; pixelX++)
                normal[SpatialIndex(pixelX, pixelY)] = Vector3.zero;
        RunSpatialCase(scene, probeMaterial, "invalid-normal",
            source,
            BuildUniformDepth(4f),
            normal,
            new[] { 1f },
            new[] { true });
    }

    static void CheckSpatialSky(SceneScope scene, Material probeMaterial)
    {
        float[] depth = BuildUniformDepth(4f);
        depth[SpatialIndex(0, 0)] = 0f;
        Color[] source = BuildUniformSource(new Color(1.5f, 1.5f, 1.5f, 1f));
        source[SpatialIndex(0, 0)] = new Color(25f, 0f, 0f, 0.1f);
        for (int pixelY = 4; pixelY < SpatialHeight; pixelY++)
            for (int pixelX = 6; pixelX < SpatialWidth; pixelX++)
            {
                depth[SpatialIndex(pixelX, pixelY)] = 0f;
                source[SpatialIndex(pixelX, pixelY)] = new Color(25f, 0f, 0f, 0.1f);
            }
        RunSpatialCase(scene, probeMaterial, "sky",
            source,
            depth,
            BuildUniformNormal(Vector3.forward),
            new[] { 1f },
            new[] { true });
    }

    static void CheckSpatialBoundary(SceneScope scene, Material probeMaterial)
    {
        Color[] source = BuildUniformSource(new Color(0.1f, 0.1f, 0.1f, 1f));
        source[SpatialIndex(0, 0)] = new Color(8f, 4f, 2f, 1f);
        source[SpatialIndex(8, 6)] = new Color(2f, 4f, 8f, 1f);
        source[SpatialIndex(4, 0)] = new Color(4f, 0f, 0f, 1f);
        source[SpatialIndex(4, 6)] = new Color(0f, 4f, 0f, 1f);
        RunSpatialCase(scene, probeMaterial, "boundary",
            source,
            BuildUniformDepth(3f),
            BuildUniformNormal(Vector3.forward),
            new[] { 2f },
            new[] { true });
    }

    static void CheckSpatialRadius(SceneScope scene, Material probeMaterial)
    {
        Color[] source = BuildUniformSource(new Color(0.2f, 0.2f, 0.2f, 1f));
        source[SpatialIndex(4, 3)] = new Color(10f, 5f, 2.5f, 1f);
        Vector3[] normal = new Vector3[SpatialWidth * SpatialHeight];
        for (int pixelY = 0; pixelY < SpatialHeight; pixelY++)
            for (int pixelX = 0; pixelX < SpatialWidth; pixelX++)
                normal[SpatialIndex(pixelX, pixelY)] = new Vector3(pixelX - 4f, pixelY - 3f, 6f).normalized
                    * (0.5f + 0.25f * ((pixelX + pixelY) % 3));
        RunSpatialCase(scene, probeMaterial, "radius",
            source,
            BuildUniformDepth(4f),
            normal,
            new[] { 1f, 2f, 1f, 2f },
            new[] { true, true, false, false });
    }

    static void RunSpatialCase(SceneScope scene, Material probeMaterial, string caseLabel,
        Color[] source, float[] depth, Vector3[] normal, float[] radii, bool[] normalizeFlags, Color fallback = default)
    {
        Require(source.Length == SpatialWidth * SpatialHeight && depth.Length == source.Length
            && normal.Length == source.Length, "Spatial reference " + caseLabel + " grid shape");
        Require(radii.Length == normalizeFlags.Length && radii.Length > 0,
            "Spatial reference " + caseLabel + " variant list");
        Texture2D sourceTexture = BuildSpatialTexture(source);
        Texture2D depthTexture = BuildSpatialTexture(ToDepthPixels(depth));
        Texture2D normalTexture = BuildSpatialTexture(ToNormalPixels(normal));
        try
        {
            probeMaterial.SetTexture("_SpatialInput", sourceTexture);
            probeMaterial.SetTexture("_SpatialDepth", depthTexture);
            probeMaterial.SetTexture("_SpatialNormal", normalTexture);
            probeMaterial.SetVector("_TestFallback", fallback);
            for (int variant = 0; variant < radii.Length; variant++)
            {
                float radius = radii[variant];
                bool normalizeSampleNormals = normalizeFlags[variant];
                probeMaterial.SetVector("_TestGuide", new Vector4(SpatialInverseSigma, SpatialNormalPower,
                    radius, normalizeSampleNormals ? 1f : 0f));
                Color[] expected = SpatialReference(source, depth, normal, SpatialInverseSigma,
                    SpatialNormalPower, radius, normalizeSampleNormals, fallback);
                Color[] actual = Capture(scene.Camera, new Vector2Int(SpatialWidth, SpatialHeight));
                string label = "Spatial reference " + caseLabel
                + " radius " + radius.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)
                    + " normalize " + (normalizeSampleNormals ? "on" : "off");
                CompareSpatial(actual, expected, SpatialTolerance, label);
            }
            Cases.Add("spatial-" + caseLabel);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceTexture);
            UnityEngine.Object.DestroyImmediate(depthTexture);
            UnityEngine.Object.DestroyImmediate(normalTexture);
        }
    }

    static Color[] SpatialReference(Color[] source, float[] depth, Vector3[] normal,
        float inverseSigma, float normalPower, float radius, bool normalizeSampleNormals, Color fallback)
    {
        Color[] reference = new Color[SpatialWidth * SpatialHeight];
        for (int centerY = 0; centerY < SpatialHeight; centerY++)
        {
            for (int centerX = 0; centerX < SpatialWidth; centerX++)
            {
                int centerIndex = SpatialIndex(centerX, centerY);
                float centerRawDepth = depth[centerIndex];
                Vector3 centerNormal = normal[centerIndex];
                bool centerSurface = centerRawDepth > 0f;
                bool centerNormalValid = !normalizeSampleNormals
                    || Vector3.Dot(centerNormal, centerNormal) >= SpatialNormalValidity;
                if (!centerSurface || !centerNormalValid)
                {
                    reference[centerIndex] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }
                float centerEyeDepth = centerRawDepth;
                Vector3 centerDirection = normalizeSampleNormals ? centerNormal.normalized : centerNormal;
                float centerU = (centerX + 0.5f) / SpatialWidth;
                float centerV = (centerY + 0.5f) / SpatialHeight;
                Vector4 accumulated = Vector4.zero;
                float totalWeight = 0f;
                for (int offsetY = -2; offsetY <= 2; offsetY++)
                {
                    for (int offsetX = -2; offsetX <= 2; offsetX++)
                    {
                        float sampleU = Mathf.Clamp01(centerU + offsetX * (1f / SpatialWidth) * radius);
                        float sampleV = Mathf.Clamp01(centerV + offsetY * (1f / SpatialHeight) * radius);
                    int texelX = Mathf.Clamp(Mathf.FloorToInt(sampleU * SpatialWidth), 0, SpatialWidth - 1);
                    int texelY = Mathf.Clamp(Mathf.FloorToInt(sampleV * SpatialHeight), 0, SpatialHeight - 1);
                        int sampleIndex = SpatialIndex(texelX, texelY);
                        float sampleRawDepth = depth[sampleIndex];
                        if (!(sampleRawDepth > 0f))
                            continue;
                        Vector3 sampleNormal = normal[sampleIndex];
                        if (normalizeSampleNormals)
                        {
                            if (Vector3.Dot(sampleNormal, sampleNormal) < SpatialNormalValidity)
                                continue;
                            sampleNormal = sampleNormal.normalized;
                        }
                        float sampleEyeDepth = sampleRawDepth;
                        float depthWeight = Mathf.Pow(2f, -Mathf.Abs(sampleEyeDepth - centerEyeDepth) * inverseSigma);
                        float normalWeight = Mathf.Pow(Mathf.Clamp01(Vector3.Dot(centerDirection, sampleNormal)), normalPower);
                        float spatialWeight = 1f / (1f + offsetX * offsetX + offsetY * offsetY);
                        float weight = depthWeight * normalWeight * spatialWeight;
                    float sourceX = sampleU * SpatialWidth - 0.5f;
                    float sourceY = sampleV * SpatialHeight - 0.5f;
                    int left = Mathf.FloorToInt(sourceX);
                    int bottom = Mathf.FloorToInt(sourceY);
                    int leftClamped = Mathf.Clamp(left, 0, SpatialWidth - 1);
                    int rightClamped = Mathf.Clamp(left + 1, 0, SpatialWidth - 1);
                    int bottomClamped = Mathf.Clamp(bottom, 0, SpatialHeight - 1);
                    int topClamped = Mathf.Clamp(bottom + 1, 0, SpatialHeight - 1);
                    Color lower = Color.LerpUnclamped(source[SpatialIndex(leftClamped, bottomClamped)],
                        source[SpatialIndex(rightClamped, bottomClamped)], sourceX - left);
                    Color upper = Color.LerpUnclamped(source[SpatialIndex(leftClamped, topClamped)],
                        source[SpatialIndex(rightClamped, topClamped)], sourceX - left);
                    Color sampleValue = Color.LerpUnclamped(lower, upper, sourceY - bottom);
                        accumulated += new Vector4(sampleValue.r, sampleValue.g, sampleValue.b, sampleValue.a) * weight;
                        totalWeight += weight;
                    }
                }
                reference[centerIndex] = totalWeight > SpatialFallbackFloor
                    ? new Color(accumulated.x / totalWeight, accumulated.y / totalWeight,
                        accumulated.z / totalWeight, accumulated.w / totalWeight)
                    : fallback;
            }
        }
        return reference;
    }

    static void CompareSpatial(Color[] actual, Color[] expected, float tolerance, string label)
    {
        Require(actual.Length == expected.Length && actual.Length > 0, label + " nonempty matching readbacks");
        float maximumDifference = 0f;
        for (int pixel = 0; pixel < actual.Length; pixel++)
        {
            maximumDifference = Mathf.Max(maximumDifference, Mathf.Abs(actual[pixel].r - expected[pixel].r));
            maximumDifference = Mathf.Max(maximumDifference, Mathf.Abs(actual[pixel].g - expected[pixel].g));
            maximumDifference = Mathf.Max(maximumDifference, Mathf.Abs(actual[pixel].b - expected[pixel].b));
            maximumDifference = Mathf.Max(maximumDifference, Mathf.Abs(actual[pixel].a - expected[pixel].a));
        }
        Require(maximumDifference <= tolerance, label + " maximum channel difference "
            + maximumDifference.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Metrics.Add("{\"name\":\"" + label + "\",\"maxChannelDifference\":"
            + maximumDifference.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}");
    }

    static Texture2D BuildSpatialTexture(Color[] pixels)
    {
        Texture2D texture = new Texture2D(SpatialWidth, SpatialHeight, TextureFormat.RGBAFloat, false, true);
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    static Color[] ToDepthPixels(float[] depth)
    {
        Color[] pixels = new Color[depth.Length];
        for (int index = 0; index < depth.Length; index++)
            pixels[index] = new Color(depth[index], 0f, 0f, 1f);
        return pixels;
    }

    static Color[] ToNormalPixels(Vector3[] normal)
    {
        Color[] pixels = new Color[normal.Length];
        for (int index = 0; index < normal.Length; index++)
            pixels[index] = new Color(normal[index].x, normal[index].y, normal[index].z, 1f);
        return pixels;
    }

    static Color[] BuildUniformSource(Color value)
    {
        Color[] source = new Color[SpatialWidth * SpatialHeight];
        for (int index = 0; index < source.Length; index++)
            source[index] = value;
        return source;
    }

    static float[] BuildUniformDepth(float value)
    {
        float[] depth = new float[SpatialWidth * SpatialHeight];
        for (int index = 0; index < depth.Length; index++)
            depth[index] = value;
        return depth;
    }

    static Vector3[] BuildUniformNormal(Vector3 value)
    {
        Vector3[] normal = new Vector3[SpatialWidth * SpatialHeight];
        for (int index = 0; index < normal.Length; index++)
            normal[index] = value;
        return normal;
    }

    static int SpatialIndex(int pixelX, int pixelY)
    {
        return pixelY * SpatialWidth + pixelX;
    }

    const string MultiscaleReferencePrefix = "Multiscale reference";
    const float MultiscaleTolerance = 2e-5f;
    const float MultiscaleSupportEpsilon = 1e-6f;
    const float MultiscaleZeroTolerance = 1e-6f;
    const float MultiscaleCrossEdgeRatio = 0.1f;
    const float MultiscaleParityRatio = 0.25f;

    sealed class MultiscaleStageResult
    {
        public Color[] Fine;
        public Color[] Coarse;
    }

    static void CheckSpatialMultiscale(SceneScope scene, Material probeMaterial, bool omitFineStage)
    {
        probeMaterial.SetInt("_SpatialFault", 0);
        probeMaterial.SetVector("_TestSize", new Vector4(1f / SpatialWidth, 1f / SpatialHeight,
            SpatialWidth, SpatialHeight));
        CheckMultiscaleCheckerboard(scene, probeMaterial, omitFineStage);
        if (omitFineStage)
            return;
        CheckMultiscaleConstant(scene, probeMaterial);
        CheckMultiscaleImpulse(scene, probeMaterial);
        CheckMultiscaleDepthEdge(scene, probeMaterial);
        CheckMultiscaleNormalEdge(scene, probeMaterial);
        CheckMultiscaleInvalidGuides(scene, probeMaterial);
        CheckMultiscaleBoundary(scene, probeMaterial);
        CheckMultiscaleConfidence(scene, probeMaterial);
    }

    static void CheckMultiscaleCheckerboard(SceneScope scene, Material probeMaterial, bool omitFineStage)
    {
        Color low = new Color(0.25f, 0.1f, 0.05f, 0.5f);
        Color high = new Color(4f, 2f, 1f, 1f);
        Color[] source = new Color[SpatialWidth * SpatialHeight];
        for (int pixelY = 0; pixelY < SpatialHeight; ++pixelY)
            for (int pixelX = 0; pixelX < SpatialWidth; ++pixelX)
                source[SpatialIndex(pixelX, pixelY)] = ((pixelX + pixelY) % 2 == 0) ? low : high;
        MultiscaleStageResult result = RunMultiscaleStageComparison(scene, probeMaterial, "checkerboard",
            source, BuildUniformDepth(4f), BuildUniformNormal(Vector3.forward), default, omitFineStage);
        if (omitFineStage)
            return;
        float sourceGap = MultiscaleParityGap(source);
        float fineGap = MultiscaleParityGap(result.Fine);
        Require(sourceGap > 0.5f, MultiscaleReferencePrefix + " checkerboard fixture contrast " + sourceGap);
        Require(fineGap < MultiscaleParityRatio * sourceGap, MultiscaleReferencePrefix
            + " checkerboard parity must not stay isolated after the fine stage: " + fineGap + " of " + sourceGap);
        Cases.Add("multiscale-checkerboard-parity");
    }

    static void CheckMultiscaleConstant(SceneScope scene, Material probeMaterial)
    {
        Color value = new Color(3f, 1.5f, 0.75f, 1f);
        MultiscaleStageResult result = RunMultiscaleStageComparison(scene, probeMaterial, "constant",
            BuildUniformSource(value), BuildUniformDepth(4f), BuildUniformNormal(Vector3.forward), default, false);
        MultiscaleRequireConstant(result.Fine, value, "fine");
        MultiscaleRequireConstant(result.Coarse, value, "coarse");
        Require(value.r > 1f, MultiscaleReferencePrefix + " constant fixture must exercise HDR above one");
        Cases.Add("multiscale-constant-hdr");
    }

    static void CheckMultiscaleImpulse(SceneScope scene, Material probeMaterial)
    {
        Color[] source = new Color[SpatialWidth * SpatialHeight];
        for (int index = 0; index < source.Length; ++index)
            source[index] = Color.clear;
        source[SpatialIndex(4, 3)] = new Color(12f, 6f, 3f, 1f);
        MultiscaleStageResult result = RunMultiscaleStageComparison(scene, probeMaterial, "impulse",
            source, BuildUniformDepth(4f), BuildUniformNormal(Vector3.forward), default, false);
        int fineExtent = MultiscaleSupportExtent(result.Fine, 4, 3, MultiscaleSupportEpsilon);
        int coarseExtent = MultiscaleSupportExtent(result.Coarse, 4, 3, MultiscaleSupportEpsilon);
        Require(fineExtent <= 2, MultiscaleReferencePrefix + " impulse fine support must stay within one stride-1 kernel");
        Require(coarseExtent >= 3 && coarseExtent > fineExtent, MultiscaleReferencePrefix
            + " coarse stage must spread beyond the single stride-1 support: " + coarseExtent + " versus " + fineExtent);
        Cases.Add("multiscale-impulse-spreading");
    }

    static void CheckMultiscaleDepthEdge(SceneScope scene, Material probeMaterial)
    {
        float[] depth = new float[SpatialWidth * SpatialHeight];
        Color[] source = new Color[SpatialWidth * SpatialHeight];
        for (int pixelY = 0; pixelY < SpatialHeight; ++pixelY)
            for (int pixelX = 0; pixelX < SpatialWidth; ++pixelX)
            {
                int index = SpatialIndex(pixelX, pixelY);
                bool near = pixelX <= 4;
                depth[index] = near ? 3f : 6f;
                source[index] = near ? new Color(10f, 0f, 0f, 1f) : new Color(0f, 10f, 0f, 1f);
            }
        MultiscaleStageResult result = RunMultiscaleStageComparison(scene, probeMaterial, "depth-edge",
            source, depth, BuildUniformNormal(Vector3.forward), default, false);
        foreach (Color[] stage in new[] { result.Fine, result.Coarse })
        {
            Color left = stage[SpatialIndex(3, 3)];
            Color right = stage[SpatialIndex(6, 3)];
            Require(left.r > 5f, MultiscaleReferencePrefix + " depth edge must keep the near-side radiance");
            Require(left.g < MultiscaleCrossEdgeRatio * left.r,
                MultiscaleReferencePrefix + " depth edge cross-edge leakage must stay bounded");
            Require(right.g > 5f, MultiscaleReferencePrefix + " depth edge must keep the far-side radiance");
            Require(right.r < MultiscaleCrossEdgeRatio * right.g,
                MultiscaleReferencePrefix + " depth edge cross-edge leakage must stay bounded on the far side");
        }
        Cases.Add("multiscale-depth-edge-leakage");
    }

    static void CheckMultiscaleNormalEdge(SceneScope scene, Material probeMaterial)
    {
        Vector3[] normal = new Vector3[SpatialWidth * SpatialHeight];
        Color[] source = new Color[SpatialWidth * SpatialHeight];
        for (int pixelY = 0; pixelY < SpatialHeight; ++pixelY)
            for (int pixelX = 0; pixelX < SpatialWidth; ++pixelX)
            {
                int index = SpatialIndex(pixelX, pixelY);
                bool near = pixelX <= 4;
                normal[index] = near ? Vector3.forward : Vector3.right;
                source[index] = near ? new Color(10f, 0f, 0f, 1f) : new Color(0f, 10f, 0f, 1f);
            }
        MultiscaleStageResult result = RunMultiscaleStageComparison(scene, probeMaterial, "normal-edge",
            source, BuildUniformDepth(4f), normal, default, false);
        foreach (Color[] stage in new[] { result.Fine, result.Coarse })
        {
            Color left = stage[SpatialIndex(3, 3)];
            Color right = stage[SpatialIndex(6, 3)];
            Require(left.r > 5f && left.g <= MultiscaleCrossEdgeRatio * left.r,
                MultiscaleReferencePrefix + " orthogonal normal edge must block cross-edge leakage");
            Require(right.g > 5f && right.r <= MultiscaleCrossEdgeRatio * right.g,
                MultiscaleReferencePrefix + " orthogonal normal edge must block cross-edge leakage on the far side");
        }
        Cases.Add("multiscale-normal-edge-leakage");
    }

    static void CheckMultiscaleInvalidGuides(SceneScope scene, Material probeMaterial)
    {
        float[] depth = BuildUniformDepth(4f);
        Vector3[] normal = BuildUniformNormal(Vector3.forward);
        Color[] source = BuildUniformSource(new Color(1.5f, 1.5f, 1.5f, 1f));
        for (int pixelY = 4; pixelY < SpatialHeight; ++pixelY)
            for (int pixelX = 6; pixelX < SpatialWidth; ++pixelX)
            {
                depth[SpatialIndex(pixelX, pixelY)] = 0f;
                source[SpatialIndex(pixelX, pixelY)] = new Color(25f, 0f, 0f, 0.1f);
            }
        normal[SpatialIndex(1, 1)] = new Vector3(0f, 0f, 0.4f);
        source[SpatialIndex(1, 1)] = new Color(25f, 0f, 0f, 0.1f);
        MultiscaleStageResult result = RunMultiscaleStageComparison(scene, probeMaterial, "invalid-guides",
            source, depth, normal, default, false);
        foreach (int index in new[] { SpatialIndex(8, 6), SpatialIndex(1, 1) })
        {
            Require(MultiscaleIsZero(result.Fine[index]),
                MultiscaleReferencePrefix + " sky/invalid fine output must stay zero");
            Require(MultiscaleIsZero(result.Coarse[index]),
                MultiscaleReferencePrefix + " sky/invalid coarse output must stay zero");
        }
        Color neighbor = result.Fine[SpatialIndex(8, 3)];
        Require(Mathf.Abs(neighbor.r - 1.5f) <= MultiscaleZeroTolerance
            && Mathf.Abs(neighbor.g - 1.5f) <= MultiscaleZeroTolerance
            && Mathf.Abs(neighbor.b - 1.5f) <= MultiscaleZeroTolerance,
            MultiscaleReferencePrefix + " valid neighbor of invalid guides must not receive sky radiance");
        Cases.Add("multiscale-invalid-guides-zero");
    }

    static void CheckMultiscaleBoundary(SceneScope scene, Material probeMaterial)
    {
        Color[] source = new Color[SpatialWidth * SpatialHeight];
        for (int index = 0; index < source.Length; ++index)
            source[index] = Color.clear;
        source[SpatialIndex(0, 0)] = new Color(8f, 4f, 2f, 1f);
        source[SpatialIndex(SpatialWidth - 1, SpatialHeight - 1)] = new Color(2f, 4f, 8f, 1f);
        MultiscaleStageResult result = RunMultiscaleStageComparison(scene, probeMaterial, "boundary",
            source, BuildUniformDepth(3f), BuildUniformNormal(Vector3.forward), default, false);
        Require(result.Fine[SpatialIndex(0, 0)].r > MultiscaleSupportEpsilon,
            MultiscaleReferencePrefix + " boundary fine output must clamp and keep corner radiance");
        Require(result.Coarse[SpatialIndex(0, 0)].r > MultiscaleSupportEpsilon,
            MultiscaleReferencePrefix + " boundary coarse output must clamp and read the fine corner");
        Cases.Add("multiscale-boundary-clamping");
    }

    static void CheckMultiscaleConfidence(SceneScope scene, Material probeMaterial)
    {
        Color[] source = new Color[SpatialWidth * SpatialHeight];
        for (int index = 0; index < source.Length; ++index)
            source[index] = Color.clear;
        source[SpatialIndex(4, 3)] = new Color(1f, 0f, 0f, 0f);
        MultiscaleStageResult result = RunMultiscaleStageComparison(scene, probeMaterial, "confidence-zero-alpha",
            source, BuildUniformDepth(4f), BuildUniformNormal(Vector3.forward), default, false);
        Color fine = result.Fine[SpatialIndex(4, 3)];
        Require(fine.r > 0.05f,
            MultiscaleReferencePrefix + " zero-alpha colored sample must still contribute to RGB");
        Require(Mathf.Abs(fine.a) <= MultiscaleZeroTolerance,
            MultiscaleReferencePrefix + " zero-alpha confidence must remain zero, not a denominator");
        Require(result.Coarse[SpatialIndex(4, 3)].r > MultiscaleSupportEpsilon,
            MultiscaleReferencePrefix + " coarse stage must propagate the zero-alpha RGB contribution");
        Cases.Add("multiscale-confidence-not-denominator");
    }

    static MultiscaleStageResult RunMultiscaleStageComparison(SceneScope scene, Material probeMaterial,
        string caseLabel, Color[] source, float[] depth, Vector3[] normal, Color fallback, bool omitFineStage)
    {
        Require(source.Length == SpatialWidth * SpatialHeight && depth.Length == source.Length
            && normal.Length == source.Length, MultiscaleReferencePrefix + " " + caseLabel + " grid shape");
        Texture2D sourceTexture = BuildSpatialTexture(source);
        Texture2D depthTexture = BuildSpatialTexture(ToDepthPixels(depth));
        Texture2D normalTexture = BuildSpatialTexture(ToNormalPixels(normal));
        Texture2D fineTexture = null;
        try
        {
            probeMaterial.SetTexture("_SpatialDepth", depthTexture);
            probeMaterial.SetTexture("_SpatialNormal", normalTexture);
            probeMaterial.SetVector("_TestFallback", fallback);
            Color[] expectedFine = SpatialReference(source, depth, normal, SpatialInverseSigma,
                SpatialNormalPower, 1f, true, fallback);
            Color[] fine = omitFineStage
                ? (Color[])source.Clone()
                : CaptureMultiscaleStage(scene, probeMaterial, sourceTexture, 1f, true, fallback);
            if (!omitFineStage)
                CompareSpatial(fine, expectedFine, MultiscaleTolerance,
                    MultiscaleReferencePrefix + " " + caseLabel + " fine stride-1");
            fineTexture = BuildSpatialTexture(fine);
            Color[] expectedCoarse = SpatialReference(expectedFine, depth, normal, SpatialInverseSigma,
                SpatialNormalPower, 2f, true, fallback);
            Color[] coarse = CaptureMultiscaleStage(scene, probeMaterial, fineTexture, 2f, true, fallback);
            CompareSpatial(coarse, expectedCoarse, MultiscaleTolerance,
                MultiscaleReferencePrefix + " " + caseLabel + " coarse stride-2 from fine");
            if (!omitFineStage)
                Cases.Add("multiscale-" + caseLabel);
            return new MultiscaleStageResult { Fine = fine, Coarse = coarse };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceTexture);
            UnityEngine.Object.DestroyImmediate(depthTexture);
            UnityEngine.Object.DestroyImmediate(normalTexture);
            if (fineTexture != null) UnityEngine.Object.DestroyImmediate(fineTexture);
        }
    }

    static Color[] CaptureMultiscaleStage(SceneScope scene, Material probeMaterial, Texture2D input,
        float radius, bool normalizeSampleNormals, Color fallback)
    {
        probeMaterial.SetTexture("_SpatialInput", input);
        probeMaterial.SetVector("_TestGuide", new Vector4(SpatialInverseSigma, SpatialNormalPower,
            radius, normalizeSampleNormals ? 1f : 0f));
        probeMaterial.SetVector("_TestFallback", fallback);
        return Capture(scene.Camera, new Vector2Int(SpatialWidth, SpatialHeight));
    }

    static void MultiscaleRequireConstant(Color[] image, Color value, string stage)
    {
        Require(image.Length == SpatialWidth * SpatialHeight,
            MultiscaleReferencePrefix + " constant " + stage + " readback");
        foreach (Color color in image)
            Require(Mathf.Abs(color.r - value.r) <= MultiscaleTolerance
                && Mathf.Abs(color.g - value.g) <= MultiscaleTolerance
                && Mathf.Abs(color.b - value.b) <= MultiscaleTolerance
                && Mathf.Abs(color.a - value.a) <= MultiscaleTolerance,
                MultiscaleReferencePrefix + " constant HDR " + stage + " preservation");
    }

    static int MultiscaleSupportExtent(Color[] image, int centerX, int centerY, float epsilon)
    {
        int extent = 0;
        for (int pixelY = 0; pixelY < SpatialHeight; ++pixelY)
            for (int pixelX = 0; pixelX < SpatialWidth; ++pixelX)
            {
                Color color = image[SpatialIndex(pixelX, pixelY)];
                float magnitude = Mathf.Max(Mathf.Abs(color.r), Mathf.Abs(color.g),
                    Mathf.Abs(color.b), Mathf.Abs(color.a));
                if (magnitude <= epsilon)
                    continue;
                extent = Mathf.Max(extent, Mathf.Max(Mathf.Abs(pixelX - centerX), Mathf.Abs(pixelY - centerY)));
            }
        return extent;
    }

    static float MultiscaleParityGap(Color[] image)
    {
        float evenSum = 0f;
        float oddSum = 0f;
        int evenCount = 0;
        int oddCount = 0;
        for (int pixelY = 2; pixelY < SpatialHeight - 2; ++pixelY)
            for (int pixelX = 2; pixelX < SpatialWidth - 2; ++pixelX)
            {
                float value = image[SpatialIndex(pixelX, pixelY)].r;
                if ((pixelX + pixelY) % 2 == 0) { evenSum += value; ++evenCount; }
                else { oddSum += value; ++oddCount; }
            }
        return Mathf.Abs(evenSum / evenCount - oddSum / oddCount);
    }

    static bool MultiscaleIsZero(Color color) => Mathf.Abs(color.r) <= MultiscaleZeroTolerance
        && Mathf.Abs(color.g) <= MultiscaleZeroTolerance && Mathf.Abs(color.b) <= MultiscaleZeroTolerance
        && Mathf.Abs(color.a) <= MultiscaleZeroTolerance;

    public static object MultiscaleNegativeControl()
    {
        _assertions = 0;
        Cases.Clear();
        Metrics.Clear();
        _output = "tmp/sessions/" + DateTime.Now.ToString("yyyy-MM-dd")
            + "-ssgi-multiscale-control-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "/output";
        Directory.CreateDirectory(_output);
        Material probeMaterial = null;
        try
        {
            using (var renderer = new RendererScope())
            using (var scene = new SceneScope(renderer.Index))
            {
                probeMaterial = new Material(Load("RuntimeFixture/SpatialChecks.shader"));
                var probe = ScriptableObject.CreateInstance<DebugOutputFeature>();
                probe.settings.debug = true;
                probe.settings.requireDepth = true;
                probe.settings.debugMaterial = probeMaterial;
                renderer.Use(probe);
                CheckSpatialMultiscale(scene, probeMaterial, true);
            }
            throw new InvalidOperationException("Omitted fine multiscale stage was not detected");
        }
        catch (Exception exception)
        {
            bool detected = exception.Message.StartsWith(MultiscaleReferencePrefix + " checkerboard coarse stride-2 from fine maximum channel difference");
            WriteResult(false, detected, exception.Message);
            if (!detected) throw;
            return new { detected, assertions = _assertions, error = exception.Message, output = _output };
        }
        finally
        {
            if (probeMaterial != null) UnityEngine.Object.DestroyImmediate(probeMaterial);
        }
    }

    sealed class SequenceExportFeature : ScriptableRendererFeature
    {
        public Material material;
        public RTHandle output;
        public int writes;
        SequenceExportPass _pass;

        sealed class SequenceExportPass : ScriptableRenderPass
        {
            sealed class Data { public Material material; public TextureHandle source; public SequenceExportFeature owner; }
            readonly SequenceExportFeature _owner;
            public SequenceExportPass(SequenceExportFeature owner)
            {
                _owner = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                using (var builder = graph.AddRasterRenderPass<Data>("SSGI.Validation.SequenceRawTrace", out var data))
                {
                    data.material = _owner.material;
                    data.source = resources.activeColorTexture;
                    data.owner = _owner;
                    builder.UseTexture(data.source, AccessFlags.Read);
                    builder.UseGlobalTexture(Shader.PropertyToID("_GITraceTexture"), AccessFlags.Read);
                    builder.SetRenderAttachment(graph.ImportTexture(_owner.output), 0, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((Data pass, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, pass.source, new Vector4(1, 1, 0, 0), pass.material, 0);
                        pass.owner.writes++;
                    });
                }
            }
        }
        public override void Create() => _pass = new SequenceExportPass(this);
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) => renderer.EnqueuePass(_pass);
    }

    sealed class SequenceSignal
    {
        public int width;
        public int height;
        public int surfacePixels;
        public int samples;
        public double frameRms;
        public double meanLuminance;
        public double temporalVariance;
        [Newtonsoft.Json.JsonIgnore] public bool[] mask;
        [Newtonsoft.Json.JsonIgnore] public Color[] mean;
        [Newtonsoft.Json.JsonIgnore] public Color[] previous;
        [Newtonsoft.Json.JsonIgnore] public double[] sum;
        [Newtonsoft.Json.JsonIgnore] public double[] squares;
        [Newtonsoft.Json.JsonIgnore] public double differenceSum;

        public SequenceSignal(int width, int height)
        {
            this.width = width;
            this.height = height;
            mean = new Color[width * height];
            sum = new double[mean.Length];
            squares = new double[mean.Length];
        }

        public void Add(Color[] colors, bool[] surface)
        {
            Finite(colors, "Sequence HDR signal");
            if (mask == null) mask = surface;
            Require(mask.SequenceEqual(surface), "Stable sequence surface mask every frame");
            for (int pixel = 0; pixel < colors.Length; ++pixel)
            {
                mean[pixel] += colors[pixel];
                if (!mask[pixel]) continue;
                double luminance = SequenceLuminance(colors[pixel]);
                sum[pixel] += luminance;
                squares[pixel] += luminance * luminance;
                if (previous == null) continue;
                double delta = luminance - SequenceLuminance(previous[pixel]);
                differenceSum += delta * delta;
            }
            previous = colors;
            samples++;
        }

        public void Finish()
        {
            surfacePixels = mask.Count(value => value);
            Require(surfacePixels > 0 && samples == 64, "Nonempty complete sequence samples");
            double varianceSum = 0;
            for (int pixel = 0; pixel < sum.Length; ++pixel)
            {
                mean[pixel] /= samples;
                if (!mask[pixel]) continue;
                meanLuminance += sum[pixel];
                varianceSum += Math.Max(0, squares[pixel] / samples - Math.Pow(sum[pixel] / samples, 2));
            }
            meanLuminance /= surfacePixels * samples;
            frameRms = Math.Sqrt(differenceSum / surfacePixels / (samples - 1));
            temporalVariance = varianceSum / surfacePixels;
            Require(meanLuminance > 0.000001 && frameRms > 0 && temporalVariance > 0,
                "Sequence is nonempty and still stochastic");
        }
    }

    sealed class SequenceNoiseResult
    {
        public string variant;
        public int warmup = 32;
        public int samples = 64;
        public SequenceSignal rawTrace;
        public SequenceSignal history;
        public List<int> frames = new();
        public List<float> historyValid = new();
        public List<int> rays = new();
        public List<int> steps = new();
        public List<float> blends = new();
        public List<float[]> times = new();
    }

    public static object SequenceComparison()
    {
        _assertions = 0;
        _output = "tmp/sessions/" + DateTime.Now.ToString("yyyy-MM-dd")
            + "-diffuse-sequence-ab-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "/output";
        Directory.CreateDirectory(_output);
        Require(!Application.isPlaying, "Sequence comparison requires Edit Mode");
        var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        Require((int)Field(pipeline, "m_DefaultRendererIndex") == 0, "Sequence default renderer zero");
        var rendererData = (ScriptableRendererData[])Field(pipeline, "m_RendererDataList");
        SSGIFeature source = rendererData[0].rendererFeatures.OfType<SSGIFeature>().Single();
        string originalSettings = EditorJsonUtility.ToJson(source);
        bool originalDirty = SceneManager.GetActiveScene().isDirty;
        Camera original = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : Camera.main;
        Require(original != null, "Sequence camera required");
        Vector3 position = original.transform.position;
        Quaternion rotation = original.transform.rotation;
        var size = new Vector2Int(640, 360);
        int divisor = new[] { 8, 4, 3 }[(int)source.settings.performance.diffuseGI];
        var traceSize = new Vector2Int((size.x + divisor - 1) / divisor, (size.y + divisor - 1) / divisor);
        int editorFrame = Time.frameCount;
        Vector4? shaderTime = null;
        Scene preview = default;
        GameObject cameraObject = null;
        Material copy = null;
        RTHandle exportTarget = null;
        var results = new List<SequenceNoiseResult>();
        try
        {
            copy = new Material(Load("RuntimeFixture/TraceCopy.shader"));
            exportTarget = RTHandles.Alloc(traceSize.x, traceSize.y,
                colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
                filterMode: FilterMode.Point, name: "SequenceValidation.RawTrace");
            using (var renderer = new RendererScope())
            {
                preview = EditorSceneManager.NewPreviewScene();
                cameraObject = new GameObject("DiffuseGI.Sequence.DisposableCamera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(original);
                camera.cameraType = CameraType.Game;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.scene = SceneManager.GetActiveScene();
                camera.enabled = false;
                camera.targetTexture = null;
                camera.allowDynamicResolution = false;
                camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = (float)size.x / size.y;
                UniversalAdditionalCameraData additional = camera.GetUniversalAdditionalCameraData();
                additional.SetRenderer(renderer.Index);
                additional.renderType = CameraRenderType.Base;
                additional.renderPostProcessing = false;
                additional.antialiasing = AntialiasingMode.None;
                foreach (string label in new[] { "baseline", "candidate", "baseline_repeat" })
                {
                    var feature = ScriptableObject.CreateInstance<DiffuseGIFeature>();
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source.settings.diffuseGI), feature.settings);
                    feature.settings.shader = label == "candidate" ? source.settings.diffuseGI.shader : Load("RuntimeFixture/SequenceBaseline.shader");
                    feature.controls.temporalShader = source.settings.temporalShader;
                    feature.controls.performance = source.settings.performance.diffuseGI;
                    feature.controls.temporalBlend = 0.5f;
                    feature.controls.receiverAlbedo = Color.white;
                    feature.controls.intensity = 1f;
                    feature.controls.distanceFalloff = 0f;
                    feature.controls.debug = DiffuseGIFeature.DebugMode.Indirect;
                    var export = ScriptableObject.CreateInstance<SequenceExportFeature>();
                    export.material = copy;
                    export.output = exportTarget;
                    renderer.Use(feature, export);
                    feature.Create();
                    results.Add(MeasureSequence(camera, feature, export, size, traceSize, label, ref shaderTime));
                    foreach (Shader shader in new[] { feature.settings.shader, copy.shader, feature.controls.temporalShader })
                        foreach (var message in ShaderUtil.GetShaderMessages(shader))
                            Require(message.severity.ToString() != "Error", "Sequence shader: " + message.message);
                }
            }
        }
        finally
        {
            exportTarget?.Release();
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
        Require(originalSettings == EditorJsonUtility.ToJson(source)
            && originalDirty == SceneManager.GetActiveScene().isDirty, "Sequence preserves live scene/settings");
        Require(Time.frameCount == editorFrame, "Sequence one Editor frame");
        foreach (SequenceNoiseResult result in results.Skip(1))
        {
            Require(result.history.mask.SequenceEqual(results[0].history.mask)
                && result.rawTrace.mask.SequenceEqual(results[0].rawTrace.mask), "Sequence exact branch masks");
        }
        const double repeatRelativeTolerance = 0.002;
        Require(SequenceRepeatStable(results[0].rawTrace, results[2].rawTrace, repeatRelativeTolerance)
            && SequenceRepeatStable(results[0].history, results[2].history, repeatRelativeTolerance),
            "Sequence repeat baseline within 0.2 percent aggregate tolerance");
        var report = new
        {
            passed = true, assertions = _assertions, restored = true, maskMatches = true, repeatStable = true, repeatRelativeTolerance,
            device = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
            width = size.x, height = size.y, traceWidth = traceSize.x, traceHeight = traceSize.y,
            position = new[] { position.x, position.y, position.z }, rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w },
            quality = source.settings.performance.diffuseGI.ToString(), backend = source.settings.diffuseGI.traceBackend.ToString(),
            temporalBlend = 0.5f, format = "R16G16B16A16_SFloat", originalSettings, editorFrame,
            shaderTime = new[] { shaderTime.Value.x, shaderTime.Value.y, shaderTime.Value.z, shaderTime.Value.w },
            results, raw = SequenceComparisonMetric(results[0].rawTrace, results[1].rawTrace),
            history = SequenceComparisonMetric(results[0].history, results[1].history),
            repeatRaw = SequenceRepeatDifference(results[0].rawTrace, results[2].rawTrace),
            repeatHistory = SequenceRepeatDifference(results[0].history, results[2].history),
            metric = "Linear HDR native Trace export and persistent history; population temporal variance on static copied current scene; no GPU timing"
        };
        File.WriteAllText(_output + "/sequence-comparison.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return new { report.passed, report.assertions, report.raw, report.history, output = _output };
    }

    static SequenceNoiseResult MeasureSequence(Camera camera, DiffuseGIFeature feature, SequenceExportFeature export,
        Vector2Int size, Vector2Int traceSize, string label, ref Vector4? shaderTime)
    {
        var result = new SequenceNoiseResult { variant = label,
            history = new SequenceSignal(size.x, size.y), rawTrace = new SequenceSignal(traceSize.x, traceSize.y) };
        object temporal = Field(Field(feature, "_pass"), "_temporal");
        var histories = (IDictionary)Field(temporal, "_histories");
        Material material = (Material)Field(feature, "_material");
        Material temporalMaterial = ((Material[])Field(temporal, "_signalMaterials"))[1];
        var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(size.x, size.y, TextureFormat.RGBAFloat, false, true);
        var traceReadback = new Texture2D(traceSize.x, traceSize.y, TextureFormat.RGBAFloat, false, true);
        RenderTexture active = RenderTexture.active;
        try
        {
            Require(target.Create(), "Sequence render target");
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "Sequence render support");
            for (int sample = -result.warmup; sample < result.samples; ++sample)
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
                Require(export.writes == sample + result.warmup + 1, "Raw Trace export executed every request");
                if (sample < 0) continue;
                object history = histories[camera.GetInstanceID()];
                int side = ((int[])Field(history, "lastColorSide"))[1];
                RTHandle signal = ((RTHandle[,])Field(history, "color"))[1, side];
                Require(signal.rt.width == size.x && signal.rt.height == size.y, "Sequence full-resolution persistent history");
                RenderTexture.active = ((RTHandle[])Field(history, "depth"))[side].rt;
                readback.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0, false);
                bool[] mask = readback.GetPixels().Select(color => color.r > 0f && color.r < camera.farClipPlane).ToArray();
                var traceMask = new bool[traceSize.x * traceSize.y];
                for (int vertical = 0; vertical < traceSize.y; ++vertical)
                    for (int horizontal = 0; horizontal < traceSize.x; ++horizontal)
                    {
                        int sourceX = Mathf.Min(size.x - 1, (int)((horizontal + 0.5f) * size.x / traceSize.x));
                        int sourceY = Mathf.Min(size.y - 1, (int)((vertical + 0.5f) * size.y / traceSize.y));
                        traceMask[vertical * traceSize.x + horizontal] = mask[sourceY * size.x + sourceX];
                    }
                RenderTexture.active = signal.rt;
                readback.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0, false);
                result.history.Add(readback.GetPixels(), mask);
                RenderTexture.active = export.output.rt;
                traceReadback.ReadPixels(new Rect(0, 0, traceSize.x, traceSize.y), 0, 0, false);
                result.rawTrace.Add(traceReadback.GetPixels(), traceMask);
                int rays = material.GetInt("_GIRayCount");
                int steps = material.GetInt("_GIStepCount");
                float blend = temporalMaterial.GetFloat("_SSGITemporalBlend");
                Vector4 actualTime = Shader.GetGlobalVector("_Time");
                if (!shaderTime.HasValue) shaderTime = actualTime;
                Require(actualTime == shaderTime.Value, "Sequence fixed shader time");
                Require(material.GetInt("_DiffuseGIFrameIndex") == sample + result.warmup, "Sequence frame index");
                Require(temporalMaterial.GetFloat("_SSGIHistoryValid") == 1f, "Sequence valid history");
                Require(rays == new[] { 4, 6, 8 }[(int)feature.controls.performance]
                    && steps == new[] { 24, 48, 64 }[(int)feature.controls.performance], "Sequence unchanged ray/step budget");
                Vector4 sourceSize = material.GetVector("_GISourceSize");
                Require(sourceSize.z == traceSize.x && sourceSize.w == traceSize.y, "Raw export native Trace size");
                Require(blend == 0.5f, "Sequence weak history blend");
                Require(material.IsKeywordEnabled("SSGI_TRACE_RAY") == (feature.settings.traceBackend == SSGITraceBackend.Ray), "Sequence backend");
                result.frames.Add(material.GetInt("_DiffuseGIFrameIndex"));
                result.historyValid.Add(temporalMaterial.GetFloat("_SSGIHistoryValid"));
                result.rays.Add(rays); result.steps.Add(steps); result.blends.Add(blend);
                result.times.Add(new[] { actualTime.x, actualTime.y, actualTime.z, actualTime.w });
            }
            result.history.Finish(); result.rawTrace.Finish();
            SequenceImage(result.history.mean, size, label + "-history-mean.png");
            SequenceImage(result.history.previous, size, label + "-history-last.png");
            SequenceImage(result.rawTrace.mean, traceSize, label + "-trace-mean.png");
            return result;
        }
        finally
        {
            RenderTexture.active = active;
            UnityEngine.Object.DestroyImmediate(readback); UnityEngine.Object.DestroyImmediate(traceReadback);
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }

    static object SequenceComparisonMetric(SequenceSignal baseline, SequenceSignal candidate)
    {
        double squared = 0;
        for (int pixel = 0; pixel < baseline.mean.Length; ++pixel)
        {
            if (!baseline.mask[pixel]) continue;
            double delta = SequenceLuminance(candidate.mean[pixel]) - SequenceLuminance(baseline.mean[pixel]);
            squared += delta * delta;
        }
        return new { frameRmsReduction = 1 - candidate.frameRms / baseline.frameRms,
            varianceReduction = 1 - candidate.temporalVariance / baseline.temporalVariance,
            meanChange = candidate.meanLuminance / baseline.meanLuminance - 1,
            meanImageRms = Math.Sqrt(squared / baseline.surfacePixels) };
    }

    static object SequenceRepeatDifference(SequenceSignal baseline, SequenceSignal repeat)
    {
        float maximum = 0;
        for (int pixel = 0; pixel < baseline.mean.Length; ++pixel)
            maximum = Mathf.Max(maximum, Mathf.Abs(baseline.mean[pixel].r - repeat.mean[pixel].r),
                Mathf.Abs(baseline.mean[pixel].g - repeat.mean[pixel].g), Mathf.Abs(baseline.mean[pixel].b - repeat.mean[pixel].b));
        return new { meanMaxRgb = maximum, metrics = SequenceComparisonMetric(baseline, repeat) };
    }

    static bool SequenceRepeatStable(SequenceSignal baseline, SequenceSignal repeat, double tolerance)
    {
        double squared = 0;
        for (int pixel = 0; pixel < baseline.mean.Length; ++pixel)
        {
            if (!baseline.mask[pixel]) continue;
            double delta = SequenceLuminance(repeat.mean[pixel]) - SequenceLuminance(baseline.mean[pixel]);
            squared += delta * delta;
        }
        return Math.Abs(repeat.frameRms / baseline.frameRms - 1) <= tolerance
            && Math.Abs(repeat.temporalVariance / baseline.temporalVariance - 1) <= tolerance
            && Math.Abs(repeat.meanLuminance / baseline.meanLuminance - 1) <= tolerance
            && Math.Sqrt(squared / baseline.surfacePixels) / baseline.meanLuminance <= tolerance;
    }

    static double SequenceLuminance(Color color) => color.r * 0.2126 + color.g * 0.7152 + color.b * 0.0722;

    static void SequenceImage(Color[] colors, Vector2Int size, string filename)
    {
        var display = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false, true);
        try
        {
            display.SetPixels(colors.Select(color => new Color(Mathf.LinearToGammaSpace(color.r),
                Mathf.LinearToGammaSpace(color.g), Mathf.LinearToGammaSpace(color.b), 1)).ToArray());
            display.Apply(); File.WriteAllBytes(_output + "/" + filename, display.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(display); }
    }


    public static object MultiscaleComparison()
    {
        _assertions = 0;
        _output = "tmp/sessions/" + DateTime.Now.ToString("yyyy-MM-dd")
            + "-diffuse-multiscale-ab-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "/output";
        Directory.CreateDirectory(_output);
        Require(!Application.isPlaying, "Multiscale comparison requires Edit Mode");
        var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        var rendererData = (ScriptableRendererData[])Field(pipeline, "m_RendererDataList");
        Require((int)Field(pipeline, "m_DefaultRendererIndex") == 0,
            "Comparison renderer scope requires default renderer zero");
        SSGIFeature source = rendererData[0].rendererFeatures.OfType<SSGIFeature>().Single();
        string originalSettings = EditorJsonUtility.ToJson(source);
        bool originalDirty = SceneManager.GetActiveScene().isDirty;
        Camera original = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : Camera.main;
        Require(original != null, "A comparison camera is required");
        Vector3 position = original.transform.position;
        Quaternion rotation = original.transform.rotation;
        var size = new Vector2Int(640, 360);
        Scene preview = default;
        GameObject cameraObject = null;
        var results = new List<MultiscaleNoiseResult>();
        int editorFrame = Time.frameCount;
        Vector4? shaderTime = null;
        try
        {
            using (var renderer = new RendererScope())
            {
                preview = EditorSceneManager.NewPreviewScene();
                cameraObject = new GameObject("DiffuseGI.Multiscale.DisposableCamera")
                    { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(original);
                camera.cameraType = CameraType.Game;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.scene = SceneManager.GetActiveScene();
                camera.enabled = false;
                camera.targetTexture = null;
                camera.allowDynamicResolution = false;
                camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = (float)size.x / size.y;
                UniversalAdditionalCameraData additional = camera.GetUniversalAdditionalCameraData();
                additional.SetRenderer(renderer.Index);
                additional.renderType = CameraRenderType.Base;
                additional.renderPostProcessing = false;
                additional.antialiasing = AntialiasingMode.None;
                Type baselineType = Type.GetType("BaselineDiffuseGIFeature, Assembly-CSharp");
                Require(baselineType != null, "Frozen single-scale Feature must be imported by --compare");
                foreach (Type featureType in new[] { baselineType, typeof(DiffuseGIFeature) })
                {
                    var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
                    object settings = Field(feature, "settings");
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source.settings.diffuseGI), settings);
                    object controls = Field(feature, "controls");
                    var commonControls = (SSGIStandaloneControls)controls;
                    commonControls.temporalShader = source.settings.temporalShader;
                    commonControls.performance = source.settings.performance.diffuseGI;
                    commonControls.temporalBlend = 0.5f;
                    controls.GetType().GetField("receiverAlbedo").SetValue(controls, Color.white);
                    controls.GetType().GetField("intensity").SetValue(controls, 1f);
                    controls.GetType().GetField("distanceFalloff").SetValue(controls, 0f);
                    FieldInfo debug = controls.GetType().GetField("debug");
                    debug.SetValue(controls, Enum.ToObject(debug.FieldType, 2));
                    renderer.Use(feature);
                    feature.Create();
                    results.Add(MeasureMultiscaleNoise(camera, feature, size, featureType.Name,
                        source.settings.performance.diffuseGI, source.settings.diffuseGI.traceBackend, ref shaderTime));
                }
            }
        }
        finally
        {
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
        Require(originalSettings == EditorJsonUtility.ToJson(source)
            && originalDirty == SceneManager.GetActiveScene().isDirty, "Comparison preserves live source settings and scene dirty state");
        Require(Time.frameCount == editorFrame, "Comparison stays within one Editor frame");
        Require(results[0].surfaceMask.SequenceEqual(results[1].surfaceMask), "Matching exact comparison surface masks");
        var report = new
        {
            passed = true, device = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
            size = new { width = size.x, height = size.y },
            position = new[] { position.x, position.y, position.z },
            rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w },
            originalSettings, editorFrame,
            shaderTime = new[] { shaderTime.Value.x, shaderTime.Value.y, shaderTime.Value.z, shaderTime.Value.w },
            temporalBlend = 0.5f, quality = source.settings.performance.diffuseGI.ToString(),
            backend = source.settings.diffuseGI.traceBackend.ToString(), assertions = _assertions, results,
            noiseReduction = 1.0 - results[1].frameRms / results[0].frameRms,
            meanChange = results[1].meanLuminance / results[0].meanLuminance - 1.0,
            restored = true, maskMatches = true,
            metric = "Persistent production Diffuse history color; static copied current pose; not GPU performance"
        };
        File.WriteAllText(_output + "/comparison.json", Newtonsoft.Json.JsonConvert.SerializeObject(report,
            Newtonsoft.Json.Formatting.Indented));
        return new { report.passed, report.assertions, report.noiseReduction, report.meanChange, output = _output };
    }

    sealed class MultiscaleNoiseResult
    {
        public string variant;
        public int warmup = 32;
        public int samples = 16;
        public int surfacePixels;
        [Newtonsoft.Json.JsonIgnore] public bool[] surfaceMask;
        public double frameRms;
        public double meanLuminance;
        public double temporalVariance;
        public int traceWidth;
        public int traceHeight;
        [Newtonsoft.Json.JsonIgnore] public Color[] mean;
        public List<int> frames = new();
        public List<float> historyValid = new();
        public List<float[]> shaderTimes = new();
    }

    static MultiscaleNoiseResult MeasureMultiscaleNoise(Camera camera, ScriptableRendererFeature feature,
        Vector2Int size, string label, SSGIQuality quality, SSGITraceBackend backend, ref Vector4? shaderTime,
        int samples = 16)
    {
        var result = new MultiscaleNoiseResult { variant = label, samples = samples };
        object pass = Field(feature, "_pass");
        object temporal = Field(pass, "_temporal");
        var histories = (IDictionary)Field(temporal, "_histories");
        var material = (Material)Field(feature, "_material");
        var temporalMaterial = ((Material[])Field(temporal, "_signalMaterials"))[1];
        var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(size.x, size.y, TextureFormat.RGBAFloat, false, true);
        RenderTexture previousActive = RenderTexture.active;
        Color[] previous = null;
        bool[] mask = null;
        double totalLuminance = 0.0;
        double squaredDifference = 0.0;
        var sums = new double[size.x * size.y];
        var squares = new double[size.x * size.y];
        var mean = new Color[size.x * size.y];
        try
        {
            Require(target.Create(), "Comparison target creation");
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "Comparison render request support");
            for (int sample = -result.warmup; sample < result.samples; ++sample)
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
                if (sample < 0) continue;
                object history = histories[camera.GetInstanceID()];
                int side = ((int[])Field(history, "lastColorSide"))[1];
                RTHandle signal = ((RTHandle[,])Field(history, "color"))[1, side];
                Require(signal != null && signal.rt.width == size.x && signal.rt.height == size.y,
                    "Comparison reads production full-resolution history");
                if (mask == null)
                {
                    RenderTexture.active = ((RTHandle[])Field(history, "depth"))[side].rt;
                    readback.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0, false);
                    mask = readback.GetPixels().Select(color => color.r > 0f && color.r < camera.farClipPlane).ToArray();
                }
                RenderTexture.active = signal.rt;
                readback.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0, false);
                Color[] colors = readback.GetPixels();
                Finite(colors, "Comparison history " + label);
                for (int pixel = 0; pixel < colors.Length; ++pixel)
                {
                    mean[pixel] += colors[pixel];
                    if (!mask[pixel]) continue;
                    double luminance = colors[pixel].r * 0.2126 + colors[pixel].g * 0.7152 + colors[pixel].b * 0.0722;
                    sums[pixel] += luminance;
                    squares[pixel] += luminance * luminance;
                    totalLuminance += luminance;
                    if (previous == null) continue;
                    double difference = luminance - (previous[pixel].r * 0.2126
                        + previous[pixel].g * 0.7152 + previous[pixel].b * 0.0722);
                    squaredDifference += difference * difference;
                }
                previous = colors;
                result.traceWidth = (int)material.GetVector("_GISourceSize").z;
                result.traceHeight = (int)material.GetVector("_GISourceSize").w;
                result.frames.Add(material.GetInt("_DiffuseGIFrameIndex"));
                result.historyValid.Add(temporalMaterial.GetFloat("_SSGIHistoryValid"));
                Vector4 actualTime = Shader.GetGlobalVector("_Time");
                result.shaderTimes.Add(new[] { actualTime.x, actualTime.y, actualTime.z, actualTime.w });
                if (!shaderTime.HasValue) shaderTime = actualTime;
                Require(actualTime == shaderTime.Value, "Comparison shader time is fixed");
                Close(material.GetInt("_GIRayCount"), new[] { 4, 6, 8 }[(int)quality], 0, "Comparison ray budget");
                Close(material.GetInt("_GIStepCount"), new[] { 24, 48, 64 }[(int)quality], 0, "Comparison step budget");
                Close(temporalMaterial.GetFloat("_SSGITemporalBlend"), 0.5f, 0.00001f, "Comparison weak temporal blend");
                Require(material.IsKeywordEnabled("SSGI_TRACE_RAY") == (backend == SSGITraceBackend.Ray), "Comparison trace backend");
            }
            result.surfacePixels = mask.Count(surface => surface);
            result.surfaceMask = mask;
            Require(result.surfacePixels > 0, "Comparison has visible geometry");
            Require(result.frames.SequenceEqual(Enumerable.Range(result.warmup, result.samples)), "Comparison matching frame sequence");
            Require(result.historyValid.All(value => value == 1f), "Comparison history is active");
            result.frameRms = Math.Sqrt(squaredDifference / result.surfacePixels / (result.samples - 1));
            result.meanLuminance = totalLuminance / result.surfacePixels / result.samples;
            for (int pixel = 0; pixel < mean.Length; ++pixel)
            {
                mean[pixel] /= result.samples;
                if (!mask[pixel]) continue;
                double average = sums[pixel] / result.samples;
                result.temporalVariance += Math.Max(0, squares[pixel] / result.samples - average * average);
            }
            result.temporalVariance /= result.surfacePixels;
            result.mean = mean;
            SequenceImage(mean, size, label + "-mean.png");
            Require(result.frameRms > 0.0 && result.meanLuminance > 0.000001,
                "Comparison has nonempty stochastic GI signal");
            var display = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false, true);
            try
            {
                display.SetPixels(previous.Select(color => new Color(Mathf.LinearToGammaSpace(color.r),
                    Mathf.LinearToGammaSpace(color.g), Mathf.LinearToGammaSpace(color.b), 1f)).ToArray());
                display.Apply();
                File.WriteAllBytes(_output + "/" + label + ".png", display.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(display); }
            return result;
        }
        finally
        {
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(readback);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    static void CheckConsumers(RendererScope renderer, SceneScope scene)
    {
        var traceMaterial = new Material(Load("RuntimeFixture/Adapter.shader"));
        try { CheckConsumersWithTrace(renderer, scene, traceMaterial); }
        finally { UnityEngine.Object.DestroyImmediate(traceMaterial); }
    }

    static void CheckConsumersWithTrace(RendererScope renderer, SceneScope scene, Material traceMaterial)
    {
        var size = new Vector2Int(189, 101);
        var diffuse = ScriptableObject.CreateInstance<DiffuseGIFeature>();
        diffuse.controls.receiverAlbedo = Color.white;
        diffuse.controls.debug = DiffuseGIFeature.DebugMode.Indirect;
        var specular = ScriptableObject.CreateInstance<SpecularGIFeature>();
        specular.settings.skyCubemap = scene.Sky;
        specular.controls.debug = SpecularGIFeature.Settings.DebugMode.Spatial;
        var integrated = ScriptableObject.CreateInstance<SSGIFeature>();
        traceMaterial.SetInt("_TestMode", 8);
        var traceExport = ScriptableObject.CreateInstance<TraceExportFeature>();
        traceExport.material = traceMaterial;
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
            renderer.Use(diffuse, traceExport);
            Color[] baselineDiffuse = Capture(scene.Camera, size);
            diffuse.settings.shader = Load("DiffuseGI/DiffuseGI.shader");
            diffuse.Create();
            Color[] rayDiffuse = Capture(scene.Camera, size);
            Compare(rayDiffuse, baselineDiffuse, 0.012f, "Diffuse RAY trace production regression " + quality);
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
                CheckDiffuseTier(diffuseMaterial, size, quality);
                Close(diffuseMaterial.GetVector("_GIFilterParams").z, 1f,
                    0, "Diffuse final-pass guide stride matches the multiscale contract");
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
                CheckDiffuseTier((Material)Field(Field(integrated, "_pass"), "_diffuseMaterial"), size, quality);
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

    static void CheckDiffuseTier(Material material, Vector2Int size, SSGIQuality quality)
    {
        int divisor = new[] { 8, 4, 3 }[(int)quality];
        Vector4 sourceSize = material.GetVector("_GISourceSize");
        Close(sourceSize.z, (size.x + divisor - 1) / divisor, 0, "Diffuse native Trace width");
        Close(sourceSize.w, (size.y + divisor - 1) / divisor, 0, "Diffuse native Trace height");
        foreach (SSGIQuality keywordQuality in Enum.GetValues(typeof(SSGIQuality)))
            Require(material.IsKeywordEnabled("DIFFUSE_GI_" + keywordQuality.ToString().ToUpperInvariant())
                == (quality == keywordQuality), "Diffuse exclusive compiled tier " + keywordQuality);
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

        Array spatialKeys = (Array)Field(history, "keys");
        object spatialKey = spatialKeys.GetValue(1);
        Vector4 sampling = (Vector4)Field(spatialKey, "_sampling");
        Close(sampling.w, 3f, 0f, "Diffuse sampling implementation signature");

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
        change(() =>
        {
            object previousKey = spatialKeys.GetValue(1);
            Vector4 previousSampling = (Vector4)Field(previousKey, "_sampling");
            previousSampling.w = 0f;
            previousKey.GetType().GetField("_sampling", InstanceFields).SetValue(previousKey, previousSampling);
            spatialKeys.SetValue(previousKey, 1);
        }, 1, 0, 1, "diffuse-single-scale-history-rejected");
        change(() =>
        {
            object previousKey = spatialKeys.GetValue(1);
            Vector4 previousSampling = (Vector4)Field(previousKey, "_sampling");
            previousSampling.w = 2f;
            previousKey.GetType().GetField("_sampling", InstanceFields).SetValue(previousKey, previousSampling);
            spatialKeys.SetValue(previousKey, 1);
        }, 1, 0, 1, "diffuse-previous-sequence-history-rejected");
        feature.settings.performance.diffuseGI = SSGIQuality.High;
        Capture(scene.Camera, size);
        Capture(scene.Camera, size);
        Flags(feature, 1, 1, 1, "High history warmup");
        change(() =>
        {
            object previousKey = spatialKeys.GetValue(1);
            Vector4 previousSampling = (Vector4)Field(previousKey, "_sampling");
            Require(previousSampling == new Vector4(3, 8, 64, 3), "High history sampling budget");
            previousSampling.x = 2f;
            previousKey.GetType().GetField("_sampling", InstanceFields).SetValue(previousKey, previousSampling);
            spatialKeys.SetValue(previousKey, 1);
        }, 1, 0, 1, "diffuse-half-high-history-rejected");
        feature.settings.performance.diffuseGI = SSGIQuality.Low;
        Capture(scene.Camera, size);
        Capture(scene.Camera, size);
        Flags(feature, 1, 1, 1, "Restored Low history warmup");
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
                poisoned.GetType().GetField("_sampling", InstanceFields).SetValue(poisoned, new Vector4(8, 4, 24, 3));
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

