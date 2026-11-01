using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Mine.HDRMaps;
using Object = UnityEngine.Object;

// unityctl script execute <this-file> <output-directory>
// Six real unlit cubes, an isolated preview scene and camera-local sky material.
// No scene, renderer/pipeline asset, importer, lighting setting or image is saved.
public class Script
{
    [Serializable] public sealed class Report
    {
        public string suite = "HDRMapCaptureChecks", graphicsDevice;
        public int passed, failed, captureCalls, axisColorChecks, negativeControls;
        public List<string> details = new List<string>();
    }
    private sealed class SceneState { public Scene scene; public bool dirty; }
    private sealed class CameraState
    {
        public string json;
        public Vector3 position;
        public Quaternion rotation;
        public CameraType type;
        public Scene scene;
        public bool enabled;
        public ulong sceneCullingMask;
        public string additionalJson;
    }
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<Object> Owned = new List<Object>();
    private static readonly Vector3[] Axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
    private static readonly Color[] FaceColors = { new Color(4, .1f, .2f, 1), new Color(.1f, 3, .2f, 1),
        new Color(.1f, .2f, 2, 1), new Color(2, 2, .1f, 1), new Color(2, .1f, 2, 1), new Color(.1f, 2, 2, 1) };
    // Must also be allowed by the active URP Renderer's opaque layer mask.
    // The project PC renderer includes Default (0) and TransparentFX (1).
    private const int SelectedLayer = 0;

