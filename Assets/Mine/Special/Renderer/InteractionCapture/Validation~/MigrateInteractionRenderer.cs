// ════════════════════════════════════════════════════════════════
//  Migrate IA_Renderer — CustomRendererData (legacy) → UniversalRendererData + InteractionCaptureFeature.
// ════════════════════════════════════════════════════════════════
using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>One-shot editor migration; args: none. Reads legacy masks from the old asset before replacing it.</summary>
public class Script
{
    private const string RendererPath = "Assets/Settings/IA_Renderer.asset";
    private const string RpAssetPath = "Assets/Settings/PC_RPAsset.asset";
    private const int RendererIndex = 1;

    public static object Main(string[] args)
    {
        if (AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath) != null)
            return Report("already-migrated", null, null);

        ScriptableRendererData legacy = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
        if (legacy == null)
            throw new InvalidOperationException($"{RendererPath} is missing or its script is already broken.");

        var legacySo = new SerializedObject(legacy);
        int depthMask = Int(legacySo, "m_DepthLayerMask.m_Bits", 64);
        int colorMask = Int(legacySo, "m_ColorLayerMask.m_Bits", 128);
        int normalMask = Int(legacySo, "m_NormalLayerMask.m_Bits", 0);
        bool copyDepth = Bool(legacySo, "m_CopyDepth", true);
        bool copyColor = Bool(legacySo, "m_CopyColor", true);
        bool copyNormal = Bool(legacySo, "m_CopyNormal", false);
        bool transparentMode = Bool(legacySo, "m_TransparentMode", false);

        AssetDatabase.DeleteAsset(RendererPath);

        var data = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(data, RendererPath);

        // URP must draw nothing of its own: the feature owns depth, colour and the prepasses.
        data.opaqueLayerMask = 0;
        data.transparentLayerMask = 0;
        data.prepassLayerMask = 0;

        var feature = ScriptableObject.CreateInstance<Mine.Rendering.InteractionCaptureFeature>();
        feature.name = "Interaction Capture";
        feature.settings.copyDepth = copyDepth;
        feature.settings.copyColor = copyColor;
        feature.settings.copyNormal = copyNormal;
        feature.settings.transparentMode = transparentMode;
        feature.settings.depthLayerMask = depthMask;
        feature.settings.colorLayerMask = colorMask;
        feature.settings.normalLayerMask = normalMask;
        AssetDatabase.AddObjectToAsset(feature, data);

        var dataSo = new SerializedObject(data);
        SerializedProperty features = dataSo.FindProperty("m_RendererFeatures");
        SerializedProperty featureMap = dataSo.FindProperty("m_RendererFeatureMap");
        if (features == null)
            throw new InvalidOperationException("m_RendererFeatures not found on UniversalRendererData.");

        features.arraySize = 1;
        features.GetArrayElementAtIndex(0).objectReferenceValue = feature;

        if (featureMap != null)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
            featureMap.arraySize = 1;
            featureMap.GetArrayElementAtIndex(0).longValue = localId;
        }

        dataSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);

        var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RpAssetPath);
        if (rp == null)
            throw new InvalidOperationException($"{RpAssetPath} not found.");

        var rpSo = new SerializedObject(rp);
        SerializedProperty renderers = rpSo.FindProperty("m_RendererDataList");
        if (renderers == null || renderers.arraySize <= RendererIndex)
            throw new InvalidOperationException($"m_RendererDataList has no slot {RendererIndex}.");

        renderers.GetArrayElementAtIndex(RendererIndex).objectReferenceValue = data;
        rpSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rp);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Read back from disk so the report reflects what got serialized.
        var saved = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        string featureGuid = AssetDatabase.AssetPathToGUID(RendererPath);

        var sb = new StringBuilder();
        sb.Append("{\"status\":").Append(Quote("migrated"));
        sb.Append(",\"assetGuid\":").Append(Quote(featureGuid));
        sb.Append(",\"opaqueLayerMask\":").Append(saved != null ? saved.opaqueLayerMask.value : -1);
        sb.Append(",\"transparentLayerMask\":").Append(saved != null ? saved.transparentLayerMask.value : -1);
        sb.Append(",\"prepassLayerMask\":").Append(saved != null ? saved.prepassLayerMask.value : -1);
        sb.Append(",\"featureCount\":").Append(saved != null ? saved.rendererFeatures.Count : -1);
        sb.Append(",\"featureType\":").Append(Quote(saved != null && saved.rendererFeatures.Count > 0 && saved.rendererFeatures[0] != null
            ? saved.rendererFeatures[0].GetType().Name : "none"));
        sb.Append(",\"legacySettings\":{\"depthMask\":").Append(depthMask)
          .Append(",\"colorMask\":").Append(colorMask)
          .Append(",\"normalMask\":").Append(normalMask)
          .Append(",\"copyDepth\":").Append(copyDepth ? "true" : "false")
          .Append(",\"copyColor\":").Append(copyColor ? "true" : "false")
          .Append(",\"copyNormal\":").Append(copyNormal ? "true" : "false")
          .Append(",\"transparentMode\":").Append(transparentMode ? "true" : "false").Append('}');
        sb.Append('}');
        return sb.ToString();
    }

    private static string Report(string status, string guid, string note)
    {
        return "{\"status\":" + Quote(status) + ",\"assetGuid\":" + Quote(guid ?? "") + ",\"note\":" + Quote(note ?? "") + "}";
    }

    private static string Quote(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static int Int(SerializedObject so, string path, int fallback)
    {
        SerializedProperty property = so.FindProperty(path);
        return property != null ? property.intValue : fallback;
    }

    private static bool Bool(SerializedObject so, string path, bool fallback)
    {
        SerializedProperty property = so.FindProperty(path);
        return property != null ? property.boolValue : fallback;
    }
}
