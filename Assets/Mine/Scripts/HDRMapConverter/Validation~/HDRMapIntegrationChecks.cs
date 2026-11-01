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
// Real sky captures and real quad/room renders. Source is an independent
// direction-color panorama; no production mapping function computes expected RGB.
public class Script
{
    [Serializable] public sealed class Report
    {
        public string suite = "HDRMapIntegrationChecks", graphicsDevice;
        public int passed, failed, skyCaptures, skyOraclePixels, interiorRenders, interiorComparisons, parallaxControls;
        public List<string> details = new List<string>();
    }
    private sealed class SceneState { public Scene scene; public bool dirty; }
    private static readonly List<Object> Owned = new List<Object>();
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3[] Axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

    public static object Main(string[] args) { return Run(args != null && args.Length > 0 ? args[0] : null); }
    public static string Run(string outputDirectory)
    {
        var report = new Report { graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName };
        var scenes = new List<SceneState>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            scenes.Add(new SceneState { scene = scene, dirty = scene.isDirty });
        }
        Scene oldActiveScene = SceneManager.GetActiveScene();
        Object[] oldSelection = Selection.objects;
        Material oldSky = RenderSettings.skybox;
        Light oldSun = RenderSettings.sun;
        Color oldAmbient = RenderSettings.ambientLight;
        float oldAmbientIntensity = RenderSettings.ambientIntensity;
        var oldAmbientMode = RenderSettings.ambientMode;
        var oldDefaultPipeline = GraphicsSettings.defaultRenderPipeline;
        var oldQualityPipeline = QualitySettings.renderPipeline;
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
            Camera camera;
            HDRMapBaker baker = MakeFramework(preview, out camera);
            Texture2D source = DirectionSource(256);
            Shader skyShader = Shader.Find("Skybox/HDRMap");
            Require(skyShader != null && skyShader.isSupported, "Native Skybox/HDRMap shader exists and is supported.");
            Material sky = Keep(new Material(skyShader));
            camera.gameObject.AddComponent<Skybox>().material = sky;
            var maps = new Texture2D[4];
            for (int pi = 0; pi < 4; pi++)
            {
                HDRMapProjection projection = (HDRMapProjection)pi;
                maps[pi] = Keep(HDRMapConverter.Convert(source, HDRMapProjection.Equirectangular, projection, 256));
                Check(report, "sky.native_shader." + projection + ".direction_oracle", () =>
                {
                    ConfigureSky(sky, maps[(int)projection], projection, 1f, 0f);
                    Texture2D capture = SkyCapture(baker, report);
                    CompareSky(capture, projection, 1f, 0f, report);
                });
            }
            Check(report, "sky.exposure_two_preserves_hdr_direction_colors", () =>
            {
                ConfigureSky(sky, maps[0], HDRMapProjection.Equirectangular, 2f, 0f);
                CompareSky(SkyCapture(baker, report), HDRMapProjection.Equirectangular, 2f, 0f, report);
            });
            Check(report, "sky.rotation_ninety_uses_rotated_direction", () =>
            {
                ConfigureSky(sky, maps[0], HDRMapProjection.Equirectangular, 1f, 90f);
                CompareSky(SkyCapture(baker, report), HDRMapProjection.Equirectangular, 1f, 90f, report);
            });

