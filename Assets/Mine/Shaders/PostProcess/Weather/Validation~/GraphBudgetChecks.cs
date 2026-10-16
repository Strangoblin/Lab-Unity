// Temporary validation: capture real Camera.main frames without saving or editing assets.
public class Script
{
    private const string SessionKey = "Weather.GraphBudgetChecks";
    private const double TimeoutSeconds = 10.0;
    private const System.Reflection.BindingFlags PublicStatic =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
    private const System.Reflection.BindingFlags PublicInstance =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

    public static object Main()
    {
        var capture = new Capture();
        try
        {
            capture.Arm();
            return "{\"status\":\"armed\",\"passed\":false,\"caseCount\":0,\"expectedCaseCount\":8,\"sessionKey\":"
                + Quote(SessionKey) + "}";
        }
        catch (System.Exception exception)
        {
            return capture.Finish(exception.GetBaseException().Message);
        }
    }

    private sealed class Capture
    {
        private UnityEngine.Camera _camera;
        private Mine.Weather.WeatherFeature _feature;
        private Mine.Weather.WeatherFeature.Settings _settings;
        private Mine.Weather.WeatherProfile _originalProfile;
        private Mine.Weather.WeatherProfile _temporaryProfile;
        private Mine.Weather.WeatherDebugMode _originalDebug;
        private bool _originalActive;
        private bool _originalBackground;
        private bool _originalPaused;
        private bool _savedFeature;
        private bool _savedEditor;
        private bool _inCameraFrame;
        private bool _finished;
        private bool _featureRestored;
        private bool _editorRestored;
        private double _startTime;
        private int _caseIndex;
        private int _frameAtBegin;
        private float _timeAtStart;
        private System.Type _sessionType;
        private object _session;
        private object _ownedSession;
        private string _sessionStatus = "not started";
        private readonly System.Collections.Generic.List<string> _cases = new System.Collections.Generic.List<string>();
        private readonly System.Collections.Generic.List<string> _errors = new System.Collections.Generic.List<string>();
        private readonly System.Collections.Generic.List<string> _cleanupErrors = new System.Collections.Generic.List<string>();

        public void Arm()
        {
            Require(UnityEngine.Application.isPlaying, "GraphBudgetChecks requires Play Mode.");
            _camera = UnityEngine.Camera.main;
            Require(_camera != null && _camera.enabled && _camera.gameObject.activeInHierarchy,
                "An enabled Camera.main is required for normal-frame graph capture.");
            Require(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, "The current render pipeline is not URP.");
            Require(UnityEngine.SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
                "A real GPU graphics device is required.");
            var renderer = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(
                "Assets/Settings/PC_Renderer.asset");
            Require(renderer != null, "PC_Renderer.asset is missing.");
            foreach (var candidate in renderer.rendererFeatures)
                if (candidate is Mine.Weather.WeatherFeature) _feature = (Mine.Weather.WeatherFeature)candidate;
            Require(_feature != null && _feature.settings != null, "PC_Renderer has no usable WeatherFeature.");
            _settings = _feature.settings;
            Require(_settings.shader != null && _settings.shader.isSupported && _settings.noiseTexture != null,
                "Weather Shader or noise texture is missing or unsupported.");
            _originalProfile = _settings.profile;
            _originalDebug = _settings.debug;
            _originalActive = _feature.isActive;
            _savedFeature = true;
            _originalBackground = UnityEngine.Application.runInBackground;
            _originalPaused = UnityEditor.EditorApplication.isPaused;
            _savedEditor = true;
            var rain = UnityEditor.AssetDatabase.LoadAssetAtPath<Mine.Weather.WeatherProfile>(
                "Assets/Mine/Shaders/PostProcess/Weather/Presets/Rain.asset");
            Require(rain != null, "The actual Rain preset is missing.");
            _temporaryProfile = UnityEngine.Object.Instantiate(rain);
            _temporaryProfile.name = "Weather graph budget temporary Rain";
            _temporaryProfile.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            Require(_temporaryProfile.Kind == Mine.Weather.WeatherKind.Rain, "The Rain preset has an unexpected weather kind.");
            BeginDebugSession();
            _startTime = UnityEditor.EditorApplication.timeSinceStartup;
            _timeAtStart = UnityEngine.Time.time;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += OnEndCamera;
            UnityEditor.EditorApplication.update += OnEditorUpdate;
            UnityEngine.Application.logMessageReceived += OnLog;
            UnityEngine.Application.runInBackground = true;
            if (_originalPaused) UnityEditor.EditorApplication.isPaused = false;
            UnityEditor.SessionState.SetString(SessionKey,
                "{\"status\":\"armed\",\"passed\":false,\"caseCount\":0,\"expectedCaseCount\":8}");
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }

        private void BeginDebugSession()
        {
            _sessionType = typeof(UnityEngine.Rendering.RenderGraphModule.RenderGraph).Assembly.GetType(
                "UnityEngine.Rendering.RenderGraphModule.RenderGraphDebugSession");
            Require(_sessionType != null, "RenderGraphDebugSession reflection type is unavailable.");
            _session = CurrentSession();
            if (_session != null)
            {
                Require(IsDebugSessionActive(), "An existing inactive RenderGraph debug session was preserved; no graph capture is possible.");
                _sessionStatus = "reused existing active debug session";
                return;
            }
            System.Type localType = null;
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                localType = assembly.GetType("UnityEngine.Rendering.RenderGraphModule.RenderGraphEditorLocalDebugSession");
                if (localType != null) break;
            }
            Require(localType != null, "RenderGraphEditorLocalDebugSession reflection type is unavailable.");
            _sessionType.GetMethod("Create", PublicStatic).MakeGenericMethod(localType).Invoke(null, null);
            _ownedSession = CurrentSession();
            _session = _ownedSession;
            Require(_ownedSession != null && IsDebugSessionActive(), "The temporary local RenderGraph debug session did not activate.");
            _sessionStatus = "created temporary local debug session";
        }

        private object CurrentSession()
        {
            return _sessionType.GetProperty("currentDebugSession", PublicStatic).GetValue(null);
        }

        private bool IsDebugSessionActive()
        {
            return (bool)_sessionType.GetProperty("hasActiveDebugSession", PublicStatic).GetValue(null);
        }

