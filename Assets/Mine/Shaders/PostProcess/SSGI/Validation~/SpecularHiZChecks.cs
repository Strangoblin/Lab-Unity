public partial class Script
{
    public static object SpecularHiZChecks() => SSGICameraChecks.SpecularHiZChecks();
}

public static partial class SSGICameraChecks
{
    static Material SpecularHiZMaterial(SpecularGIFeature feature) =>
        (Material)Field(Field(feature, "_pass"), "_material");

    static Material IntegratedSpecularHiZMaterial(SSGIFeature feature) =>
        (Material)Field(Field(Field(feature, "_pass"), "_specularPass"), "_material");

    static void SpecularHiZKeywords(Material material, bool hiz, bool ray, string label)
    {
        Require(material.IsKeywordEnabled("SSGI_TRACE_HIZ") == hiz, label + " HiZ keyword");
        Require(material.IsKeywordEnabled("SSGI_TRACE_RAY") == ray, label + " Ray keyword");
        if (hiz) Require(material.GetInt("_SSGIPyramidMipCount") == 9, label + " 321x179 mip count");
    }

    // Freeze the existing temporal sampler; raw Trace and ScreenSource bypass history.
    static void SpecularHiZSample(object temporal, Camera camera, int index)
    {
        var histories = (IDictionary)Field(temporal, "_histories");
        object history = histories[camera.GetInstanceID()];
        Require(history != null, "Sample reset requires a warmup capture");
        var field = history.GetType().GetField("sampleIndex", InstanceFields);
        Require(field != null, "sampleIndex field");
        field.SetValue(history, index);
    }

