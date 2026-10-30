public partial class Script { public static object SharedTiming() => SSGICameraChecks.SharedTiming(); }
public static partial class SSGICameraChecks
{
    public static object SharedTiming()
    {
        _assertions = 0;
        _output = "tmp/sessions/" + DateTime.Now.ToString("yyyy-MM-dd") + "-ssgi-shared-hiz-timing-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.CreateDirectory(_output);
        Camera original = Camera.main;
        Require(original != null && !Application.isPlaying, "Main camera and Edit Mode required");
        var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        var allData = (ScriptableRendererData[])Field(pipeline, "m_RendererDataList");
        var source = allData[0].rendererFeatures.OfType<SSGIFeature>().Single();
        string settings = EditorJsonUtility.ToJson(source);
        bool dirty = SceneManager.GetActiveScene().isDirty;
        int editorFrame = Time.frameCount;
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Mine/Special/Rendering/ScreenSpaceTrace/ScreenSpaceDepthPyramid.compute");
        Require(compute != null, "HiZ compute required");
        var sizes = new[] { new Vector2Int(original.pixelWidth, original.pixelHeight), new Vector2Int(1920, 1080) }.Distinct().ToArray();
        var blocks = new List<object>();
        Vector4? shaderTime = null;
        Scene preview = default;
        GameObject cameraObject = null;
        try
        {
            using (var renderer = new RendererScope())
            {
                var feature = UnityEngine.Object.Instantiate(source);
                feature.SetActive(true);
                feature.settings.trace.depthPyramidShader = compute;
                feature.settings.intensity.ao = 0f;
                feature.settings.intensity.specularGI = 1f;
                feature.settings.temporal.specularGI = 0.5f;
                feature.settings.temporal.diffuseGI = 0.5f;
                feature.settings.debug = SSGIFeature.DebugMode.Off;
                renderer.Use(feature);
                preview = EditorSceneManager.NewPreviewScene();
                cameraObject = new GameObject("SSGI.SharedHiZ.TimingCamera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(original);
                camera.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                camera.scene = original.scene;
                camera.enabled = false;
                camera.targetTexture = null;
                camera.allowDynamicResolution = false;
                camera.rect = new Rect(0, 0, 1, 1);
                var additional = camera.GetUniversalAdditionalCameraData();
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(original.GetUniversalAdditionalCameraData()), additional);
                additional.SetRenderer(renderer.Index);
                additional.renderType = CameraRenderType.Base;
                foreach (Vector2Int size in sizes)
                {
                    var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                        { antiAliasing = 1, useMipMap = false, useDynamicScale = false };
                    var pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                    RenderTexture active = RenderTexture.active;
                    try
                    {
                        Require(target.Create(), "Timing target");
                        camera.aspect = (float)size.x / size.y;
                        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                        Require(RenderPipeline.SupportsRenderRequest(camera, request), "Timing request supported");
                        foreach (bool both in new[] { false, true })
                            foreach (SSGIQuality quality in new[] { SSGIQuality.Medium, SSGIQuality.High })
                                for (int round = 0; round < 3; ++round)
                                    for (int order = 0; order < 2; ++order)
                                    {
                                        int variant = round % 2 == 0 ? order : 1 - order;
                                        feature.settings.trace.mode = variant == 0 ? SSGITraceMode.DDA : SSGITraceMode.HiZ;
                                        feature.settings.intensity.diffuseGI = both ? 1f : 0f;
                                        feature.settings.performance.diffuseGI = quality;
                                        feature.settings.performance.specularGI = quality;
                                        feature.Create();
                                        for (int i = 0; i < 8; ++i)
                                        {
                                            RenderPipeline.SubmitRenderRequest(camera, request);
                                            RenderTexture.active = target;
                                            pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                                        }
                                        object pass = Field(feature, "_pass");
                                        Material specular = (Material)Field(Field(pass, "_specularPass"), "_material");
                                        Material diffuse = (Material)Field(pass, "_diffuseMaterial");
                                        Require(specular.IsKeywordEnabled("SSGI_TRACE_HIZ") == (variant == 1), "Specular active mode");
                                        Require(!specular.IsKeywordEnabled("SSGI_TRACE_RAY"), "Ray disabled");
                                        Require(specular.GetVector("_TraceParams").w == (quality == SSGIQuality.High ? 96 : 64), "Specular budget");
                                        if (both)
                                        {
                                            Require(diffuse.IsKeywordEnabled("SSGI_TRACE_HIZ") == (variant == 1), "Diffuse active mode");
                                            Require(diffuse.GetInt("_GIRayCount") == (quality == SSGIQuality.High ? 8 : 6), "Diffuse budget");
                                        }
                                        var submitted = new double[12];
                                        var synchronized = new double[12];
                                        var frames = new int[12];
                                        var watch = new System.Diagnostics.Stopwatch();
                                        for (int sample = 0; sample < 12; ++sample)
                                        {
                                            watch.Restart();
                                            RenderPipeline.SubmitRenderRequest(camera, request);
                                            submitted[sample] = watch.Elapsed.TotalMilliseconds;
                                            RenderTexture.active = target;
                                            pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                                            watch.Stop();
                                            synchronized[sample] = watch.Elapsed.TotalMilliseconds;
                                            frames[sample] = (int)specular.GetFloat("_FrameIndex");
                                            Vector4 time = Shader.GetGlobalVector("_Time");
                                            if (!shaderTime.HasValue) shaderTime = time;
                                            Require(shaderTime.Value == time, "Fixed shader time");
                                        }
                                        blocks.Add(new { mode = variant == 0 ? "DDA" : "HiZ", both, quality = quality.ToString(),
                                            width = size.x, height = size.y, round, order, frames, submitted, synchronized });
                                    }
                    }
                    finally { RenderTexture.active = active; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixel); }
                }
            }
        }
        finally
        {
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
        Require(settings == EditorJsonUtility.ToJson(source) && dirty == SceneManager.GetActiveScene().isDirty, "Original state restored");
        Require(Time.frameCount == editorFrame && blocks.Count == sizes.Length * 24, "Timing matrix complete within one frame");
        var report = new { passed = true, device = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
            unity = Application.unityVersion, scene = SceneManager.GetActiveScene().path, settings, restored = true,
            position = new[] { original.transform.position.x, original.transform.position.y, original.transform.position.z },
            warmup = 8, samples = 12, rounds = 3, timing = "SubmitRenderRequest + 1x1 ReadPixels synchronized wall time",
            shaderTime = new[] { shaderTime.Value.x, shaderTime.Value.y, shaderTime.Value.z, shaderTime.Value.w }, blocks };
        File.WriteAllText(_output + "/timing.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return new { passed = true, output = _output, blocks = blocks.Count, assertions = _assertions };
    }
}
