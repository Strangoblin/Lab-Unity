// One-shot, Edit Mode integration checks. Invoked by unityctl Roslyn Script.Main(args).
// Writes only session PNG/JSON; owns a disposable preview scene and renderer tail slot.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Mine.ScreenWarp;

public class Script
{
    static string Output;
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Report report;

    [Serializable] public sealed class Report
    {
        public bool passed;
        public string device;
        public string pipeline;
        public string format = "ARGBFloat linear numeric comparison; PNG RGB converted linear to sRGB";
        public string scope = "Isolated real URP SingleCameraRequest + RenderGraph; no original scene or assets modified. Camera stack, XR, dynamic resolution and other project Features are not covered.";
        public int assertions;
        public int requests;
        public int baseRequests;
        public int overlayRequests;
        public int overlayAssertions;
        public bool originalRendererStateRestored;
        public bool originalSceneStateRestored;
        public bool activeRenderTextureRestored;
        public string error;
        public List<Comparison> comparisons = new List<Comparison>();
        public List<string> files = new List<string>();
        public List<string> errors = new List<string>();
    }
    [Serializable] public sealed class Comparison
    {
        public string name;
        public int width;
        public int height;
        public float meanRgbDifference;
        public float maxRgbDifference;
        public float maxAlphaDifference;
        public int sampledPixelCount;
        public float changedPixelFraction;
    }

