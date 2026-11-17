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
    private const string FieldManagerPrefab = "Assets/Mine/Scripts/FieldManager/FieldManager.prefab";
    private const string WaveComputePath = "Assets/Mine/Scripts/FieldManager/Wave/WaveField.compute";

    private const int DepthLayer    = 6; // Interaction
    private const int InteractorLayer = 7; // Interactor
    private const int RendererIndex = 1; // IA_Renderer

    public static object Main(string[] args)
    {
        Scene previousActive = EditorSceneManager.GetActiveScene();
        Scene scene = default;
        var sb = new StringBuilder();

        // 目标场景如果已经打开，SaveScene 到同一路径会失败（Unity 不允许覆盖已加载的场景）；
        // 而且已加载场景里的 FieldManager 会和新建的 FFT provider 抢同一个 field key。
        // 做法：先叠加一个空场景做「垫片」（让目标场景不再是唯一场景）、把目标场景卸载，构建完再打开。
        bool targetWasOpen = false;
        Scene scratch = default;
        try
        {
            Scene open = EditorSceneManager.GetSceneByPath(ScenePath);
            if (open.IsValid() && open.isLoaded)
            {
                if (open.isDirty) EditorSceneManager.SaveScene(open);
                scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.CloseScene(open, true);
                targetWasOpen = true;
            }

            EnsureFolder(MaterialsFolder);
            Material probeMaterial  = EnsureMaterial("InteractionProbe",  "InteractionManager/InteractorObject");
            Material waterMaterial  = EnsureMaterial("InteractionWater",  "Render/Water");
            Material seaBedMaterial = EnsureMaterial("SeaBed",            "Universal Render Pipeline/Lit");
            // 交互物体的可见外观由 Shader 的 UniversalForward Pass 输出，主相机直接看得到。
            probeMaterial.SetColor("_BaseColor", new Color(1f, 0.45f, 0.15f, 1f));
            EditorUtility.SetDirty(probeMaterial);
            // 水面：交互 RT → 位移 + 法线，FFT 波浪由 FieldManager 发布
            waterMaterial.SetFloat("_TessellationFactor", 32f);
            waterMaterial.SetFloat("_InteractionHeightScale", 2.5f);
            waterMaterial.SetFloat("_InteractionNormalStrength", 2.5f);
            // 水体覆盖率：1 m 处 C = 1 - exp(-2) ≈ 0.86，对齐旧版 _Alpha = 0.85 的水体感
            waterMaterial.SetFloat("_ScatterDensity", 2f);
            waterMaterial.SetFloat("_FoamIntensity", 0.9f);
            // 焦散在正片里是「点缀」，全强度会在平整海底上织出一整片白网格；0.25 只留几缕。
            waterMaterial.SetFloat("_CausticsIntensity", 0.25f);
            waterMaterial.SetColor("_baseColorA", new Color(0.02f, 0.12f, 0.18f, 1f));
            waterMaterial.SetColor("_baseColorB", new Color(0.10f, 0.42f, 0.48f, 1f));
            EditorUtility.SetDirty(waterMaterial);
            seaBedMaterial.SetColor("_BaseColor", new Color(0.075f, 0.115f, 0.115f, 1f));
            seaBedMaterial.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(seaBedMaterial);

            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            Require(compute != null, $"Missing compute shader at {ComputePath}");
            var waveCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(WaveComputePath);
            Require(waveCompute != null, $"Missing wave compute shader at {WaveComputePath}");

            // 有空垫片场景在时不能再叠加新建未保存场景（Unity 拒绝），所以直接替换它。
            NewSceneMode sceneMode = targetWasOpen ? NewSceneMode.Single : NewSceneMode.Additive;
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);
            EditorSceneManager.SetActiveScene(scene);

            // ── 水面（layer 6：既是主相机可见的渲染面，也是交互的深度参照）──
            // 水面 shader 另有一个不做位移的 DepthOnly Pass，TransparentDepthMergeFeature 在
            // 不透明之后按这个 Tag 把水面重画进 _CameraDepthTexture。用「位移前的网格」当参照，
            // 就不会出现「位移越深 → 参照越深 → 穿透更深」的反馈。
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "WaterSurface";
            water.layer = DepthLayer;
            water.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            water.transform.localScale = new Vector3(2f, 1f, 2f);   // 20×20，交互域仍是中心 10×10
            water.GetComponent<MeshRenderer>().sharedMaterial = waterMaterial;
            UnityEngine.Object.DestroyImmediate(water.GetComponent<Collider>());

            // ── 交互物体：倒立凹形（Π），模仿双足触水 ──────────────
            // 一个物体、两个 Pass，靠 LightMode Tag 分流：
            //   InteractionCapture → 只有正交交互相机的 CustomFeature 命中，输出穿透深度
            //   UniversalForward   → 主相机用，输出可见外观
            // 两条腿各自是一个闭合的胶囊体，从正上方看每个像素只有一个背面（下半个球面）——
            // 这是捕获 Pass（Cull Front + ZWrite Off）能被确定采样的前提。
            // 横梁放在 layer 0：交互相机 cullingMask 只含 layer 6/7，完全看不到它。
            // 它若参与捕获，会和一整片腿部像素抢同一个背面（后画的赢），表现为穿透深度闪烁。
            var probe = new GameObject("InteractorProbe");
            probe.transform.position = Vector3.zero;
            // 腿用胶囊：底面是半球，穿透深度从脚尖 0.4 m 平滑过渡到边缘 0，
            // 冲量是渐变的而不是一块平底的阶跃。
            AddInteractorLeg(probe, "FootL", probeMaterial, InteractorLayer,
                             new Vector3(-0.28f, -0.325f, 0f), new Vector3(0.24f, 0.85f, 0.24f));
            AddInteractorLeg(probe, "FootR", probeMaterial, InteractorLayer,
                             new Vector3(0.28f, -0.325f, 0f), new Vector3(0.24f, 0.85f, 0.24f));
            AddInteractorBox(probe, "Bridge", probeMaterial, 0,
                             new Vector3(0f, 0.14f, 0f), new Vector3(0.78f, 0.18f, 0.32f));
            // 脚底 -0.75 是对的：水面本身有 FFT 波浪位移（不是 y=0），脚要真的插进去 ~0.4 m，
            // `contact = saturate(depth × 4)` 才够满，冲量才有量级。

            // ── 海底 + 四边遮挡 ────────────────────────────────────
            // 水面必须压在几何体上：没有海底时 ComputeCaustics 的 sceneDepDf 会飞到远裁剪面，整屏爆白。
            BuildEnclosure(seaBedMaterial);

            // ── 交互总控 + 水面处理器 ──────────────────────────────
            var managerGO = new GameObject("InteractionManager");
            var processor = managerGO.AddComponent<WaterInteractionProcessor>();
            SetSerialized(processor, "_computeShader", compute);
            // 物体交互只认穿透深度的帧间变化：静止的探针不再把水面压出一个恒定的坑。
            // 水体表现只剩一个美术参数：0 = 浑浊，1 = 清澈。0.5 = 原来手调的一组
            // （波速 0.15 / 每秒衰减 0.14 / 粘稠度 0.08 / 冲量 0.5）。
            SetSerializedFloat(processor, "_clarity", 0.5f);

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
            // layer 6 走 TransparentDepthMerge（水面按 DepthOnly 再画一遍进深度），
            // layer 7 走颜色捕获；两个 Pass 由 LightMode 分流，同一物体在主相机里也照常显示。
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
            mainCamera.backgroundColor = new Color(0.055f, 0.09f, 0.105f, 1f);
            mainCamera.farClipPlane = 100f;
            // layer 6 是水面本身（也是深度参照），主相机必须画它——参照面已经不外露了，
            // 热力图只出现在捕获相机的 RT 里。
            mainCamera.cullingMask = -1;
            // 不挂 AudioListener：这个场景常与其它场景叠加加载，两套监听器只会刷警告。

            UniversalAdditionalCameraData mainAdditional = mainCamera.GetUniversalAdditionalCameraData();
            mainAdditional.SetRenderer(0);
            mainAdditional.renderPostProcessing = false;

            // ── 波浪场：FieldManager 调度 + Wave 生产者 ──────────────
            // Water.shader 的 Wave 通道由 FieldManager 发布的全局纹理提供；
            // WaveFieldProvider 初始化失败会自己 enabled=false 且不报错（症状：水面平坦、_WaveFieldValid=0）。
            var fieldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FieldManagerPrefab);
            Require(fieldPrefab != null, $"Missing FieldManager prefab at {FieldManagerPrefab}");
            PrefabUtility.InstantiatePrefab(fieldPrefab);

            // 先建不激活的物体再挂组件：AddComponent 会立刻触发 OnEnable，
            // 那时 _fftCS 还是 null，provider 会报「requires WaveField.compute」并把自己 enabled=false。
            var waveGO = new GameObject("Wave");
            waveGO.SetActive(false);
            var wave = waveGO.AddComponent<Mine.Fields.WaveFieldProvider>();
            SetSerialized(wave, "_fftCS", waveCompute);
            waveGO.SetActive(true);

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
            AssetDatabase.SaveAssets();
            if (targetWasOpen)
            {
                // Single 模式会把空垫片场景一起换掉，回到「只开着 Demo 场景」的原状态。
                Scene reopened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                EditorSceneManager.SetActiveScene(reopened);
            }
            else
            {
                if (scratch.IsValid()) EditorSceneManager.CloseScene(scratch, true);
                if (previousActive.IsValid()) EditorSceneManager.SetActiveScene(previousActive);
            }
        }
    }

    /// <summary>海底 + 四面墙：把水面围住，挡住地平线和空场；也给折射/焦散一个真实深度。</summary>
    private static void BuildEnclosure(Material seaBedMaterial)
    {
        GameObject seaBed = GameObject.CreatePrimitive(PrimitiveType.Plane);
        seaBed.name = "SeaBed";
        seaBed.layer = 0;
        seaBed.transform.SetPositionAndRotation(new Vector3(0f, -5f, 0f), Quaternion.identity);
        seaBed.transform.localScale = new Vector3(6f, 1f, 6f);   // 60×60
        var seaBedRenderer = seaBed.GetComponent<MeshRenderer>();
        seaBedRenderer.sharedMaterial = seaBedMaterial;
        // 海底自投影会把影子打到水面上，形成一条条自阴影花纹。
        seaBedRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        UnityEngine.Object.DestroyImmediate(seaBed.GetComponent<Collider>());

        var background = new GameObject("Background");
        const float half = 11f, height = 22f, bottom = -5f;
        AddWall(background.transform, "SeaWall_N", seaBedMaterial, new Vector3(0f, bottom + height * 0.5f, +half), Vector3.back, height);
        AddWall(background.transform, "SeaWall_S", seaBedMaterial, new Vector3(0f, bottom + height * 0.5f, -half), Vector3.forward, height);
        AddWall(background.transform, "SeaWall_E", seaBedMaterial, new Vector3(+half, bottom + height * 0.5f, 0f), Vector3.left, height);
        AddWall(background.transform, "SeaWall_W", seaBedMaterial, new Vector3(-half, bottom + height * 0.5f, 0f), Vector3.right, height);
    }

    private static void AddWall(Transform parent, string name, Material material, Vector3 position, Vector3 inward, float height)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Plane);
        wall.name = name;
        wall.layer = 0;
        UnityEngine.Object.DestroyImmediate(wall.GetComponent<Collider>());
        wall.transform.SetParent(parent, false);
        wall.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, inward));
        // Plane 的局部 X 是宽度、局部 Z 是高度；FromToRotation(up → inward) 之后局部 Z 就立起来了。
        wall.transform.localScale = new Vector3(2.4f, 1f, height / 10f);
        var renderer = wall.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>互动物体的一块盒体。尺寸用世界米（Cube 默认 1 m，localScale 即尺寸）。</summary>
    private static void AddInteractorBox(GameObject parent, string name, Material material, int layer,
                                         Vector3 center, Vector3 size)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.layer = layer;
        UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
        box.transform.SetParent(parent.transform, false);
        box.transform.localPosition = center;
        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>互动物体的一条腿（胶囊）。尺寸用世界米；底面是半球，穿透深度自带渐变。</summary>
    private static void AddInteractorLeg(GameObject parent, string name, Material material, int layer,
                                         Vector3 center, Vector3 size)
    {
        GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        leg.name = name;
        leg.layer = layer;
        UnityEngine.Object.DestroyImmediate(leg.GetComponent<Collider>());
        leg.transform.SetParent(parent.transform, false);
        leg.transform.localPosition = center;
        leg.transform.localScale = size;
        leg.GetComponent<MeshRenderer>().sharedMaterial = material;
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

    private static void SetSerializedFloat(UnityEngine.Object target, string property, float value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty field = serialized.FindProperty(property);
        Require(field != null, $"{target.GetType().Name} has no serialized field '{property}'");
        field.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
