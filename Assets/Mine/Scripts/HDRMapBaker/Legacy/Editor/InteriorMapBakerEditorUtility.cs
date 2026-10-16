#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Mine.HDRMaps.Editor;

/// <summary>Legacy framework API; creation and repair are owned by the HDR tools.</summary>
public static class InteriorMapBakerEditorUtility
{
    public static InteriorMapBaker CreateStandardFramework()
    {
        var root = new GameObject("HDRMapBaker");
        Undo.RegisterCreatedObjectUndo(root, "Create HDR Map Baker Framework");
        var baker = Undo.AddComponent<InteriorMapBaker>(root);
        HDRMapBakerEditorUtility.InitializeFramework(baker);
        return baker;
    }
    public static void InitializeFramework(InteriorMapBaker baker)
        => HDRMapBakerEditorUtility.InitializeFramework(baker);
}
#endif
