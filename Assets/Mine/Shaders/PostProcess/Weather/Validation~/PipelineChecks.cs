// Temporary validation only. Main submits real URP render requests; it saves no assets.
public class Script
{
    private const float ErrorLimit = 0.001f;
    private static readonly UnityEngine.Color Sentinel = new UnityEngine.Color(0.125f, 0.375f, 0.625f, 0.875f);
    private static readonly string[] ModeKeywords = { "WEATHER_RAIN", "WEATHER_SAND", "WEATHER_SNOW" };

    [System.Serializable]
    public sealed class CaseResult
    {
        public string name;
        public int width;
        public int height;
        public bool orthographic;
        public float renderScale;
        public int moduleMask;
        public string debug;
        public int changedPixels;
        public float maxError;
        public float normalizedMaxError;
        public bool declaredDepth;
        public string[] exclusiveKeywords;
        public float[] boundTargetSize;
        public int weatherPassCount = -1;
        public string[] executedWeatherPasses;
        public string graphBudgetStatus;
    }

    [System.Serializable]
    public sealed class Result
    {
        public string status;
        public string renderer;
        public string graphicsDevice;
        public int caseCount;
        public int renderRequestCount;
        public int budgetVerifiedCaseCount;
        public float maxAllOffNormalizedError;
        public float maxAllOffError;
        public float timeAtStart;
        public float timeAtEnd;
        public bool snowOrthographicOnlyStability;
        public bool featureSettingsRestored;
        public bool renderScaleRestored;
        public string graphSessionStatus;
        public string[] expectedWeatherDiagnostics;
        public string[] graphBudgetNotes;
    }

    public static object Main()
    {
        Require(UnityEngine.Application.isPlaying, "PipelineChecks requires Play Mode.");
        Require(UnityEngine.Camera.main != null, "Camera.main is missing.");
        Require(UnityEngine.SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
            "A real GPU graphics device is required.");
        Require(UnityEngine.SystemInfo.SupportsRenderTextureFormat(UnityEngine.RenderTextureFormat.ARGBFloat),
            "The GPU does not support an ARGBFloat render target.");
        Require(UnityEngine.SystemInfo.SupportsTextureFormat(UnityEngine.TextureFormat.RGBAFloat),
            "The GPU does not support RGBAFloat CPU readback.");

        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
            as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        Require(pipeline != null, "The current render pipeline is not URP.");
        var rendererData = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(
            "Assets/Settings/PC_Renderer.asset");
        Require(rendererData != null, "PC_Renderer.asset is missing.");
        Mine.Weather.WeatherFeature feature = null;
        foreach (var candidate in rendererData.rendererFeatures)
            if (candidate is Mine.Weather.WeatherFeature) feature = (Mine.Weather.WeatherFeature)candidate;
        Require(feature != null && feature.settings != null, "PC_Renderer has no usable WeatherFeature.");
        Require(feature.settings.shader != null && feature.settings.shader.isSupported,
            "Weather Shader is missing or unsupported.");
        Require(feature.settings.noiseTexture != null, "Weather noise texture is missing.");

