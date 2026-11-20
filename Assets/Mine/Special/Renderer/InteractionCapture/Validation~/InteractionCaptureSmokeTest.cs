// ════════════════════════════════════════════════════════════════
//  Interaction Capture Smoke Test — the feature must own the camera target.
// ════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text;
using Mine.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders a throwaway camera through IA_Renderer with real InteractionManager materials and asserts the
/// capture reaches the camera target. The control render disables the feature: the target must then stay
/// empty, which proves URP's own (masked-out) passes contribute nothing and the feature is the only source.
/// Args: none. Never saves assets or the open scene.
/// </summary>
public class Script
{
    private const string RendererPath = "Assets/Settings/IA_Renderer.asset";
    private const int RendererIndex = 1;
    private const int DepthLayer = 6;
    private const int ColorLayer = 7;
    private const int Width = 64;
    private const int Height = 64;
    private static readonly Color Sentinel = new Color(-7f, -7f, -7f, -7f);

    public static object Main(string[] args)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var owned = new List<UnityEngine.Object>();
        var createdMaterials = new List<Material>();
        RenderTexture target = null;
        RenderTexture previousActive = RenderTexture.active;
        var feature = LoadFeature();
        bool originalActive = feature == null || feature.isActive;

        Application.LogCallback capture = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(message);
            else if (type == LogType.Warning)
                warnings.Add(message);
        };
        Application.logMessageReceived += capture;

        try
        {
            Require(feature != null, $"{RendererPath} has no InteractionCaptureFeature.");

            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                name = "Interaction capture smoke target",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
            };
            Require(target.Create(), "Could not create the smoke-test target.");

            Camera camera = CreateCamera(owned);
            CreateCaptureGeometry(createdMaterials, owned);

            feature.SetActive(true);
            float withFeature = RenderCamera(camera, target);
            feature.SetActive(false);
            float withoutFeature = RenderCamera(camera, target);
            feature.SetActive(true);

            var sb = new StringBuilder();
            sb.Append("{\"status\":");
            bool passed = withFeature > 0.5f && withoutFeature < 0.01f && errors.Count == 0
                          && warnings.TrueForAll(w => !w.Contains("depth buffer"));
            sb.Append(passed ? "\"ok\"" : "\"failed\"");
            sb.Append(",\"withFeatureMaxR\":").Append(withFeature.ToString("F4"));
            sb.Append(",\"withoutFeatureMaxR\":").Append(withoutFeature.ToString("F4"));
            sb.Append(",\"rendererIndex\":").Append(RendererIndex);
            sb.Append(",\"errors\":[");
            for (int i = 0; i < errors.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(errors[i].Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ")).Append('"');
            }
            sb.Append("],\"warnings\":[");
            for (int i = 0; i < warnings.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(warnings[i].Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ")).Append('"');
            }
            sb.Append("]}");
            return sb.ToString();
        }
        finally
        {
            if (feature != null)
                feature.SetActive(originalActive);
            Application.logMessageReceived -= capture;

            foreach (Material material in createdMaterials)
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
            foreach (UnityEngine.Object item in owned)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);

            RenderTexture.active = previousActive;
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }

    private static InteractionCaptureFeature LoadFeature()
    {
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (data == null || data.rendererFeatures == null)
            return null;
        foreach (ScriptableRendererFeature feature in data.rendererFeatures)
            if (feature is InteractionCaptureFeature capture) return capture;
        return null;
    }

    private static Camera CreateCamera(List<UnityEngine.Object> owned)
    {
        var go = new GameObject("Interaction capture smoke camera") { hideFlags = HideFlags.HideAndDontSave };
        owned.Add(go);

        var camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 1f;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 10f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = (1 << DepthLayer) | (1 << ColorLayer);
        camera.allowMSAA = false;
        camera.allowHDR = false;
        camera.targetTexture = null;
        go.transform.SetPositionAndRotation(new Vector3(0f, 0f, -5f), Quaternion.identity);

        UniversalAdditionalCameraData additional = camera.GetUniversalAdditionalCameraData();
        additional.SetRenderer(RendererIndex);
        additional.renderPostProcessing = false;
        return camera;
    }

    private static void CreateCaptureGeometry(List<Material> createdMaterials, List<UnityEngine.Object> owned)
    {
        // Layer 6: the scene surface whose raw depth the capture publishes.
        var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
        surface.name = "Smoke depth surface";
        surface.hideFlags = HideFlags.HideAndDontSave;
        surface.layer = DepthLayer;
        surface.transform.position = new Vector3(0f, 0f, 0f);
        surface.transform.localScale = new Vector3(2f, 2f, 1f);
        owned.Add(surface);
        UnityEngine.Object.DestroyImmediate(surface.GetComponent<Collider>());

        Material surfaceMaterial = NewMaterial("InteractionManager/InteractionDebug", createdMaterials);
        surface.GetComponent<MeshRenderer>().sharedMaterial = surfaceMaterial;

        // Layer 7: the interactor, placed behind the surface so its depth difference stays positive.
        var interactor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        interactor.name = "Smoke interactor";
        interactor.hideFlags = HideFlags.HideAndDontSave;
        interactor.layer = ColorLayer;
        interactor.transform.position = new Vector3(0f, 0f, 1f);
        interactor.transform.localScale = new Vector3(0.6f, 0.6f, 0.2f);
        owned.Add(interactor);
        UnityEngine.Object.DestroyImmediate(interactor.GetComponent<Collider>());

        Material interactorMaterial = NewMaterial("InteractionManager/InteractorObject", createdMaterials);
        interactorMaterial.SetFloat("_Intensity", 1f);
        interactor.GetComponent<MeshRenderer>().sharedMaterial = interactorMaterial;
    }

    private static Material NewMaterial(string shaderName, List<Material> createdMaterials)
    {
        Shader shader = Shader.Find(shaderName);
        Require(shader != null, $"Shader '{shaderName}' not found.");
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        createdMaterials.Add(material);
        return material;
    }

    private static float RenderCamera(Camera camera, RenderTexture target)
    {
        RenderTexture previous = RenderTexture.active;
        Texture2D readback = null;
        try
        {
            RenderTexture.active = target;
            GL.Clear(true, true, Sentinel);

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request),
                "URP SingleCameraRequest is unsupported in this pipeline.");

            RenderPipeline.SubmitRenderRequest(camera, request);

            RenderTexture.active = target;
            readback = new Texture2D(Width, Height, TextureFormat.RFloat, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            readback.Apply(false, false);

            Color[] pixels = readback.GetPixels();
            Require(pixels.Length == Width * Height, "Float readback dimensions are incorrect.");
            Require(Mathf.Abs(pixels[0].r - Sentinel.r) > 0.01f, "Render request left the target at the sentinel.");

            float max = 0f;
            for (int i = 0; i < pixels.Length; i++)
                max = Mathf.Max(max, pixels[i].r);
            return max;
        }
        finally
        {
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            RenderTexture.active = previous;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
