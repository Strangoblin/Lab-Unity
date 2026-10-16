using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

public class Script
{
    public static object Main()
    {
        const int resolution = 512;
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Mine/Shaders/PostProcess/Snowy/Snowy.shader");
        var material = new Material(shader);
        var source = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var target = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        source.Create(); target.Create();
        RTHandle sourceHandle = RTHandles.Alloc(source);
        RTHandle targetHandle = RTHandles.Alloc(target);
        var previous = RenderTexture.active;
        var report = new System.Text.StringBuilder();
        try
        {
            RenderTexture.active = source;
            GL.Clear(false, true, new Color(0.2f, 0.3f, 0.4f, 0.7f));
            material.SetTexture("_ParticleTex", Texture2D.whiteTexture);
            material.SetFloat("_SwayAmplitude", 0f);
            material.SetFloat("_RotationAmplitude", 0f);
            material.SetFloat("_ScaleAmplitude", 0f);
            material.SetFloat("_DebugView", 1f);
            CheckBounds(report, "single 0.1 UV", Draw(material, sourceHandle, targetHandle, target), 0.5f, 0.5f, 0.1f, 0.1f);
            material.SetVector("_Scale", new Vector4(2f, 1f, 0f, 0f));
            material.SetFloat("_Rotation", 90f);
            CheckBounds(report, "rotate 90 scale 2x1", Draw(material, sourceHandle, targetHandle, target), 0.5f, 0.5f, 0.1f, 0.2f);
            material.SetVector("_Offset", new Vector4(0.2f, -0.1f, 0f, 0f));
            CheckBounds(report, "translate +0.2,-0.1", Draw(material, sourceHandle, targetHandle, target), 0.7f, 0.4f, 0.1f, 0.2f);
            material.SetVector("_Scale", Vector4.one);
            material.SetFloat("_Rotation", 0f);
            material.SetVector("_Offset", Vector4.zero);
            material.SetVector("_Velocity", new Vector4(0f, -0.1f, 0f, 0f));
            material.SetFloat("_RotationSpeed", 0f);
            material.SetFloat("_PreviewTime", 1f);
            CheckBounds(report, "time 1 fall -0.1 UV", Draw(material, sourceHandle, targetHandle, target), 0.5f, 0.4f, 0.1f, 0.1f);
            material.SetFloat("_PreviewTime", 0.25f);
            material.SetFloat("_SwayAmplitude", 0.02f);
            material.SetFloat("_SwayFrequency", 1f);
            CheckBounds(report, "directional drift plus sway", Draw(material, sourceHandle, targetHandle, target), 0.52f, 0.475f, 0.1f, 0.1f);
            material.SetVector("_Velocity", Vector4.zero);
            CheckBounds(report, "zero velocity finite sway", Draw(material, sourceHandle, targetHandle, target), 0.52f, 0.5f, 0.1f, 0.1f);
            material.SetFloat("_SwayAmplitude", 0f);
            material.SetFloat("_ScaleAmplitude", 0.5f);
            material.SetFloat("_ScaleFrequency", 1f);
            CheckBounds(report, "scale pulse quarter cycle", Draw(material, sourceHandle, targetHandle, target), 0.5f, 0.5f, 0.15f, 0.15f);
            material.SetFloat("_PreviewTime", 0.75f);
            CheckBounds(report, "scale pulse three quarter cycle", Draw(material, sourceHandle, targetHandle, target), 0.5f, 0.5f, 0.05f, 0.05f);
            material.SetFloat("_ScaleAmplitude", 0f);
            material.SetFloat("_PreviewTime", 0.25f);
            material.SetVector("_Scale", new Vector4(2f, 1f, 0f, 0f));
            material.SetFloat("_RotationAmplitude", 90f);
            material.SetFloat("_RotationFrequency", 1f);
            CheckBounds(report, "rotation sway quarter cycle", Draw(material, sourceHandle, targetHandle, target), 0.5f, 0.5f, 0.1f, 0.2f);
            material.SetFloat("_RotationAmplitude", 0f);
            material.SetVector("_Scale", Vector4.one);
            material.SetFloat("_FlipEnabled", 1f);
            material.SetFloat("_FlipSpeed", 0f);
            material.SetFloat("_FlipAngle", 60f);
            CheckBounds(report, "Y flip 60 degrees", Draw(material, sourceHandle, targetHandle, target), 0.5f, 0.5f, 0.05f, 0.1f);
            material.SetFloat("_Rotation", 90f);
            CheckBounds(report, "Y flip plus planar rotation", Draw(material, sourceHandle, targetHandle, target), 0.5f, 0.5f, 0.1f, 0.05f);
            material.SetFloat("_Rotation", 0f);
            material.SetFloat("_FlipAxis", 1f);
            CheckBounds(report, "X flip 60 degrees", Draw(material, sourceHandle, targetHandle, target), 0.5f, 0.5f, 0.1f, 0.05f);
            material.SetFloat("_FlipAxis", 0f);
            material.SetFloat("_FlipAngle", 90f);
            var edge = Draw(material, sourceHandle, targetHandle, target);
            foreach (var pixel in edge)
                if (pixel.r > 0.01f) throw new System.Exception("Edge-on flip must disappear without a stripe");
            report.AppendLine("PASS edge-on 90 degrees: no stripe or invalid visible values");
            material.SetFloat("_FlipAngle", 0f);
            material.SetFloat("_FlipSpeed", 180f);
            material.SetFloat("_PreviewTime", 1f);
            material.SetFloat("_DebugView", 2f);
            var mirrored = Draw(material, sourceHandle, targetHandle, target);
            float leftU = mirrored[256 * 512 + 243].r;
            float rightU = mirrored[256 * 512 + 268].r;
            if (leftU < 0.7f || rightU > 0.3f) throw new System.Exception("Backface UV did not mirror: " + leftU + ", " + rightU);
            report.AppendLine("PASS animated 180 degree backface UV mirrors: left=" + leftU + " right=" + rightU);
            material.SetFloat("_DebugView", 1f);
            material.SetFloat("_FlipEnabled", 0f);
            material.SetFloat("_PreviewTime", 0f);
            material.SetFloat("_Layout", 1f);
            material.SetFloat("_ParticleSize", 0.35f);
            var grid = Draw(material, sourceHandle, targetHandle, target);
            int islands = CountIslands(grid);
            if (islands != 100) throw new System.Exception("Expected 100 grid particles, got " + islands);
            report.AppendLine("PASS grid 10x10: 100 separate particles");
            material.SetFloat("_Layout", 0f);
            material.SetFloat("_DebugView", 0f);
            material.SetVector("_Scale", Vector4.zero);
            var invisible = Draw(material, sourceHandle, targetHandle, target);
            Color center = invisible[256 * 512 + 256];
            if (Mathf.Abs(center.r - 0.2f) > 0.01f || Mathf.Abs(center.a - 0.7f) > 0.01f)
                throw new System.Exception("Zero scale did not preserve scene: " + center);
            report.AppendLine("PASS zero scale: scene RGBA preserved");
            Object.DestroyImmediate(material);
            material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Mine/Shaders/PostProcess/Snowy/Snowy_Grid.mat"));
            material.SetFloat("_Animate", 0f);
            material.SetFloat("_PreviewTime", 1.75f);
            var preview = Draw(material, sourceHandle, targetHandle, target);
            var image = new Texture2D(512, 512, TextureFormat.RGBA32, false, true);
            image.SetPixels(preview); image.Apply();
            System.IO.File.WriteAllBytes(".codex/tmp/snowy-grid-preview.png", image.EncodeToPNG());
            Object.DestroyImmediate(image);
            report.AppendLine("Saved .codex/tmp/snowy-grid-preview.png");
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity.ToString() == "Error") throw new System.Exception(message.message);
            report.AppendLine("PASS shader messages: no errors");
            return report.ToString();
        }
        finally
        {
            RenderTexture.active = previous;
            sourceHandle.Release(); targetHandle.Release();
            source.Release(); target.Release();
            Object.DestroyImmediate(source); Object.DestroyImmediate(target); Object.DestroyImmediate(material);
        }
    }

    private static Color[] Draw(Material material, RTHandle source, RTHandle target, RenderTexture output)
    {
        var command = new CommandBuffer { name = "Snowy GPU Verification" };
        Vector4 oldScaledScreen = Shader.GetGlobalVector("_ScaledScreenParams");
        command.SetGlobalVector("_ScaledScreenParams", new Vector4(512, 512, 1f + 1f / 512f, 1f + 1f / 512f));
        Blitter.BlitCameraTexture(command, source, target, material, 0);
        command.SetGlobalVector("_ScaledScreenParams", oldScaledScreen);
        Graphics.ExecuteCommandBuffer(command);
        command.Release();
        RenderTexture.active = output;
        var readback = new Texture2D(512, 512, TextureFormat.RGBA32, false, true);
        readback.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
        readback.Apply();
        var colors = readback.GetPixels();
        Object.DestroyImmediate(readback);
        return colors;
    }

    private static void CheckBounds(System.Text.StringBuilder report, string label, Color[] pixels, float cx, float cy, float width, float height)
    {
        int minX = 512, minY = 512, maxX = -1, maxY = -1;
        for (int y = 0; y < 512; y++)
        for (int x = 0; x < 512; x++)
        {
            if (pixels[y * 512 + x].r < 0.5f) continue;
            minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
            maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
        }
        float actualX = (minX + maxX + 1f) / 1024f;
        float actualY = (minY + maxY + 1f) / 1024f;
        float actualWidth = (maxX - minX + 1f) / 512f;
        float actualHeight = (maxY - minY + 1f) / 512f;
        string details = $"center=({actualX:F4},{actualY:F4}) size=({actualWidth:F4},{actualHeight:F4})";
        if (Mathf.Abs(actualX - cx) > 0.004f || Mathf.Abs(actualY - cy) > 0.004f || Mathf.Abs(actualWidth - width) > 0.004f || Mathf.Abs(actualHeight - height) > 0.004f)
            throw new System.Exception(label + ": " + details);
        report.AppendLine("PASS " + label + ": " + details);
    }

    private static int CountIslands(Color[] pixels)
    {
        var visited = new bool[pixels.Length];
        var queue = new System.Collections.Generic.Queue<int>();
        int count = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (visited[i] || pixels[i].r < 0.5f) continue;
            count++; visited[i] = true; queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int p = queue.Dequeue();
                int x = p % 512;
                int y = p / 512;
                int[] neighbours = { x > 0 ? p - 1 : -1, x < 511 ? p + 1 : -1, y > 0 ? p - 512 : -1, y < 511 ? p + 512 : -1 };
                foreach (int next in neighbours)
                {
                    if (next < 0 || visited[next] || pixels[next].r < 0.5f) continue;
                    visited[next] = true; queue.Enqueue(next);
                }
            }
        }
        return count;
    }
}
