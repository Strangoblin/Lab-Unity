// ════════════════════════════════════════════════════════════
//  TerrainManagerInspector — 两组 SDF 对照按钮与运行时重生成
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
            SerializedProperty featureMode = settings.FindPropertyRelative("_featureSdfMode");
            SerializedProperty roadMode = settings.FindPropertyRelative("_roadSdfMode");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("SDF Shape Comparison", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Feature region");
            int selectedFeature = GUILayout.Toolbar(featureMode.enumValueIndex,
                new[] { "Circle SDF", "Voronoi Polygon SDF" });
            EditorGUI.BeginChangeCheck();
            if (selectedFeature == (int)TerrainFeatureSdfMode.VoronoiPolygon)
            {
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("_voronoiRegionWidth"),
                    new GUIContent("Voronoi Region Width"));
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("_voronoiRegionSoftness"),
                    new GUIContent("Voronoi Region Softness"));
            }
            EditorGUILayout.LabelField("Road distribution");
            int selectedRoad = GUILayout.Toolbar(roadMode.enumValueIndex,
                new[] { "Site Link SDF", "Voronoi Gap SDF" });
            if (selectedRoad == (int)TerrainRoadSdfMode.VoronoiCell)
            {
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("_voronoiRoadWidth"),
                    new GUIContent("Voronoi Route Width"));
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("_voronoiCornerSoftness"),
                    new GUIContent("Voronoi Corner Softness"));
                EditorGUILayout.HelpBox("Voronoi road width uses the nearest/second-nearest " +
                    "seed distance gap. Corner softness blends in the third-nearest " +
                    "seed near junctions; zero keeps the unsmoothed field.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("_roadWidth"),
                    new GUIContent("Site Link Road Width"));
            }
            bool widthChanged = EditorGUI.EndChangeCheck();

            bool modeChanged = selectedFeature != featureMode.enumValueIndex ||
                selectedRoad != roadMode.enumValueIndex;
            featureMode.enumValueIndex = selectedFeature;
            roadMode.enumValueIndex = selectedRoad;
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
            if ((modeChanged || widthChanged) && Application.isPlaying)
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