    static float SpecularHiZMaxDelta(Color[] a, Color[] b)
    {
        Require(a.Length == b.Length && a.Length > 0, "Fault control matching readbacks");
        float max = 0;
        for (int i = 0; i < a.Length; ++i)
            max = Mathf.Max(max, Mathf.Abs(a[i].r - b[i].r), Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b));
        return max;
    }

    public static object SpecularHiZChecks()
    {
        _assertions = 0;
        _output = "tmp/sessions/2026-10-28-specular-hiz/validation/run-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(_output);
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Mine/Special/Renderer/ScreenSpaceTrace/ScreenSpaceDepthPyramid.compute");
        var bad = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "RuntimeFixture/SpecularBadPyramid.compute");
        var missing = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "RuntimeFixture/SpecularMissingKernel.compute");
        Require(compute != null && bad != null && missing != null, "Import the two disposable compute fixtures before running");
        Require(compute.HasKernel("CopyDepth") && compute.HasKernel("ReduceDepth")
            && bad.HasKernel("CopyDepth") && bad.HasKernel("ReduceDepth")
            && missing.HasKernel("CopyDepth") && !missing.HasKernel("ReduceDepth"), "Compute fixture kernel contract");
        bool sceneDirty = SceneManager.GetActiveScene().isDirty;
        var size = new Vector2Int(321, 179);
        float faultDelta = 0, hitEnergy = 0;
        using (var renderer = new RendererScope())
        using (var scene = new SceneScope(renderer.Index))
        {
            // Grazing geometry and roughness supply screen hits for the fault control.
            scene.AddCube(new Vector3(1, 0, 3.5f), new Vector3(1, 2, 0.7f));
            scene.AddCube(new Vector3(-1.3f, 0.1f, 3.2f), new Vector3(0.7f, 2.5f, 1.2f));
            var feature = ScriptableObject.CreateInstance<SpecularGIFeature>();
            feature.settings.shader = Load("SpecularGI/SpecularGI.shader");
            feature.trace.depthPyramidShader = compute;
            feature.trace.mode = SSGITraceMode.HiZ;
            feature.settings.roughness = 0.35f;
            feature.controls.temporalShader = Load("SSGITemporal.shader");
            feature.controls.temporalBlend = 0f;
            feature.controls.debug = SpecularGIFeature.Settings.DebugMode.ScreenSource;
            renderer.Use(feature);
            foreach (SSGIQuality quality in Enum.GetValues(typeof(SSGIQuality)))
            {
                feature.controls.performance = quality;
                Color[] image = Capture(scene.Camera, size, "standalone-" + quality + ".png");
                Material material = SpecularHiZMaterial(feature);
                SpecularHiZKeywords(material, true, false, "Standalone " + quality);
                Close(material.GetVector("_TraceParams").w, new[] { 32, 64, 96 }[(int)quality], 0f, "Specular step budget " + quality);
                Finite(image, "Standalone HiZ " + quality);
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    Require(message.severity.ToString() != "Error", "Specular shader: " + message.message);
            }
            feature.controls.performance = SSGIQuality.High;
            object temporal = Field(Field(feature, "_pass"), "_temporal");
            Material m = SpecularHiZMaterial(feature);

            // Same seed and raw trace: equality is required ONLY for fallback to DDA.
            feature.trace.mode = SSGITraceMode.DDA;
            feature.trace.depthPyramidShader = compute;
            SpecularHiZSample(temporal, scene.Camera, 11);
            Color[] dda = Capture(scene.Camera, size);
            SpecularHiZKeywords(m, false, false, "DDA");
            feature.trace.depthPyramidShader = bad;
            SpecularHiZSample(temporal, scene.Camera, 11);
            Compare(Capture(scene.Camera, size), dda, 0.00001f, "DDA ignores bad compute");
            feature.trace.mode = SSGITraceMode.HiZ;
            feature.trace.depthPyramidShader = null;
            SpecularHiZSample(temporal, scene.Camera, 11);
            Color[] fallback = Capture(scene.Camera, size);
            SpecularHiZKeywords(m, false, false, "Missing compute fallback");
            Compare(fallback, dda, 0.00001f, "Missing compute DDA fallback");
            feature.trace.depthPyramidShader = missing;
            SpecularHiZSample(temporal, scene.Camera, 11);
            Color[] invalid = Capture(scene.Camera, size);
            SpecularHiZKeywords(m, false, false, "Missing kernel fallback");
            Compare(invalid, dda, 0.00001f, "Missing kernel DDA fallback");
            feature.trace.depthPyramidShader = compute;
            scene.Camera.rect = new Rect(0.1f, 0.1f, 0.8f, 0.8f);
            SpecularHiZSample(temporal, scene.Camera, 11);
            Color[] viewport = Capture(scene.Camera, size);
            SpecularHiZKeywords(m, false, false, "Partial viewport fallback");
            feature.trace.mode = SSGITraceMode.DDA;
            SpecularHiZSample(temporal, scene.Camera, 11);
            Compare(Capture(scene.Camera, size), viewport, 0.00001f, "Partial viewport DDA fallback");
            scene.Camera.rect = new Rect(0, 0, 1, 1);
            feature.trace.mode = SSGITraceMode.Ray;
            SpecularHiZSample(temporal, scene.Camera, 11);
            Capture(scene.Camera, size);
            SpecularHiZKeywords(m, false, true, "Ray mode");

            // Negative control modifies the actual input pyramid, not a keyword or image.
            // Require both nonzero screen hits and a visible differential, otherwise the
            // fixture is inconclusive and must be adjusted; never assume DDA == HiZ.
            feature.trace.mode = SSGITraceMode.HiZ;
            for (int sample = 0; sample < 12; ++sample)
            {
                feature.trace.depthPyramidShader = compute;
                SpecularHiZSample(temporal, scene.Camera, sample);
                Color[] good = Capture(scene.Camera, size);
                SpecularHiZKeywords(m, true, false, "Good pyramid " + sample);
                Finite(good, "Good pyramid " + sample);
                hitEnergy = Mathf.Max(hitEnergy, (float)Energy(good));
                feature.trace.depthPyramidShader = bad;
                SpecularHiZSample(temporal, scene.Camera, sample);
                Color[] corrupt = Capture(scene.Camera, size);
                SpecularHiZKeywords(m, true, false, "Bad pyramid " + sample);
                Finite(corrupt, "Bad pyramid " + sample);
                faultDelta = Mathf.Max(faultDelta, SpecularHiZMaxDelta(good, corrupt));
            }
            Require(hitEnergy > 0.00001f, "Fault control inconclusive: no screen-hit signal; energy=" + hitEnergy);
            Require(faultDelta > 0.0001f, "Fault control inconclusive or pyramid unused; delta=" + faultDelta);

            // Reference uses the existing Adapter TestMode 9: independently decoded URP
            // default environment at flat pixels, as in CheckEnvironmentAndIntensity.
            feature.trace.depthPyramidShader = compute;
            feature.settings.roughness = 0f;
            feature.settings.maxDistance = 0.001f;
            feature.controls.debug = SpecularGIFeature.Settings.DebugMode.Trace;
            scene.Camera.orthographic = true;
            scene.Camera.orthographicSize = 2f;
            SpecularHiZSample(temporal, scene.Camera, 0);
            Color[] sky = Capture(scene.Camera, size, "hiz-sky.png");
            SpecularHiZKeywords(m, true, false, "HiZ sky fallback");
            var referenceMaterial = new Material(Load("RuntimeFixture/Adapter.shader"));
            try
            {
                referenceMaterial.SetInt("_TestMode", 9);
                var reference = ScriptableObject.CreateInstance<DebugOutputFeature>();
                reference.settings.debug = true;
                reference.settings.requireDepth = true;
                reference.settings.debugMaterial = referenceMaterial;
                renderer.Use(feature, reference);
                Color[] expected = Capture(scene.Camera, size);
                foreach (Vector2Int point in new[] { new Vector2Int(size.x / 4, size.y * 3 / 4), new Vector2Int(size.x / 4, size.y / 2) })
                {
                    int i = point.y * size.x + point.x;
                    Require(expected[i].r + expected[i].g + expected[i].b > 0.001f, "Nonzero default environment reference required");
                    Close(sky[i].r, expected[i].r, 0.03f, "HiZ sky red");
                    Close(sky[i].g, expected[i].g, 0.03f, "HiZ sky green");
                    Close(sky[i].b, expected[i].b, 0.03f, "HiZ sky blue");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(referenceMaterial); }
            scene.Camera.orthographic = false;

            var integrated = ScriptableObject.CreateInstance<SSGIFeature>();
            integrated.settings.compositeShader = Load("SSGIComposite.shader");
            integrated.settings.temporalShader = Load("SSGITemporal.shader");
            integrated.settings.ao.shader = Load("AO/AO.shader");
            integrated.settings.diffuseGI.shader = Load("DiffuseGI/DiffuseGI.shader");
            integrated.settings.specularGI.shader = Load("SpecularGI/SpecularGI.shader");
            integrated.settings.trace.depthPyramidShader = compute;
            integrated.settings.trace.mode = SSGITraceMode.HiZ;
            integrated.settings.performance.ao = integrated.settings.performance.diffuseGI = SSGIQuality.Low;
            integrated.settings.temporal.ao = integrated.settings.temporal.diffuseGI = integrated.settings.temporal.specularGI = 0.5f;
            integrated.settings.debug = SSGIFeature.DebugMode.SpecularGI;
            renderer.Use(integrated);
            foreach (SSGIQuality quality in Enum.GetValues(typeof(SSGIQuality)))
            {
                integrated.settings.performance.specularGI = quality;
                Color[] image = Capture(scene.Camera, size, "integrated-" + quality + ".png");
                Finite(image, "Integrated " + quality);
                Material im = IntegratedSpecularHiZMaterial(integrated);
                SpecularHiZKeywords(im, true, false, "Integrated " + quality);
                Close(im.GetVector("_TraceParams").w, new[] { 32, 64, 96 }[(int)quality], 0f, "Integrated budget " + quality);
                Material dm = (Material)Field(Field(integrated, "_pass"), "_diffuseMaterial");
                Require(dm.IsKeywordEnabled("SSGI_TRACE_HIZ"), "Diffuse and Specular both request HiZ");
            }
            Capture(scene.Camera, size);
            Flags(integrated, 1, 1, 1, "Integrated HiZ warmup");
            integrated.settings.trace.mode = SSGITraceMode.DDA;
            Capture(scene.Camera, size);
            SpecularHiZKeywords(IntegratedSpecularHiZMaterial(integrated), false, false, "Integrated DDA");
            Flags(integrated, 1, 0, 0, "Shared HiZ to DDA rejects both GI histories");
            Capture(scene.Camera, size);
            Flags(integrated, 1, 1, 1, "Integrated DDA history recovers");
            integrated.settings.trace.mode = SSGITraceMode.HiZ;
            Capture(scene.Camera, size);
            Flags(integrated, 1, 0, 0, "Shared DDA to HiZ rejects both GI histories");
            Capture(scene.Camera, size);
            Flags(integrated, 1, 1, 1, "Integrated HiZ history recovers");
            integrated.settings.trace.depthPyramidShader = null;
            Capture(scene.Camera, size);
            SpecularHiZKeywords(IntegratedSpecularHiZMaterial(integrated), false, false, "Integrated missing compute");
            Flags(integrated, 1, 0, 0, "Shared fallback rejects both GI histories");
            Capture(scene.Camera, size);
            Flags(integrated, 1, 1, 1, "Integrated fallback history recovers");
            integrated.settings.trace.mode = SSGITraceMode.Ray;
            Capture(scene.Camera, size);
            SpecularHiZKeywords(IntegratedSpecularHiZMaterial(integrated), false, true, "Integrated Ray");
            Flags(integrated, 1, 0, 0, "Shared Ray rejects both GI histories");

        }
        Require(sceneDirty == SceneManager.GetActiveScene().isDirty, "Scene dirty state preserved");
        return new { passed = true, assertions = _assertions, output = _output, faultDelta, hitEnergy,
            checks = new[] { "standalone-and-integrated-three-tiers", "keyword-and-budget", "dda-ray-and-resource-fallback",
                "bad-pyramid-consumption-control", "hiz-sky-reference", "integrated-history-isolation" } };
    }
}
