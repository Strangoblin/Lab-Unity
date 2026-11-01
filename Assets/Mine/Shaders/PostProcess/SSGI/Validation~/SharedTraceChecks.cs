public partial class Script { public static object SharedTraceChecks() => SSGICameraChecks.SharedTraceChecks(); }

public static partial class SSGICameraChecks
{
    sealed class PyramidCacheProbe : ScriptableRendererFeature
    {
        public int count = -1;
        sealed class Probe : ScriptableRenderPass
        {
            readonly PyramidCacheProbe owner;
            public Probe(PyramidCacheProbe owner) { this.owner = owner; renderPassEvent = RenderPassEvent.AfterRenderingSkybox; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                Type type = typeof(Mine.ScreenSpaceTrace.ScreenSpaceDepthPyramid).GetNestedType("FrameCache", BindingFlags.NonPublic);
                var contains = typeof(ContextContainer).GetMethod("Contains").MakeGenericMethod(type);
                if (!(bool)contains.Invoke(frameData, null)) { owner.count = 0; return; }
                var get = typeof(ContextContainer).GetMethod("Get").MakeGenericMethod(type);
                object cache = get.Invoke(frameData, null);
                owner.count = ((IList)Field(cache, "Entries")).Count;
            }
        }
        Probe pass;
        public override void Create() => pass = new Probe(this);
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) => renderer.EnqueuePass(pass);
    }

    public static object SharedTraceChecks()
    {
        _assertions = 0;
        _output = "tmp/sessions/" + DateTime.Now.ToString("yyyy-MM-dd") + "-ssgi-shared-trace-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.CreateDirectory(_output);
        ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Mine/Special/Renderer/ScreenSpaceTrace/ScreenSpaceDepthPyramid.compute");
        Require(compute != null, "Compute available");
        var migration = ScriptableObject.CreateInstance<SSGIFeature>();
        try
        {
            migration.settings.trace = new SSGITraceControls();
            migration.settings.diffuseGI.traceBackend = SSGITraceBackend.DDA;
            migration.settings.diffuseGI.experimentalHiZ = true;
            migration.settings.diffuseGI.depthPyramidShader = compute;
            migration.settings.specularGI.traceBackend = SSGITraceBackend.Ray;
            migration.Create();
            Require(migration.settings.trace.mode == SSGITraceMode.HiZ && migration.settings.trace.depthPyramidShader == compute,
                "Old Diffuse HiZ and compute become shared settings");
            migration.settings.trace.mode = SSGITraceMode.Ray;
            migration.Create();
            Require(migration.settings.trace.mode == SSGITraceMode.Ray, "Recreate does not overwrite shared selection");
            string serialized = EditorJsonUtility.ToJson(migration);
            var restored = ScriptableObject.CreateInstance<SSGIFeature>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(serialized, restored);
                restored.Create();
                Require(restored.settings.trace.mode == SSGITraceMode.Ray && restored.settings.trace.depthPyramidShader == compute,
                    "Serialized trace survives restore and reinitialization");
            }
            finally { restored.Dispose(); UnityEngine.Object.DestroyImmediate(restored); }
            foreach (SSGITraceBackend legacy in new[] { SSGITraceBackend.DDA, SSGITraceBackend.Ray })
            {
                migration.settings.trace = new SSGITraceControls();
                migration.settings.diffuseGI.traceBackend = legacy;
                migration.settings.diffuseGI.experimentalHiZ = false;
                migration.settings.diffuseGI.depthPyramidShader = null;
                migration.Create();
                Require((int)migration.settings.trace.mode == (int)legacy, "Legacy DDA/Ray mapping");
            }
        }
        finally { migration.Dispose(); UnityEngine.Object.DestroyImmediate(migration); }

        var standaloneDiffuse = ScriptableObject.CreateInstance<DiffuseGIFeature>();
        var standaloneSpecular = ScriptableObject.CreateInstance<SpecularGIFeature>();
        try
        {
            standaloneDiffuse.settings.shader = Load("DiffuseGI/DiffuseGI.shader");
            standaloneDiffuse.settings.experimentalHiZ = true;
            standaloneDiffuse.settings.depthPyramidShader = compute;
            standaloneDiffuse.trace = new SSGITraceControls();
            standaloneDiffuse.Create();
            Require(standaloneDiffuse.trace.mode == SSGITraceMode.HiZ && standaloneDiffuse.trace.depthPyramidShader == compute,
                "Standalone Diffuse old HiZ migrates");
            standaloneSpecular.settings.shader = Load("SpecularGI/SpecularGI.shader");
            standaloneSpecular.settings.traceBackend = SSGITraceBackend.Ray;
            standaloneSpecular.settings.experimentalHiZ = true;
            standaloneSpecular.trace = new SSGITraceControls();
            standaloneSpecular.Create();
            Require(standaloneSpecular.trace.mode == SSGITraceMode.Ray, "Standalone legacy Ray takes precedence");
        }
        finally
        {
            standaloneDiffuse.Dispose(); UnityEngine.Object.DestroyImmediate(standaloneDiffuse);
            standaloneSpecular.Dispose(); UnityEngine.Object.DestroyImmediate(standaloneSpecular);
        }

        bool dirty = SceneManager.GetActiveScene().isDirty;
        using (var renderer = new RendererScope())
        using (var scene = new SceneScope(renderer.Index))
        {
            var feature = ScriptableObject.CreateInstance<SSGIFeature>();
            feature.settings.compositeShader = Load("SSGIComposite.shader");
            feature.settings.temporalShader = Load("SSGITemporal.shader");
            feature.settings.ao.shader = Load("AO/AO.shader");
            feature.settings.diffuseGI.shader = Load("DiffuseGI/DiffuseGI.shader");
            feature.settings.specularGI.shader = Load("SpecularGI/SpecularGI.shader");
            feature.settings.trace.mode = SSGITraceMode.HiZ;
            feature.settings.trace.depthPyramidShader = compute;
            feature.settings.performance.ao = feature.settings.performance.diffuseGI = feature.settings.performance.specularGI = SSGIQuality.Low;
            var probe = ScriptableObject.CreateInstance<PyramidCacheProbe>();
            renderer.Use(feature, probe);
            var size = new Vector2Int(321, 179);
            Capture(scene.Camera, size);
            Material diffuse = (Material)Field(Field(feature, "_pass"), "_diffuseMaterial");
            Material specular = (Material)Field(Field(Field(feature, "_pass"), "_specularPass"), "_material");
            Require(diffuse.IsKeywordEnabled("SSGI_TRACE_HIZ") && specular.IsKeywordEnabled("SSGI_TRACE_HIZ"), "Both use shared HiZ");
            Require(probe.count == 1, "Two GI consumers share one pyramid request, count=" + probe.count);
            feature.settings.intensity.diffuseGI = 0f;
            Capture(scene.Camera, size);
            Require(probe.count == 1 && specular.IsKeywordEnabled("SSGI_TRACE_HIZ"), "Specular alone requests one pyramid");
            feature.settings.intensity.diffuseGI = 1f;
            feature.settings.trace.mode = SSGITraceMode.DDA;
            Capture(scene.Camera, size);
            Require(probe.count == 0 && !diffuse.IsKeywordEnabled("SSGI_TRACE_HIZ") && !specular.IsKeywordEnabled("SSGI_TRACE_HIZ"), "DDA requests no pyramid");
            feature.settings.trace.mode = SSGITraceMode.Ray;
            Capture(scene.Camera, size);
            Require(probe.count == 0 && diffuse.IsKeywordEnabled("SSGI_TRACE_RAY") && specular.IsKeywordEnabled("SSGI_TRACE_RAY"), "Ray applies to both without pyramid");
        }
        Require(dirty == SceneManager.GetActiveScene().isDirty, "Scene dirty state preserved");
        return new { passed = true, assertions = _assertions, output = _output,
            checks = new[] { "legacy-migration", "migration-persistence", "shared-pyramid-once", "single-consumer", "dda-ray-no-pyramid" } };
    }
}
