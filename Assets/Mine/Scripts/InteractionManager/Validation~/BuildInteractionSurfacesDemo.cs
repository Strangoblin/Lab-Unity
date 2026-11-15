// ════════════════════════════════════════════════════════════════
//  Build InteractionSurfacesDemo — 草地 / 雪地 / 沙地 三子集共用一个交互系统
// ════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mine.Fields;
using Mine.Grass;
using Mine.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 搭建「一套正交交互捕获 + 三个表面子集」的验证场景：
///   GrassInteractionProcessor / SnowInteractionProcessor / SandInteractionProcessor
/// 共用同一个 UniversalInteractionManager、同一台交互相机和同一个跟随探针。
/// 场景保存为 Assets/Mine/Scripts/InteractionManager/InteractionSurfacesDemo.unity。
/// 不修改当前打开场景的内容。
/// </summary>
public class Script
{
    private const string Folder          = "Assets/Mine/Scripts/InteractionManager";
    private const string DemoFolder      = Folder + "/Demo";
    private const string ScenePath       = Folder + "/InteractionSurfacesDemo.unity";
    private const string ProbeMaterial   = DemoFolder + "/Materials/SurfaceProbe.mat";
    private const string SnowMaterial    = DemoFolder + "/Materials/SnowGround.mat";
    private const string SandMaterial    = DemoFolder + "/Materials/SandGround.mat";
    private const string GrassMaterial   = DemoFolder + "/Materials/GrassGround.mat";
    private const string PatchMeshPath   = DemoFolder + "/Meshes/InteractionPatch.asset";

    private const string GrassComputePath = Folder + "/Grass/GrassInteraction.compute";
    private const string SnowComputePath  = Folder + "/Snow/SnowInteraction.compute";
    private const string SandComputePath  = Folder + "/Sand/SandInteraction.compute";
    private const string WindComputePath  = "Assets/Mine/Scripts/FieldManager/Wind/WindField.compute";
    private const string GrassMeshPath    = "Assets/Mine/Scripts/GrassManager/Grass/Normalized Grass.fbx";
    private const string GrassWindPath    = "Assets/Mine/Scripts/GrassManager/Grass/GrassWind.mat";

    private const int SurfaceLayer    = 6;   // Interaction: 既是可见表面，也是深度参照
    private const int InteractorLayer = 7;
    private const int RendererIndex   = 1;   // IA_Renderer

    private const float PatchSize = 12f;
    private const float PatchSpacing = 12f;   // 三块相邻拼接，探针全程都有地面参照
    private const float ProbeY = 0.30f;

    public static object Main(string[] args)
    {
        Scene previousActive = EditorSceneManager.GetActiveScene();
        Scene scene = default;
        var suspended = new List<(Behaviour behaviour, bool enabled)>();
        var sb = new StringBuilder();

