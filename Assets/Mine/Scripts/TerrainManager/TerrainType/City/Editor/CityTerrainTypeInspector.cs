// ════════════════════════════════════════════════════════════
//  CityTerrainTypeInspector — 城市路网、园区配额与子类型内容参数面板
// ════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System;

namespace Mine.Terrain.Editor
{
    /// <summary>
    /// 城市参数只归属方案资产；面板负责整理输入，不保存地图运行状态。
    /// </summary>
    [CustomEditor(typeof(CityTerrainType))]
    public sealed class CityTerrainTypeInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty settings = serializedObject.FindProperty("_settings");
            DrawGrid(settings.FindPropertyRelative("_grid"));
            DrawSection("Artistic · Zone Mix");
            DrawZones(settings.FindPropertyRelative("_zones"));
            EditorGUILayout.HelpBox("Weights are relative block quotas, not percentages. " +
                "Zero disables a zone. Enabled subtypes are represented when enough blocks fit; " +
                "small maps may reduce the requested grid counts.", MessageType.Info);

            DrawSection("Artistic · Facilities & Vegetation");
            DrawField(settings, "_buildingSetback", "Building Setback",
                "World-space distance from the block edge; also reserves a street-facing entrance.");
            DrawSubtypes(settings.FindPropertyRelative("_subtypes"));
            EditorGUILayout.HelpBox("Facilities and trees use complete placeholder geometry. " +
                "Counts are per-block upper targets; items that do not fit are skipped. " +
                "Grass density is plants per square world unit, only for Park and Undeveloped. " +
                "Set a subtype weight to zero to exclude it.", MessageType.Info);

            serializedObject.ApplyModifiedProperties();
            DrawValidation();
            DrawRegenerateButton();
        }

        // ════════════════════════════════════════════════════════════
        //  路网与配方 — 数值按语义分组，数组保留增删和子类型选择能力
        // ════════════════════════════════════════════════════════════

        private static void DrawGrid(SerializedProperty grid)
        {
            DrawSection("Technical · Orthogonal Grid");
            DrawField(grid, "_columns", "Block Columns", "Requested X-axis block count.");
            DrawField(grid, "_rows", "Block Rows", "Requested Z-axis block count.");
            DrawField(grid, "_mainRoadEvery", "Main Road Interval",
                "Every N road bands is a main road. Zero disables main roads.");
            DrawField(grid, "_mainRoadWidth", "Main Road Width", "Carriageway width in world units.");
            DrawField(grid, "_streetWidth", "Street Width", "Carriageway width in world units.");
            DrawField(grid, "_sidewalkWidth", "Sidewalk Width (Each Side)",
                "World-space width on each side of the carriageway. Zero disables sidewalks.");
            DrawField(grid, "_minBlockSpan", "Minimum Block Span",
                "Minimum clear block width and height, excluding roads and sidewalks.");
        }

        private static void DrawZones(SerializedProperty zones)
        {
            if (!DrawArrayHeader(zones, "Zone Rules"))
                return;
            for (int i = 0; i < zones.arraySize; i++)
            {
                SerializedProperty rule = zones.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawField(rule, "_zone", "Zone");
                DrawField(rule, "_weight", "Quota Weight");
                EditorGUILayout.EndVertical();
            }
        }

        private static void DrawSubtypes(SerializedProperty subtypes)
        {
            if (!DrawArrayHeader(subtypes, "Subtype Rules"))
                return;
            for (int i = 0; i < subtypes.arraySize; i++)
            {
                SerializedProperty rule = subtypes.GetArrayElementAtIndex(i);
                SerializedProperty subtype = rule.FindPropertyRelative("_subtype");
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawField(rule, "_subtype", "Subtype");
                DrawField(rule, "_weight", "Weight Within Zone");
                DrawField(rule, "_maxFacilitiesPerBlock", "Facilities / Block");
                DrawField(rule, "_treesPerBlock", "Trees / Block");
                bool green = subtype.intValue == (int)CitySubtype.Park ||
                    subtype.intValue == (int)CitySubtype.Undeveloped;
                using (new EditorGUI.DisabledScope(!green))
                    DrawField(rule, "_grassDensity", "Grass / Square Unit");
                EditorGUILayout.EndVertical();
            }
        }

        private static bool DrawArrayHeader(SerializedProperty array, string title)
        {
            array.isExpanded = EditorGUILayout.Foldout(array.isExpanded,
                $"{title} ({array.arraySize})", true);
            if (!array.isExpanded)
                return false;
            array.arraySize = Mathf.Max(0, EditorGUILayout.DelayedIntField("Rule Count", array.arraySize));
            return true;
        }

        // ════════════════════════════════════════════════════════════
        //  参数反馈 — 展示配方错误，仅用显式按钮重生成引用该资产的地图
        // ════════════════════════════════════════════════════════════

        private void DrawValidation()
        {
            try
            {
                ((CityTerrainType)target).Validate();
            }
            catch (ArgumentException exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
            }
            catch (InvalidOperationException exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
            }
        }

        private void DrawRegenerateButton()
        {
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (!GUILayout.Button("Regenerate Linked Maps (Play Mode)"))
                    return;
                TerrainManager[] managers = UnityEngine.Object.FindObjectsByType<TerrainManager>(
                    FindObjectsSortMode.None);
                foreach (TerrainManager manager in managers)
                {
                    if (manager.TerrainType != target)
                        continue;
                    try
                    {
                        manager.Generate();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, manager);
                    }
                }
            }
        }

        private static void DrawSection(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void DrawField(SerializedProperty parent, string name, string label,
            string tooltip = null)
        {
            EditorGUILayout.PropertyField(parent.FindPropertyRelative(name),
                new GUIContent(label, tooltip));
        }
    }
}
