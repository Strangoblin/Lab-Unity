using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

public class Script
{
    public static object Main()
    {
        const string root = "Assets/Mine/Shaders/PostProcess/Snowy/";
        string texturePath = root + "SnowyParticle.png";
        if (!System.IO.File.Exists(texturePath))
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            var colors = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / 64f - 0.5f, (y + 0.5f) / 64f - 0.5f);
                float stem = Mathf.Max(Mathf.Abs(p.x) - 0.035f, Mathf.Abs(p.y) - 0.43f);
                float armA = Mathf.Max(Mathf.Abs(p.x * 0.8660254f + p.y * 0.5f) - 0.028f, Mathf.Abs(-p.x * 0.5f + p.y * 0.8660254f) - 0.32f);
                float armB = Mathf.Max(Mathf.Abs(p.x * 0.8660254f - p.y * 0.5f) - 0.028f, Mathf.Abs(p.x * 0.5f + p.y * 0.8660254f) - 0.32f);
                float distance = Mathf.Min(stem, Mathf.Min(armA, armB));
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.012f, 0.012f, distance));
                colors[y * 64 + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels(colors);
            texture.Apply();
            System.IO.File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        AssetDatabase.ImportAsset(root + "Snowy.shader", ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(root + "Snowy.shader");
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
            if (message.severity.ToString() == "Error") throw new System.Exception(message.message);
        var sprite = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        Material single = CreateMaterial(root + "Snowy_Single.mat", shader, sprite, false);
        CreateMaterial(root + "Snowy_Grid.mat", shader, sprite, true);
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        var feature = renderer.rendererFeatures.Find(f => f.name == "Snowy Experiment") as FullScreenPassRendererFeature;
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = "Snowy Experiment";
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);
        }
        feature.passMaterial = single;
        feature.fetchColorBuffer = true;
        feature.requirements = ScriptableRenderPassInput.None;
        feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
        feature.passIndex = 0;
        feature.SetActive(true);
        feature.Create();
        EditorUtility.SetDirty(feature);
        renderer.SetDirty();
        EditorUtility.SetDirty(renderer);
        AssetDatabase.SaveAssetIfDirty(renderer);
        Selection.activeObject = single;
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
        return "Created RGBA particle, Single/Grid materials; PC_Renderer Snowy Experiment active with Single material. Scene was not saved.";
    }

    private static Material CreateMaterial(string path, Shader shader, Texture2D texture, bool grid)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(shader);
        material.SetTexture("_ParticleTex", texture);
        material.SetFloat("_Layout", grid ? 1f : 0f);
        material.SetFloat("_ParticleSize", grid ? 0.35f : 0.1f);
        material.SetFloat("_Animate", grid ? 1f : 0f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
