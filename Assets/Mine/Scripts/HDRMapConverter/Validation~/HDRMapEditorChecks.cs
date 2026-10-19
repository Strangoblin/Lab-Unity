using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Mine.HDRMaps;
using Mine.HDRMaps.Editor;
using Object = UnityEngine.Object;

// unityctl script execute <this-file> <output-directory>
// Hidden window instances and a disposable additive scene. No Show, dialogs or persistent writes.
public class Script
{
    [Serializable] public sealed class Report
    {
        public string suite = "HDRMapEditorChecks";
        public int passed, failed;
        public List<string> details = new List<string>();
    }
    private sealed class SceneState { public Scene scene; public bool dirty; }
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<Object> Owned = new List<Object>();

    public static object Main(string[] args)
    {
        return Run(args != null && args.Length > 0 ? args[0] : null);
    }

    public static string Run(string outputDirectory)
    {
        var report = new Report();
        var scenes = new List<SceneState>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            scenes.Add(new SceneState { scene = scene, dirty = scene.isDirty });
        }
        Scene oldActive = SceneManager.GetActiveScene();
        Object[] oldSelection = Selection.objects;
        Material oldSky = RenderSettings.skybox;
        var oldPipeline = GraphicsSettings.defaultRenderPipeline;
        var oldQualityPipeline = QualitySettings.renderPipeline;
        RenderTexture oldTarget = RenderTexture.active;
        Scene temporary = default(Scene);
        int undoGroup = -1;
        var errors = new List<string>();
        Application.LogCallback listener = (message, trace, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(type + ": " + message);
        };
        Application.logMessageReceived += listener;

