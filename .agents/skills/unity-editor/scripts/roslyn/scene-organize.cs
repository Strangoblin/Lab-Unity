using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class Script
{
    // 匹配规则：名称中包含这些关键词的物体会被归类
    static string[] TestPatterns = { "Test", "Temp", "Debug", "Debug_", "_Test", "_Temp", "_Debug" };

    public static object Main()
    {
        var sb = new System.Text.StringBuilder();
        var testObjects = new System.Collections.Generic.List<GameObject>();

        // 扫描根物体及子物体
        var allRoots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (var root in allRoots)
        {
            CollectTestObjects(root, testObjects);
        }

        if (testObjects.Count == 0)
        {
            return "未发现测试物体，场景无需整理。";
        }

        // 创建容器
        var container = GameObject.Find("__TestObjects__");
        if (container == null)
        {
            container = new GameObject("__TestObjects__");
            Undo.RegisterCreatedObjectUndo(container, "Create Test Container");
        }

        // 按功能分组
        var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<GameObject>>();
        foreach (var go in testObjects)
        {
            var group = CategorizeObject(go.name);
            if (!groups.ContainsKey(group))
                groups[group] = new System.Collections.Generic.List<GameObject>();
            groups[group].Add(go);
        }

        int movedCount = 0;
        foreach (var kvp in groups)
        {
            var groupGo = new GameObject($"__{kvp.Key}__");
            groupGo.transform.SetParent(container.transform);
            Undo.RegisterCreatedObjectUndo(groupGo, "Create Group");

            foreach (var go in kvp.Value)
            {
                Undo.SetTransformParent(go.transform, groupGo.transform, "Move Test Object");
                movedCount++;
            }
            sb.AppendLine($"  [{kvp.Key}] {kvp.Value.Count} 个物体");
        }

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        sb.Insert(0, $"场景整理完成：归类 {movedCount} 个测试物体到 __TestObjects__ 下\n分组详情:\n");
        return sb.ToString();
    }

    static void CollectTestObjects(GameObject go, System.Collections.Generic.List<GameObject> list)
    {
        foreach (Transform child in go.transform)
        {
            CollectTestObjects(child.gameObject, list);
        }

        foreach (var pattern in TestPatterns)
        {
            if (go.name.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                list.Add(go);
                break;
            }
        }
    }

    static string CategorizeObject(string name)
    {
        var lower = name.ToLower();
        if (lower.Contains("rain")) return "RainTest";
        if (lower.Contains("picker")) return "PickerTest";
        if (lower.Contains("boid")) return "BoidsTest";
        if (lower.Contains("noise")) return "NoiseTest";
        if (lower.Contains("ik") || lower.Contains("ragdoll")) return "IKTest";
        if (lower.Contains("shader") || lower.Contains("card")) return "ShaderTest";
        return "OtherTests";
    }
}
