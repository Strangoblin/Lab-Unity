using UnityEngine;
using UnityEngine.SceneManagement;

public class Script
{
    public static object Main()
    {
        var sb = new System.Text.StringBuilder();
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        sb.AppendLine($"根物体数量: {roots.Length}");

        foreach (var root in roots)
        {
            PrintHierarchy(root.transform, "", sb);
        }
        return sb.ToString();
    }

    static void PrintHierarchy(Transform t, string indent, System.Text.StringBuilder sb)
    {
        sb.AppendLine($"{indent}{t.name} [{t.GetComponents<Component>().Length} components]");
        foreach (Transform child in t)
        {
            PrintHierarchy(child, indent + "  ", sb);
        }
    }
}
