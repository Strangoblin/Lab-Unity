// ════════════════════════════════════════════════════════════
//  TerrainManagerInspector — 数据参数、渲染入口与生成状态
// ════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System;

namespace Mine.Terrain.Editor
{
    /// <summary>
    /// 场景组件只需暴露数据配置、渲染入口和当前快照状态。
    /// </summary>
    [CustomEditor(typeof(TerrainManager))]
    public sealed class TerrainManagerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawSection("Data");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_generateOnStart"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_settings"), true);

            DrawSection("Render");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_surfaceMaterial"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_castShadows"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_receiveShadows"));

            serializedObject.ApplyModifiedProperties();

            TerrainManager manager = (TerrainManager)target;
            TerrainSettings settings = manager.Settings;
            string validationError = null;
            bool valid = settings != null && settings.TryValidate(out validationError);
            if (!valid)
                EditorGUILayout.HelpBox(validationError ?? "Terrain settings are missing.",
                    MessageType.Error);

            DrawActions(manager, valid);
            DrawStatus(manager);
        }

        // ════════════════════════════════════════════════════════════
        //  操作 — 生成与释放
        // ════════════════════════════════════════════════════════════

        private static void DrawActions(TerrainManager manager, bool valid)
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!valid))
                {
                    if (GUILayout.Button("Generate"))
                        Generate(manager);
                }

                using (new EditorGUI.DisabledScope(!manager.HasData))
                {
                    if (GUILayout.Button("Release"))
                        manager.Release();
                }
            }
        }

        private static void Generate(TerrainManager manager)
        {
            try
            {
                manager.Generate();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, manager);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  状态 — 当前快照尺寸与高度范围
        // ════════════════════════════════════════════════════════════

        private static void DrawStatus(TerrainManager manager)
        {
            if (!manager.HasData)
            {
                EditorGUILayout.HelpBox("No terrain data has been generated.",
                    MessageType.Info);
                return;
            }

            TerrainGrid grid = manager.CurrentGrid;
            DrawSection("Status");
            EditorGUILayout.LabelField("Version", manager.DataVersion.ToString());
            EditorGUILayout.LabelField("Cells", $"{grid.WidthCells} x {grid.HeightCells}");
            EditorGUILayout.LabelField("Vertices", grid.VertexCount.ToString());
            EditorGUILayout.LabelField("World Size", $"{grid.WorldWidth:F2} x {grid.WorldDepth:F2}");
            EditorGUILayout.LabelField("Height Range", $"{grid.MinHeight:F2} .. {grid.MaxHeight:F2}");
        }

        private static void DrawSection(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
