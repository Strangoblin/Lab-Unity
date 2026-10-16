// ════════════════════════════════════════════════════════════
//  CountrysideTerrainTypeInspector — 在方案资产中组织郊野参数与实时 SDF 调参
// ════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;

namespace Mine.Terrain.Editor
{
    /// <summary>
    /// 郊野专用参数归属方案资产；四个 SDF 滑杆在 Play Mode 重生成引用该资产的地图。
    /// </summary>
    [CustomEditor(typeof(CountrysideTerrainType))]
    public sealed class CountrysideTerrainTypeInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty settings = serializedObject.FindProperty("_settings");

            DrawSection("Feature Seeds");
            DrawField(settings, "_featureRules", true);

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

            DrawSection("Dense Vegetation");
            SerializedProperty plants = serializedObject.FindProperty("_plantSettings");
            DrawField(plants, "_maxPlants");
            DrawField(plants, "_rules", true);

            serializedObject.ApplyModifiedProperties();
            if (Mathf.Approximately(settings.FindPropertyRelative("_broadWarpDistance").floatValue, 0f) &&
                Mathf.Approximately(settings.FindPropertyRelative("_detailWarpDistance").floatValue, 0f))
                EditorGUILayout.HelpBox("Both final data warp distances are zero. " +
                    "Regions and roads will keep their unperturbed SDF shapes.", MessageType.Info);

            if ((regionChanged || roadChanged) && Application.isPlaying)
                RegenerateLinkedMaps();
        }

        // ════════════════════════════════════════════════════════════
        //  实时参数同步 — 独立资产面板与嵌入式面板使用同一重生成入口
        // ════════════════════════════════════════════════════════════

        private void RegenerateLinkedMaps()
        {
            TerrainManager[] managers = UnityEngine.Object.FindObjectsByType<TerrainManager>(
                FindObjectsSortMode.None);
            for (int i = 0; i < managers.Length; i++)
                if (managers[i].TerrainType == target)
                    managers[i].Generate();
        }

        private static void DrawSection(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void DrawField(SerializedProperty parent, string name,
            bool includeChildren = false)
        {
            EditorGUILayout.PropertyField(parent.FindPropertyRelative(name), includeChildren);
        }
    }
}
