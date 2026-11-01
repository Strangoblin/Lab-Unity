#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Rendering.Universal;

[CustomEditor(typeof(CustomRendererData), true)]
public class CustomRendererDataEditor : ScriptableRendererDataEditor
{
    SerializedProperty m_OpaqueLayerMask;
    SerializedProperty m_TransparentLayerMask;
    SerializedProperty m_DepthLayerMask;
    SerializedProperty m_ColorLayerMask;
    SerializedProperty m_NormalLayerMask;
    SerializedProperty m_CopyDepth;
    SerializedProperty m_CopyColor;
    SerializedProperty m_CopyNormal;
    SerializedProperty m_TransparentMode;

    private void OnEnable()
    {
        m_OpaqueLayerMask      = serializedObject.FindProperty("m_OpaqueLayerMask");
        m_TransparentLayerMask = serializedObject.FindProperty("m_TransparentLayerMask");
        m_DepthLayerMask       = serializedObject.FindProperty("m_DepthLayerMask");
        m_ColorLayerMask       = serializedObject.FindProperty("m_ColorLayerMask");
        m_NormalLayerMask      = serializedObject.FindProperty("m_NormalLayerMask");
        m_CopyDepth            = serializedObject.FindProperty("m_CopyDepth");
        m_CopyColor            = serializedObject.FindProperty("m_CopyColor");
        m_CopyNormal           = serializedObject.FindProperty("m_CopyNormal");
        m_TransparentMode      = serializedObject.FindProperty("m_TransparentMode");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Pass Setting", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(m_CopyDepth);
        EditorGUILayout.PropertyField(m_CopyColor);
        EditorGUILayout.PropertyField(m_CopyNormal);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Per-Pass Layer Mask", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(m_DepthLayerMask,  new GUIContent("Depth Layer Mask"));
        EditorGUILayout.PropertyField(m_ColorLayerMask,  new GUIContent("Color Layer Mask"));
        EditorGUILayout.PropertyField(m_NormalLayerMask, new GUIContent("Normal Layer Mask"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Queue / Transparency", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(m_OpaqueLayerMask);
        EditorGUILayout.PropertyField(m_TransparentLayerMask);
        EditorGUILayout.PropertyField(m_TransparentMode);

        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.Space();
        base.OnInspectorGUI();
    }
}
#endif
