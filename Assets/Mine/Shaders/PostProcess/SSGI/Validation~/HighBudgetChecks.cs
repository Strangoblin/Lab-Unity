public partial class Script
{
    public static object HighBudgetComparison() => SSGICameraChecks.HighBudgetComparison();
}

public static partial class SSGICameraChecks
{
    public static object HighBudgetComparison()
    {
        _assertions = 0;
        _output = "tmp/sessions/" + DateTime.Now.ToString("yyyy-MM-dd")
            + "-diffuse-high-budget-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "/output";
        Directory.CreateDirectory(_output);
        Require(!Application.isPlaying, "High budget comparison requires Edit Mode");
        var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        var rendererData = (ScriptableRendererData[])Field(pipeline, "m_RendererDataList");
        SSGIFeature source = rendererData[0].rendererFeatures.OfType<SSGIFeature>().Single();
        Camera original = Camera.main;
        Camera observation = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : original;
        Require(original != null && observation != null, "Main and observation cameras required");
        int rendererIndex = (int)Field(original.GetUniversalAdditionalCameraData(), "m_RendererIndex");
        Require(rendererIndex == 0 || rendererIndex < 0 && (int)Field(pipeline, "m_DefaultRendererIndex") == 0,
            "High budget comparison requires renderer zero");
        string originalSettings = EditorJsonUtility.ToJson(source);
        bool originalDirty = SceneManager.GetActiveScene().isDirty;
        int editorFrame = Time.frameCount;
        var sizes = new[] { new Vector2Int(original.pixelWidth, original.pixelHeight), new Vector2Int(1920, 1080) }.Distinct().ToArray();
        var blocks = new List<object>();
        var quality = new List<MultiscaleNoiseResult>();
        Vector4? shaderTime = null;
        Scene preview = default;
        GameObject cameraObject = null;
        try
        {
            using (var renderer = new RendererScope())
            {
                preview = EditorSceneManager.NewPreviewScene();
                cameraObject = new GameObject("DiffuseGI.HighBudget.DisposableCamera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                CopyBudgetCamera(original, camera, renderer.Index);
                Type baselineType = Type.GetType("HalfHighDiffuseGIFeature, Assembly-CSharp");
                Require(baselineType != null, "Half High resolution control must be imported");
                var baseline = (ScriptableRendererFeature)ScriptableObject.CreateInstance(baselineType);
                var candidate = ScriptableObject.CreateInstance<DiffuseGIFeature>();
                ConfigureBudgetFeature(baseline, source);
                ConfigureBudgetFeature(candidate, source);
                renderer.Use(baseline, candidate);
                foreach (Vector2Int size in sizes)
                {
                    var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                        { antiAliasing = 1, useMipMap = false, useDynamicScale = false };
                    var pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                    RenderTexture active = RenderTexture.active;
                    try
                    {
                        Require(target.Create(), "High budget target creation");
                        camera.aspect = (float)size.x / size.y;
                        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                        Require(RenderPipeline.SupportsRenderRequest(camera, request), "High budget request support");
                        for (int round = 0; round < 3; ++round)
                            for (int order = 0; order < 2; ++order)
                            {
                                int variant = round % 2 == 0 ? order : 1 - order;
                                ScriptableRendererFeature feature = variant == 0 ? baseline : candidate;
                                int divisor = variant == 0 ? 2 : 3;
                                renderer.Use(feature);
                                feature.Create();
                                for (int warmup = 0; warmup < 8; ++warmup)
                                {
                                    RenderPipeline.SubmitRenderRequest(camera, request);
                                    RenderTexture.active = target;
                                    pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                                }
                                var material = (Material)Field(feature, "_material");
                                Vector4 traceSize = material.GetVector("_GISourceSize");
                                Require(material.GetInt("_GIRayCount") == 8 && material.GetInt("_GIStepCount") == 64,
                                    "High budget keeps rays and steps");
                                Require(traceSize.z == (size.x + divisor - 1) / divisor
                                    && traceSize.w == (size.y + divisor - 1) / divisor, "High budget native Trace dimensions");
                                Require(material.shader == source.settings.diffuseGI.shader
                                    && material.IsKeywordEnabled("DIFFUSE_GI_HIGH"), "High budget production Shader and tier");
                                Require(material.IsKeywordEnabled("SSGI_TRACE_RAY") == (source.settings.diffuseGI.traceBackend == SSGITraceBackend.Ray),
                                    "High budget production backend");
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
                                    Vector4 actualTime = Shader.GetGlobalVector("_Time");
                                    if (!shaderTime.HasValue) shaderTime = actualTime;
                                    Require(actualTime == shaderTime.Value, "High budget fixed Shader time");
                                    object temporal = Field(Field(feature, "_pass"), "_temporal");
                                    var temporalMaterial = ((Material[])Field(temporal, "_signalMaterials"))[1];
                                    Require(temporalMaterial.GetFloat("_SSGIHistoryValid") == 1f
                                        && temporalMaterial.GetFloat("_SSGITemporalBlend") == 0.5f, "High budget active weak history");
                                }
                                Require(frames.SequenceEqual(Enumerable.Range(8, 12)), "High budget matched ray frames");
                                foreach (Shader shader in new[] { material.shader, source.settings.temporalShader })
                                    foreach (var message in ShaderUtil.GetShaderMessages(shader))
                                        Require(message.severity.ToString() != "Error", "High budget Shader: " + message.message);
                                blocks.Add(new { variant = variant == 0 ? "HalfHigh" : "ThirdHigh", width = size.x, height = size.y,
                                    round, order, divisor, traceWidth = (int)traceSize.z, traceHeight = (int)traceSize.w, rays = 8, steps = 64,
                                    shader = AssetDatabase.GetAssetPath(material.shader), frames,
                                    shaderTime = new[] { shaderTime.Value.x, shaderTime.Value.y, shaderTime.Value.z, shaderTime.Value.w },
                                    submitMilliseconds = submitted, synchronizedMilliseconds = synchronized });
                            }
                    }
                    finally
                    {
                        RenderTexture.active = active;
                        target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixel);
                    }
                }
                CopyBudgetCamera(observation, camera, renderer.Index);
                camera.cameraType = CameraType.Game;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                camera.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.None;
                var qualitySize = new Vector2Int(640, 360);
                camera.aspect = (float)qualitySize.x / qualitySize.y;
                foreach (string label in new[] { "HalfHigh", "ThirdHigh", "HalfHighRepeat" })
                {
                    ScriptableRendererFeature feature = label == "ThirdHigh" ? candidate : baseline;
                    renderer.Use(feature);
                    feature.Create();
                    quality.Add(MeasureMultiscaleNoise(camera, feature, qualitySize, label, SSGIQuality.High,
                        source.settings.diffuseGI.traceBackend, ref shaderTime, 64));
                }
            }
        }
        finally
        {
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
        Require(originalSettings == EditorJsonUtility.ToJson(source)
            && originalDirty == SceneManager.GetActiveScene().isDirty, "High budget preserves original scene and settings");
        Require(Time.frameCount == editorFrame, "High budget stays within one Editor frame");
        Require(quality.Count == 3 && blocks.Count == sizes.Length * 6, "High budget complete matrix");
        Require(quality.Skip(1).All(result => result.surfaceMask.SequenceEqual(quality[0].surfaceMask)), "High budget exact surface masks");
        double repeatFrameRmsChange = quality[2].frameRms / quality[0].frameRms - 1;
        double repeatVarianceChange = quality[2].temporalVariance / quality[0].temporalVariance - 1;
        double repeatMeanChange = quality[2].meanLuminance / quality[0].meanLuminance - 1;
        double repeatMeanImageRms = BudgetMeanImageRms(quality[0], quality[2]);
        const double repeatRelativeTolerance = 0.002;
        bool repeatStable = Math.Abs(repeatFrameRmsChange) <= repeatRelativeTolerance
            && Math.Abs(repeatVarianceChange) <= repeatRelativeTolerance && Math.Abs(repeatMeanChange) <= repeatRelativeTolerance
            && repeatMeanImageRms / quality[0].meanLuminance <= repeatRelativeTolerance;
        Require(repeatStable, "High budget repeated baseline drift bound");
        var report = new { passed = true, restored = true, maskMatches = true, assertions = _assertions,
            device = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType, unity = Application.unityVersion,
            scene = SceneManager.GetActiveScene().path, originalSettings, renderScale = pipeline.renderScale, editorFrame,
            timingPosition = new[] { original.transform.position.x, original.transform.position.y, original.transform.position.z },
            timingRotation = new[] { original.transform.rotation.x, original.transform.rotation.y, original.transform.rotation.z, original.transform.rotation.w },
            qualityPosition = new[] { observation.transform.position.x, observation.transform.position.y, observation.transform.position.z },
            qualityRotation = new[] { observation.transform.rotation.x, observation.transform.rotation.y, observation.transform.rotation.z, observation.transform.rotation.w },
            timingProjection = new { original.orthographic, original.fieldOfView, original.orthographicSize, original.nearClipPlane, original.farClipPlane },
            qualityProjection = new { observation.orthographic, observation.fieldOfView, observation.orthographicSize, observation.nearClipPlane, observation.farClipPlane },
            shaderTime = new[] { shaderTime.Value.x, shaderTime.Value.y, shaderTime.Value.z, shaderTime.Value.w },
            backend = source.settings.diffuseGI.traceBackend.ToString(), isolatedSSGI = true, temporalBlend = 0.5f,
            quality = "High", warmup = 8, samples = 12, rounds = 3,
            timingSizes = sizes.Select(size => new { width = size.x, height = size.y }).ToArray(), blocks,
            results = quality, noiseReduction = 1 - quality[1].frameRms / quality[0].frameRms,
            varianceReduction = 1 - quality[1].temporalVariance / quality[0].temporalVariance,
            meanChange = quality[1].meanLuminance / quality[0].meanLuminance - 1,
            meanImageRms = BudgetMeanImageRms(quality[0], quality[1]), repeatStable, repeatRelativeTolerance,
            repeatFrameRmsChange, repeatVarianceChange, repeatMeanChange, repeatMeanImageRms,
            metric = "GPU-synchronized SubmitRenderRequest + 1x1 ReadPixels wall time, not pure GPU or per-Pass time; isolated Diffuse High resolution only; debug Indirect; quality uses persistent history, not equal-quality proof" };
        File.WriteAllText(_output + "/high-budget.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return new { report.passed, report.assertions, output = _output, blocks = blocks.Count };
    }

    static void ConfigureBudgetFeature(ScriptableRendererFeature feature, SSGIFeature source)
    {
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source.settings.diffuseGI), Field(feature, "settings"));
        object controls = Field(feature, "controls");
        var common = (SSGIStandaloneControls)controls;
        common.performance = SSGIQuality.High;
        common.temporalShader = source.settings.temporalShader;
        common.temporalBlend = 0.5f;
        controls.GetType().GetField("intensity").SetValue(controls, 1f);
        controls.GetType().GetField("receiverAlbedo").SetValue(controls, Color.white);
        controls.GetType().GetField("distanceFalloff").SetValue(controls, 0f);
        FieldInfo debug = controls.GetType().GetField("debug");
        debug.SetValue(controls, Enum.ToObject(debug.FieldType, 2));
    }

    static void CopyBudgetCamera(Camera original, Camera camera, int rendererIndex)
    {
        camera.CopyFrom(original);
        camera.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
        camera.scene = SceneManager.GetActiveScene();
        camera.enabled = false;
        camera.targetTexture = null;
        camera.allowDynamicResolution = false;
        camera.rect = new Rect(0, 0, 1, 1);
        var additional = camera.GetUniversalAdditionalCameraData();
        UniversalAdditionalCameraData originalAdditional = original.GetComponent<UniversalAdditionalCameraData>();
        if (originalAdditional != null)
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(originalAdditional), additional);
        additional.SetRenderer(rendererIndex);
        additional.renderType = CameraRenderType.Base;
    }

    static double BudgetMeanImageRms(MultiscaleNoiseResult baseline, MultiscaleNoiseResult candidate)
    {
        double squared = 0;
        for (int pixel = 0; pixel < baseline.mean.Length; ++pixel)
            if (baseline.surfaceMask[pixel])
            {
                double delta = SequenceLuminance(candidate.mean[pixel]) - SequenceLuminance(baseline.mean[pixel]);
                squared += delta * delta;
            }
        return Math.Sqrt(squared / baseline.surfacePixels);
    }
}