    public static object Main(string[] args)
    {
        return Run(args != null && args.Length > 0 ? args[0] : null);
    }
    public static string Run(string outputDirectory)
    {
        var report = new Report { graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName };
        var scenes = new List<SceneState>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            scenes.Add(new SceneState { scene = scene, dirty = scene.isDirty });
        }
        Scene activeScene = SceneManager.GetActiveScene();
        Material oldSkybox = RenderSettings.skybox;
        Light oldSun = RenderSettings.sun;
        AmbientMode oldAmbientMode = RenderSettings.ambientMode;
        Color oldAmbient = RenderSettings.ambientLight;
        float oldAmbientIntensity = RenderSettings.ambientIntensity;
        float oldReflectionIntensity = RenderSettings.reflectionIntensity;
        var oldDefaultPipeline = GraphicsSettings.defaultRenderPipeline;
        var oldQualityPipeline = QualitySettings.renderPipeline;
        Object[] oldSelection = Selection.objects;
        RenderTexture oldRT = RenderTexture.active;
        bool oldSRGBWrite = GL.sRGBWrite;
        Scene preview = default(Scene);
        var errors = new List<string>();
        Application.LogCallback listener = (message, trace, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(type + ": " + message);
        };
        Application.logMessageReceived += listener;
        try
        {
            Require(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "URP required.");
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Real GPU required.");
            preview = EditorSceneManager.NewPreviewScene();
            HDRMapBaker baker = MakeFramework(preview);
            Camera camera = baker.BakeCamera;
            for (int i = 0; i < Axes.Length; i++) MakeCube(preview, Axes[i] * 3f, FaceColors[i], SelectedLayer, "axis " + i);
            // This different-layer foreground occluder must not replace the selected +Z cube.
            MakeCube(preview, Vector3.forward * 1.25f, Color.white, 1, "excluded foreground layer");
            AddLocalSky(camera);
            SetLayersAndSky(baker, 1 << SelectedLayer, false);

            Check(report, "framework.bound_children_and_camera_are_dedicated", () =>
            {
                Require(baker.IsInitialized, "fixture initialized");
                Require(baker.Owns(baker.Volume) && baker.Owns(baker.Direction) && baker.Owns(camera), "dedicated owned children");
                Require(camera.scene == preview, "preview camera scene");
            });

            Check(report, "capture.identity_six_faces_layer_filter_and_hdr", () =>
            {
                CameraState before = Snapshot(camera);
                Texture2D panorama = Capture(baker, 65, report);
                Require(panorama.width == 130 && panorama.height == 65, "Baker outputs 2:1 panorama at exact odd resolution");
                Require(panorama.format == TextureFormat.RGBAHalf && panorama.mipmapCount == 1, "HDR mip0 source panorama");
                AssertAxes(panorama, Quaternion.identity, report);
                AssertCamera(before, camera);
            });

            Check(report, "capture.frame_rotation_is_applied_once", () =>
            {
                Quaternion previousRotation = baker.Direction.transform.rotation;
                try
                {
                    baker.Direction.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                    CameraState before = Snapshot(camera);
                    Texture2D panorama = Capture(baker, 65, report);
                    AssertAxes(panorama, baker.Direction.transform.rotation, report);
                    AssertCamera(before, camera);
                }
                finally { baker.Direction.transform.rotation = previousRotation; }
            });

            Check(report, "capture.empty_layers_without_sky_is_transparent", () =>
            {
                SetLayersAndSky(baker, 0, false);
                CameraState before = Snapshot(camera);
                Texture2D empty = Capture(baker, 33, report);
                foreach (Color color in empty.GetPixels()) Near(Color.clear, color, .001f, "empty layer/no sky pixel");
                AssertCamera(before, camera);
            });

            Check(report, "capture.empty_layers_with_sky_is_pure_sky", () =>
            {
                SetLayersAndSky(baker, 0, true);
                CameraState before = Snapshot(camera);
                Texture2D sky = Capture(baker, 33, report);
                float minimumAlpha = 1f, maximumValue = 0f;
                Color first = sky.GetPixel(0, 0);
                foreach (Color color in sky.GetPixels())
                {
                    minimumAlpha = Mathf.Min(minimumAlpha, color.a);
                    maximumValue = Mathf.Max(maximumValue, Mathf.Max(color.r, Mathf.Max(color.g, color.b)));
                    Near(first, color, .015f, "constant cubemap sky is spatially constant");
                }
                Require(minimumAlpha > .99f, "sky has opaque coverage; minAlpha=" + minimumAlpha);
                Require(maximumValue > 1.2f, "sky preserves radiance above 1; max=" + maximumValue);
                Require(first.b > first.g && first.g > first.r, "camera-local blue HDR sky is used");
                AssertCamera(before, camera);
            });

            Check(report, "capture.layers_with_sky_keeps_objects_and_fills_background", () =>
            {
                SetLayersAndSky(baker, 1 << SelectedLayer, true);
                Texture2D combined = Capture(baker, 65, report);
                AssertAxes(combined, Quaternion.identity, report);
                Color between = PanoramaSample(combined, new Vector3(1f, 1f, 1f).normalized);
                Require(between.a > .99f, "sky fills gaps between six separated cubes");
                Require(between.b > between.g && between.g > between.r, "gap has local blue sky color");
            });

            Check(report, "capture.negative_control_wrong_rotation_must_fail", () =>
            {
                SetLayersAndSky(baker, 1 << SelectedLayer, false);
                Texture2D identity = Capture(baker, 33, report);
                bool caught = false;
                try { Near(FaceColors[0], PanoramaSample(identity, Vector3.forward), .03f, "deliberately wrong +Z expected +X"); }
                catch (InvalidOperationException) { caught = true; }
                Require(caught, "six-color capture assertion rejects a known wrong orientation");
                report.negativeControls++;
            });

            foreach (int invalid in new[] { 0, 15, 4097 })
                Check(report, "capture.arguments.invalid_resolution=" + invalid, () =>
                {
                    CameraState before = Snapshot(camera);
                    bool rejected = false;
                    Texture2D unexpected = null;
                    try { unexpected = HDRMapCapture.Bake(baker, invalid); }
                    catch (ArgumentOutOfRangeException) { rejected = true; }
                    finally { if (unexpected != null) Object.DestroyImmediate(unexpected); }
                    Require(rejected, "invalid capture resolution must throw, not silently clamp");
                    AssertCamera(before, camera);
                });
            Check(report, "capture.arguments.missing_framework_is_rejected", () =>
            {
                bool rejected = false;
                try { HDRMapCapture.Bake((HDRMapBaker)null, 33); }
                catch (UnityException) { rejected = true; }
                Require(rejected, "missing framework rejected");
            });
        }
        catch (Exception exception) { Failure(report, "fixture.unexpected_exception", exception); }
        finally
        {
            for (int i = Owned.Count - 1; i >= 0; i--)
                if (Owned[i] != null) Object.DestroyImmediate(Owned[i]);
            Owned.Clear();
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            Selection.objects = oldSelection;
            Application.logMessageReceived -= listener;
        }
        Check(report, "cleanup.original_scenes_unchanged", () =>
        {
            Require(SceneManager.GetActiveScene() == activeScene, "active scene unchanged");
            Require(SceneManager.sceneCount == scenes.Count, "original loaded-scene count");
            foreach (SceneState state in scenes)
                Require(state.scene.IsValid() && state.scene.isDirty == state.dirty, "scene/dirty flag preserved: " + state.scene.path);
        });
        Check(report, "cleanup.original_render_settings_pipeline_and_render_state_unchanged", () =>
        {
            Require(RenderSettings.skybox == oldSkybox && RenderSettings.sun == oldSun, "global sky/sun unchanged");
            Require(RenderSettings.ambientMode == oldAmbientMode && RenderSettings.ambientLight == oldAmbient
                && RenderSettings.ambientIntensity == oldAmbientIntensity && RenderSettings.reflectionIntensity == oldReflectionIntensity,
                "global lighting settings unchanged");
            Require(GraphicsSettings.defaultRenderPipeline == oldDefaultPipeline && QualitySettings.renderPipeline == oldQualityPipeline,
                "pipeline references unchanged");
            Require(RenderTexture.active == oldRT && GL.sRGBWrite == oldSRGBWrite, "render target/sRGB state restored");
        });
        Check(report, "unity.no_unexpected_error_logs", () => Require(errors.Count == 0, string.Join(" | ", errors.ToArray())));
        Check(report, "suite.required_capture_work_executed", () =>
        {
            Require(report.captureCalls == 6, "six actual captures executed; actual=" + report.captureCalls);
            Require(report.axisColorChecks == 18, "identity, rotated and sky/object captures each checked six axes");
            Require(report.negativeControls == 1, "wrong-orientation capture control rejected");
        });
        string json = JsonUtility.ToJson(report, true);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "capture-report.json"), json);
        }
        if (report.failed != 0) throw new InvalidOperationException(json);
        return json;
    }

    private static HDRMapBaker MakeFramework(Scene preview)
    {
        GameObject root = MakeObject(preview, "HDR capture validation framework");
        HDRMapBaker baker = root.AddComponent<HDRMapBaker>();
        GameObject volumeObject = MakeObject(preview, "BakeVolume");
        volumeObject.transform.SetParent(root.transform, false);
        HDRMapBakeVolume volume = volumeObject.AddComponent<HDRMapBakeVolume>();
        typeof(HDRMapBakeVolume).GetField("_size", Private).SetValue(volume, new Vector3(12f, 12f, 12f));
        GameObject directionObject = MakeObject(preview, "BakeDirection");
        directionObject.transform.SetParent(root.transform, false);
        HDRMapBakeDirection direction = directionObject.AddComponent<HDRMapBakeDirection>();
        GameObject cameraObject = MakeObject(preview, "BakeCamera");
        cameraObject.transform.SetParent(root.transform, false);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.scene = preview;
        // Reflection cameras do not automatically inherit the Preview camera's
        // scene culling path. Explicitly include this scene's isolated culling bit.
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
        camera.backgroundColor = new Color(.12f, .34f, .56f, .78f);
        camera.clearFlags = CameraClearFlags.Depth;
        camera.fieldOfView = 47f;
        camera.aspect = 1.3f;
        camera.cullingMask = 1 << 29;
        camera.nearClipPlane = .2f;
        camera.farClipPlane = 25f;
        camera.allowHDR = true;
        camera.allowMSAA = true;
        camera.transform.SetPositionAndRotation(new Vector3(.1f, .2f, .3f), Quaternion.Euler(7f, 11f, 13f));
        UniversalAdditionalCameraData data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.allowXRRendering = true;
        data.allowHDROutput = true;
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        data.renderShadows = false;
        data.volumeLayerMask = 0;
        typeof(HDRMapBaker).GetField("_volume", Private).SetValue(baker, volume);
        typeof(HDRMapBaker).GetField("_direction", Private).SetValue(baker, direction);
        typeof(HDRMapBaker).GetField("_camera", Private).SetValue(baker, camera);
        return baker;
    }
    private static void SetLayersAndSky(HDRMapBaker baker, int layers, bool sky)
    {
        typeof(HDRMapBaker).GetField("_renderLayers", Private).SetValue(baker, (LayerMask)layers);
        typeof(HDRMapBaker).GetField("_includeSky", Private).SetValue(baker, sky);
    }
    private static void AddLocalSky(Camera camera)
    {
        Shader shader = Shader.Find("Skybox/Cubemap");
        Require(shader != null && shader.isSupported, "built-in cubemap sky shader required");
        Material material = Keep(new Material(shader));
        Cubemap cube = Keep(new Cubemap(16, TextureFormat.RGBAHalf, false));
        var pixels = new Color[16 * 16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(.25f, .5f, 2f, 1f);
        for (int i = 0; i < 6; i++) cube.SetPixels(pixels, (CubemapFace)i);
        cube.Apply(false, false);
        material.SetTexture("_Tex", cube);
        material.SetColor("_Tint", new Color(.5f, .5f, .5f, 1f));
        material.SetFloat("_Exposure", 1f);
        camera.gameObject.AddComponent<Skybox>().material = material;
    }
    private static void MakeCube(Scene preview, Vector3 position, Color color, int layer, string name)
    {
        GameObject cube = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
        cube.name = name;
        SceneManager.MoveGameObjectToScene(cube, preview);
        cube.layer = layer;
        cube.transform.position = position;
        cube.transform.localScale = Vector3.one * .8f;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        Require(shader != null && shader.isSupported, "URP Unlit shader required");
        Material material = Keep(new Material(shader));
        // ShaderLab's Color property reaches the shader in linear space. The
        // fixture's FaceColors are desired linear radiance, so encode the value
        // for the material property rather than comparing against gamma colors.
        material.SetColor("_BaseColor", color.gamma);
        cube.GetComponent<Renderer>().sharedMaterial = material;
    }
    private static GameObject MakeObject(Scene preview, string name)
    {
        GameObject go = Keep(new GameObject(name));
        SceneManager.MoveGameObjectToScene(go, preview);
        return go;
    }
    private static Texture2D Capture(HDRMapBaker baker, int resolution, Report report)
    {
        Texture2D output = Keep(HDRMapCapture.Bake(baker, resolution));
        report.captureCalls++;
        return output;
    }
    private static void AssertAxes(Texture2D panorama, Quaternion frame, Report report)
    {
        for (int i = 0; i < Axes.Length; i++)
        {
            Vector3 world = frame * Axes[i];
            int expectedFace = ClosestFace(world);
            Near(FaceColors[expectedFace], PanoramaSample(panorama, Axes[i]), .035f,
                "local axis " + i + " should see world face " + expectedFace);
            report.axisColorChecks++;
        }
    }
    private static int ClosestFace(Vector3 d)
    {
        int closest = 0;
        float best = float.NegativeInfinity;
        for (int i = 0; i < Axes.Length; i++)
        {
            float dot = Vector3.Dot(d, Axes[i]);
            if (dot > best) { closest = i; best = dot; }
        }
        return closest;
    }
    private static Color PanoramaSample(Texture2D panorama, Vector3 direction)
    {
        direction.Normalize();
        float u = Mathf.Atan2(direction.x, direction.z) / (2f * Mathf.PI) + .5f;
        float v = Mathf.Asin(direction.y) / Mathf.PI + .5f;
        return panorama.GetPixelBilinear(u, v);
    }
    private static CameraState Snapshot(Camera camera)
    {
        return new CameraState { json = EditorJsonUtility.ToJson(camera), position = camera.transform.position,
            rotation = camera.transform.rotation, type = camera.cameraType, scene = camera.scene, enabled = camera.enabled,
            sceneCullingMask = camera.overrideSceneCullingMask,
            additionalJson = EditorJsonUtility.ToJson(camera.GetComponent<UniversalAdditionalCameraData>()) };
    }
    private static void AssertCamera(CameraState before, Camera camera)
    {
        Require(EditorJsonUtility.ToJson(camera) == before.json, "all serialized camera properties restored");
        Require(camera.transform.position == before.position && Quaternion.Angle(camera.transform.rotation, before.rotation) < .001f,
            "camera transform restored");
        Require(camera.cameraType == before.type && camera.scene == before.scene && camera.enabled == before.enabled,
            "camera type, render scene and enabled state restored");
        Require(camera.overrideSceneCullingMask == before.sceneCullingMask, "camera scene culling mask restored");
        Require(EditorJsonUtility.ToJson(camera.GetComponent<UniversalAdditionalCameraData>()) == before.additionalJson,
            "all serialized URP camera data restored");
    }
    private static T Keep<T>(T value) where T : Object { value.hideFlags = HideFlags.HideAndDontSave; Owned.Add(value); return value; }
    private static void Near(Color expected, Color actual, float tolerance, string message)
    {
        float error = Mathf.Max(Mathf.Max(Mathf.Abs(expected.r - actual.r), Mathf.Abs(expected.g - actual.g)),
            Mathf.Max(Mathf.Abs(expected.b - actual.b), Mathf.Abs(expected.a - actual.a)));
        Require(!float.IsNaN(error) && error <= tolerance, message + " expected=" + expected + " actual=" + actual + " error=" + error);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(Report report, string name, Action operation)
    {
        try { operation(); report.passed++; report.details.Add("PASS " + name); }
        catch (Exception exception) { Failure(report, name, exception); }
    }
    private static void Failure(Report report, string name, Exception exception)
    {
        report.failed++;
        report.details.Add("FAIL " + name + ": " + exception.Message);
    }
}
