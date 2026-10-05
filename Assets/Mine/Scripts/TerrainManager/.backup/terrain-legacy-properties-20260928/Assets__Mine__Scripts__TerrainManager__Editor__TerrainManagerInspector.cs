// ════════════════════════════════════════════════════════════
//  TerrainManagerInspector — Voronoi 地区与道路参数、运行时重生成
// ════════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;

namespace Mine.Terrain.Editor
{
    [CustomEditor(typeof(TerrainManager))]
    public sealed class TerrainManagerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            SerializedProperty settings = serializedObject.FindProperty("_settings");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Voronoi Distribution", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("_voronoiRegionWidth"),
                new GUIContent("Voronoi Region Width"));
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("_voronoiRegionSoftness"),
                new GUIContent("Voronoi Region Softness"));
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("_voronoiRoadWidth"),
                new GUIContent("Voronoi Route Width"));
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("_voronoiCornerSoftness"),
                new GUIContent("Voronoi Corner Softness"));
            bool distributionChanged = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            if (HasLegacyGrassRule(settings))
                EditorGUILayout.HelpBox("Grassland is now the base layer. The serialized Grassland " +
                    "site rule is retained for compatibility but ignored during site placement.",
                    MessageType.Info);
            float broadWarp = settings.FindPropertyRelative("_broadWarpDistance").floatValue;
            float detailWarp = settings.FindPropertyRelative("_detailWarpDistance").floatValue;
            if (Mathf.Approximately(broadWarp, 0f) && Mathf.Approximately(detailWarp, 0f))
                EditorGUILayout.HelpBox("Both final data warp distances are zero. " +
                    "Regions and roads will keep their unperturbed SDF shapes.", MessageType.Info);

            TerrainManager manager = (TerrainManager)target;
            if (distributionChanged && Application.isPlaying)
                manager.Generate();
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Regenerate Terrain"))
                    manager.Generate();
            }
        }

        private static bool HasLegacyGrassRule(SerializedProperty settings)
        {
            SerializedProperty rules = settings.FindPropertyRelative("_featureRules");
            for (int i = 0; i < rules.arraySize; i++)
            {
                SerializedProperty region = rules.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("_region");
                if (region.enumValueIndex == (int)TerrainRegion.Grassland)
                    return true;
            }
            return false;
        }
    }
}
