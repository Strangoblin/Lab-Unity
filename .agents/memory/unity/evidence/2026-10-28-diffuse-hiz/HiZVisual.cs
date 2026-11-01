public partial class Script { public static object HiZVisual() => SSGICameraChecks.HiZVisual(); }
public static partial class SSGICameraChecks
{
    public static object HiZVisual()
    {
        _assertions = 0;
        _output = "tmp/sessions/2026-10-28-diffuse-hiz/visual-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.CreateDirectory(_output);
        bool dirty = SceneManager.GetActiveScene().isDirty;
        Camera original = Camera.main;
        Require(original != null, "Main camera required");
        var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        var list = (ScriptableRendererData[])Field(pipeline, "m_RendererDataList");
        var source = list[0].rendererFeatures.OfType<SSGIFeature>().Single();
        string settings = EditorJsonUtility.ToJson(source);
        Scene preview = default;
        GameObject cameraObject = null;
        var results = new List<object>();
        try
        {
            using (var renderer = new RendererScope())
            {
                preview = EditorSceneManager.NewPreviewScene();
                cameraObject = new GameObject("SSGI.HiZ.Visual.Camera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(original);
                camera.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                camera.scene = original.scene;
                camera.enabled = false;
                camera.allowDynamicResolution = false;
                camera.rect = new Rect(0, 0, 1, 1);
                var data = camera.GetUniversalAdditionalCameraData();
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(original.GetUniversalAdditionalCameraData()), data);
                data.SetRenderer(renderer.Index);
                data.renderType = CameraRenderType.Base;
                foreach (SSGIQuality quality in new[] { SSGIQuality.Medium, SSGIQuality.High })
                {
                    var pair = new Color[2][];
                    for (int variant = 0; variant < 2; variant++)
                    {
                        var f = ScriptableObject.CreateInstance<DiffuseGIFeature>();
                        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source.settings.diffuseGI), f.settings);
                        f.settings.shader = source.settings.diffuseGI.shader;
                        f.settings.traceBackend = SSGITraceBackend.DDA;
                        f.settings.experimentalHiZ = variant == 1;
                        f.settings.depthPyramidShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Mine/Special/Rendering/ScreenSpaceTrace/ScreenSpaceDepthPyramid.compute");
                        f.controls.performance = quality;
                        f.controls.debug = DiffuseGIFeature.DebugMode.Indirect;
                        f.controls.receiverAlbedo = Color.white;
                        f.controls.temporalShader = null;
                        renderer.Use(f);
                        pair[variant] = Capture(camera, new Vector2Int(640, 360), quality + (variant == 0 ? "-DDA.png" : "-HiZ.png"));
                        var material = (Material)Field(f, "_material");
                        Require(material.IsKeywordEnabled("SSGI_TRACE_HIZ") == (variant == 1), "Visual backend");
                        Require(material.GetInt("_DiffuseGIFrameIndex") == 0, "Matched sample sequence");
                        Finite(pair[variant], "Visual output");
                    }
                    double square = 0, absolute = 0;
                    for (int i = 0; i < pair[0].Length; ++i)
                    {
                        double a = 0.2126 * pair[0][i].r + 0.7152 * pair[0][i].g + 0.0722 * pair[0][i].b;
                        double b = 0.2126 * pair[1][i].r + 0.7152 * pair[1][i].g + 0.0722 * pair[1][i].b;
                        square += (a - b) * (a - b);
                        absolute += Math.Abs(a - b);
                    }
                    results.Add(new { quality = quality.ToString(), meanAbsolute = absolute / pair[0].Length,
                        rms = Math.Sqrt(square / pair[0].Length), ddaEnergy = Energy(pair[0]), hizEnergy = Energy(pair[1]) });
                }
            }
        }
        finally
        {
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
        Require(dirty == SceneManager.GetActiveScene().isDirty && settings == EditorJsonUtility.ToJson(source), "Original state preserved");
        File.WriteAllText(_output + "/visual.json", Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented));
        return new { passed = true, output = _output, results };
    }
}
