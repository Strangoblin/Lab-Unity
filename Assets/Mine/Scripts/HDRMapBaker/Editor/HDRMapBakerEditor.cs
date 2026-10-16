#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Mine.HDRMaps.Editor
{
    /// <summary>Edits capture content independently from the converter's projection and coverage.</summary>
    [CustomEditor(typeof(HDRMapBaker), true)]
    public class HDRMapBakerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("HDR Map Baker", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_renderLayers"), new GUIContent("Render Layers"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_includeSky"), new GUIContent("Include Sky"));
            EditorGUILayout.HelpBox(
                "Nothing + Include Sky captures only the sky. Output is a linear HDR 2:1 panorama. "
                + "Choose projection and interior/panorama coverage in HDR Map Converter.",
                MessageType.Info);
            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button("Create / Repair Framework", GUILayout.Height(30f)))
            {
                HDRMapBakerEditorUtility.InitializeFramework((HDRMapBaker)target);
                serializedObject.Update();
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_volume"), new GUIContent("Capture Range"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_direction"), new GUIContent("Capture Frame"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_camera"), new GUIContent("Capture Camera"));
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
