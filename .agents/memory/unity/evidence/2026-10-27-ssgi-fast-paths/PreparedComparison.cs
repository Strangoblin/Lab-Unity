public partial class Script
{
    public static object PreparedComparison() => SSGICameraChecks.PreparedComparison();
}

public static partial class SSGICameraChecks
{
    public static object PreparedComparison()
    {
        _assertions = 0;
        Cases.Clear();
        Metrics.Clear();
        _output = "tmp/sessions/" + DateTime.Now.ToString("yyyy-MM-dd")
            + "-diffuse-prepared-ab-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "/output";
        Directory.CreateDirectory(_output);
        Require(!Application.isPlaying, "Prepared comparison requires Edit Mode");
        var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        var rendererData = (ScriptableRendererData[])Field(pipeline, "m_RendererDataList");
        SSGIFeature source = rendererData[0].rendererFeatures.OfType<SSGIFeature>().Single();
        Camera original = Camera.main;
        Require(original != null, "Main Camera required");
        string originalSettings = EditorJsonUtility.ToJson(source);
        bool originalDirty = SceneManager.GetActiveScene().isDirty;
        int editorFrame = Time.frameCount;
        Vector3 position = original.transform.position;
        Quaternion rotation = original.transform.rotation;
        var sizes = new[] { new Vector2Int(original.pixelWidth, original.pixelHeight), new Vector2Int(1920, 1080) }.Distinct().ToArray();
        var comparisons = new List<object>();
        var blocks = new List<object>();
        Vector4? shaderTime = null;
        Scene preview = default;
        GameObject cameraObject = null;
        try
        {
            using (var renderer = new RendererScope())
            {
                preview = EditorSceneManager.NewPreviewScene();
                cameraObject = new GameObject("DiffuseGI.Prepared.DisposableCamera") { hideFlags = HideFlags.HideAndDontSave };
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
                var additional = camera.GetUniversalAdditionalCameraData();
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(original.GetUniversalAdditionalCameraData()), additional);
                additional.SetRenderer(renderer.Index);
                additional.renderType = CameraRenderType.Base;
                var baseline = ScriptableObject.CreateInstance<DiffuseGIFeature>();
                var candidate = ScriptableObject.CreateInstance<DiffuseGIFeature>();
                foreach (DiffuseGIFeature feature in new[] { baseline, candidate })
                {
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source.settings.diffuseGI), feature.settings);
                    feature.controls.temporalShader = source.settings.temporalShader;
                    feature.controls.temporalBlend = 0.5f;
                    feature.controls.intensity = 1f;
                    feature.controls.receiverAlbedo = Color.white;
                    feature.controls.distanceFalloff = 0f;
                }
                baseline.settings.shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "OptimizationFixture/UnpreparedDiffuse.shader");
                candidate.settings.shader = source.settings.diffuseGI.shader;
                Require(baseline.settings.shader != null && candidate.settings.shader == Load("DiffuseGI/DiffuseGI.shader"),
                    "Both production and frozen reference shaders must be present");
                var comparisonSize = new Vector2Int(321, 179);
                bool negativeDetected = false;
                foreach (SSGITraceBackend backend in new[] { SSGITraceBackend.Ray, SSGITraceBackend.DDA })
                    foreach (SSGIQuality quality in new[] { SSGIQuality.Low, SSGIQuality.Medium, SSGIQuality.High })
                        foreach (DiffuseGIFeature.DebugMode debug in new[] { DiffuseGIFeature.DebugMode.Trace, DiffuseGIFeature.DebugMode.Confidence })
                        {
                            Color[][] images = new Color[2][];
                            int[] frames = new int[2];
                            int variant = 0;
                            foreach (DiffuseGIFeature feature in new[] { baseline, candidate })
                            {
                                feature.settings.traceBackend = backend;
                                feature.controls.performance = quality;
                                feature.controls.debug = debug;
                                renderer.Use(feature);
                                feature.Create();
                                images[variant] = Capture(camera, comparisonSize);
                                var material = (Material)Field(feature, "_material");
                                frames[variant] = material.GetInt("_DiffuseGIFrameIndex");
                                Require(material.IsKeywordEnabled("SSGI_TRACE_RAY") == (backend == SSGITraceBackend.Ray), "Comparison backend");
                                Require(material.GetInt("_GIRayCount") == new[] { 4, 6, 8 }[(int)quality]
                                    && material.GetInt("_GIStepCount") == new[] { 24, 48, 64 }[(int)quality], "Comparison ray budget");
                                Vector4 actualTime = Shader.GetGlobalVector("_Time");
                                if (!shaderTime.HasValue) shaderTime = actualTime;
                                Require(shaderTime.Value == actualTime, "Comparison fixed shader time");
                                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                                    Require(message.severity.ToString() != "Error", "Comparison shader: " + message.message);
                                ++variant;
                            }
                            Require(frames[0] == frames[1], "Comparison matching sample frame");
                            Finite(images[0], "Baseline HDR");
                            Finite(images[1], "Candidate HDR");
                            Require(Energy(images[0]) > 0.000001, "Comparison has nonempty signal");
                            Compare(images[1], images[0], 0.0005f, "Prepared " + backend + " " + quality + " " + debug);
                            if (!negativeDetected)
                            {
                                Color[] corrupted = (Color[])images[0].Clone();
                                corrupted[0].r += 0.5f;
                                try { Compare(images[1], corrupted, 0.0005f, "Injected trace defect"); }
                                catch (InvalidOperationException exception)
                                {
                                    negativeDetected = exception.Message.StartsWith("Injected trace defect");
                                }
                                Require(negativeDetected, "Comparison detects injected trace defect");
                            }
                            comparisons.Add(new { backend = backend.ToString(), quality = quality.ToString(), debug = debug.ToString(), frames });
                        }
                foreach (Vector2Int size in sizes)
                {
                    var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                        { antiAliasing = 1, useMipMap = false, useDynamicScale = false };
                    var pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                    RenderTexture active = RenderTexture.active;
                    try
                    {
                        Require(target.Create(), "Timing target creation");
                        camera.aspect = (float)size.x / size.y;
                        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                        Require(RenderPipeline.SupportsRenderRequest(camera, request), "Timing request support");
                        foreach (SSGIQuality quality in new[] { SSGIQuality.Medium, SSGIQuality.High })
                            for (int round = 0; round < 3; ++round)
                                for (int order = 0; order < 2; ++order)
                                {
                                    int variant = round % 2 == 0 ? order : 1 - order;
                                    DiffuseGIFeature feature = variant == 0 ? baseline : candidate;
                                    feature.settings.traceBackend = SSGITraceBackend.DDA;
                                    feature.controls.performance = quality;
                                    feature.controls.debug = DiffuseGIFeature.DebugMode.Indirect;
                                    renderer.Use(feature);
                                    feature.Create();
                                    for (int warmup = 0; warmup < 8; ++warmup)
                                    {
                                        RenderPipeline.SubmitRenderRequest(camera, request);
                                        RenderTexture.active = target;
                                        pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                                    }
                                    var material = (Material)Field(feature, "_material");
                                    Require(!material.IsKeywordEnabled("SSGI_TRACE_RAY"), "Timing is DDA");
                                    Require(material.shader == (variant == 0 ? baseline.settings.shader : candidate.settings.shader), "Timing shader");
                                    Vector4 traceSize = material.GetVector("_GISourceSize");
                                    int divisor = quality == SSGIQuality.High ? 3 : 4;
                                    Require(traceSize.z == (size.x + divisor - 1) / divisor && traceSize.w == (size.y + divisor - 1) / divisor,
                                        "Timing native trace size");
                                    int rayCount = quality == SSGIQuality.High ? 8 : 6;
                                    int stepCount = quality == SSGIQuality.High ? 64 : 48;
                                    Require(material.GetInt("_GIRayCount") == rayCount && material.GetInt("_GIStepCount") == stepCount,
                                        "Timing effective budgets");
                                    var submitted = new double[12];
                                    var synchronized = new double[12];
                                    var frames = new int[12];
                                    var stopwatch = new System.Diagnostics.Stopwatch();
                                    for (int sample = 0; sample < 12; ++sample)
                                    {
                                        stopwatch.Restart();
                                        RenderPipeline.SubmitRenderRequest(camera, request);
                                        submitted[sample] = stopwatch.Elapsed.TotalMilliseconds;
                                        RenderTexture.active = target;
                                        pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                                        stopwatch.Stop();
                                        synchronized[sample] = stopwatch.Elapsed.TotalMilliseconds;
                                        frames[sample] = material.GetInt("_DiffuseGIFrameIndex");
                                        Require(Shader.GetGlobalVector("_Time") == shaderTime.Value, "Timing fixed shader time");
                                    }
                                    blocks.Add(new { variant = variant == 0 ? "baseline" : "prepared", width = size.x, height = size.y,
                                        quality = quality.ToString(), round, order, rayCount, stepCount, traceWidth = (int)traceSize.z,
                                        traceHeight = (int)traceSize.w, frames, submitMilliseconds = submitted, synchronizedMilliseconds = synchronized });
                                }
                    }
                    finally
                    {
                        RenderTexture.active = active;
                        target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixel);
                    }
                }
                Require(negativeDetected, "Reference comparison negative control");
            }
        }
        finally
        {
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
        Require(originalSettings == EditorJsonUtility.ToJson(source)
            && originalDirty == SceneManager.GetActiveScene().isDirty, "Comparison preserves original scene and settings");
        Require(Time.frameCount == editorFrame, "Comparison remains within one Editor frame");
        Require(comparisons.Count == 12 && blocks.Count == sizes.Length * 12, "Complete quality and timing matrix");
        var report = new { passed = true, restored = true, negativeDetected = true, assertions = _assertions,
            device = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType, unity = Application.unityVersion,
            scene = SceneManager.GetActiveScene().path, position = new[] { position.x, position.y, position.z },
            rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w }, renderScale = pipeline.renderScale,
            originalSettings, shaderTime = new[] { shaderTime.Value.x, shaderTime.Value.y, shaderTime.Value.z, shaderTime.Value.w },
            warmup = 8, samples = 12, rounds = 3, temporalBlend = 0.5f, isolatedSSGI = true,
            comparisons, metrics = Metrics, blocks,
            metric = "GPU-synchronized whole render request plus 1x1 readback wall time; not GPU timestamps or per-pass time" };
        File.WriteAllText(_output + "/comparison.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return new { report.passed, report.assertions, output = _output, comparisons = comparisons.Count, blocks = blocks.Count };
    }
}
