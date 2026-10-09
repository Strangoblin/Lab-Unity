public class Script
{
    private const System.Reflection.BindingFlags InstanceFlags = System.Reflection.BindingFlags.Instance
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    private const System.Reflection.BindingFlags StaticFlags = System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    public static object Main()
    {
        var cases = new System.Collections.Generic.List<string>();
        var diagnostics = new System.Collections.Generic.List<string>();
        var initializationDiagnostics = new System.Collections.Generic.List<string>();
        var unexpectedWeatherLogs = new System.Collections.Generic.List<string>();
        Mine.Weather.WeatherFeature feature = null;
        Mine.Weather.WeatherProfile profile = null;
        UnityEngine.Shader legacySand = null;
        UnityEngine.Application.LogCallback capture = (condition, stackTrace, type) =>
        {
            if (!condition.StartsWith("[Weather]", System.StringComparison.Ordinal))
                return;
            if (type == UnityEngine.LogType.Warning)
                diagnostics.Add(condition);
            else
                unexpectedWeatherLogs.Add(type + ": " + condition);
        };

        UnityEngine.Application.logMessageReceived += capture;
        try
        {
            UnityEngine.Shader shader = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>(
                "Assets/Mine/Shaders/PostProcess/Weather/Weather.shader");
            string legacySource = System.IO.File.ReadAllText(System.IO.Path.Combine(UnityEngine.Application.dataPath,
                "Mine/Shaders/PostProcess/Weather/Validation~/Legacy/Sandy/Sandy.shader"));
            legacySand = UnityEditor.ShaderUtil.CreateShaderAsset(legacySource, true);
            if (legacySand != null) legacySand.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            Require(shader != null && shader.isSupported, "Weather Shader must be imported and supported");
            Require(legacySand != null && legacySand.isSupported, "Legacy Sand Shader must be imported and supported");

            profile = UnityEngine.ScriptableObject.CreateInstance<Mine.Weather.WeatherProfile>();
            profile.ConfigurePreset(Mine.Weather.WeatherKind.Rain, 1f, 0.5f, 0.5f, 1f);
            feature = UnityEngine.ScriptableObject.CreateInstance<Mine.Weather.WeatherFeature>();
            initializationDiagnostics.AddRange(diagnostics);
            diagnostics.Clear();
            var reported = (System.Collections.Generic.HashSet<string>)ReadField(feature, "_reported");
            reported.Clear();
            feature.settings = new Mine.Weather.WeatherFeature.Settings
            {
                shader = shader,
                noiseTexture = UnityEngine.Texture2D.whiteTexture,
                profile = profile,
                debug = Mine.Weather.WeatherDebugMode.Off,
                showInSceneView = true
            };

            CheckMaterialLifecycle(feature, shader, legacySand, diagnostics, cases);
            CheckPassInputs(feature, profile, cases);
            CheckEarlyGuards(feature, profile, shader, diagnostics, cases);

            UnityEngine.Material finalMaterial = (UnityEngine.Material)ReadField(feature, "_material");
            feature.Dispose();
            RequireReleased(feature, finalMaterial, "Dispose");
            feature.Dispose();
            RequireReleased(feature, finalMaterial, "Repeated Dispose");
            cases.Add("Dispose and repeated Dispose release runtime resources");
            Require(unexpectedWeatherLogs.Count == 0,
                "Expected Weather fault diagnostics must be warnings, not errors or exceptions");

            return new
            {
                caseCount = cases.Count,
                cases = cases.ToArray(),
                initializationDiagnostics = initializationDiagnostics.ToArray(),
                expectedDiagnostics = diagnostics.ToArray(),
                nativeDestructionChecked = !UnityEngine.Application.isPlaying
            };
        }
        finally
        {
            if (feature != null)
            {
                feature.Dispose();
                UnityEngine.Object.DestroyImmediate(feature);
            }
            if (profile != null)
                UnityEngine.Object.DestroyImmediate(profile);
            if (legacySand != null)
                UnityEngine.Object.DestroyImmediate(legacySand);
            UnityEngine.Application.logMessageReceived -= capture;
        }
    }

    private static void CheckMaterialLifecycle(Mine.Weather.WeatherFeature feature, UnityEngine.Shader shader,
        UnityEngine.Shader legacySand, System.Collections.Generic.List<string> diagnostics,
        System.Collections.Generic.List<string> cases)
    {
        feature.Create();
        Require(EnsureMaterial(feature), "Supported Weather Shader must create a usable material");
        UnityEngine.Material material = (UnityEngine.Material)ReadField(feature, "_material");
        object pass = ReadField(feature, "_pass");
        Require(material != null && material.shader == shader && pass != null,
            "Create must hold a Weather material and pass");
        Require(!UnityEditor.EditorUtility.IsPersistent(material), "Runtime material must not be a persistent asset");
        Require(material.FindPass("Particles") >= 0 && material.FindPass("Atmosphere") >= 0,
            "Runtime material must expose both named passes");
        Require((UnityEngine.Shader)ReadField(feature, "_loadedShader") == shader,
            "Create must track the loaded Shader");
        cases.Add("Valid Shader creates an independent runtime material");

        Require(EnsureMaterial(feature) && EnsureMaterial(feature), "Same Shader remains valid");
        Require(System.Object.ReferenceEquals(material, ReadField(feature, "_material"))
            && System.Object.ReferenceEquals(pass, ReadField(feature, "_pass")),
            "Same Shader must reuse material and pass");
        cases.Add("Same Shader reuses material and pass");

        int before = diagnostics.Count;
        feature.settings.shader = null;
        Require(!EnsureMaterial(feature) && !EnsureMaterial(feature), "Missing Shader must reject drawing");
        RequireReleased(feature, material, "Missing Shader");
        Require(diagnostics.Count == before + 1, "Repeated missing Shader reports exactly one warning");
        cases.Add("Missing Shader releases resources and diagnoses once");

        feature.settings.shader = shader;
        Require(EnsureMaterial(feature), "Restoring Shader must recreate usable resources");
        material = (UnityEngine.Material)ReadField(feature, "_material");
        Require(material != null && material.shader == shader, "Shader recovery recreates correct material");
        cases.Add("Weather resources recover after missing Shader");

        before = diagnostics.Count;
        feature.settings.shader = legacySand;
        Require(!EnsureMaterial(feature) && !EnsureMaterial(feature), "Legacy pass names must reject Weather drawing");
        if (!UnityEngine.Application.isPlaying)
            Require(material == null, "Shader replacement destroys the previous native material");
        object invalidPass = ReadField(feature, "_pass");
        Require(invalidPass != null, "Invalid-pass check must inspect the replacement Shader");
        System.Reflection.PropertyInfo expectedPasses = invalidPass.GetType().GetProperty("HasExpectedPasses", InstanceFlags);
        Require(expectedPasses != null && !(bool)expectedPasses.GetValue(invalidPass),
            "Legacy Sand names must fail the actual HasExpectedPasses check");
        Require(diagnostics.Count == before + 1, "Repeated invalid pass names report exactly one warning");
        cases.Add("Legacy Sand pass names are rejected and diagnosed once");

        UnityEngine.Material invalidMaterial = (UnityEngine.Material)ReadField(feature, "_material");
        feature.settings.shader = shader;
        Require(EnsureMaterial(feature), "Weather Shader recovers after invalid pass names");
        Require((UnityEngine.Material)ReadField(feature, "_material") != null
            && (UnityEngine.Shader)ReadField(feature, "_loadedShader") == shader,
            "Recovery restores the Weather Shader");
        if (!UnityEngine.Application.isPlaying)
            Require(invalidMaterial == null, "Recovery destroys the rejected native material");
        cases.Add("Weather resources recover after incompatible Shader");
    }

    private static void CheckPassInputs(Mine.Weather.WeatherFeature feature, Mine.Weather.WeatherProfile profile,
        System.Collections.Generic.List<string> cases)
    {
        object pass = ReadField(feature, "_pass");
        System.Reflection.MethodInfo needsDepth = FindMethod(pass.GetType(), "NeedsDepth", StaticFlags);
        System.Reflection.MethodInfo prepare = FindMethod(pass.GetType(), "Prepare", InstanceFlags);
        var renderPass = (UnityEngine.Rendering.Universal.ScriptableRenderPass)pass;
        bool[] depthWithFog = { true, false, false, false, true, true };
        bool[] depthWithoutFog = { false, false, false, false, true, false };

        for (int fogState = 0; fogState < 2; fogState++)
        {
            Mine.Weather.ResolvedWeatherSettings parameters = profile.Resolve();
            parameters.FogStrength = fogState == 0 ? 0f : 0.5f;
            foreach (Mine.Weather.WeatherDebugMode debug in System.Enum.GetValues(typeof(Mine.Weather.WeatherDebugMode)))
            {
                bool expected = fogState == 0 ? depthWithoutFog[(int)debug] : depthWithFog[(int)debug];
                bool actual = (bool)needsDepth.Invoke(null, new object[] { parameters, debug });
                Require(actual == expected, "NeedsDepth " + debug + " with fogState=" + fogState);
                prepare.Invoke(pass, new object[] { parameters, UnityEngine.Texture2D.whiteTexture, debug });
                var expectedInput = expected
                    ? UnityEngine.Rendering.Universal.ScriptableRenderPassInput.Depth
                    : UnityEngine.Rendering.Universal.ScriptableRenderPassInput.None;
                Require(renderPass.input == expectedInput,
                    "Prepare must request exactly " + expectedInput + " for " + debug + " fogState=" + fogState);
                Require(renderPass.renderPassEvent == UnityEngine.Rendering.Universal.RenderPassEvent.AfterRenderingPostProcessing,
                    "Prepare must preserve AfterRenderingPostProcessing");
                Require(renderPass.requiresIntermediateTexture, "Weather needs a sampleable intermediate color");
                Require((Mine.Weather.WeatherDebugMode)ReadField(pass, "_debug") == debug,
                    "Prepare captures its Debug mode");
                cases.Add("Depth/input contract " + debug + " fogState=" + fogState);
            }
        }

        Mine.Weather.ResolvedWeatherSettings snapshot = profile.Resolve();
        snapshot.Randomness = 0.321f;
        prepare.Invoke(pass, new object[] { snapshot, UnityEngine.Texture2D.whiteTexture, Mine.Weather.WeatherDebugMode.Lens });
        snapshot.Randomness = 0.999f;
        var captured = (Mine.Weather.ResolvedWeatherSettings)ReadField(pass, "_parameters");
        Require(UnityEngine.Mathf.Abs(captured.Randomness - 0.321f) < 0.000001f,
            "Prepare must capture parameters by value");
        cases.Add("Prepare captures an independent value snapshot");
    }

    private static void CheckEarlyGuards(Mine.Weather.WeatherFeature feature, Mine.Weather.WeatherProfile profile,
        UnityEngine.Shader shader, System.Collections.Generic.List<string> diagnostics,
        System.Collections.Generic.List<string> cases)
    {
        var frame = new UnityEngine.Rendering.ContextContainer();
        try
        {
            var camera = frame.Create<UnityEngine.Rendering.Universal.UniversalCameraData>();
            object boxedCameraData = new UnityEngine.Rendering.Universal.CameraData();
            FindField(typeof(UnityEngine.Rendering.Universal.CameraData), "frameData").SetValue(boxedCameraData, frame);
            var rendering = new UnityEngine.Rendering.Universal.RenderingData
            {
                cameraData = (UnityEngine.Rendering.Universal.CameraData)boxedCameraData
            };
            var settings = feature.settings;
            object material = ReadField(feature, "_material");
            object pass = ReadField(feature, "_pass");
            object parameters = ReadField(pass, "_parameters");
            object debug = ReadField(pass, "_debug");
            settings.shader = null;

            camera.cameraType = UnityEngine.CameraType.Game;
            camera.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
            camera.resolveFinalTarget = true;
            CheckGuard(feature, ref rendering, material, pass, parameters, debug, "Overlay camera", cases);

            camera.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Base;
            camera.cameraType = UnityEngine.CameraType.Preview;
            CheckGuard(feature, ref rendering, material, pass, parameters, debug, "Preview camera", cases);
            camera.cameraType = UnityEngine.CameraType.Reflection;
            CheckGuard(feature, ref rendering, material, pass, parameters, debug, "Reflection camera", cases);
            camera.cameraType = UnityEngine.CameraType.SceneView;
            settings.showInSceneView = false;
            CheckGuard(feature, ref rendering, material, pass, parameters, debug, "Disabled SceneView camera", cases);

            camera.cameraType = UnityEngine.CameraType.Game;
            camera.resolveFinalTarget = false;
            int before = diagnostics.Count;
            CheckGuard(feature, ref rendering, material, pass, parameters, debug, "Non-final Base camera", cases);
            CallAddGuard(feature, ref rendering, "Repeated non-final Base camera");
            Require(diagnostics.Count == before + 1, "Non-final Camera Stack reports exactly one warning");

            camera.resolveFinalTarget = true;
            settings.debug = Mine.Weather.WeatherDebugMode.Off;
            profile.Particles.Opacity = 0f;
            profile.Lens.Strength = 0f;
            profile.Fog.Strength = 0f;
            profile.Tone.Strength = 0f;
            CheckGuard(feature, ref rendering, material, pass, parameters, debug, "All modules disabled", cases);

            profile.ConfigurePreset(Mine.Weather.WeatherKind.Rain);
            settings.profile = null;
            before = diagnostics.Count;
            CheckGuard(feature, ref rendering, material, pass, parameters, debug, "Missing Profile", cases);
            CallAddGuard(feature, ref rendering, "Repeated missing Profile");
            Require(diagnostics.Count == before + 1, "Missing resources report exactly one warning");
            settings.profile = profile;
            settings.noiseTexture = null;
            CheckGuard(feature, ref rendering, material, pass, parameters, debug, "Missing noise", cases);
            Require(diagnostics.Count == before + 1, "Missing Profile and noise share one resource diagnostic");

            feature.settings = null;
            var emptyRendering = new UnityEngine.Rendering.Universal.RenderingData();
            CallAddGuard(feature, ref emptyRendering, "Missing Settings");
            Require(System.Object.ReferenceEquals(ReadField(feature, "_material"), material),
                "Missing Settings must return before touching runtime resources");
            cases.Add("Missing Settings exits before camera access");

            feature.settings = settings;
            settings.shader = shader;
            settings.profile = profile;
            settings.noiseTexture = UnityEngine.Texture2D.whiteTexture;
            settings.showInSceneView = true;
        }
        finally
        {
            frame.Dispose();
        }
    }

    private static void CheckGuard(Mine.Weather.WeatherFeature feature,
        ref UnityEngine.Rendering.Universal.RenderingData rendering, object material, object pass,
        object parameters, object debug, string name, System.Collections.Generic.List<string> cases)
    {
        CallAddGuard(feature, ref rendering, name);
        Require(System.Object.ReferenceEquals(ReadField(feature, "_material"), material)
            && System.Object.ReferenceEquals(ReadField(feature, "_pass"), pass),
            name + " must return before EnsureMaterial");
        Require(parameters.Equals(ReadField(pass, "_parameters")) && debug.Equals(ReadField(pass, "_debug")),
            name + " must return before Prepare");
        cases.Add(name + " exits before material preparation and enqueue");
    }

    private static void CallAddGuard(Mine.Weather.WeatherFeature feature,
        ref UnityEngine.Rendering.Universal.RenderingData rendering, string name)
    {
        try
        {
            feature.AddRenderPasses(null, ref rendering);
        }
        catch (System.Exception exception)
        {
            throw new System.InvalidOperationException("Weather Feature guard failed: " + name
                + " reached an invalid camera access or null renderer enqueue", exception);
        }
    }

    private static bool EnsureMaterial(Mine.Weather.WeatherFeature feature)
    {
        return (bool)FindMethod(feature.GetType(), "EnsureMaterial", InstanceFlags).Invoke(feature, null);
    }

    private static void RequireReleased(Mine.Weather.WeatherFeature feature, UnityEngine.Material previous, string name)
    {
        Require(ReadField(feature, "_material") == null && ReadField(feature, "_pass") == null
            && ReadField(feature, "_loadedShader") == null, name + " must clear all runtime resource references");
        if (!UnityEngine.Application.isPlaying)
            Require(previous == null, name + " must destroy the native material in Edit Mode");
    }

    private static object ReadField(object target, string name)
    {
        return FindField(target.GetType(), name).GetValue(target);
    }

    private static System.Reflection.FieldInfo FindField(System.Type type, string name)
    {
        System.Reflection.FieldInfo field = type.GetField(name, InstanceFlags);
        Require(field != null, "Reflection field exists: " + type.FullName + "." + name);
        return field;
    }

    private static System.Reflection.MethodInfo FindMethod(System.Type type, string name, System.Reflection.BindingFlags flags)
    {
        System.Reflection.MethodInfo method = type.GetMethod(name, flags);
        Require(method != null, "Reflection method exists: " + type.FullName + "." + name);
        return method;
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
            throw new System.InvalidOperationException("Weather Feature check failed: " + name);
    }
}
