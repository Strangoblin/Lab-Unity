using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

public class Script
{
    public static object Main()
    {
        var camera = Camera.main;
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        var feature = renderer.rendererFeatures.Find(f => f.name == "Snowy Experiment") as FullScreenPassRendererFeature;
        var originalMaterial = feature.passMaterial;
        bool originalActive = feature.isActive;
        var output = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
        output.Create();
        var previous = RenderTexture.active;
        var temporary = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Mine/Shaders/PostProcess/Snowy/Snowy_Grid.mat"));
        temporary.SetFloat("_Animate", 0f);
        temporary.SetFloat("_PreviewTime", 0.75f);
        var messages = new System.Collections.Generic.List<string>();
        Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception) messages.Add(message); };
        Application.logMessageReceived += capture;
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = output };
            feature.SetActive(false);
            RenderPipeline.SubmitRenderRequest(camera, request);
            var baseline = Read(output);
            feature.passMaterial = temporary;
            feature.SetActive(true);
            RenderPipeline.SubmitRenderRequest(camera, request);
            var snow = Read(output);
            int changed = 0;
            for (int i = 0; i < snow.Length; i++)
                if (Mathf.Abs(snow[i].r - baseline[i].r) + Mathf.Abs(snow[i].g - baseline[i].g) + Mathf.Abs(snow[i].b - baseline[i].b) > 0.03f) changed++;
            if (changed < 100) throw new System.Exception("Snowy camera output missing, changed=" + changed);
            var image = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            image.SetPixels(snow); image.Apply();
            System.IO.File.WriteAllBytes(".codex/tmp/snowy-camera-preview.png", image.EncodeToPNG());
            Object.DestroyImmediate(image);
            if (messages.Count > 0) throw new System.Exception(string.Join("\n", messages));
            return "PASS main camera URP render request: " + changed + " changed pixels; zero new errors. Saved camera preview; restored original material and previous active state.";
        }
        finally
        {
            Application.logMessageReceived -= capture;
            feature.passMaterial = originalMaterial;
            feature.SetActive(originalActive);
            RenderTexture.active = previous;
            output.Release();
            Object.DestroyImmediate(output); Object.DestroyImmediate(temporary);
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }

    private static Color[] Read(RenderTexture output)
    {
        RenderTexture.active = output;
        var image = new Texture2D(output.width, output.height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, output.width, output.height), 0, 0);
        image.Apply();
        var pixels = image.GetPixels();
        Object.DestroyImmediate(image);
        return pixels;
    }
}
