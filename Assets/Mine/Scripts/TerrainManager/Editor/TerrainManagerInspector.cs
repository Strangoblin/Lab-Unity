// ════════════════════════════════════════════════════════════
//  TerrainManagerInspector — 展示通用地图、方案资产与渲染设置
// ════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;

namespace Mine.Terrain.Editor
{
    /// <summary>
    /// 场景组件只显示共享地图和渲染参数；专用配置交由所选方案的资产编辑器。
    /// </summary>
    [CustomEditor(typeof(TerrainManager))]
    public sealed class TerrainManagerInspector : UnityEditor.Editor
    {
        private UnityEditor.Editor _terrainTypeEditor;
        private bool _showTerrainTypeSettings = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty settings = serializedObject.FindProperty("_settings");

            DrawSection("General Map");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_generateOnStart"));
            DrawField(settings, "_width");
            DrawField(settings, "_height");
            DrawField(settings, "_cellSize");
            DrawField(settings, "_chunkCells");
            DrawField(settings, "_origin");
            DrawField(settings, "_seed");

            DrawSection("Terrain Type");
            SerializedProperty terrainType = serializedObject.FindProperty("_terrainType");
            EditorGUILayout.PropertyField(terrainType);

            DrawSection("Renderer");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_renderSettings"), true);

            serializedObject.ApplyModifiedProperties();
            DrawTerrainTypeInspector(terrainType.objectReferenceValue);

            TerrainManager manager = (TerrainManager)target;
            using (new EditorGUI.DisabledScope(!Application.isPlaying ||
                terrainType.objectReferenceValue == null))
            {
                if (GUILayout.Button("Regenerate Terrain"))
                    manager.Generate();
            }
        }

        private void OnDisable()
        {
            if (_terrainTypeEditor != null)
                DestroyImmediate(_terrainTypeEditor);
        }

        private void DrawTerrainTypeInspector(UnityEngine.Object terrainType)
        {
            if (terrainType == null)
            {
                if (_terrainTypeEditor != null)
                    DestroyImmediate(_terrainTypeEditor);
                _terrainTypeEditor = null;
                EditorGUILayout.HelpBox("Assign a Terrain Type asset before generating the map.",
                    MessageType.Info);
                return;
            }

            _showTerrainTypeSettings = EditorGUILayout.Foldout(_showTerrainTypeSettings,
                "Terrain Type Settings (shared asset)", true);
            if (!_showTerrainTypeSettings)
                return;
            UnityEditor.Editor.CreateCachedEditor(terrainType, null, ref _terrainTypeEditor);
            using (new EditorGUI.IndentLevelScope())
                _terrainTypeEditor.OnInspectorGUI();
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