    static void Require(bool condition, string message)
    {
        report.assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    sealed class RendererScope : IDisposable
    {
        readonly UniversalRenderPipelineAsset pipeline;
        readonly FieldInfo dataField;
        readonly FieldInfo renderersField;
        readonly ScriptableRendererData[] originalData;
        readonly ScriptableRenderer[] originalRenderers;
        readonly ScriptableRendererFeature[][] originalFeatures;
        readonly bool[][] originalActive;
        readonly ScriptableRendererData[] expandedData;
        readonly ScriptableRenderer[] expandedRenderers;
        readonly UniversalRendererData clone;
        readonly List<ScriptableRendererFeature> owned = new List<ScriptableRendererFeature>();
        public readonly int Index;

        public RendererScope()
        {
            pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Require(pipeline != null, "URP pipeline required");
            dataField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", Fields);
            renderersField = typeof(UniversalRenderPipelineAsset).GetField("m_Renderers", Fields);
            Require(dataField != null && renderersField != null, "URP renderer-array fields available");
            originalData = (ScriptableRendererData[])dataField.GetValue(pipeline);
            originalRenderers = (ScriptableRenderer[])renderersField.GetValue(pipeline);
            Require(originalData != null && originalRenderers != null && originalData.Length > 0
                && originalData.Length == originalRenderers.Length, "URP renderer arrays already initialized");
            Require(originalData[0] is UniversalRendererData, "Source renderer must be UniversalRendererData");
            originalFeatures = originalData.Select(d => d == null ? null : d.rendererFeatures.ToArray()).ToArray();
            originalActive = originalFeatures.Select(f => f == null ? null : f.Select(x => x != null && x.isActive).ToArray()).ToArray();
            Index = originalData.Length;
            clone = UnityEngine.Object.Instantiate((UniversalRendererData)originalData[0]);
            clone.name = "ScreenWarp.Validation.DisposableRenderer";
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.rendererFeatures.Clear();
            expandedData = new ScriptableRendererData[Index + 1];
            expandedRenderers = new ScriptableRenderer[Index + 1];
            Array.Copy(originalData, expandedData, Index);
            Array.Copy(originalRenderers, expandedRenderers, Index);
            expandedData[Index] = clone;
            dataField.SetValue(pipeline, expandedData);
            renderersField.SetValue(pipeline, expandedRenderers);
        }

        public void Use(ScriptableRendererFeature feature)
        {
            clone.rendererFeatures.Clear();
            feature.hideFlags = HideFlags.HideAndDontSave;
            clone.rendererFeatures.Add(feature);
            owned.Add(feature);
            clone.SetDirty();
            Require(pipeline.GetRenderer(Index) != null, "Temporary renderer instantiated");
            for (int i = 0; i < Index; i++)
                Require(ReferenceEquals(expandedRenderers[i], originalRenderers[i]), "Original renderer instance preserved during setup");
        }

        public void Dispose()
        {
            Require(ReferenceEquals(dataField.GetValue(pipeline), expandedData)
                && ReferenceEquals(renderersField.GetValue(pipeline), expandedRenderers),
                "Concurrent renderer-array change; cannot safely restore");
            // Only the appended renderer may be disposed. Never call DestroyRenderers on the RP.
            try { expandedRenderers[Index]?.Dispose(); }
            finally
            {
                dataField.SetValue(pipeline, originalData);
                renderersField.SetValue(pipeline, originalRenderers);
                foreach (ScriptableRendererFeature feature in owned)
                {
                    feature.Dispose();
                    UnityEngine.Object.DestroyImmediate(feature);
                }
                UnityEngine.Object.DestroyImmediate(clone);
            }
            Require(ReferenceEquals(dataField.GetValue(pipeline), originalData)
                && ReferenceEquals(renderersField.GetValue(pipeline), originalRenderers), "Original RP array references restored");
            for (int i = 0; i < Index; i++)
            {
                Require(ReferenceEquals(expandedRenderers[i], originalRenderers[i]), "Original renderer identity unchanged");
                if (originalData[i] == null) continue;
                Require(originalData[i].rendererFeatures.SequenceEqual(originalFeatures[i]), "Original Feature list unchanged");
                Require(originalData[i].rendererFeatures.Select(x => x != null && x.isActive).SequenceEqual(originalActive[i]),
                    "Original Feature active flags unchanged");
            }
            report.originalRendererStateRestored = true;
        }
    }

    sealed class SceneScope : IDisposable
    {
        public readonly Scene Scene;
        public readonly Camera Camera;
        readonly List<Material> materials = new List<Material>();
        public SceneScope(int renderer)
        {
            Scene = EditorSceneManager.NewPreviewScene();
            try
            {
            var go = new GameObject("ScreenWarp.Validation.Camera");
            go.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(go, Scene);
            Camera = go.AddComponent<Camera>();
            Camera.scene = Scene;
            Camera.cameraType = CameraType.Game;
            Camera.enabled = false;
            Camera.transform.position = new Vector3(0f, 0f, -8f);
            Camera.transform.rotation = Quaternion.identity;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 50f;
            Camera.fieldOfView = 55f;
            Camera.orthographicSize = 6.2f;
            Camera.rect = new Rect(0, 0, 1, 1);
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0.025f, 0.04f, 0.065f, 1f);
            Camera.allowHDR = true;
            Camera.allowMSAA = false;
            Camera.allowDynamicResolution = false;
            Camera.useOcclusionCulling = false;
            UniversalAdditionalCameraData data = Camera.GetUniversalAdditionalCameraData();
            data.SetRenderer(renderer);
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.volumeLayerMask = 0;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Require(shader != null && shader.isSupported, "URP Unlit fixture shader available");
            Material wall = Material(shader, new Color(0.04f, 0.065f, 0.09f, 1));
            Material grid = Material(shader, new Color(0.35f, 0.42f, 0.5f, 1));
            Material red = Material(shader, new Color(0.9f, 0.06f, 0.04f, 1));
            Material green = Material(shader, new Color(0.06f, 0.8f, 0.16f, 1));
            Material blue = Material(shader, new Color(0.04f, 0.25f, 0.95f, 1));
            Material yellow = Material(shader, new Color(0.95f, 0.7f, 0.04f, 1));
            Cube(new Vector3(0, 0, 9), new Vector3(60, 40, 0.1f), wall);
            for (int x = -14; x <= 14; x += 2)
                Cube(new Vector3(x, 0, 8), new Vector3(x == 0 ? 0.12f : 0.055f, 40, 0.08f), grid);
            for (int y = -10; y <= 10; y += 2)
                Cube(new Vector3(0, y, 7.9f), new Vector3(60, y == 0 ? 0.12f : 0.055f, 0.08f), grid);
            // Asymmetric near/far colored markers show orientation and actual scene reprojection.
            Cube(new Vector3(-2.4f, 1.9f, 1), new Vector3(1.4f, 0.45f, 0.6f), red);
            Cube(new Vector3(-2.85f, 1.45f, 1), new Vector3(0.5f, 1.25f, 0.6f), red);
            Cube(new Vector3(2.2f, 1.25f, 3), new Vector3(1.2f, 1.6f, 0.8f), green);
            Cube(new Vector3(-1.45f, -1.8f, 4), new Vector3(1.6f, 0.8f, 0.8f), blue);
            Cube(new Vector3(1.65f, -1.1f, 0), new Vector3(0.65f, 1.3f, 0.8f), yellow);
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        Material Material(Shader shader, Color color)
        {
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_BaseColor", color);
            materials.Add(material);
            return material;
        }
        void Cube(Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ScreenWarp.Validation.Marker";
            go.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(go, Scene);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
        public void Dispose()
        {
            if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene);
            foreach (Material material in materials) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    sealed class CameraState
    {
        readonly Matrix4x4 projection, view, pose;
        readonly float fov, size, aspect;
        readonly bool orthographic;
        readonly RenderTexture target;
        public CameraState(Camera camera)
        {
            projection = camera.projectionMatrix;
            view = camera.worldToCameraMatrix;
            pose = camera.transform.localToWorldMatrix;
            fov = camera.fieldOfView;
            size = camera.orthographicSize;
            aspect = camera.aspect;
            orthographic = camera.orthographic;
            target = camera.targetTexture;
        }
        public void Check(Camera camera, string name)
        {
            Require(MatrixDifference(projection, camera.projectionMatrix) <= 1e-6f, name + ": projectionMatrix unchanged");
            Require(MatrixDifference(view, camera.worldToCameraMatrix) <= 1e-6f, name + ": worldToCameraMatrix unchanged");
            Require(MatrixDifference(pose, camera.transform.localToWorldMatrix) <= 1e-6f, name + ": Transform unchanged");
            Require(camera.fieldOfView == fov && camera.orthographicSize == size && camera.aspect == aspect
                && camera.orthographic == orthographic && camera.targetTexture == target, name + ": camera properties unchanged");
        }
    }
    static float MatrixDifference(Matrix4x4 a, Matrix4x4 b)
    {
        float maximum = 0;
        for (int i = 0; i < 16; i++) maximum = Mathf.Max(maximum, Mathf.Abs(a[i] - b[i]));
        return maximum;
    }

    static Color[] Capture(Camera camera, int width, int height, string name)
    {
        var state = new CameraState(camera);
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            name = "ScreenWarp.Validation.Target", antiAliasing = 1, useMipMap = false,
            autoGenerateMips = false, useDynamicScale = false, hideFlags = HideFlags.HideAndDontSave
        };
        Texture2D readback = null;
        try
        {
            Require(target.Create(), name + ": target creation");
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), name + ": real SingleCameraRequest supported");
            RenderPipeline.SubmitRenderRequest(camera, request);
            report.requests++;
            RenderTexture.active = target;
            readback = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            readback.Apply(false, false);
            Color[] pixels = readback.GetPixels();
            Require(pixels.Length == width * height, name + ": complete readback");
            Require(pixels.All(c => Finite(c.r) && Finite(c.g) && Finite(c.b) && Finite(c.a)), name + ": finite RGBA");
            var display = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                display.SetPixels(pixels.Select(c => new Color(Mathf.LinearToGammaSpace(c.r),
                    Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b), 1)).ToArray());
                display.Apply(false, false);
                string filename = "pipeline-" + name + ".png";
                File.WriteAllBytes(Path.Combine(Output, filename), display.EncodeToPNG());
                report.files.Add(filename);
            }
            finally { UnityEngine.Object.DestroyImmediate(display); }
            return pixels;
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            state.Check(camera, name);
            Require(RenderTexture.active == previousActive, name + ": active RT restored");
        }
    }
    static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

    static Comparison Compare(Color[] a, Color[] b, int width, int height, string name)
    {
        Require(a.Length == b.Length, name + ": comparison size");
        double sum = 0;
        float maximum = 0;
        float maximumAlpha = 0;
        int changed = 0;
        for (int i = 0; i < a.Length; i++)
        {
            float r = Mathf.Abs(a[i].r - b[i].r);
            float g = Mathf.Abs(a[i].g - b[i].g);
            float blue = Mathf.Abs(a[i].b - b[i].b);
            float delta = Mathf.Max(r, g, blue);
            sum += (r + g + blue) / 3.0;
            maximum = Mathf.Max(maximum, delta);
            maximumAlpha = Mathf.Max(maximumAlpha, Mathf.Abs(a[i].a - b[i].a));
            if (delta > 0.02f) changed++;
        }
        var result = new Comparison { name = name, width = width, height = height,
            meanRgbDifference = (float)(sum / a.Length), maxRgbDifference = maximum,
            maxAlphaDifference = maximumAlpha, sampledPixelCount = a.Length,
            changedPixelFraction = (float)changed / a.Length };
        report.comparisons.Add(result);
        return result;
    }

    static void CheckGroup(ScreenWarpFeature feature, Camera camera, bool ortho, int width, int height)
    {
        camera.orthographic = ortho;
        camera.aspect = (float)width / height;
        camera.ResetProjectionMatrix();
        var state = new CameraState(camera);
        string label = (ortho ? "orthographic" : "perspective") + "-" + width + "x" + height;
        feature.settings.mode = ScreenWarpFeature.WarpMode.Barrel;
        feature.settings.strength = 1;
        feature.settings.center = new Vector2(0.5f, 0.5f);
        feature.settings.zoom = 1;
        feature.settings.border = ScreenWarpFeature.BorderMode.Color;
        feature.settings.borderColor = new Color(0.9f, 0.02f, 0.7f, 1);
        feature.settings.debug = ScreenWarpFeature.DebugMode.Off;
        feature.SetActive(false);
        Color[] baseline = Capture(camera, width, height, label + "-off");
        // Verify the fixture contains real scene contrast, not only a cleared/empty target.
        Require(baseline.Max(c => c.r) - baseline.Min(c => c.r) > 0.3f
            && baseline.Max(c => c.g) - baseline.Min(c => c.g) > 0.3f, label + ": scene geometry visible");
        feature.SetActive(true);
        feature.settings.strength = 0;
        feature.settings.zoom = 1.8f;
        Color[] zero = Capture(camera, width, height, label + "-zero");
        Require(Compare(baseline, zero, width, height, label + "-off-equals-zero-including-zoom").maxRgbDifference <= 1e-5f,
            label + ": inactive and strength=0/debug=Off produce same actual camera image");
        feature.settings.strength = 1;
        feature.settings.zoom = 1;
        foreach (ScreenWarpFeature.WarpMode mode in Enum.GetValues(typeof(ScreenWarpFeature.WarpMode)))
        {
            feature.settings.mode = mode;
            Color[] warped = Capture(camera, width, height, label + "-" + mode);
            Comparison difference = Compare(baseline, warped, width, height, label + "-" + mode + "-changes-real-color");
            Require(difference.meanRgbDifference > 0.003f && difference.changedPixelFraction > 0.02f,
                label + ": " + mode + " must visibly remap the actual scene");
        }
        if (!ortho && width > height)
        {
            feature.settings.mode = ScreenWarpFeature.WarpMode.Barrel;
            feature.settings.debug = ScreenWarpFeature.DebugMode.SourceUV;
            feature.settings.strength = 0;
            Color[] identityUV = Capture(camera, width, height, label + "-uv-identity");
            feature.settings.strength = 1;
            Color[] warpedUV = Capture(camera, width, height, label + "-uv-barrel");
            Require(Compare(identityUV, warpedUV, width, height, label + "-uv-coordinate-change").meanRgbDifference > 0.02f,
                "SourceUV debug proves actual feature coordinate change");
            feature.settings.debug = ScreenWarpFeature.DebugMode.Off;
            feature.settings.border = ScreenWarpFeature.BorderMode.Clamp;
            Color[] clamp = Capture(camera, width, height, label + "-barrel-clamp");
            feature.settings.border = ScreenWarpFeature.BorderMode.Color;
            Color[] color = Capture(camera, width, height, label + "-barrel-color");
            Require(Compare(clamp, color, width, height, label + "-border-mode-integration").meanRgbDifference > 0.01f,
                "Border setting changes real pipeline output");
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            bool originalPostProcessing = data.renderPostProcessing;
            try
            {
                data.renderPostProcessing = true;
                Require(data.volumeLayerMask.value == 0, "PP integration probe has no Volume influences");
                feature.settings.border = ScreenWarpFeature.BorderMode.Clamp;
                Color[] ppClamp = Capture(camera, width, height, label + "-pp-barrel-clamp");
                feature.settings.border = ScreenWarpFeature.BorderMode.Color;
                Color[] ppColor = Capture(camera, width, height, label + "-pp-barrel-color");
                Require(Compare(ppClamp, ppColor, width, height, label + "-postprocessing-border-integration").meanRgbDifference > 0.01f,
                    "ScreenWarp border remains active with the main postprocessing path enabled");
            }
            finally { data.renderPostProcessing = originalPostProcessing; }
        }
        state.Check(camera, label + "-entire-effect-toggle-sequence");
    }

    // The program textures are disposable validation inputs, not imported assets.
    // Binary colors avoid requiring an assumed display transform for fixed-coordinate checks.
    static Texture2D OverlayTexture(bool transparentCenter)
    {
        const int side = 64;
        var texture = new Texture2D(side, side, TextureFormat.RGBAFloat, false, true)
        {
            name = transparentCenter ? "ScreenWarp.Validation.RGBAFrame" : "ScreenWarp.Validation.OpaqueOverlay",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color[side * side];
        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
        {
            Color color = x < side / 2
                ? (y < side / 2 ? Color.red : Color.yellow)
                : (y < side / 2 ? Color.blue : Color.cyan);
            if (transparentCenter && x >= side / 4 && x < side * 3 / 4
                && y >= side / 4 && y < side * 3 / 4)
                color = new Color(1, 0, 1, 0); // Nonzero RGB must not leak through alpha=0.
            pixels[y * side + x] = color;
        }
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    static Comparison CompareOverlayRegion(Color[] a, Color[] b, int width, int height,
        string name, bool center)
    {
        Require(a.Length == width * height && b.Length == a.Length, name + ": matching image dimensions");
        double sum = 0;
        float maximum = 0;
        float maximumAlpha = 0;
        int changed = 0;
        int samples = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            float v = (y + 0.5f) / height;
            // Stay several texture texels away from alpha/filter boundaries.
            bool selected = center ? u > 0.30f && u < 0.70f && v > 0.30f && v < 0.70f
                : u < 0.18f || u > 0.82f || v < 0.18f || v > 0.82f;
            if (!selected) continue;
            int index = y * width + x;
            float r = Mathf.Abs(a[index].r - b[index].r);
            float g = Mathf.Abs(a[index].g - b[index].g);
            float blue = Mathf.Abs(a[index].b - b[index].b);
            float delta = Mathf.Max(r, g, blue);
            sum += (r + g + blue) / 3.0;
            maximum = Mathf.Max(maximum, delta);
            maximumAlpha = Mathf.Max(maximumAlpha, Mathf.Abs(a[index].a - b[index].a));
            if (delta > 0.02f) changed++;
            samples++;
        }
        Require(samples > width * height / 10, name + ": region contains a substantial number of pixels");
        var comparison = new Comparison { name = name, width = width, height = height,
            meanRgbDifference = (float)(sum / samples), maxRgbDifference = maximum,
            maxAlphaDifference = maximumAlpha, sampledPixelCount = samples,
            changedPixelFraction = (float)changed / samples };
        report.comparisons.Add(comparison);
        return comparison;
    }

    static void EqualRGBA(Comparison comparison, string message)
    {
        Require(comparison.maxRgbDifference <= 1e-5f && comparison.maxAlphaDifference <= 1e-5f, message);
    }

    static void CheckOverlay(ScreenWarpFeature feature, Camera camera, int width, int height)
    {
        int requestStart = report.requests;
        int assertionStart = report.assertions;
        ScreenWarpFeature.Settings originalSettings = feature.settings;
        bool originalActive = feature.isActive;
        bool originalOrthographic = camera.orthographic;
        float originalAspect = camera.aspect;
        Matrix4x4 originalProjection = camera.projectionMatrix;
        var originalCamera = new CameraState(camera);
        Texture2D opaque = null;
        Texture2D frame = null;
        string label = "overlay-" + width + "x" + height;
        try
        {
            camera.orthographic = false;
            camera.aspect = (float)width / height;
            camera.ResetProjectionMatrix();
            var fixedCamera = new CameraState(camera);
            feature.settings = new ScreenWarpFeature.Settings
            {
                shader = originalSettings.shader,
                mode = ScreenWarpFeature.WarpMode.Barrel,
                strength = 0,
                center = new Vector2(0.5f, 0.5f),
                zoom = 1,
                border = ScreenWarpFeature.BorderMode.Color,
                borderColor = new Color(0.9f, 0.02f, 0.7f, 1),
                debug = ScreenWarpFeature.DebugMode.Off,
                overlayTexture = null,
                overlayOpacity = 1
            };
            opaque = OverlayTexture(false);
            frame = OverlayTexture(true);
            feature.SetActive(false);
            Color[] baseline = Capture(camera, width, height, label + "-baseline");
            feature.SetActive(true);
            Color[] nullOverlay = Capture(camera, width, height, label + "-null");
            EqualRGBA(Compare(baseline, nullOverlay, width, height, label + "-null-preserves-baseline"),
                "Null overlay and zero warp preserve baseline RGBA");

            feature.settings.overlayTexture = opaque;
            feature.settings.overlayOpacity = 0;
            Color[] opacityZero = Capture(camera, width, height, label + "-opacity-zero");
            EqualRGBA(Compare(baseline, opacityZero, width, height, label + "-opacity-zero-preserves-baseline"),
                "Opacity zero and zero warp preserve baseline RGBA");

            feature.settings.overlayOpacity = 1;
            Color[] opaqueOnly = Capture(camera, width, height, label + "-opaque-zero-warp");
            Comparison opaqueDifference = Compare(baseline, opaqueOnly, width, height, label + "-opaque-overlay-enqueues-at-zero-warp");
            Require(opaqueDifference.meanRgbDifference > 0.1f && opaqueDifference.changedPixelFraction > 0.9f,
                "Opaque overlay must render over the real scene even when warp strength is zero");
            feature.settings.strength = 1;
            Color[] opaqueWarp = Capture(camera, width, height, label + "-opaque-with-warp");
            EqualRGBA(Compare(opaqueOnly, opaqueWarp, width, height, label + "-opaque-fixed-screen-coordinates"),
                "Opaque overlay stays at identical screen coordinates when warp changes");

            feature.settings.overlayTexture = null;
            Color[] warped = Capture(camera, width, height, label + "-warped-control");
            Comparison warpControl = Compare(baseline, warped, width, height, label + "-warp-control-is-visible");
            Require(warpControl.meanRgbDifference > 0.003f && warpControl.changedPixelFraction > 0.02f,
                "Overlay region checks require an actually changed warped-scene control");
            feature.settings.overlayTexture = frame;
            feature.settings.overlayOpacity = 0;
            Color[] warpedOpacityZero = Capture(camera, width, height, label + "-warped-opacity-zero");
            EqualRGBA(Compare(warped, warpedOpacityZero, width, height, label + "-opacity-zero-keeps-warp"),
                "Opacity zero disables overlay without disabling scene warp");

            feature.settings.overlayOpacity = 1;
            Color[] warpedFrame = Capture(camera, width, height, label + "-warped-transparent-frame");
            EqualRGBA(CompareOverlayRegion(warped, warpedFrame, width, height, label + "-transparent-center-keeps-warped-rgba", true),
                "Zero-alpha frame center preserves the already-warped scene RGBA");
            Comparison visibleFrame = CompareOverlayRegion(warped, warpedFrame, width, height, label + "-opaque-frame-border-visible", false);
            Require(visibleFrame.meanRgbDifference > 0.1f && visibleFrame.changedPixelFraction > 0.9f,
                "The frame test also requires a visible opaque border");

            feature.settings.strength = 0;
            Color[] unwarpedFrame = Capture(camera, width, height, label + "-unwarped-transparent-frame");
            EqualRGBA(CompareOverlayRegion(baseline, unwarpedFrame, width, height, label + "-transparent-center-keeps-baseline-rgba", true),
                "Zero-alpha frame center preserves unwarped baseline RGBA");
            EqualRGBA(CompareOverlayRegion(warpedFrame, unwarpedFrame, width, height, label + "-frame-border-fixed-screen-coordinates", false),
                "Opaque frame edge remains fixed to the same screen pixels with warp on/off");

            feature.settings.strength = 1;
            feature.settings.debug = ScreenWarpFeature.DebugMode.SourceUV;
            feature.settings.overlayTexture = null;
            Color[] debugControl = Capture(camera, width, height, label + "-sourceuv-control");
            feature.settings.overlayTexture = opaque;
            Color[] debugOverlay = Capture(camera, width, height, label + "-sourceuv-with-opaque-overlay");
            EqualRGBA(Compare(debugControl, debugOverlay, width, height, label + "-debug-bypasses-overlay"),
                "SourceUV debug remains visible and is not covered by the opaque overlay");
            Require(Compare(opaqueOnly, debugOverlay, width, height, label + "-debug-control-is-distinct").meanRgbDifference > 0.1f,
                "Debug bypass comparison must use a debug image visibly distinct from the overlay");
            fixedCamera.Check(camera, label + "-entire-overlay-sequence");
        }
        finally
        {
            feature.settings = originalSettings;
            feature.SetActive(originalActive);
            camera.orthographic = originalOrthographic;
            camera.aspect = originalAspect;
            camera.projectionMatrix = originalProjection;
            if (opaque != null) UnityEngine.Object.DestroyImmediate(opaque);
            if (frame != null) UnityEngine.Object.DestroyImmediate(frame);
            report.overlayRequests += report.requests - requestStart;
            try { originalCamera.Check(camera, label + "-camera-restored"); }
            finally { report.overlayAssertions += report.assertions - assertionStart; }
        }
    }

    public static object Main(string[] args)
    {
        Output = args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0]
            : "tmp/sessions/" + DateTime.Now.ToString("yyyy-MM-dd") + "-screenwarp-pipeline-"
                + Guid.NewGuid().ToString("N").Substring(0, 8) + "/output";
        report = new Report { device = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
            pipeline = GraphicsSettings.currentRenderPipeline == null ? "null" : GraphicsSettings.currentRenderPipeline.name };
        Directory.CreateDirectory(Output);
        Scene originalActiveScene = SceneManager.GetActiveScene();
        Scene[] originals = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        bool[] dirty = originals.Select(s => s.isDirty).ToArray();
        RenderTexture originalActiveTexture = RenderTexture.active;
        Application.LogCallback logger = (condition, stackTrace, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                report.errors.Add(type + ": " + condition + "\n" + stackTrace);
        };
        Application.logMessageReceived += logger;
        try
        {
            Require(!Application.isPlaying, "Edit Mode required; this script never enters Play");
            Require(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat), "ARGBFloat RT supported");
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Mine/Shaders/PostProcess/ScreenWarp/ScreenWarp.shader");
            Require(shader != null && shader.isSupported, "ScreenWarp shader exists and supported");
            using (var renderer = new RendererScope())
            using (var scene = new SceneScope(renderer.Index))
            {
                var feature = ScriptableObject.CreateInstance<ScreenWarpFeature>();
                feature.settings.shader = shader;
                feature.name = "ScreenWarp.Validation.Feature";
                renderer.Use(feature);
                Require(scene.Camera.cameraType == CameraType.Game, "Probe camera type is Game");
                Require(scene.Camera.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Base,
                    "Probe camera is Base");
                CheckGroup(feature, scene.Camera, false, 640, 360);
                CheckGroup(feature, scene.Camera, false, 360, 640);
                CheckGroup(feature, scene.Camera, true, 640, 360);
                CheckGroup(feature, scene.Camera, true, 360, 640);
                report.baseRequests = report.requests;
                CheckOverlay(feature, scene.Camera, 640, 360);
                CheckOverlay(feature, scene.Camera, 360, 640);
            }
            Require(SceneManager.sceneCount == originals.Length && SceneManager.GetActiveScene() == originalActiveScene,
                "Original scene list and active scene unchanged");
            for (int i = 0; i < originals.Length; i++)
                Require(originals[i].IsValid() && SceneManager.GetSceneAt(i) == originals[i] && originals[i].isDirty == dirty[i],
                    "Original scene and dirty state unchanged: " + originals[i].name);
            report.originalSceneStateRestored = true;
            Require(RenderTexture.active == originalActiveTexture, "Original active RenderTexture restored");
            report.activeRenderTextureRestored = true;
            Require(report.errors.Count == 0, "Actual render requests must emit zero error/exception/assert logs");
            report.passed = true;
        }
        catch (Exception exception)
        {
            report.error = exception.ToString();
        }
        finally
        {
            Application.logMessageReceived -= logger;
            report.originalSceneStateRestored = SceneManager.sceneCount == originals.Length
                && SceneManager.GetActiveScene() == originalActiveScene;
            for (int i = 0; i < originals.Length && report.originalSceneStateRestored; i++)
                report.originalSceneStateRestored = originals[i].IsValid()
                    && SceneManager.GetSceneAt(i) == originals[i] && originals[i].isDirty == dirty[i];
            report.activeRenderTextureRestored = RenderTexture.active == originalActiveTexture;
            string json = JsonUtility.ToJson(report, true);
            if (!json.Contains("\"comparisons\""))
                json = json.TrimEnd().TrimEnd('}') + ",\n\"comparisons\": [\n"
                    + string.Join(",\n", report.comparisons.Select(c => JsonUtility.ToJson(c, true))) + "\n]}";
            File.WriteAllText(Path.Combine(Output, "pipeline-checks.json"), json);
        }
        return new { passed = report.passed, assertions = report.assertions, requests = report.requests,
            baseRequests = report.baseRequests, overlayRequests = report.overlayRequests, overlayAssertions = report.overlayAssertions,
            errorCount = report.errors.Count, error = report.error, output = Output + "/pipeline-checks.json" };
    }
}
