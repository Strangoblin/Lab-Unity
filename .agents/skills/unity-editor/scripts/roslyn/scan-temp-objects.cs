using UnityEngine;
using UnityEditor;
using System.IO;

public class Script
{
    public static object Main()
    {
        var sb = new System.Text.StringBuilder();

        // 扫描临时 GameObject
        var allObjects = Object.FindObjectsOfType<GameObject>(true);
        var tempObjs = new System.Collections.Generic.List<string>();
        foreach (var go in allObjects)
        {
            if (go.name.StartsWith("_Temp") || go.name.StartsWith("_Debug") ||
                go.name.StartsWith("Temp_") || go.name.StartsWith("Test_"))
            {
                tempObjs.Add($"{go.name} (root: {go.transform.root.name})");
            }
        }

        sb.AppendLine($"=== 临时物体: {tempObjs.Count} 个 ===");
        foreach (var name in tempObjs)
            sb.AppendLine($"  - {name}");

        return sb.ToString();
    }
}