        try
        {
            EnsureFolder(DemoFolder + "/Materials");
            EnsureFolder(DemoFolder + "/Meshes");

            ComputeShader grassCompute = Load<ComputeShader>(GrassComputePath);
            ComputeShader snowCompute  = Load<ComputeShader>(SnowComputePath);
            ComputeShader sandCompute  = Load<ComputeShader>(SandComputePath);
            ComputeShader windCompute  = Load<ComputeShader>(WindComputePath);
            Require(grassCompute != null, "Missing " + GrassComputePath);
            Require(snowCompute != null,  "Missing " + SnowComputePath);
            Require(sandCompute != null,  "Missing " + SandComputePath);
            Require(windCompute != null,  "Missing " + WindComputePath);

            Mesh grassMesh = AssetDatabase.LoadAllAssetsAtPath(GrassMeshPath).OfType<Mesh>()
                .FirstOrDefault(mesh => mesh.vertexCount > 0);
            Material grassWind = Load<Material>(GrassWindPath);
            Require(grassMesh != null, "Missing grass mesh at " + GrassMeshPath);
            Require(grassWind != null, "Missing " + GrassWindPath);

            // ── 材质 ─────────────────────────────────────────────
            Material snowGround = EnsureMaterial(SnowMaterial, "InteractionManager/SnowGround");
            snowGround.SetColor("_BaseColor", new Color(0.93f, 0.96f, 1.00f, 1f));
            snowGround.SetColor("_ShadowTint", new Color(0.47f, 0.61f, 0.82f, 1f));
            snowGround.SetFloat("_DepthScale", 0.22f);
            snowGround.SetFloat("_NormalStrength", 0.9f);
            snowGround.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(snowGround);

            Material sandGround = EnsureMaterial(SandMaterial, "InteractionManager/SandGround");
            sandGround.SetColor("_BaseColor", new Color(0.80f, 0.66f, 0.40f, 1f));
            sandGround.SetColor("_ShadowTint", new Color(0.42f, 0.33f, 0.20f, 1f));
            sandGround.SetFloat("_DepthScale", 0.32f);
            sandGround.SetFloat("_NormalStrength", 1.0f);
            sandGround.SetFloat("_Smoothness", 0.18f);
            sandGround.SetFloat("_RippleScale", 2.2f);
            sandGround.SetFloat("_RippleStrength", 0.08f);
            EditorUtility.SetDirty(sandGround);

            Material grassGround = EnsureMaterial(GrassMaterial, "InteractionManager/GrassGround");
            grassGround.SetColor("_BaseColor", new Color(0.22f, 0.30f, 0.13f, 1f));
            grassGround.SetColor("_ShadowTint", new Color(0.09f, 0.14f, 0.07f, 1f));
            grassGround.SetFloat("_Smoothness", 0.08f);
            grassGround.SetFloat("_PatchScale", 1.2f);
            grassGround.SetFloat("_PatchStrength", 0.15f);
            EditorUtility.SetDirty(grassGround);

            Material probeMaterial = EnsureMaterial(ProbeMaterial, "InteractionManager/InteractorObject");
            probeMaterial.SetFloat("_Intensity", 0.4f);
            probeMaterial.SetColor("_BaseColor", new Color(1f, 0.45f, 0.15f, 1f));
            EditorUtility.SetDirty(probeMaterial);

            Mesh patchMesh = EnsurePatchMesh(PatchMeshPath, PatchSize, 96);

            // ── 场景 ─────────────────────────────────────────────
            // Manager / Processor 的注册表是全局静态的：别的场景里已启用的交互对象会
            // 抢走「唯一活动 Manager」并把旧 originRT 挂到我们的处理器上。
            // 构建期间先挂起它们，保存并关闭新场景后再原样恢复。
            suspended = SuspendForeignInteraction();

            Scene existing = EditorSceneManager.GetSceneByPath(ScenePath);
            if (existing.IsValid() && existing.isLoaded)
                EditorSceneManager.CloseScene(existing, true);

            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);

            // 光照
            var lightGO = new GameObject("Directional Light");
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            RenderSettings.sun = light;

            // 主相机
            // GrassChunkManager 只生成相机所在 3×3 chunk 邻域内的草，相机必须落在
            // 草地所在的 chunk 窗口内（地形原点 z = -6、chunk 6 m → 相机 z ≥ -12）。
            var mainGO = new GameObject("Main Camera") { tag = "MainCamera" };
            mainGO.transform.SetPositionAndRotation(new Vector3(0f, 12f, -12f), Quaternion.identity);
            mainGO.transform.LookAt(new Vector3(0f, 0f, 0f));
            var mainCamera = mainGO.AddComponent<Camera>();
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.055f, 0.075f, 0.10f, 1f);
            mainCamera.farClipPlane = 200f;
            mainCamera.cullingMask = -1;
            var mainAdditional = mainCamera.GetUniversalAdditionalCameraData();
            mainAdditional.SetRenderer(0);
            mainAdditional.renderPostProcessing = false;