        var originalProfile = feature.settings.profile;
        var originalDebug = feature.settings.debug;
        bool originalActive = feature.isActive;
        float originalScale = pipeline.renderScale;
        UnityEngine.RenderTexture originalRT = UnityEngine.RenderTexture.active;
        var temporaryObjects = new System.Collections.Generic.List<UnityEngine.Object>();
        var profiles = new System.Collections.Generic.List<Mine.Weather.WeatherProfile>();
        var cases = new System.Collections.Generic.List<CaseResult>();
        var expectedDiagnostics = new System.Collections.Generic.List<string>();
        var unexpectedWeather = new System.Collections.Generic.List<string>();
        var renderErrors = new System.Collections.Generic.List<string>();
        var reportedField = typeof(Mine.Weather.WeatherFeature).GetField("_reported",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var reported = reportedField == null ? null : reportedField.GetValue(feature) as System.Collections.Generic.HashSet<string>;
        string[] originalReports = reported == null ? null : new System.Collections.Generic.List<string>(reported).ToArray();
        bool expectingStackWarning = false;
        int renderCount = 0;
        int beginCount = 0;
        float maxOff = 0f;
        float maxOffNormalized = 0f;
        float startTime = UnityEngine.Time.time;
        var graphProbe = new GraphProbe();

        UnityEngine.Application.LogCallback capture = (message, stack, type) =>
        {
            if (type == UnityEngine.LogType.Error || type == UnityEngine.LogType.Exception || type == UnityEngine.LogType.Assert)
                renderErrors.Add(message);
            if (!message.StartsWith("[Weather]", System.StringComparison.Ordinal)) return;
            if (expectingStackWarning && message.Contains("Camera Stack")) expectedDiagnostics.Add(message);
            else unexpectedWeather.Add(message);
        };
        System.Action<UnityEngine.Rendering.ScriptableRenderContext, UnityEngine.Camera> begin = (context, camera) => beginCount++;
        UnityEngine.Application.logMessageReceived += capture;
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += begin;
        Result result = null;
        try
        {
            graphProbe.Begin();
            foreach (var kind in new[] { Mine.Weather.WeatherKind.Rain, Mine.Weather.WeatherKind.Sand, Mine.Weather.WeatherKind.Snow })
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Mine.Weather.WeatherProfile>(
                    "Assets/Mine/Shaders/PostProcess/Weather/Presets/" + kind + ".asset");
                Require(asset != null, "Missing compatibility preset: " + kind);
                var temporary = UnityEngine.Object.Instantiate(asset);
                temporary.name = "Weather validation " + kind;
                temporary.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                profiles.Add(temporary);
                temporaryObjects.Add(temporary);
            }

            var camera = MakeCamera(UnityEngine.Camera.main, "Weather Pipeline Validation", temporaryObjects);
            pipeline.renderScale = 1f;
            feature.settings.debug = Mine.Weather.WeatherDebugMode.Off;
            feature.SetActive(false);
            UnityEngine.Color[] baseline = Render(camera, 320, 180, graphProbe, ref renderCount, ref beginCount);
            feature.SetActive(true);
            for (int kindIndex = 0; kindIndex < profiles.Count; kindIndex++)
            {
                for (int mask = 0; mask < 16; mask++)
                {
                    SetModules(profiles[kindIndex], mask);
                    feature.settings.profile = profiles[kindIndex];
                    UnityEngine.Color[] pixels = Render(camera, 320, 180, graphProbe, ref renderCount, ref beginCount);
                    var item = Inspect(feature, camera, pixels, baseline, kindIndex, mask,
                        "matrix/" + profiles[kindIndex].Kind + "/" + mask, 320, 180, pipeline.renderScale, graphProbe);
                    AssertBudget(item, profiles[kindIndex].Resolve(), feature.settings.debug);
                    if (mask == 0) CheckAllOff(item, ref maxOff, ref maxOffNormalized);
                    if (mask == 15) Require(item.changedPixels > 0,
                        item.name + " produced no Weather change against the same-frame disabled baseline.");
                    cases.Add(item);
                }
            }

            // Additional aspect ratios, projection stability, and both renderScale values.
            int[] widths = { 256, 384, 320, 320 };
            int[] heights = { 256, 128, 180, 180 };
            bool[] orthographic = { false, false, true, false };
            float[] scales = { 1f, 1f, 1f, 0.75f };
            for (int geometry = 0; geometry < widths.Length; geometry++)
            {
                camera.orthographic = orthographic[geometry];
                pipeline.renderScale = scales[geometry];
                feature.SetActive(false);
                var reference = Render(camera, widths[geometry], heights[geometry], graphProbe, ref renderCount, ref beginCount);
                feature.SetActive(true);
                for (int kindIndex = 0; kindIndex < profiles.Count; kindIndex++)
                {
                    foreach (int mask in new[] { 0, 15 })
                    {
                        SetModules(profiles[kindIndex], mask);
                        feature.settings.profile = profiles[kindIndex];
                        var pixels = Render(camera, widths[geometry], heights[geometry], graphProbe, ref renderCount, ref beginCount);
                        var item = Inspect(feature, camera, pixels, reference, kindIndex, mask,
                            "geometry/" + geometry + "/" + profiles[kindIndex].Kind + "/" + mask,
                            widths[geometry], heights[geometry], pipeline.renderScale, graphProbe);
                        AssertBudget(item, profiles[kindIndex].Resolve(), feature.settings.debug);
                        if (mask == 0) CheckAllOff(item, ref maxOff, ref maxOffNormalized);
                        else Require(item.changedPixels > 0, item.name + " did not execute a visible Weather contribution.");
                        cases.Add(item);
                    }
                }
            }

            camera.orthographic = false;
            pipeline.renderScale = 1f;
            // Switch through all exclusive local variants, then return to Rain.
            foreach (int kindIndex in new[] { 0, 1, 2, 0 })
            {
                SetModules(profiles[kindIndex], 15);
                feature.settings.profile = profiles[kindIndex];
                var pixels = Render(camera, 320, 180, graphProbe, ref renderCount, ref beginCount);
                var item = Inspect(feature, camera, pixels, baseline, kindIndex, 15,
                    "switch/" + cases.Count + "/" + profiles[kindIndex].Kind, 320, 180, 1f, graphProbe);
                Require(item.changedPixels > 0, item.name + " produced an unchanged output.");
                AssertBudget(item, profiles[kindIndex].Resolve(), feature.settings.debug);
                cases.Add(item);
            }

            // A disabled normal profile still allows explicit diagnostic rendering.
            for (int kindIndex = 0; kindIndex < profiles.Count; kindIndex++)
            {
                SetModules(profiles[kindIndex], 0);
                feature.settings.profile = profiles[kindIndex];
                for (int debugIndex = 0; debugIndex <= 5; debugIndex++)
                {
                    feature.settings.debug = (Mine.Weather.WeatherDebugMode)debugIndex;
                    var pixels = Render(camera, 320, 180, graphProbe, ref renderCount, ref beginCount);
                    var item = Inspect(feature, camera, pixels, baseline, kindIndex, 0,
                        "zero-modules-debug/" + profiles[kindIndex].Kind + "/" + feature.settings.debug,
                        320, 180, 1f, graphProbe);
                    AssertBudget(item, profiles[kindIndex].Resolve(), feature.settings.debug);
                    if (debugIndex == 0) CheckAllOff(item, ref maxOff, ref maxOffNormalized);
                    cases.Add(item);
                }
            }

            // An independent second Base with different dimensions exercises per-camera snapshots.
            feature.settings.debug = Mine.Weather.WeatherDebugMode.Off;
            var second = MakeCamera(UnityEngine.Camera.main, "Weather Independent Base Validation", temporaryObjects);
            feature.SetActive(false);
            var secondReference = Render(second, 192, 256, graphProbe, ref renderCount, ref beginCount);
            feature.SetActive(true);
            SetModules(profiles[1], 15);
            feature.settings.profile = profiles[1];
            var secondPixels = Render(second, 192, 256, graphProbe, ref renderCount, ref beginCount);
            var secondCase = Inspect(feature, second, secondPixels, secondReference, 1, 15,
                "independent-second-base", 192, 256, 1f, graphProbe);
            Require(secondCase.changedPixels > 0, "The independent second Base did not receive Weather.");
            AssertBudget(secondCase, profiles[1].Resolve(), feature.settings.debug);
            cases.Add(secondCase);

            // StandardRequest renders the actual stack. Both stack Base and Overlay must skip Weather.
            var baseData = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var overlay = MakeCamera(camera, "Weather Overlay Skip Validation", temporaryObjects);
            var overlayData = overlay.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            overlayData.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
            overlayData.renderPostProcessing = false;
            overlay.cullingMask = 0;
            overlay.enabled = true;
            Require(baseData.cameraStack != null, "PC Renderer does not support a Base camera stack.");
            baseData.cameraStack.Add(overlay);
            feature.SetActive(false);
            var stackReference = Render(camera, 320, 180, graphProbe, ref renderCount, ref beginCount);
            feature.SetActive(true);
            expectingStackWarning = true;
            var stackPixels = Render(camera, 320, 180, graphProbe, ref renderCount, ref beginCount);
            expectingStackWarning = false;
            var stackCase = Inspect(feature, camera, stackPixels, stackReference, -1, 15,
                "base-plus-overlay-safe-skip", 320, 180, 1f, graphProbe);
            CheckAllOff(stackCase, ref maxOff, ref maxOffNormalized);
            if (stackCase.weatherPassCount >= 0)
                Require(stackCase.weatherPassCount == 0, "Weather was unexpectedly recorded for the non-terminal stack Base.");
            cases.Add(stackCase);
            baseData.cameraStack.Clear();
            overlay.enabled = false;

            Require(unexpectedWeather.Count == 0, "Unexpected Weather diagnostics: " + string.Join(" | ", unexpectedWeather.ToArray()));
            Require(renderErrors.Count == 0, "Render errors: " + string.Join(" | ", renderErrors.ToArray()));
            int budgetCases = 0;
            foreach (var item in cases) if (item.weatherPassCount >= 0) budgetCases++;
            result = new Result
            {
                status = "PASS real URP render requests and RGBAFloat output checks",
                renderer = rendererData.name,
                graphicsDevice = UnityEngine.SystemInfo.graphicsDeviceType + " / " + UnityEngine.SystemInfo.graphicsDeviceName,
                caseCount = cases.Count,
                renderRequestCount = renderCount,
                budgetVerifiedCaseCount = budgetCases,
                maxAllOffError = maxOff,
                maxAllOffNormalizedError = maxOffNormalized,
                timeAtStart = startTime,
                timeAtEnd = UnityEngine.Time.time,
                snowOrthographicOnlyStability = true,
                graphSessionStatus = graphProbe.Status,
                expectedWeatherDiagnostics = expectedDiagnostics.ToArray(),
                graphBudgetNotes = graphProbe.Notes.ToArray()
            };
        }
        finally
        {
            feature.settings.profile = originalProfile;
            feature.settings.debug = originalDebug;
            feature.SetActive(originalActive);
            pipeline.renderScale = originalScale;
            UnityEngine.RenderTexture.active = originalRT;
            UnityEngine.Application.logMessageReceived -= capture;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= begin;
            if (reported != null && originalReports != null)
            {
                reported.Clear();
                foreach (string key in originalReports) reported.Add(key);
            }
            for (int i = temporaryObjects.Count - 1; i >= 0; i--)
                if (temporaryObjects[i] != null) UnityEngine.Object.DestroyImmediate(temporaryObjects[i]);
            graphProbe.Dispose();
        }
        result.featureSettingsRestored = feature.settings.profile == originalProfile
            && feature.settings.debug == originalDebug && feature.isActive == originalActive;
        result.renderScaleRestored = UnityEngine.Mathf.Approximately(pipeline.renderScale, originalScale);
        Require(result.featureSettingsRestored && result.renderScaleRestored, "Validation did not restore original runtime configuration.");
        string summary = UnityEngine.JsonUtility.ToJson(result, true).TrimEnd();
        var json = new System.Text.StringBuilder(summary.Substring(0, summary.Length - 1));
        json.Append(",\n\"cases\":[");
        for (int i = 0; i < cases.Count; i++)
        {
            if (i > 0) json.Append(',');
            string entry = UnityEngine.JsonUtility.ToJson(cases[i]);
            Require(entry.Contains("changedPixels"), "Case serialization omitted its evidence.");
            json.Append(entry);
        }
        json.Append("]}");
        return json.ToString();
    }

