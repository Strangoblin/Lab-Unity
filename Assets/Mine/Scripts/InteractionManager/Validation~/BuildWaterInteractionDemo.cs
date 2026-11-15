// ════════════════════════════════════════════════════════════════
//  Build WaterInteractionDemo — 搭建整套交互配置的验证场景
// ════════════════════════════════════════════════════════════════
using System;
using System.Text;
using Mine.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 在 Additive 场景里搭好「正交交互相机 + UniversalInteractionManager + WaterInteractionProcessor」
/// 的完整配置，保存为 Assets/Mine/Scripts/InteractionManager/WaterInteractionDemo.unity，再关闭该场景。
/// 不触碰当前打开的场景与它的未保存修改。
/// Args: 无。
/// </summary>
public class Script
{
    private const string Folder          = "Assets/Mine/Scripts/InteractionManager";
    private const string MaterialsFolder = Folder + "/Materials";
    private const string ScenePath       = Folder + "/WaterInteractionDemo.unity";
    private const string ComputePath     = Folder + "/Water/WaterInteraction.compute";

    private const int DepthLayer    = 6; // Interaction
    private const int InteractorLayer = 7; // Interactor
    private const int RendererIndex = 1; // IA_Renderer

    public static object Main(string[] args)
    {
        Scene previousActive = EditorSceneManager.GetActiveScene();
        Scene scene = default;
        var sb = new StringBuilder();

        try
        {
            EnsureFolder(MaterialsFolder);
            Material debugMaterial  = EnsureMaterial("InteractionDebug",  "InteractionManager/InteractionDebug");
            Material probeMaterial  = EnsureMaterial("InteractionProbe",  "InteractionManager/InteractorObject");
            // 交互物体的可见外观由 Shader 的 UniversalForward Pass 输出，主相机直接看得到。
            probeMaterial.SetColor("_BaseColor", new Color(1f, 0.45f, 0.15f, 1f));
            EditorUtility.SetDirty(probeMaterial);
            // 调试 Shader 直接输出「高度 * _BaseColor」：凹陷能到 -1，但向外扩散的涟漪只有 0.05 量级，
            // 增益要拉到 8 波纹才看得清（凹陷会被 LDR 截断成纯蓝，不影响观察）。
            debugMaterial.SetColor("_BaseColor", new Color(8f, 8f, 8f, 1f));
            EditorUtility.SetDirty(debugMaterial);

            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            Require(compute != null, $"Missing compute shader at {ComputePath}");

            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);

            // ── 水面（layer 6：只进正交相机的深度捕获）──────────────
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "WaterSurface";
            water.layer = DepthLayer;
            water.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            water.GetComponent<MeshRenderer>().sharedMaterial = debugMaterial;
            UnityEngine.Object.DestroyImmediate(water.GetComponent<Collider>());

            // ── 交互物体 ───────────────────────────────────────────
            // 一个物体、两个 Pass，靠 LightMode Tag 分流：
            //   InteractionCapture → 只有正交交互相机的 InteractionCaptureFeature 命中，输出穿透深度
            //   UniversalForward   → 主相机用，输出可见外观
            // layer 7 只用来决定「捕获相机的颜色捕获画不画它」，不再是「主相机看不见」的意思。
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            probe.name = "InteractorProbe";
            probe.layer = InteractorLayer;
            probe.transform.localScale = Vector3.one * 1.5f;   // 世界半径 0.75
            probe.transform.position = new Vector3(0f, -0.25f, 0f);
            probe.GetComponent<MeshRenderer>().sharedMaterial = probeMaterial;
            UnityEngine.Object.DestroyImmediate(probe.GetComponent<Collider>());

            // ── 交互总控 + 水面处理器 ──────────────────────────────
            var managerGO = new GameObject("InteractionManager");
            var processor = managerGO.AddComponent<WaterInteractionProcessor>();
            SetSerialized(processor, "_computeShader", compute);

            var manager = managerGO.AddComponent<UniversalInteractionManager>();

            // ── 正交交互相机 ───────────────────────────────────────
            var cameraGO = new GameObject("InteractionCamera");
            cameraGO.transform.SetParent(managerGO.transform, false);
            var camera = cameraGO.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.nearClipPlane = -10f;
            camera.farClipPlane = 10f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.cullingMask = (1 << DepthLayer) | (1 << InteractorLayer);
            camera.depth = -10f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;

            UniversalAdditionalCameraData additional = camera.GetUniversalAdditionalCameraData();
            additional.SetRenderer(RendererIndex);
            additional.renderPostProcessing = false;
            additional.renderShadows = false;

            // ── 主相机 ─────────────────────────────────────────────
            var mainGO = new GameObject("Main Camera") { tag = "MainCamera" };
            mainGO.transform.SetPositionAndRotation(
                new Vector3(0f, 6.5f, -6.5f), Quaternion.Euler(35f, 0f, 0f));
            var mainCamera = mainGO.AddComponent<Camera>();
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);
            mainCamera.farClipPlane = 100f;
            mainCamera.cullingMask = ~0; // layer 7 也要画：主相机渲染的是同一个物体的 UniversalForward Pass
            // 不挂 AudioListener：这个场景常与其它场景叠加加载，两套监听器只会刷警告。

            UniversalAdditionalCameraData mainAdditional = mainCamera.GetUniversalAdditionalCameraData();
            mainAdditional.SetRenderer(0);
            mainAdditional.renderPostProcessing = false;

            // ── 光照（当前材质都是 Unlit，留一盏灯供后续替换材质）──
            var lightGO = new GameObject("Directional Light");
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;

            // ── 连线：Manager 需要相机，重新走一遍 OnEnable ─────────
            SetSerialized(manager, "_orthoCamera", camera);
            manager.enabled = false;
            manager.enabled = true;

            // originRT 由 Manager 运行时创建，不写进场景
            camera.targetTexture = null;

            Require(EditorSceneManager.SaveScene(scene, ScenePath), $"Could not save {ScenePath}");

            sb.Append("{\"status\":\"ok\",\"scene\":\"").Append(ScenePath).Append('"');
            sb.Append(",\"objects\":").Append(scene.rootCount);
            sb.Append(",\"assets\":[\"").Append(MaterialsFolder).Append("\"]}");
            return sb.ToString();
        }
        finally
        {
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            if (previousActive.IsValid()) EditorSceneManager.SetActiveScene(previousActive);
            AssetDatabase.SaveAssets();
        }
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder)!.Replace('\\', '/');
        string name   = System.IO.Path.GetFileName(folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static Material EnsureMaterial(string fileName, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        Require(shader != null, $"Shader '{shaderName}' not found");
        string path = $"{MaterialsFolder}/{fileName}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            if (existing.shader != shader) existing.shader = shader;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        var material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void SetSerialized(UnityEngine.Object target, string property, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty field = serialized.FindProperty(property);
        Require(field != null, $"{target.GetType().Name} has no serialized field '{property}'");
        field.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