            // ── 共享交互探针 ─────────────────────────────────────
            var probeGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            probeGO.name = "InteractorProbe";
            probeGO.layer = InteractorLayer;
            probeGO.transform.position = new Vector3(-PatchSpacing, ProbeY, 0f);
            probeGO.transform.localScale = Vector3.one * 1.4f;   // 半径 0.7，底部 -0.4 m
            probeGO.GetComponent<MeshRenderer>().sharedMaterial = probeMaterial;
            Object.DestroyImmediate(probeGO.GetComponent<Collider>());
            var driver = probeGO.AddComponent<InteractionProbeDriver>();
            SetSerializedVector3(driver, "_start", new Vector3(-PatchSpacing, ProbeY, 0f));
            SetSerializedVector3(driver, "_end",   new Vector3( PatchSpacing, ProbeY, 0f));
            SetSerializedFloat(driver, "_speed", 0.12f);
            SetSerializedFloat(driver, "_sway", 1.2f);

            // ── 三块表面 ─────────────────────────────────────────
            BuildSnowSandPatch("SnowPatch", -PatchSpacing, patchMesh, snowGround);
            BuildSnowSandPatch("SandPatch",  PatchSpacing, patchMesh, sandGround);
            BuildGrassPatch("GrassPatch", grassGround, grassMesh, grassWind, mainCamera);

            // ── 交互总控 + 三个子集 ───────────────────────────────
            var managerGO = new GameObject("Interaction Manager");

            var grassProcessor = managerGO.AddComponent<GrassInteractionProcessor>();
            SetSerialized(grassProcessor, "_computeShader", grassCompute);
            SetSerializedFloat(grassProcessor, "_springBack", 0.55f);

            var snowProcessor = managerGO.AddComponent<SnowInteractionProcessor>();
            SetSerialized(snowProcessor, "_computeShader", snowCompute);
            SetSerializedFloat(snowProcessor, "_creep", 0.25f);    // 雪印坑壁慢慢摊圆
            SetSerializedFloat(snowProcessor, "_settle", 0.2f);    // 雪印随时间变浅（约 10 s 减半）

            var sandProcessor = managerGO.AddComponent<SandInteractionProcessor>();
            SetSerialized(sandProcessor, "_computeShader", sandCompute);
            SetSerializedFloat(sandProcessor, "_rim", 0.7f);       // 沙子被挤到坑沿堆起
            SetSerializedFloat(sandProcessor, "_repose", 0.5f);    // 坑壁最陡约 30°
            SetSerializedFloat(sandProcessor, "_settle", 0.25f);   // 沙痕比雪痕消失得快

            var interactionCameraGO = new GameObject("Interaction Camera");
            interactionCameraGO.transform.SetParent(managerGO.transform, false);
            var interactionCamera = interactionCameraGO.AddComponent<Camera>();
            interactionCamera.orthographic = true;
            interactionCamera.orthographicSize = PatchSize * 0.5f;
            interactionCamera.nearClipPlane = -12f;
            interactionCamera.farClipPlane = 12f;
            interactionCamera.clearFlags = CameraClearFlags.SolidColor;
            interactionCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            interactionCamera.cullingMask = (1 << SurfaceLayer) | (1 << InteractorLayer);
            interactionCamera.depth = -10f;
            interactionCamera.allowHDR = false;
            interactionCamera.allowMSAA = false;
            interactionCamera.useOcclusionCulling = false;
            var interactionAdditional = interactionCamera.GetUniversalAdditionalCameraData();
            interactionAdditional.SetRenderer(RendererIndex);
            interactionAdditional.renderPostProcessing = false;
            interactionAdditional.renderShadows = false;

            var manager = managerGO.AddComponent<UniversalInteractionManager>();
            SetSerialized(manager, "_orthoCamera", interactionCamera);
            SetSerialized(manager, "_followTarget", probeGO.transform);
            SetSerializedFloat(manager, "_areaSize", PatchSize);
            SetSerializedFloat(manager, "_orthoHeight", 8f);
            SetSerializedFloat(manager, "_orthoNear", -12f);
            SetSerializedFloat(manager, "_orthoFar", 12f);
            SetSerializedInt(manager, "_resolution", 256);
            SetSerializedBool(manager, "_editorPreview", true);