    private static UnityEngine.Camera MakeCamera(UnityEngine.Camera source, string name,
        System.Collections.Generic.List<UnityEngine.Object> cleanup)
    {
        var go = new UnityEngine.GameObject(name);
        go.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        cleanup.Add(go);
        var camera = go.AddComponent<UnityEngine.Camera>();
        camera.CopyFrom(source);
        camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        camera.enabled = false;
        camera.targetTexture = null;
        camera.allowDynamicResolution = false;
        var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        data.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Base;
        data.SetRenderer(0);
        var sourceData = source.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        if (sourceData != null)
        {
            data.renderPostProcessing = sourceData.renderPostProcessing;
            data.renderShadows = sourceData.renderShadows;
            data.volumeLayerMask = sourceData.volumeLayerMask;
            data.volumeTrigger = sourceData.volumeTrigger;
        }
        // Temporal AA would change the reference input between repeated synchronous requests.
        data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
        return camera;
    }

    private static UnityEngine.Color[] Render(UnityEngine.Camera camera, int width, int height,
        GraphProbe graph, ref int renderCount, ref int beginCount)
    {
        camera.aspect = (float)width / height;
        var target = new UnityEngine.RenderTexture(width, height, 24, UnityEngine.RenderTextureFormat.ARGBFloat,
            UnityEngine.RenderTextureReadWrite.Linear);
        target.name = "Weather validation float output";
        target.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        target.antiAliasing = 1;
        target.useMipMap = false;
        target.autoGenerateMips = false;
        UnityEngine.Texture2D readback = null;
        var previous = UnityEngine.RenderTexture.active;
        try
        {
            Require(target.Create() && target.IsCreated(), "GPU float target creation failed.");
            UnityEngine.RenderTexture.active = target;
            UnityEngine.GL.Clear(true, true, Sentinel);
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = target };
            Require(UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request),
                "The current pipeline does not support StandardRequest.");
            int beginBefore = beginCount;
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
            renderCount++;
            Require(beginCount > beginBefore, "The request did not invoke the real camera rendering pipeline.");
            Require(camera.targetTexture == null, "URP failed to restore the temporary camera targetTexture.");
            UnityEngine.RenderTexture.active = target;
            readback = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGBAFloat, false, true);
            readback.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            readback.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0, false);
            readback.Apply(false, false);
            var pixels = readback.GetPixels();
            Require(pixels.Length == width * height, "Readback dimensions do not match the requested destination.");
            int notSentinel = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Require(Finite(pixels[i].r) && Finite(pixels[i].g) && Finite(pixels[i].b) && Finite(pixels[i].a),
                    camera.name + " readback contains NaN/Infinity at pixel " + i);
                if (UnityEngine.Mathf.Abs(pixels[i].r - Sentinel.r) > 0.00001f
                    || UnityEngine.Mathf.Abs(pixels[i].g - Sentinel.g) > 0.00001f
                    || UnityEngine.Mathf.Abs(pixels[i].b - Sentinel.b) > 0.00001f
                    || UnityEngine.Mathf.Abs(pixels[i].a - Sentinel.a) > 0.00001f) notSentinel++;
            }
            Require(notSentinel > 0, "Render request left the whole target at the pre-render sentinel value.");
            graph.Read(camera);
            return pixels;
        }
        finally
        {
            UnityEngine.RenderTexture.active = previous;
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void SetModules(Mine.Weather.WeatherProfile profile, int mask)
    {
        float strength = profile.Kind == Mine.Weather.WeatherKind.Rain ? 0.5f
            : profile.Kind == Mine.Weather.WeatherKind.Snow ? 0.3f : 1f;
        profile.Particles.Opacity = (mask & 1) == 0 ? 0f : strength;
        profile.Lens.Strength = (mask & 2) == 0 ? 0f : strength;
        profile.Fog.Strength = (mask & 4) == 0 ? 0f : strength;
        profile.Tone.Strength = (mask & 8) == 0 ? 0f : 1f;
    }

    private static CaseResult Inspect(Mine.Weather.WeatherFeature feature, UnityEngine.Camera camera,
        UnityEngine.Color[] pixels, UnityEngine.Color[] reference, int expectedKind, int mask,
        string name, int width, int height, float scale, GraphProbe graph)
    {
        var item = new CaseResult { name = name, width = width, height = height,
            orthographic = camera.orthographic, renderScale = scale, moduleMask = mask,
            debug = feature.settings.debug.ToString(), weatherPassCount = graph.LastCount,
            executedWeatherPasses = graph.LastPasses, graphBudgetStatus = graph.LastStatus };
        Require(pixels.Length == reference.Length, "Reference dimensions differ in " + name);
        for (int i = 0; i < pixels.Length; i++)
        {
            float error = 0f;
            for (int channel = 0; channel < 4; channel++)
            {
                float diff = UnityEngine.Mathf.Abs(pixels[i][channel] - reference[i][channel]);
                item.maxError = UnityEngine.Mathf.Max(item.maxError, diff);
                item.normalizedMaxError = UnityEngine.Mathf.Max(item.normalizedMaxError,
                    diff / (1f + UnityEngine.Mathf.Abs(reference[i][channel])));
                error = UnityEngine.Mathf.Max(error, diff);
            }
            if (error > 0.00001f) item.changedPixels++;
        }
        var material = GetPrivate(feature, "_material") as UnityEngine.Material;
        var pass = GetPrivate(feature, "_pass") as UnityEngine.Rendering.Universal.ScriptableRenderPass;
        item.declaredDepth = pass != null
            && (pass.input & UnityEngine.Rendering.Universal.ScriptableRenderPassInput.Depth) != 0;
        var active = new System.Collections.Generic.List<string>();
        if (material != null)
        {
            foreach (string keyword in ModeKeywords) if (material.IsKeywordEnabled(keyword)) active.Add(keyword);
            var size = material.GetVector("_WeatherTargetSize");
            item.boundTargetSize = new[] { size.x, size.y, size.z, size.w };
        }
        item.exclusiveKeywords = active.ToArray();
        bool actuallyDraws = mask != 0 || feature.settings.debug != Mine.Weather.WeatherDebugMode.Off;
        if (expectedKind >= 0 && actuallyDraws)
        {
            Require(active.Count == 1 && active[0] == ModeKeywords[expectedKind],
                name + " contains missing or non-exclusive Weather mode keywords.");
            Require(item.boundTargetSize != null && item.boundTargetSize[0] > 0f && item.boundTargetSize[1] > 0f,
                name + " did not bind valid source dimensions.");
            bool needsDepth = feature.settings.debug == Mine.Weather.WeatherDebugMode.Fog
                || ((feature.settings.debug == Mine.Weather.WeatherDebugMode.Off
                    || feature.settings.debug == Mine.Weather.WeatherDebugMode.Final) && (mask & 4) != 0);
            Require(item.declaredDepth == needsDepth, name + " declared the wrong Weather depth input.");
        }
        return item;
    }

    private static void AssertBudget(CaseResult item, Mine.Weather.ResolvedWeatherSettings p,
        Mine.Weather.WeatherDebugMode debug)
    {
        if (item.weatherPassCount < 0) return;
        bool particleDebug = debug == Mine.Weather.WeatherDebugMode.Particles || debug == Mine.Weather.WeatherDebugMode.Refraction;
        bool atmosphereDebug = debug == Mine.Weather.WeatherDebugMode.Lens || debug == Mine.Weather.WeatherDebugMode.Fog;
        int expected = ((!atmosphereDebug && p.ParticlesActive) || particleDebug ? 1 : 0)
            + (!particleDebug && (p.AtmosphereActive || atmosphereDebug || debug == Mine.Weather.WeatherDebugMode.Final) ? 1 : 0);
        Require(item.weatherPassCount == expected,
            item.name + " executed " + item.weatherPassCount + " Weather passes; expected " + expected);
    }

    private static void CheckAllOff(CaseResult item, ref float maxError, ref float maxNormalized)
    {
        maxError = UnityEngine.Mathf.Max(maxError, item.maxError);
        maxNormalized = UnityEngine.Mathf.Max(maxNormalized, item.normalizedMaxError);
        Require(item.normalizedMaxError <= ErrorLimit,
            item.name + " differs from disabled Weather; normalized max=" + item.normalizedMaxError);
    }

    private static object GetPrivate(object target, string field)
    {
        var info = target.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return info == null ? null : info.GetValue(target);
    }

    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new System.InvalidOperationException(message);
    }

    // Graph debug data are optional: URP explicitly suppresses generation for processing render requests.
    // No stale data from another camera are used, and an existing session is never replaced.
    private sealed class GraphProbe : System.IDisposable
    {
        public string Status = "unavailable";
        public int LastCount = -1;
        public string[] LastPasses = new string[0];
        public string LastStatus = "not queried";
        public readonly System.Collections.Generic.List<string> Notes = new System.Collections.Generic.List<string>();
        private System.Type type;
        private object createdSession;

        public void Begin()
        {
            try
            {
                type = typeof(UnityEngine.Rendering.RenderGraphModule.RenderGraph).Assembly.GetType(
                    "UnityEngine.Rendering.RenderGraphModule.RenderGraphDebugSession");
                if (type == null) { Note("RenderGraphDebugSession type is unavailable."); return; }
                var current = type.GetProperty("currentDebugSession").GetValue(null);
                if (current != null)
                {
                    bool active = (bool)type.GetProperty("hasActiveDebugSession").GetValue(null);
                    Status = active ? "reused existing active debug session" : "existing inactive debug session preserved";
                    return;
                }
                System.Type local = null;
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    local = assembly.GetType("UnityEngine.Rendering.RenderGraphModule.RenderGraphEditorLocalDebugSession");
                    if (local != null) break;
                }
                if (local == null) { Note("Editor local debug session type is unavailable."); return; }
                type.GetMethod("Create").MakeGenericMethod(local).Invoke(null, null);
                createdSession = type.GetProperty("currentDebugSession").GetValue(null);
                Status = "created temporary local debug session";
            }
            catch (System.Exception ex) { Note("Graph budget probe unavailable: " + ex.GetBaseException().Message); }
        }

        public void Read(UnityEngine.Camera camera)
        {
            LastCount = -1;
            LastPasses = new string[0];
            LastStatus = "unavailable: no debug data for this request camera";
            if (type == null) return;
            try
            {
                if (!(bool)type.GetProperty("hasActiveDebugSession").GetValue(null)) return;
                object cameraId = camera.GetEntityId();
                var graphs = (System.Collections.IEnumerable)type.GetMethod("GetRegisteredGraphs").Invoke(null, null);
                foreach (object graphName in graphs)
                {
                    var executions = (System.Collections.IEnumerable)type.GetMethod("GetExecutions").Invoke(null, new[] { graphName });
                    foreach (object execution in executions)
                    {
                        object id = execution.GetType().GetField("id").GetValue(execution);
                        if (!id.Equals(cameraId)) continue;
                        object debugData = type.GetMethod("GetDebugData").Invoke(null, new[] { graphName, id });
                        var passes = (System.Collections.IEnumerable)debugData.GetType().GetField("passList").GetValue(debugData);
                        var weather = new System.Collections.Generic.List<string>();
                        int totalPasses = 0;
                        foreach (object pass in passes)
                        {
                            totalPasses++;
                            string name = (string)pass.GetType().GetField("name").GetValue(pass);
                            bool culled = (bool)pass.GetType().GetField("culled").GetValue(pass);
                            if (!culled && (name == "Weather.Particles" || name == "Weather.Atmosphere")) weather.Add(name);
                        }
                        if (totalPasses == 0) continue;
                        LastCount = weather.Count;
                        LastPasses = weather.ToArray();
                        LastStatus = "measured compiled non-culled passes for this camera execution";
                        return;
                    }
                }
                Note("StandardRequest generated no graph debug data for the validation camera; URP disables generateDebugData while camera.isProcessingRenderRequest. Normal camera-frame budget capture remains pending.");
            }
            catch (System.Exception ex)
            {
                LastStatus = "unavailable: " + ex.GetBaseException().Message;
                Note(LastStatus);
            }
        }

        private void Note(string text) { if (!Notes.Contains(text)) Notes.Add(text); }
        public void Dispose()
        {
            if (type != null && createdSession != null
                && System.Object.ReferenceEquals(type.GetProperty("currentDebugSession").GetValue(null), createdSession))
                type.GetMethod("EndSession").Invoke(null, null);
        }
    }
}
