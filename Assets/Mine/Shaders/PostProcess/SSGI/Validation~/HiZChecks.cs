public partial class Script
{
    public static object HizChecks() => SSGICameraChecks.HizChecks();
}

public static partial class SSGICameraChecks
{
    public static object HizChecks()
    {
        _assertions = 0;
        _output = "tmp/sessions/" + DateTime.Now.ToString("yyyy-MM-dd") + "-diffuse-hiz-check-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.CreateDirectory(_output);
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Mine/Special/Rendering/ScreenSpaceTrace/ScreenSpaceDepthPyramid.compute");
        Require(compute != null, "Depth pyramid compute exists");
        bool sceneDirty = SceneManager.GetActiveScene().isDirty;
        using (var renderer = new RendererScope())
        using (var scene = new SceneScope(renderer.Index))
        {
            scene.AddCube(new Vector3(1, 0, 3.5f), new Vector3(1, 2, 0.7f));
            var feature = ScriptableObject.CreateInstance<DiffuseGIFeature>();
            feature.settings.shader = Load("DiffuseGI/DiffuseGI.shader");
            feature.trace.depthPyramidShader = compute;
            feature.trace.mode = SSGITraceMode.HiZ;
            feature.controls.debug = DiffuseGIFeature.DebugMode.Indirect;
            feature.controls.intensity = 1;
            renderer.Use(feature);
            var size = new Vector2Int(321, 179);
            Color[] highHiZ = null;
            foreach (SSGIQuality quality in Enum.GetValues(typeof(SSGIQuality)))
            {
                feature.controls.performance = quality;
                Color[] colors = Capture(scene.Camera, size, "hiz-" + quality + ".png");
                if (quality == SSGIQuality.High) highHiZ = colors;
                Material material = (Material)Field(feature, "_material");
                Require(material.IsKeywordEnabled("SSGI_TRACE_HIZ"), "HiZ actual keyword " + quality + " depthTexel=" + Shader.GetGlobalVector("_CameraDepthTexture_TexelSize") + " shaderScale=" + Shader.GetGlobalVector("_RTHandleScale"));
                Require(material.GetInt("_SSGIPyramidMipCount") == 9, "HiZ mip count " + quality);
                Finite(colors, "HiZ " + quality);
                Require(Energy(colors) > 0.000001, "HiZ produces nonempty signal " + quality);
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    Require(message.severity.ToString() != "Error", "Shader: " + message.message);
            }
            feature.trace.mode = SSGITraceMode.DDA;
            Color[] dda = Capture(scene.Camera, size);
            Material m = (Material)Field(feature, "_material");
            Require(!m.IsKeywordEnabled("SSGI_TRACE_HIZ"), "DDA does not use HiZ");
            feature.trace.mode = SSGITraceMode.HiZ;
            feature.trace.depthPyramidShader = null;
            Color[] fallback = Capture(scene.Camera, size);
            Require(!m.IsKeywordEnabled("SSGI_TRACE_HIZ"), "Missing compute falls back to DDA");
            Compare(fallback, dda, 0.00001f, "Missing compute DDA fallback");
            feature.trace.depthPyramidShader = compute;
            scene.Camera.rect = new Rect(0.1f, 0.1f, 0.8f, 0.8f);
            Color[] viewportFallback = Capture(scene.Camera, size);
            Require(!m.IsKeywordEnabled("SSGI_TRACE_HIZ"), "Partial viewport falls back to DDA");
            feature.trace.mode = SSGITraceMode.DDA;
            Color[] viewportDDA = Capture(scene.Camera, size);
            Compare(viewportFallback, viewportDDA, 0.00001f, "Partial viewport DDA fallback");
            scene.Camera.rect = new Rect(0f, 0f, 1f, 1f);
            feature.trace.mode = SSGITraceMode.Ray;
            Capture(scene.Camera, size);
            Require(m.IsKeywordEnabled("SSGI_TRACE_RAY") && !m.IsKeywordEnabled("SSGI_TRACE_HIZ"), "Ray mode does not use HiZ");

            var corrupted = ScriptableObject.CreateInstance<DiffuseGIFeature>();
            corrupted.settings.shader = Load("RuntimeFixture/CorruptPyramid.shader");
            corrupted.trace.depthPyramidShader = compute;
            corrupted.trace.mode = SSGITraceMode.HiZ;
            corrupted.controls.debug = DiffuseGIFeature.DebugMode.Indirect;
            corrupted.controls.performance = SSGIQuality.High;
            renderer.Use(corrupted);
            Color[] bad = Capture(scene.Camera, size);
            Material badMaterial = (Material)Field(corrupted, "_material");
            Require(badMaterial.IsKeywordEnabled("SSGI_TRACE_HIZ"), "Corrupt fixture uses HiZ variant");
            float maxDelta = 0f;
            for (int i = 0; i < bad.Length; ++i)
                maxDelta = Mathf.Max(maxDelta, Mathf.Abs(bad[i].r - highHiZ[i].r),
                    Mathf.Abs(bad[i].g - highHiZ[i].g), Mathf.Abs(bad[i].b - highHiZ[i].b));
            Require(maxDelta > 0.00001f, "Injected coarse pyramid corruption must affect production output; delta=" + maxDelta);

            var integrated = ScriptableObject.CreateInstance<SSGIFeature>();
            integrated.settings.compositeShader = Load("SSGIComposite.shader");
            integrated.settings.temporalShader = Load("SSGITemporal.shader");
            integrated.settings.ao.shader = Load("AO/AO.shader");
            integrated.settings.diffuseGI.shader = Load("DiffuseGI/DiffuseGI.shader");
            integrated.settings.trace.depthPyramidShader = compute;
            integrated.settings.specularGI.shader = Load("SpecularGI/SpecularGI.shader");
            integrated.settings.performance.ao = integrated.settings.performance.diffuseGI = integrated.settings.performance.specularGI = SSGIQuality.Low;
            renderer.Use(integrated);
            Capture(scene.Camera, size);
            Capture(scene.Camera, size);
            Flags(integrated, 1, 1, 1, "DDA warmup");
            integrated.settings.trace.mode = SSGITraceMode.HiZ;
            Capture(scene.Camera, size);
            var integratedMaterial = (Material)Field(Field(integrated, "_pass"), "_diffuseMaterial");
            Require(integratedMaterial.IsKeywordEnabled("SSGI_TRACE_HIZ"), "Integrated HiZ actual keyword");
            Flags(integrated, 1, 0, 0, "Switch to HiZ rejects both GI channels");
            Capture(scene.Camera, size);
            Flags(integrated, 1, 1, 1, "HiZ history recovers");
            integrated.settings.trace.depthPyramidShader = null;
            Capture(scene.Camera, size);
            Flags(integrated, 1, 0, 0, "HiZ fallback rejects both GI channels");
        }
        Require(sceneDirty == SceneManager.GetActiveScene().isDirty, "Scene dirty preserved");
        return new { passed = true, assertions = _assertions, output = _output,
            checks = new[] { "three-quality-actual-hiz", "missing-compute-dda-fallback", "ray-isolation",
                "partial-viewport-dda-fallback", "coarse-mip-corruption-detected", "integrated-history-isolation" } };
    }
}