            Shader interiorShader = Shader.Find("Render/InteriorMapping");
            Require(interiorShader != null && interiorShader.isSupported, "Native room shader exists and is supported.");
            Material room = Keep(new Material(interiorShader));
            room.SetVector("_RoomSize", new Vector4(2f, 2f, 3f, 0f));
            room.SetVector("_WindowSize", new Vector4(1f, 1f, 0f, 0f));
            room.SetColor("_RoomTint", new Color(1f, 1f, 1f, 0f));
            room.SetColor("_FallbackColor", Color.black);
            room.SetFloat("_RoomBrightness", 1f);
            room.SetFloat("_MapCoverage", 1f);
            room.SetTextureScale("_InteriorMap", Vector2.one);
            room.SetTextureOffset("_InteriorMap", Vector2.zero);
            GameObject quad = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));
            SceneManager.MoveGameObjectToScene(quad, preview);
            quad.name = "HDR map integration window";
            quad.layer = 0; // Included by the project's PC Renderer opaque mask.
            quad.transform.localScale = new Vector3(2f, 2f, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = room;
            ConfigureInteriorCamera(camera);

            for (int shape = 0; shape < 2; shape++)
            {
                int roomShape = shape;
                Check(report, "interior.room_shape=" + roomShape + ".all_projections_agree_and_parallax_is_live", () =>
                {
                    room.SetFloat("_ProjectionType", roomShape);
                    room.SetFloat("_MapProjection", 0f);
                    room.SetTexture("_InteriorMap", maps[0]);
                    Color[] baseline = RenderInterior(camera, report);
                    Color center = baseline[64 * 128 + 64];
                    Require(center.b > .94f && Mathf.Abs(center.r - .5f) < .035f && Mathf.Abs(center.g - .5f) < .035f,
                        "room center samples +Z map direction, not fallback or another axis; actual=" + center);
                    for (int pi = 1; pi < 4; pi++)
                    {
                        room.SetFloat("_MapProjection", pi);
                        room.SetTexture("_InteriorMap", maps[pi]);
                        Color[] actual = RenderInterior(camera, report);
                        float rms, maximum;
                        CompareCentral(baseline, actual, out rms, out maximum);
                        Require(rms < .02f && maximum < .02f, "shape=" + roomShape + " projection=" + pi
                            + " differs from panorama baseline; RMS=" + rms + " max=" + maximum);
                        report.interiorComparisons++;
                        report.details.Add("metric.interior.shape=" + roomShape + ".projection=" + pi
                            + ".rms=" + rms.ToString("R") + ".max=" + maximum.ToString("R"));
                    }
                    room.SetFloat("_MapProjection", 0f);
                    room.SetTexture("_InteriorMap", maps[0]);
                    Vector3 savedPosition = camera.transform.position;
                    Matrix4x4 savedProjection = camera.projectionMatrix;
                    float originalViewCenter = camera.worldToCameraMatrix.MultiplyPoint3x4(Vector3.zero).x;
                    try
                    {
                        camera.transform.position += Vector3.right * .5f;
                        // Recenter only the orthographic projection. The same quad UVs
                        // stay on the same pixels while the physical observer moves.
                        // A plain UV texture must remain identical and fail this control.
                        float movedViewCenter = camera.worldToCameraMatrix.MultiplyPoint3x4(Vector3.zero).x;
                        Matrix4x4 recentered = savedProjection;
                        recentered.m03 -= recentered.m00 * (movedViewCenter - originalViewCenter);
                        camera.projectionMatrix = recentered;
                        Color[] moved = RenderInterior(camera, report);
                        float rms, maximum;
                        CompareCentral(baseline, moved, out rms, out maximum);
                        Require(rms > .012f && maximum > .025f,
                            "camera motion must change the room image; plain UV/static-color paths fail this control. RMS=" + rms + " max=" + maximum);
                        report.parallaxControls++;
                        report.details.Add("metric.interior.shape=" + roomShape + ".camera_motion_rms=" + rms.ToString("R")
                            + ".max=" + maximum.ToString("R"));
                    }
                    finally
                    {
                        camera.transform.position = savedPosition;
                        camera.projectionMatrix = savedProjection;
                    }
                });
            }
            Check(report, "shaders.native_sky_and_native_room_have_no_compiler_errors", () =>
            {
                NoShaderErrors(skyShader);
                NoShaderErrors(interiorShader);
            });
        }
        catch (Exception exception) { Failure(report, "fixture.unexpected_exception", exception); }
        finally
        {
            for (int i = Owned.Count - 1; i >= 0; i--)
            {
                if (Owned[i] == null) continue;
                RenderTexture rt = Owned[i] as RenderTexture;
                if (rt != null) rt.Release();
                Object.DestroyImmediate(Owned[i]);
            }
            Owned.Clear();
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            Selection.objects = oldSelection;
            Application.logMessageReceived -= listener;
        }
        Check(report, "cleanup.original_scenes_lighting_pipeline_and_render_state_unchanged", () =>
        {
            Require(SceneManager.GetActiveScene() == oldActiveScene && SceneManager.sceneCount == scenes.Count, "original scene selection/count");
            foreach (SceneState state in scenes)
                Require(state.scene.IsValid() && state.scene.isDirty == state.dirty, "original scene/dirty state " + state.scene.path);
            Require(RenderSettings.skybox == oldSky && RenderSettings.sun == oldSun && RenderSettings.ambientLight == oldAmbient
                && RenderSettings.ambientMode == oldAmbientMode && RenderSettings.ambientIntensity == oldAmbientIntensity,
                "global lighting settings unchanged");
            Require(GraphicsSettings.defaultRenderPipeline == oldDefaultPipeline && QualitySettings.renderPipeline == oldQualityPipeline,
                "original pipeline references unchanged");
            Require(RenderTexture.active == oldRT && GL.sRGBWrite == oldSRGBWrite, "original render state restored");
        });
        Check(report, "unity.no_unexpected_error_logs", () => Require(errors.Count == 0, string.Join(" | ", errors.ToArray())));
        Check(report, "suite.required_real_shader_work_executed", () =>
        {
            Require(report.skyCaptures == 6 && report.skyOraclePixels > 2000, "six actual sky captures and direction oracle comparisons");
            Require(report.interiorRenders == 10 && report.interiorComparisons == 6, "both room shapes and four real map projections rendered");
            Require(report.parallaxControls == 2, "both room shapes reject a static/no-viewpoint image");
        });
        string json = JsonUtility.ToJson(report, true);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "integration-report.json"), json);
        }
        if (report.failed != 0) throw new InvalidOperationException(json);
        return json;
    }

    private static HDRMapBaker MakeFramework(Scene preview, out Camera camera)
    {
        GameObject root = MakeObject(preview, "HDR integration framework");
        HDRMapBaker baker = root.AddComponent<HDRMapBaker>();
        GameObject volumeGO = MakeObject(preview, "BakeVolume");
        volumeGO.transform.SetParent(root.transform, false);
        HDRMapBakeVolume volume = volumeGO.AddComponent<HDRMapBakeVolume>();
        GameObject directionGO = MakeObject(preview, "BakeDirection");
        directionGO.transform.SetParent(root.transform, false);
        HDRMapBakeDirection direction = directionGO.AddComponent<HDRMapBakeDirection>();
        GameObject cameraGO = MakeObject(preview, "BakeCamera");
        cameraGO.transform.SetParent(root.transform, false);
        camera = cameraGO.AddComponent<Camera>();
        camera.enabled = false;
        camera.scene = preview;
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
        camera.allowHDR = true;
        camera.allowMSAA = false;
        UniversalAdditionalCameraData data = cameraGO.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.allowXRRendering = false;
        data.allowHDROutput = false;
        data.antialiasing = AntialiasingMode.None;
        data.volumeLayerMask = 0;
        typeof(HDRMapBaker).GetField("_volume", Private).SetValue(baker, volume);
        typeof(HDRMapBaker).GetField("_direction", Private).SetValue(baker, direction);
        typeof(HDRMapBaker).GetField("_camera", Private).SetValue(baker, camera);
        typeof(HDRMapBaker).GetField("_renderLayers", Private).SetValue(baker, (LayerMask)0);
        typeof(HDRMapBaker).GetField("_includeSky", Private).SetValue(baker, true);
        return baker;
    }
    private static GameObject MakeObject(Scene preview, string name)
    {
        GameObject go = Keep(new GameObject(name));
        SceneManager.MoveGameObjectToScene(go, preview);
        return go;
    }
    private static void ConfigureSky(Material material, Texture map, HDRMapProjection projection, float exposure, float rotation)
    {
        material.SetTexture("_HDRMap", map);
        material.SetFloat("_MapProjection", (float)projection);
        material.SetFloat("_MapCoverage", 0f);
        material.SetFloat("_Exposure", exposure);
        material.SetFloat("_Rotation", rotation);
    }
    private static Texture2D SkyCapture(HDRMapBaker baker, Report report)
    {
        Texture2D panorama = Keep(HDRMapCapture.Bake(baker, 128));
        report.skyCaptures++;
        return panorama;
    }
    private static void CompareSky(Texture2D capture, HDRMapProjection projection, float exposure, float rotation, Report report)
    {
        Color[] pixels = capture.GetPixels();
        Quaternion rotate = Quaternion.Euler(0f, rotation, 0f);
        float maximum = 0f;
        for (int y = 3; y < capture.height; y += 6)
        for (int x = 3; x < capture.width; x += 6)
        {
            Vector3 direction = rotate * PanoramaDirection((x + .5f) / capture.width, (y + .5f) / capture.height);
            if (projection == HDRMapProjection.Hemisphere && Mathf.Abs(direction.z) < .25f) continue;
            Color expected = projection == HDRMapProjection.Hemisphere && direction.z < 0 ? Color.black : DirectionColor(direction) * exposure;
            expected.a = 1f;
            Color actual = pixels[y * capture.width + x];
            float error = ColorError(expected, actual);
            maximum = Mathf.Max(maximum, error);
            Require(error < .025f, "Sky direction oracle " + projection + " exp=" + exposure + " rotation=" + rotation
                + " pixel=" + x + "," + y + " dir=" + direction + " expected=" + expected + " actual=" + actual + " error=" + error);
            report.skyOraclePixels++;
        }
        foreach (Vector3 axis in Axes)
        {
            Vector3 direction = rotate * axis;
            if (projection == HDRMapProjection.Hemisphere && Mathf.Abs(direction.z) < .25f) continue;
            Color expected = projection == HDRMapProjection.Hemisphere && direction.z < 0 ? Color.black : DirectionColor(direction) * exposure;
            expected.a = 1f;
            float u = Mathf.Atan2(axis.x, axis.z) / (2f * Mathf.PI) + .5f;
            float v = Mathf.Asin(axis.y) / Mathf.PI + .5f;
            Color actual = capture.GetPixelBilinear(u, v);
            Require(ColorError(expected, actual) < .025f, "Sky axis " + axis + " expected=" + expected + " actual=" + actual);
            report.skyOraclePixels++;
        }
        report.details.Add("metric.sky." + projection + ".exposure=" + exposure + ".rotation=" + rotation + ".max=" + maximum.ToString("R"));
    }
    private static void ConfigureInteriorCamera(Camera camera)
    {
        camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, 2f), Quaternion.LookRotation(Vector3.back, Vector3.up));
        camera.cullingMask = 1;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.orthographic = true;
        camera.orthographicSize = 1.1f;
        camera.aspect = 1f;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 10f;
        camera.rect = new Rect(0, 0, 1, 1);
        camera.ResetWorldToCameraMatrix();
        camera.ResetProjectionMatrix();
    }
    private static Color[] RenderInterior(Camera camera, Report report)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture target = null;
        Texture2D readback = null;
        try
        {
            target = RenderTexture.GetTemporary(128, 128, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "2D URP render requests supported.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            readback = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 128, 128), 0, 0, false);
            readback.Apply(false, false);
            report.interiorRenders++;
            return readback.GetPixels();
        }
        finally
        {
            RenderTexture.active = previous;
            if (readback != null) Object.DestroyImmediate(readback);
            if (target != null) RenderTexture.ReleaseTemporary(target);
        }
    }
    private static void CompareCentral(Color[] expected, Color[] actual, out float rms, out float maximum)
    {
        double squared = 0;
        int channels = 0;
        maximum = 0f;
        // Same screen pixels, safely inside the quad in both camera positions.
        for (int y = 32; y < 96; y++)
        for (int x = 32; x < 96; x++)
        {
            Color a = expected[y * 128 + x], b = actual[y * 128 + x];
            Require(!float.IsNaN(b.r) && !float.IsNaN(b.g) && !float.IsNaN(b.b), "finite real interior image");
            maximum = Mathf.Max(maximum, ColorError(a, b));
            squared += (a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b);
            channels += 3;
        }
        rms = (float)Math.Sqrt(squared / channels);
    }
    private static Texture2D DirectionSource(int height)
    {
        Texture2D source = Keep(new Texture2D(height * 2, height, TextureFormat.RGBAFloat, false, true));
        var pixels = new Color[source.width * source.height];
        for (int y = 0; y < source.height; y++)
        for (int x = 0; x < source.width; x++)
            pixels[y * source.width + x] = DirectionColor(PanoramaDirection((x + .5f) / source.width, (y + .5f) / source.height));
        source.SetPixels(pixels);
        source.Apply(false, false);
        source.wrapModeU = TextureWrapMode.Repeat;
        source.wrapModeV = TextureWrapMode.Clamp;
        return source;
    }
    private static Vector3 PanoramaDirection(float u, float v)
    {
        double latitude = (v - .5) * Math.PI, longitude = (u - .5) * 2 * Math.PI;
        return new Vector3((float)(Math.Sin(longitude) * Math.Cos(latitude)), (float)Math.Sin(latitude),
            (float)(Math.Cos(longitude) * Math.Cos(latitude)));
    }
    private static Color DirectionColor(Vector3 d) { return new Color(d.x * .5f + .5f, d.y * .5f + .5f, d.z * .5f + .5f, 1f); }
    private static float ColorError(Color a, Color b)
    {
        return Mathf.Max(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g)), Mathf.Max(Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a)));
    }
    private static void NoShaderErrors(Shader shader)
    {
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
            Require(message.severity.ToString() != "Error", shader.name + ": " + message.message + " @ " + message.file + ":" + message.line);
    }
    private static T Keep<T>(T value) where T : Object { value.hideFlags = HideFlags.HideAndDontSave; Owned.Add(value); return value; }
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
