using UnityEngine;
using UnityEngine.Rendering;

public class Script
{
    public static object Main()
    {
        var sb = new System.Text.StringBuilder();
        var pipeline = GraphicsSettings.currentRenderPipeline;
        sb.AppendLine($"Render Pipeline: {pipeline?.GetType().Name ?? "Built-in"}");
        sb.AppendLine($"Quality Level: {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
        return sb.ToString();
    }
}
