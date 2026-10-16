#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Mine.HDRMaps.Editor;

/// <summary>Compatibility entry for previously serialized window instances and API calls.</summary>
public sealed class InteriorMapBakerWindow : EditorWindow
{
    public static void ShowWindow() => HDRMapBakerWindow.ShowWindow();
    private void OnGUI()
    {
        EditorGUILayout.LabelField("HDR Map Tools", EditorStyles.boldLabel);
        if (GUILayout.Button("Open HDR Map Baker", GUILayout.Height(30f))) HDRMapBakerWindow.ShowWindow();
        if (GUILayout.Button("Open HDR Map Converter", GUILayout.Height(30f))) HDRMapConverterWindow.ShowWindow();
    }
}
#endif
