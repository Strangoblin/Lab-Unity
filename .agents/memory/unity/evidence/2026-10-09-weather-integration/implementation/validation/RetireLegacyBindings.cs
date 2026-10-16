public class Script
{
    public static object Main()
    {
        var renderer = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        var weather = renderer.rendererFeatures.Find(f => f is Mine.Weather.WeatherFeature) as Mine.Weather.WeatherFeature;
        if (weather == null || weather.settings.profile == null)
            throw new System.InvalidOperationException("Weather must be installed before legacy retirement.");
        foreach (var item in renderer.rendererFeatures)
        {
            var debug = item as Mine.RenderingDebug.DebugOutputFeature;
            if (debug == null) continue;
            string path = debug.settings.debugMaterial != null ? UnityEditor.AssetDatabase.GetAssetPath(debug.settings.debugMaterial)
                : debug.settings.debugShader != null ? UnityEditor.AssetDatabase.GetAssetPath(debug.settings.debugShader) : "";
            if (!path.StartsWith("Assets/Mine/Shaders/PostProcess/Rainy/")
                && !path.StartsWith("Assets/Mine/Shaders/PostProcess/Sandy/")
                && !path.StartsWith("Assets/Mine/Shaders/PostProcess/Snowy/"))
                continue;
            debug.settings.debugMaterial = null;
            debug.settings.debugShader = null;
            debug.settings.debug = false;
            debug.settings.requireDepth = false;
            debug.settings.additionalPassIndex = -1;
            debug.SetActive(false);
            debug.Create();
            UnityEditor.EditorUtility.SetDirty(debug);
        }
        var validate = typeof(UnityEngine.Rendering.Universal.ScriptableRendererData).GetMethod("ValidateRendererFeatures",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (validate == null || !(bool)validate.Invoke(renderer, null))
            throw new System.InvalidOperationException("Renderer feature validation failed.");
        renderer.SetDirty();
        UnityEditor.EditorUtility.SetDirty(renderer);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(renderer);
        return "Retired legacy bindings. Weather profile=" + weather.settings.profile.name + "; active=" + weather.isActive;
    }
}
