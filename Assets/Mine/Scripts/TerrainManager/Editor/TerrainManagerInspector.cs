// ════════════════════════════════════════════════════════════
//  TerrainManagerInspector — 按生成顺序组织数据、内容与表现参数
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
            SerializedProperty settings = serializedObject.FindProperty("_settings");

            DrawSection("Scene");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_generateOnStart"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_surfaceShader"));

            DrawSection("Map");
            DrawField(settings, "_width");
            DrawField(settings, "_height");
            DrawField(settings, "_cellSize");
            DrawField(settings, "_chunkCells");
            DrawField(settings, "_origin");
            DrawField(settings, "_seed");

            DrawSection("Feature Seeds");
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("_featureRules"), true);

            DrawSection("Voronoi Regions");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("_voronoiRegionWidth"),
                new GUIContent("Region Width"));
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("_voronoiRegionSoftness"),
                new GUIContent("Region Softness"));
            bool regionChanged = EditorGUI.EndChangeCheck();
            DrawField(settings, "_depressionDepth");

            DrawSection("Voronoi Roads");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("_voronoiRoadWidth"),
                new GUIContent("Route Width"));
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("_voronoiCornerSoftness"),
                new GUIContent("Corner Softness"));
            bool roadChanged = EditorGUI.EndChangeCheck();

            DrawSection("Final Data Warp (300-unit reference)");
            DrawField(settings, "_broadWarpCycles");
            DrawField(settings, "_broadWarpDistance");
            DrawField(settings, "_detailWarpCycles");
            DrawField(settings, "_detailWarpDistance");

            DrawSection("Sparse Content");
            DrawField(settings, "_houseCount");
            DrawField(settings, "_treeCount");

            SerializedProperty plants = serializedObject.FindProperty("_plantSettings");
            DrawSection("Dense Vegetation");
            DrawField(plants, "_maxPlants");
            EditorGUILayout.PropertyField(plants.FindPropertyRelative("_rules"), true);

            serializedObject.ApplyModifiedProperties();

            float broadWarp = settings.FindPropertyRelative("_broadWarpDistance").floatValue;
            float detailWarp = settings.FindPropertyRelative("_detailWarpDistance").floatValue;
            if (Mathf.Approximately(broadWarp, 0f) && Mathf.Approximately(detailWarp, 0f))
                EditorGUILayout.HelpBox("Both final data warp distances are zero. " +
                    "Regions and roads will keep their unperturbed SDF shapes.", MessageType.Info);

            TerrainManager manager = (TerrainManager)target;
            if ((regionChanged || roadChanged) && Application.isPlaying)
                manager.Generate();
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Regenerate Terrain"))
                    manager.Generate();
            }
        }

        private static void DrawSection(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void DrawField(SerializedProperty parent, string name)
        {
            EditorGUILayout.PropertyField(parent.FindPropertyRelative(name));
        }
    }
}