        try
        {
            Check(report, "windows.baker_to_converter_is_independent_hdr_snapshot", () =>
            {
                HDRMapBakerWindow baker = Window<HDRMapBakerWindow>();
                HDRMapConverterWindow converter = Window<HDRMapConverterWindow>();
                Texture2D source = Texture("baker panorama", new Color(2f, .25f, 4f, .4f));
                Invoke(baker, "SetAsset", source, true);
                Invoke(converter, "AssignSource", source);
                Texture2D snapshot = Field<Texture2D>(converter, "_source");
                Require(snapshot != null && snapshot.GetInstanceID() != source.GetInstanceID(), "distinct converter source instance");
                Require(snapshot == Field<Texture2D>(converter, "_ownedSource"), "snapshot ownership is explicit");
                Require(snapshot.format == TextureFormat.RGBAHalf && snapshot.width == source.width && snapshot.height == source.height,
                    "HDR format and dimensions preserved");
                Near(source.GetPixel(1, 1), snapshot.GetPixel(1, 1), .001f, "snapshot radiance and alpha");
                source.SetPixel(1, 1, Color.black);
                source.Apply(false, false);
                Near(new Color(2f, .25f, 4f, .4f), snapshot.GetPixel(1, 1), .001f, "original mutation cannot affect snapshot");
                Object.DestroyImmediate(baker);
                Require(source == null, "closing baker releases its owned original");
                Require(snapshot != null, "converter snapshot survives baker close");
                Near(new Color(2f, .25f, 4f, .4f), snapshot.GetPixel(1, 1), .001f, "snapshot remains readable after original destruction");
                Object.DestroyImmediate(converter);
                Require(snapshot == null, "closing converter releases snapshot");
            });

            Check(report, "windows.converter_source_replacement_and_release", () =>
            {
                HDRMapConverterWindow window = Window<HDRMapConverterWindow>();
                Texture2D first = Texture("first external source", Color.red);
                Texture2D second = Texture("second external source", Color.blue);
                Invoke(window, "AssignSource", first);
                Texture2D firstSnapshot = Field<Texture2D>(window, "_source");
                Invoke(window, "AssignSource", second);
                Texture2D secondSnapshot = Field<Texture2D>(window, "_source");
                Require(firstSnapshot == null, "replacing source releases old owned snapshot");
                Require(first != null && second != null, "caller source instances remain alive");
                Require(secondSnapshot != null && secondSnapshot != second, "second source receives its own snapshot");
                int snapshotId = secondSnapshot.GetInstanceID();
                Invoke(window, "AssignSource", secondSnapshot);
                Require(Field<Texture2D>(window, "_source").GetInstanceID() == snapshotId, "reassigning current snapshot is a no-op");
                Invoke(window, "ReleaseSource");
                Require(secondSnapshot == null && Field<Texture2D>(window, "_source") == null
                    && Field<Texture2D>(window, "_ownedSource") == null, "explicit source release clears ownership");
                Require(second != null, "explicit source release retains caller original");
            });

            Check(report, "windows.converter_close_releases_owned_source_and_output", () =>
            {
                HDRMapConverterWindow window = Window<HDRMapConverterWindow>();
                Texture2D source = Texture("caller source", Color.white);
                Texture2D output = Texture("converter owned output", Color.green);
                Invoke(window, "AssignSource", source);
                Texture2D snapshot = Field<Texture2D>(window, "_ownedSource");
                Invoke(window, "SetAsset", output, true);
                Object.DestroyImmediate(window);
                Require(snapshot == null && output == null, "OnDisable destroys both owned objects");
                Require(source != null, "OnDisable retains caller source");
            });

            foreach (Type windowType in new[] { typeof(HDRMapBakerWindow), typeof(HDRMapConverterWindow) })
            {
                Check(report, "windows." + windowType.Name + ".replacement_releases_only_owned_asset", () =>
                {
                    EditorWindow window = (EditorWindow)Keep(ScriptableObject.CreateInstance(windowType));
                    Texture2D owned = Texture("owned preview", Color.red);
                    Texture2D external = Texture("externally managed preview", Color.blue);
                    Invoke(window, "SetAsset", owned, true);
                    Require(Field<bool>(window, "_ownsAsset"), "owned flag set");
                    Invoke(window, "SetAsset", external, false);
                    Require(owned == null && external != null, "replacement releases owned old preview");
                    Require(!Field<bool>(window, "_ownsAsset"), "nonowned flag set");
                    Invoke(window, "ReleaseAsset");
                    Require(external != null && Field<Object>(window, "_asset") == null, "explicit release retains nonowned object");
                    Invoke(window, "SetAsset", external, false);
                    Object.DestroyImmediate(window);
                    Require(external != null, "OnDisable retains nonowned object");
                });

                Check(report, "windows." + windowType.Name + ".asset_path_and_type_guards", () =>
                {
                    EditorWindow window = (EditorWindow)Keep(ScriptableObject.CreateInstance(windowType));
                    Type helper = windowType.GetNestedType("AssetFiles", BindingFlags.NonPublic);
                    Require(helper != null, "path helper exists");
                    MethodInfo valid = helper.GetMethod("IsValidAssetPath", BindingFlags.Public | BindingFlags.Static);
                    foreach (string invalid in new[] { "", " ", "/tmp/test.asset", "../Assets/test.asset", "Assets/../test.asset",
                        "Assets/./test.asset", "Assets/test.png", "Packages/test.asset" })
                        Require(!(bool)valid.Invoke(null, new object[] { invalid }), "invalid path rejected: " + invalid);
                    Require((bool)valid.Invoke(null, new object[] { "Assets/Textures/HDRTest.asset" }), "valid asset path recognized without writing");
                    Cubemap unsupported = Keep(new Cubemap(4, TextureFormat.RGBAHalf, false));
                    Invoke(window, "SetAsset", unsupported, false);
                    Require(!(bool)windowType.GetProperty("HasSaveableAsset", Private).GetValue(window, null), "Cubemap cannot enter Texture2D Save action");
                    Texture2D accepted = Texture("valid texture type", Color.white);
                    Invoke(window, "SetAsset", accepted, false);
                    Require((bool)windowType.GetProperty("HasSaveableAsset", Private).GetValue(window, null), "Texture2D is saveable");
                });
            }

            Check(report, "windows.converter_nonowned_source_is_not_destroyed", () =>
            {
                HDRMapConverterWindow window = Window<HDRMapConverterWindow>();
                Texture2D externallyManaged = Texture("external source ownership fixture", Color.cyan);
                SetField(window, "_source", externallyManaged);
                SetField(window, "_ownedSource", null);
                Invoke(window, "ReleaseSource");
                Require(externallyManaged != null && Field<Texture2D>(window, "_source") == null, "nonowned release preserves original");
                SetField(window, "_source", externallyManaged);
                Object.DestroyImmediate(window);
                Require(externallyManaged != null, "nonowned source survives close");
            });

            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Temporary HDR Map Editor Validation");
            temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            // NewScene may already activate the scene. The return value of a
            // redundant SetActiveScene call is not the active-scene invariant.
            if (SceneManager.GetActiveScene() != temporary)
                SceneManager.SetActiveScene(temporary);
            Require(temporary.IsValid() && temporary.isLoaded && SceneManager.GetActiveScene() == temporary,
                "disposable additive scene is active; target=" + temporary.handle + ", loaded=" + temporary.isLoaded
                + ", actual=" + SceneManager.GetActiveScene().handle);
            HDRMapBaker framework = MakeBaker<HDRMapBaker>("HDR framework fixture");
            HDRMapBaker other = MakeBaker<HDRMapBaker>("Other HDR framework fixture");

            Check(report, "framework.initialize_is_idempotent_and_camera_is_dedicated", () =>
            {
                HDRMapBakerEditorUtility.InitializeFramework(framework);
                Require(framework.IsInitialized && framework.transform.childCount == 3, "three initialized dedicated children");
                int cameraId = framework.BakeCamera.GetInstanceID();
                Scene renderSceneOverride = framework.BakeCamera.scene;
                HDRMapBakerEditorUtility.InitializeFramework(framework);
                Require(framework.transform.childCount == 3 && framework.BakeCamera.GetInstanceID() == cameraId, "repair reuses existing objects");
                Require(!framework.BakeCamera.enabled && framework.BakeCamera.cameraType == CameraType.Reflection, "camera remains disabled reflection type");
                Require(framework.gameObject.scene == temporary && framework.BakeCamera.gameObject.scene == temporary
                    && framework.Volume.gameObject.scene == temporary && framework.Direction.gameObject.scene == temporary,
                    "framework and all owned GameObjects belong to temporary scene");
                Require(framework.BakeCamera.scene == renderSceneOverride,
                    "repair preserves Camera.scene rendering override independently from GameObject ownership");
            });

            Check(report, "framework.cross_baker_references_are_rejected_and_repaired", () =>
            {
                HDRMapBakerEditorUtility.InitializeFramework(other);
                Camera otherCamera = other.BakeCamera;
                string otherCameraBefore = EditorJsonUtility.ToJson(otherCamera);
                var serialized = new SerializedObject(framework);
                serialized.FindProperty("_volume").objectReferenceValue = other.Volume;
                serialized.FindProperty("_direction").objectReferenceValue = other.Direction;
                serialized.FindProperty("_camera").objectReferenceValue = otherCamera;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Require(!framework.IsInitialized && !framework.Owns(otherCamera), "cross-framework binding is invalid");
                HDRMapBakerEditorUtility.InitializeFramework(framework);
                Require(framework.IsInitialized && framework.BakeCamera != otherCamera, "repair restores dedicated ownership");
                Require(framework.transform.childCount == 3 && other.transform.childCount == 3, "repair adds no duplicate children");
                Require(EditorJsonUtility.ToJson(otherCamera) == otherCameraBefore && other.BakeCamera == otherCamera,
                    "repair does not mutate the other baker camera");
            });

            Check(report, "framework.legacy_private_fields_and_guids_remain_compatible", () =>
            {
                var bakeParameters = typeof(InteriorMapTextureBaker).GetMethod("Bake").GetParameters();
                Require(bakeParameters[1].HasDefaultValue && (int)bakeParameters[1].DefaultValue == 256,
                    "legacy Bake(framework) keeps its default face resolution");
                InteriorMapBaker legacy = MakeBaker<InteriorMapBaker>("Legacy framework fixture");
                AddChild<InteriorMapBakeVolume>(legacy.transform, "BakeVolume");
                AddChild<InteriorMapBakeDirection>(legacy.transform, "BakeDirection");
                Camera camera = AddChild<Camera>(legacy.transform, "BakeCamera");
                camera.enabled = false;
                HDRMapBakerEditorUtility.InitializeFramework(legacy);
                Require(legacy.IsInitialized && legacy.Volume is InteriorMapBakeVolume && legacy.Direction is InteriorMapBakeDirection,
                    "new utility reuses old derived components");
                var serialized = new SerializedObject(legacy);
                foreach (string name in new[] { "_volume", "_direction", "_camera", "_renderLayers", "_includeSky" })
                    Require(serialized.FindProperty(name) != null, "inherited serialized field resolves: " + name);
                Require(serialized.FindProperty("_renderLayers").intValue == -1
                    && serialized.FindProperty("_includeSky").boolValue, "new capture defaults survive legacy inheritance");
                Require(new SerializedObject(legacy.Volume).FindProperty("_size") != null
                    && new SerializedObject(legacy.Direction).FindProperty("_originOffset") != null, "legacy geometry fields retained");
                AssertScriptGuid(legacy, "fb0c464000f6545c2a96cceacdba9f8c");
                AssertScriptGuid(legacy.Volume, "0a98b9de5888b4eaf8f07d3f049c47ac");
                AssertScriptGuid(legacy.Direction, "3d7528a74d76e4a29aaeb18d54bceed6");
            });
        }
        catch (Exception exception) { Failure(report, "fixture.unexpected_exception", exception); }
        finally
        {
            for (int i = Owned.Count - 1; i >= 0; i--)
                if (Owned[i] != null) Object.DestroyImmediate(Owned[i]);
            Owned.Clear();
            if (undoGroup >= 0)
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(undoGroup);
            }
            if (oldActive.IsValid()) SceneManager.SetActiveScene(oldActive);
            if (temporary.IsValid()) EditorSceneManager.CloseScene(temporary, true);
            Selection.objects = oldSelection;
            Application.logMessageReceived -= listener;
        }

        Check(report, "cleanup.original_scenes_and_selection_unchanged", () =>
        {
            Require(SceneManager.sceneCount == scenes.Count && SceneManager.GetActiveScene() == oldActive, "loaded scenes and active scene restored");
            foreach (SceneState state in scenes)
                Require(state.scene.IsValid() && state.scene.isDirty == state.dirty, "original scene dirty flag retained: " + state.scene.path);
            Object[] selection = Selection.objects;
            Require(selection.Length == oldSelection.Length, "selection count restored");
            for (int i = 0; i < selection.Length; i++) Require(selection[i] == oldSelection[i], "selection entry restored");
        });
        Check(report, "cleanup.render_and_pipeline_settings_unchanged", () =>
            Require(RenderTexture.active == oldTarget && RenderSettings.skybox == oldSky
                && GraphicsSettings.defaultRenderPipeline == oldPipeline && QualitySettings.renderPipeline == oldQualityPipeline,
                "render target, active-scene sky and pipeline references preserved"));
        Check(report, "unity.no_unexpected_error_logs", () => Require(errors.Count == 0, string.Join(" | ", errors.ToArray())));
        Check(report, "suite.required_editor_checks_executed", () => Require(report.passed + report.failed >= 14, "at least fourteen substantive checks executed"));

        string json = JsonUtility.ToJson(report, true);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "editor-report.json"), json);
        }
        if (report.failed != 0) throw new InvalidOperationException(json);
        return json;
    }

    private static T Window<T>() where T : EditorWindow { return Keep(ScriptableObject.CreateInstance<T>()); }
    private static Texture2D Texture(string name, Color color)
    {
        Texture2D texture = Keep(new Texture2D(8, 4, TextureFormat.RGBAHalf, false, true));
        texture.name = name;
        var pixels = new Color[32];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }
    private static T MakeBaker<T>(string name) where T : HDRMapBaker
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create temporary HDR fixture");
        return Undo.AddComponent<T>(go);
    }
    private static T AddChild<T>(Transform parent, string name) where T : Component
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create temporary HDR child");
        Undo.SetTransformParent(go.transform, parent, "Parent temporary HDR child");
        return Undo.AddComponent<T>(go);
    }
    private static void AssertScriptGuid(MonoBehaviour component, string expected)
    {
        MonoScript script = MonoScript.FromMonoBehaviour(component);
        Require(script != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script)) == expected,
            "legacy component retained original script GUID: " + component.GetType().Name);
    }
    private static object Invoke(object target, string name, params object[] arguments)
    {
        try { return target.GetType().GetMethod(name, Private).Invoke(target, arguments); }
        catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
    }
    private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Private).GetValue(target); }
    private static void SetField(object target, string name, object value) { target.GetType().GetField(name, Private).SetValue(target, value); }
    private static T Keep<T>(T value) where T : Object { value.hideFlags = HideFlags.HideAndDontSave; Owned.Add(value); return value; }
    private static void Near(Color expected, Color actual, float tolerance, string label)
    {
        float error = Mathf.Max(Mathf.Max(Mathf.Abs(expected.r - actual.r), Mathf.Abs(expected.g - actual.g)),
            Mathf.Max(Mathf.Abs(expected.b - actual.b), Mathf.Abs(expected.a - actual.a)));
        Require(error <= tolerance, label + " error=" + error);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(Report report, string name, Action action)
    {
        try { action(); report.passed++; report.details.Add("PASS " + name); }
        catch (Exception exception) { Failure(report, name, exception); }
    }
    private static void Failure(Report report, string name, Exception exception)
    {
        report.failed++;
        report.details.Add("FAIL " + name + ": " + exception.Message);
    }
}
