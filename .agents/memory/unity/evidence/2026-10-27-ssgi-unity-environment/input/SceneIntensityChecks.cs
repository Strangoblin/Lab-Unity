public partial class Script
{
    public static object SceneIntensity() => SSGICameraChecks.SceneIntensity();
}

public static partial class SSGICameraChecks
{
    public static object SceneIntensity()
    {
        _assertions = 0;
        _output = "tmp/sessions/" + DateTime.Now.ToString("yyyy-MM-dd")
            + "-ssgi-scene-intensity-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "/output";
        Directory.CreateDirectory(_output);
        Require(!Application.isPlaying, "Scene comparison requires Edit Mode");
        var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        var rendererData = (ScriptableRendererData[])Field(pipeline, "m_RendererDataList");
        SSGIFeature source = rendererData[0].rendererFeatures.OfType<SSGIFeature>().Single();
        Camera original = Camera.main;
        Require(original != null, "Main camera is required");
        string originalSettings = EditorJsonUtility.ToJson(source);
        bool originalDirty = SceneManager.GetActiveScene().isDirty;
        Scene preview = default;
        GameObject cameraObject = null;
        Color[] intensityOne;
        Color[] intensityTen;
        var size = new Vector2Int(original.pixelWidth, original.pixelHeight);
        Require(size.x > 0 && size.y > 0, "Main camera has positive dimensions");
        try
        {
            using (var renderer = new RendererScope())
            {
                preview = EditorSceneManager.NewPreviewScene();
                cameraObject = new GameObject("SSGI.SceneIntensity.Camera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(original);
                camera.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                camera.scene = SceneManager.GetActiveScene();
                camera.enabled = false;
                camera.targetTexture = null;
                camera.allowDynamicResolution = false;
                var additional = camera.GetUniversalAdditionalCameraData();
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(original.GetUniversalAdditionalCameraData()), additional);
                additional.SetRenderer(renderer.Index);
                additional.renderType = CameraRenderType.Base;
                var feature = ScriptableObject.CreateInstance<SSGIFeature>();
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), feature);
                feature.SetActive(true);
                feature.settings.debug = SSGIFeature.DebugMode.Off;
                feature.settings.temporal.ao = 0f;
                feature.settings.temporal.diffuseGI = 0f;
                feature.settings.temporal.specularGI = 0f;
                feature.settings.intensity.specularGI = 1f;
                renderer.Use(feature);
                feature.Create();
                intensityOne = Capture(camera, size, "specular-1.png");
                feature.settings.intensity.specularGI = 10f;
                renderer.Use(feature);
                feature.Create();
                intensityTen = Capture(camera, size, "specular-10.png");
            }
        }
        finally
        {
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
        Require(originalSettings == EditorJsonUtility.ToJson(source)
            && originalDirty == SceneManager.GetActiveScene().isDirty, "Scene comparison preserves live settings and dirty state");
        Finite(intensityOne, "Scene intensity 1");
        Finite(intensityTen, "Scene intensity 10");
        double sum = 0;
        float maximum = 0;
        int changed = 0;
        for (int pixel = 0; pixel < intensityOne.Length; ++pixel)
        {
            Color difference = intensityTen[pixel] - intensityOne[pixel];
            float delta = Mathf.Max(Mathf.Abs(difference.r), Mathf.Abs(difference.g), Mathf.Abs(difference.b));
            maximum = Mathf.Max(maximum, delta);
            sum += delta;
            if (delta > 0.001f) ++changed;
        }
        Require(changed > 0 && maximum > 0.001f, "Specular intensity changes the real scene output");
        var report = new { passed = true, assertions = _assertions, restored = true,
            scene = SceneManager.GetActiveScene().path, width = size.x, height = size.y,
            position = new[] { original.transform.position.x, original.transform.position.y, original.transform.position.z },
            quality = source.settings.performance.specularGI.ToString(), backend = source.settings.specularGI.traceBackend.ToString(),
            roughness = source.settings.specularGI.roughness, temporalBlend = 0, isolatedSSGI = true,
            changedPixels = changed, maxRgbDifference = maximum, meanMaxRgbDifference = sum / intensityOne.Length,
            metric = "Disposable Main Camera / SSGI-only Renderer; full linear HDR output; 1 vs 10 Specular intensity" };
        File.WriteAllText(_output + "/comparison.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return new { report.passed, report.assertions, report.changedPixels, report.maxRgbDifference, output = _output };
    }
}