        private void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (_finished || camera != _camera || _inCameraFrame) return;
            try
            {
                Require(UnityEngine.Application.isPlaying, "Play Mode ended before graph capture finished.");
                Require(System.Object.ReferenceEquals(CurrentSession(), _session) && IsDebugSessionActive(),
                    "The RenderGraph debug session changed or became inactive during graph capture.");
                _inCameraFrame = true;
                _frameAtBegin = UnityEngine.Time.frameCount;
                _errors.Clear();
                InvalidateCameraDebugData();
                SetModules(_temporaryProfile, MaskFor(_caseIndex));
                _settings.profile = _temporaryProfile;
                _settings.debug = DebugFor(_caseIndex);
                _feature.SetActive(true);
            }
            catch (System.Exception exception)
            {
                Finish(exception.GetBaseException().Message);
            }
        }

        private void InvalidateCameraDebugData()
        {
            foreach (var entry in FindCameraDebugData())
                entry.Data.GetType().GetField("valid", PublicInstance).SetValue(entry.Data, false);
        }

        private void OnEndCamera(UnityEngine.Rendering.ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (_finished || camera != _camera || !_inCameraFrame) return;
            string failure = null;
            try
            {
                Require(_errors.Count == 0, "Normal camera rendering reported errors: " + string.Join(" | ", _errors.ToArray()));
                CaptureCase();
                _caseIndex++;
            }
            catch (System.Exception exception)
            {
                failure = exception.GetBaseException().Message;
            }
            finally
            {
                _inCameraFrame = false;
                try { RestoreFeature(); }
                catch (System.Exception exception) { failure = "Feature restoration failed: " + exception.GetBaseException().Message; }
            }
            if (failure != null || _caseIndex == 8) Finish(failure);
            else UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }

        private void CaptureCase()
        {
            var entries = FindCameraDebugData();
            var validEntries = new System.Collections.Generic.List<GraphEntry>();
            foreach (var entry in entries)
                if ((bool)entry.Data.GetType().GetField("valid", PublicInstance).GetValue(entry.Data)) validEntries.Add(entry);
            Require(validEntries.Count == 1,
                "Expected one fresh, valid normal-frame RenderGraph execution for Camera.main; found " + validEntries.Count + ".");
            var selected = validEntries[0];
            var weather = new System.Collections.Generic.List<string>();
            int totalPasses = 0;
            int culledWeatherPasses = 0;
            var passes = (System.Collections.IEnumerable)selected.Data.GetType().GetField("passList", PublicInstance).GetValue(selected.Data);
            Require(passes != null, "The captured RenderGraph passList is null.");
            foreach (object pass in passes)
            {
                totalPasses++;
                string name = (string)pass.GetType().GetField("name", PublicInstance).GetValue(pass);
                bool culled = (bool)pass.GetType().GetField("culled", PublicInstance).GetValue(pass);
                if (name != "Weather.Particles" && name != "Weather.Atmosphere") continue;
                if (culled) culledWeatherPasses++;
                else weather.Add(name);
            }
            Require(totalPasses > 0, "The captured RenderGraph execution has no passes.");
            var expected = ExpectedPasses(_caseIndex);
            bool matches = weather.Count == expected.Length;
            for (int index = 0; matches && index < expected.Length; index++) matches = weather[index] == expected[index];
            _cases.Add("{\"name\":" + Quote(NameFor(_caseIndex))
                + ",\"moduleMask\":" + MaskFor(_caseIndex)
                + ",\"debug\":" + Quote(DebugFor(_caseIndex).ToString())
                + ",\"passed\":" + Bool(matches)
                + ",\"frame\":" + _frameAtBegin
                + ",\"graph\":" + Quote(selected.Graph)
                + ",\"execution\":" + Quote(selected.Execution)
                + ",\"valid\":true,\"totalPasses\":" + totalPasses
                + ",\"culledWeatherPasses\":" + culledWeatherPasses
                + ",\"weatherPassCount\":" + weather.Count
                + ",\"expectedWeatherPassCount\":" + expected.Length
                + ",\"executedWeatherPasses\":" + StringArray(weather.ToArray())
                + ",\"expectedWeatherPasses\":" + StringArray(expected) + "}");
            Require(matches, NameFor(_caseIndex) + " expected [" + string.Join(",", expected)
                + "] but measured [" + string.Join(",", weather.ToArray()) + "].");
        }

        private System.Collections.Generic.List<GraphEntry> FindCameraDebugData()
        {
            var entries = new System.Collections.Generic.List<GraphEntry>();
            object cameraId = _camera.GetEntityId();
            var graphs = (System.Collections.IEnumerable)_sessionType.GetMethod("GetRegisteredGraphs", PublicStatic).Invoke(null, null);
            foreach (object graph in graphs)
            {
                var executions = (System.Collections.IEnumerable)_sessionType.GetMethod("GetExecutions", PublicStatic).Invoke(null, new object[] { graph });
                foreach (object execution in executions)
                {
                    string name = (string)execution.GetType().GetProperty("name", PublicInstance).GetValue(execution);
                    object id = execution.GetType().GetProperty("id", PublicInstance).GetValue(execution);
                    if (name != _camera.name || !id.Equals(cameraId)) continue;
                    object data = _sessionType.GetMethod("GetDebugData", PublicStatic).Invoke(null, new object[] { graph, id });
                    if (data != null) entries.Add(new GraphEntry { Graph = (string)graph, Execution = name, Data = data });
                }
            }
            return entries;
        }

        private void OnEditorUpdate()
        {
            if (_finished) return;
            if (!UnityEngine.Application.isPlaying) { Finish("Play Mode ended before normal-frame capture completed."); return; }
            if (UnityEditor.EditorApplication.timeSinceStartup - _startTime >= TimeoutSeconds)
            {
                Finish("Timed out after 10 seconds waiting for normal Camera.main frames; captured " + _cases.Count + " of 8 cases.");
                return;
            }
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }

        private void OnLog(string message, string stackTrace, UnityEngine.LogType type)
        {
            if (!_inCameraFrame) return;
            if (type == UnityEngine.LogType.Error || type == UnityEngine.LogType.Exception || type == UnityEngine.LogType.Assert
                || message.StartsWith("[Weather]", System.StringComparison.Ordinal)) _errors.Add(message);
        }

        private void RestoreFeature()
        {
            if (!_savedFeature) return;
            Require(_feature != null, "WeatherFeature was destroyed during validation.");
            _feature.settings = _settings;
            _settings.profile = _originalProfile;
            _settings.debug = _originalDebug;
            _feature.SetActive(_originalActive);
            _featureRestored = System.Object.ReferenceEquals(_settings.profile, _originalProfile)
                && _settings.debug == _originalDebug && _feature.isActive == _originalActive;
        }

        public string Finish(string failure)
        {
            if (_finished) return UnityEditor.SessionState.GetString(SessionKey, "");
            _finished = true;
            try
            {
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
                UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnEndCamera;
                UnityEditor.EditorApplication.update -= OnEditorUpdate;
                UnityEngine.Application.logMessageReceived -= OnLog;
            }
            finally
            {
                try { RestoreFeature(); }
                catch (System.Exception exception) { _cleanupErrors.Add("Restore Feature: " + exception.GetBaseException().Message); }
                try
                {
                    if (_savedEditor)
                    {
                        UnityEngine.Application.runInBackground = _originalBackground;
                        UnityEditor.EditorApplication.isPaused = _originalPaused;
                        _editorRestored = UnityEngine.Application.runInBackground == _originalBackground
                            && UnityEditor.EditorApplication.isPaused == _originalPaused;
                    }
                }
                catch (System.Exception exception) { _cleanupErrors.Add("Restore Editor: " + exception.GetBaseException().Message); }
                try
                {
                    if (_temporaryProfile != null) UnityEngine.Object.DestroyImmediate(_temporaryProfile);
                    _temporaryProfile = null;
                }
                catch (System.Exception exception) { _cleanupErrors.Add("Release temporary Profile: " + exception.GetBaseException().Message); }
                try
                {
                    if (_ownedSession != null && System.Object.ReferenceEquals(CurrentSession(), _ownedSession))
                        _sessionType.GetMethod("EndSession", PublicStatic).Invoke(null, null);
                }
                catch (System.Exception exception) { _cleanupErrors.Add("End owned debug session: " + exception.GetBaseException().Message); }
            }
            if (failure == null && _cases.Count != 8) failure = "Normal-frame graph capture did not complete all 8 cases.";
            if (_cleanupErrors.Count > 0) failure = (failure == null ? "" : failure + " | ") + string.Join(" | ", _cleanupErrors.ToArray());
            bool passed = failure == null && _featureRestored && _editorRestored;
            string result = "{\"status\":" + Quote(passed ? "passed" : "failed")
                + ",\"passed\":" + Bool(passed)
                + ",\"reason\":" + Quote(failure ?? "Measured normal-camera-frame RenderGraph budgets for all 8 cases.")
                + ",\"caseCount\":" + _cases.Count + ",\"expectedCaseCount\":8"
                + ",\"camera\":" + Quote(_camera == null ? "" : _camera.name)
                + ",\"graphSessionStatus\":" + Quote(_sessionStatus)
                + ",\"featureSettingsRestored\":" + Bool(_featureRestored)
                + ",\"editorSettingsRestored\":" + Bool(_editorRestored)
                + ",\"originalFeatureActive\":" + Bool(_originalActive)
                + ",\"originalRunInBackground\":" + Bool(_originalBackground)
                + ",\"originalPaused\":" + Bool(_originalPaused)
                + ",\"timeAtStart\":" + _timeAtStart.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"timeAtEnd\":" + UnityEngine.Time.time.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"cases\":[" + string.Join(",", _cases.ToArray()) + "]}";
            UnityEditor.SessionState.SetString(SessionKey, result);
            return result;
        }
    }

    private sealed class GraphEntry
    {
        public string Graph;
        public string Execution;
        public object Data;
    }

    private static int MaskFor(int index)
    {
        return index == 0 ? 0 : index == 1 ? 1 : index == 2 ? 14 : 15;
    }

    private static Mine.Weather.WeatherDebugMode DebugFor(int index)
    {
        return index < 4 ? Mine.Weather.WeatherDebugMode.Off : (Mine.Weather.WeatherDebugMode)(index - 3);
    }

    private static string NameFor(int index)
    {
        return index < 4 ? "Off/mask" + MaskFor(index) : "Debug/" + DebugFor(index);
    }

    private static string[] ExpectedPasses(int index)
    {
        if (index == 0) return new string[0];
        if (index == 1 || index == 4 || index == 5) return new[] { "Weather.Particles" };
        if (index == 3) return new[] { "Weather.Particles", "Weather.Atmosphere" };
        return new[] { "Weather.Atmosphere" };
    }

    private static void SetModules(Mine.Weather.WeatherProfile profile, int mask)
    {
        profile.Particles.Opacity = (mask & 1) == 0 ? 0f : 0.5f;
        profile.Lens.Strength = (mask & 2) == 0 ? 0f : 0.5f;
        profile.Fog.Strength = (mask & 4) == 0 ? 0f : 0.5f;
        profile.Tone.Strength = (mask & 8) == 0 ? 0f : 1f;
    }

    private static string StringArray(string[] values)
    {
        var quoted = new string[values.Length];
        for (int index = 0; index < values.Length; index++) quoted[index] = Quote(values[index]);
        return "[" + string.Join(",", quoted) + "]";
    }

    private static string Quote(string value)
    {
        var result = new System.Text.StringBuilder("\"");
        foreach (char character in value ?? "")
        {
            switch (character)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                default:
                    if (character < 32) result.Append("\\u" + ((int)character).ToString("x4"));
                    else result.Append(character);
                    break;
            }
        }
        return result.Append('"').ToString();
    }

    private static string Bool(bool value) { return value ? "true" : "false"; }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new System.InvalidOperationException(reason);
    }
}
