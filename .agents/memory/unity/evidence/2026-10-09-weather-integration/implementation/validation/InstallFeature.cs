public class Script
{
    public static object Main()
    {
        const string path = "Assets/Settings/PC_Renderer.asset";
        var renderer = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(path);
        var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>("Assets/Mine/Shaders/PostProcess/Weather/Weather.shader");
        var profile = UnityEditor.AssetDatabase.LoadAssetAtPath<Mine.Weather.WeatherProfile>("Assets/Mine/Shaders/PostProcess/Weather/Presets/Sand.asset");
        var noise = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(UnityEditor.AssetDatabase.GUIDToAssetPath("acde34ba16b7a4de1ad501e1068301d9"));
        if (renderer == null || shader == null || profile == null || noise == null)
            throw new System.InvalidOperationException("Missing Weather installation resource");
        foreach (var item in renderer.rendererFeatures)
            if (item is Mine.Weather.WeatherFeature)
                throw new System.InvalidOperationException("Weather already installed; inspect instead of duplicating");
        var before = new System.Text.StringBuilder();
        foreach (var item in renderer.rendererFeatures)
            before.AppendLine(item.name + ":" + item.isActive);
        var feature = UnityEngine.ScriptableObject.CreateInstance<Mine.Weather.WeatherFeature>();
        feature.name = "Weather";
        feature.settings.shader = shader;
        feature.settings.profile = profile;
        feature.settings.noiseTexture = noise;
        feature.settings.showInSceneView = true;
        feature.settings.debug = Mine.Weather.WeatherDebugMode.Off;
        feature.SetActive(true);
        feature.Create();
        UnityEditor.AssetDatabase.AddObjectToAsset(feature, renderer);
        renderer.rendererFeatures.Add(feature);
        renderer.SetDirty();
        UnityEditor.EditorUtility.SetDirty(feature);
        UnityEditor.EditorUtility.SetDirty(renderer);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(renderer);
        return "Installed Weather / Sand with shared Noise2DRG. Existing states:\n" + before;
    }
}