            // 重新走一遍 OnEnable：此时相机 / 跟随目标 / 处理器都已就位。
            manager.enabled = false;
            manager.enabled = true;

            // ── 草的风场 ─────────────────────────────────────────
            var fieldGO = new GameObject("Field Manager");
            fieldGO.SetActive(false);
            fieldGO.AddComponent<FieldManager>();
            var windGO = new GameObject("Wind");
            windGO.transform.SetParent(fieldGO.transform, false);
            var wind = windGO.AddComponent<WindFieldProvider>();
            SetSerialized(wind, "_computeShader", windCompute);
            SetSerialized(wind, "_followTarget", mainCamera.transform);
            SetSerializedFloat(wind, "_lowerHeightGain", 1f);
            SetSerializedFloat(wind, "_upperHeightGain", 1f);
            fieldGO.SetActive(true);

            // originRT 由 Manager 运行时创建，不写进场景
            interactionCamera.targetTexture = null;

            Require(EditorSceneManager.SaveScene(scene, ScenePath), "Could not save " + ScenePath);

            sb.Append("{\"status\":\"ok\",\"scene\":\"").Append(ScenePath).Append('"');
            sb.Append(",\"objects\":").Append(scene.rootCount);
            sb.Append(",\"processors\":[\"grass\",\"snow\",\"sand\"]}");
            return sb.ToString();
        }
        finally
        {
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            RestoreForeignInteraction(suspended);
            AssetDatabase.SaveAssets();
            if (previousActive.IsValid()) EditorSceneManager.SetActiveScene(previousActive);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  全局注册表隔离
    // ════════════════════════════════════════════════════════════

    private static List<(Behaviour behaviour, bool enabled)> SuspendForeignInteraction()
    {
        var suspended = new List<(Behaviour, bool)>();

        foreach (var manager in Object.FindObjectsByType<UniversalInteractionManager>(
                     FindObjectsSortMode.None))
        {
            if (!manager.enabled) continue;
            suspended.Add((manager, true));
            manager.enabled = false;
        }

        foreach (var processor in Object.FindObjectsByType<InteractionProcessor>(
                     FindObjectsSortMode.None))
        {
            if (!processor.enabled) continue;
            suspended.Add((processor, true));
            processor.enabled = false;
        }

        // FieldManager / FieldProvider 也是全局注册表，同样会跨场景抢「唯一活动 Manager」
        // 和同名 producer（Wind）。不挂起的话新场景的风场会注册失败并被禁用。
        foreach (var fieldManager in Object.FindObjectsByType<FieldManager>(
                     FindObjectsSortMode.None))
        {
            if (!fieldManager.enabled) continue;
            suspended.Add((fieldManager, true));
            fieldManager.enabled = false;
        }

        foreach (var provider in Object.FindObjectsByType<FieldProvider>(
                     FindObjectsSortMode.None))
        {
            if (!provider.enabled) continue;
            suspended.Add((provider, true));
            provider.enabled = false;
        }

        return suspended;
    }

    private static void RestoreForeignInteraction(List<(Behaviour behaviour, bool enabled)> suspended)
    {
        if (suspended == null) return;
        // 先恢复处理器（重新注册），再恢复 Manager（重新挂接全部就绪子集）。
        for (int i = suspended.Count - 1; i >= 0; i--)
        {
            var (behaviour, enabled) = suspended[i];
            if (behaviour != null) behaviour.enabled = enabled;
        }
        suspended.Clear();
    }

    // ════════════════════════════════════════════════════════════
    //  场景物体
    // ════════════════════════════════════════════════════════════

    private static void BuildSnowSandPatch(string name, float centerX, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.layer = SurfaceLayer;
        go.transform.SetPositionAndRotation(new Vector3(centerX, 0f, 0f), Quaternion.identity);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static void BuildGrassPatch(string name, Material groundMaterial, Mesh grassMesh,
        Material grassMaterial, Camera targetCamera)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(0f, 0f, 0f);   // 三块中间
        var terrain = go.AddComponent<FlatGrassTerrain>();
        SetSerializedVector2(terrain, "_size", new Vector2(PatchSize, PatchSize));
        // 单 chunk：GrassChunkManager 只生成相机 3×3 chunk 邻域，12 m 的整块地形必须
        // 落在一个 chunk 里，否则相机站在地形外时只会生成近处那一条。
        SetSerializedFloat(terrain, "_chunkSize", PatchSize);
        SetSerialized(terrain, "_groundMaterial", groundMaterial);
        SetSerializedInt(terrain, "_surfaceLayer", SurfaceLayer);
        SetSerializedInt(terrain, "_surfaceSubdivisions", 8);
        terrain.Initialize();

        var manager = go.AddComponent<GrassChunkManager>();
        manager.Configure(terrain, targetCamera, grassMesh, grassMaterial);
        manager.TerrainLayers = GrassTerrainLayer.Grass;
        manager.MeshRotation = new Vector3(-90f, 0f, 0f);
        manager.Density = 3.2f;
        manager.Size = 3f;
        manager.Rebuild();
    }

    // ════════════════════════════════════════════════════════════
    //  资产
    // ════════════════════════════════════════════════════════════

    private static Mesh EnsurePatchMesh(string path, float size, int subdivisions)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        int side = subdivisions + 1;
        Vector3[] vertices = new Vector3[side * side];
        Vector2[] uvs = new Vector2[side * side];
        Vector3[] normals = new Vector3[side * side];
        int[] triangles = new int[subdivisions * subdivisions * 6];

        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            int i = x + z * side;
            float u = x / (float)subdivisions;
            float v = z / (float)subdivisions;
            vertices[i] = new Vector3((u - 0.5f) * size, 0f, (v - 0.5f) * size);
            uvs[i] = new Vector2(u, v);
            normals[i] = Vector3.up;
        }

        int index = 0;
        for (int z = 0; z < subdivisions; z++)
        for (int x = 0; x < subdivisions; x++)
        {
            int i = x + z * side;
            triangles[index++] = i;
            triangles[index++] = i + side;
            triangles[index++] = i + 1;
            triangles[index++] = i + 1;
            triangles[index++] = i + side;
            triangles[index++] = i + side + 1;
        }

        Mesh mesh = new Mesh { name = "Interaction Patch" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.triangles = triangles;
        // 顶点位移会离开零厚度的原始 bounds，留出 ±0.6 m 余量避免错误剔除。
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 1.2f, size));
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static Material EnsureMaterial(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        Require(shader != null, "Cannot find shader " + shaderName);

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }
        return material;
    }

    private static T Load<T>(string path) where T : Object =>
        AssetDatabase.LoadAssetAtPath<T>(path);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new System.InvalidOperationException(message);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        string leaf = path.Substring(slash + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    // ════════════════════════════════════════════════════════════
    //  Serialized helpers
    // ════════════════════════════════════════════════════════════

    private static SerializedProperty Property(Object target, string name)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        Require(property != null, target.GetType().Name + " has no field " + name);
        return property;
    }

    private static void SetSerialized(Object target, string name, Object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        Require(property != null, target.GetType().Name + " has no field " + name);
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetSerializedFloat(Object target, string name, float value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        Require(property != null, target.GetType().Name + " has no field " + name);
        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetSerializedInt(Object target, string name, int value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        Require(property != null, target.GetType().Name + " has no field " + name);
        property.intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetSerializedBool(Object target, string name, bool value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        Require(property != null, target.GetType().Name + " has no field " + name);
        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetSerializedVector2(Object target, string name, Vector2 value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        Require(property != null, target.GetType().Name + " has no field " + name);
        property.vector2Value = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetSerializedVector3(Object target, string name, Vector3 value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        Require(property != null, target.GetType().Name + " has no field " + name);
        property.vector3Value = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
