// ═══════════════════════════════════════════════════════════════
//  Manager 模板（核心层）
//
//  家族定位：运行时场景编排 MonoBehaviour —— 持有并驱动多个组件/
//    处理器，跨帧协调一个完整系统。不做资产产出、不建工具窗口。
//
//  历史实源（仅溯源）：
//    Assets/Mine/Scripts/InteractionManager/InteractionManager.cs
//      —— 处理器自动发现 + RT 生命周期 + 嵌套 Editor
//    Assets/Mine/Scripts/FGDLutBaker/FGDLutManager.cs
//      —— sealed 小组件形态（自行探测输入即生效）
//    Assets/Mine/Scripts/InstanceManager/UniversalInstanceManager.cs
//
//  使用方式：
//    1. 复制到 <category.Script>/<YourSystem>/，类名改为 <YourSystem>Manager
//    2. 替换所有 ⚠️ 标记；不需要的部分（单例/处理器/RT）直接删除
//    3. Inspector 两种组织任选：同文件嵌套 Editor 类（InteractionManager.cs
//       先例）或 Editor/<Name>Editor.cs 独立文件（IK/ActiveRagdollManagerEditor.cs 先例）
//
//  结构规范：references/standard/script/script-structure.md
// ═══════════════════════════════════════════════════════════════

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ⚠️<YourSystem> 运行时总控。负责 ⚠️(处理器发现 / RT 与全局属性同步 / 帧循环驱动)。
/// </summary>
[ExecuteAlways] // ⚠️ 需要编辑模式运行时保留；否则删除
public class YourManager : MonoBehaviour
{
    [Header("⚙️ 系统引用")] // ⚠️ Header 分组：场景引用序列化字段归组（实源先例）
    [SerializeField] private Camera _bakeCamera;      // ⚠️ 示例引用
    [SerializeField] private Transform _followTarget; // ⚠️ 示例引用

    // ── Shader Property IDs（静态缓存，避免每帧字符串哈希）────────
    private static readonly int s_SourceTexID = Shader.PropertyToID("_YourSourceTex"); // ⚠️

    // ── 运行时状态 ──────────────────────────────────────────────
    private RenderTexture _originRT;
    private bool _initialized;

    // ════════════════════════════════════════════════════════════
    //  生命周期 — OnEnable/OnDisable 对称注册与释放
    // ════════════════════════════════════════════════════════════

    private void OnEnable()
    {
        FindComponents();     // ⚠️ 子组件/处理器自动发现
        EnsureResources();    // ⚠️ RT / 材质等创建
        SyncGlobalProperties();
        _initialized = true;
    }

    private void OnDisable()
    {
        ReleaseResources();   // ⚠️ 与 EnsureResources 对称
        _initialized = false;
    }

    private void OnValidate()
    {
        // ⚠️ 编辑态改参数即时生效（FGDLutManager 先例：isActiveAndEnabled 时 Apply）
    }

    private void Update()
    {
        if (!_initialized) return;
        // ⚠️ 帧循环：同步 → 驱动处理器（Processor.Process(deltaTime, ...)）→ 清理
    }

    // ════════════════════════════════════════════════════════════
    //  资源生命周期 — 释放分支必须区分播放/编辑模式
    // ════════════════════════════════════════════════════════════

    private void EnsureResources()
    {
        if (_originRT != null) return;
        _originRT = new RenderTexture(256, 256, 0, RenderTextureFormat.RFloat) // ⚠️
        {
            name = "_YourSourceTex",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        _originRT.Create();
    }

    private void ReleaseResources()
    {
        if (_originRT == null) return;

        _originRT.Release();
        // ⚠️ 铁律：编辑模式 DestroyImmediate，播放模式 Destroy
        //   （2026-08-27 POSSManager 实坑：编辑模式调 Destroy → 报错）
        if (Application.isPlaying)
        {
            Destroy(_originRT);
        }
        else
        {
            DestroyImmediate(_originRT);
        }
        _originRT = null;
    }

    // ════════════════════════════════════════════════════════════
    //  组件发现与全局同步
    // ════════════════════════════════════════════════════════════

    private void FindComponents()
    {
        // ⚠️ 处理器自动发现（InteractionManager 先例）：
        //   _processor = GetComponent<IUniversalInteractionProcessor>();
        //   新行为 = 新接口实现，Manager 零改动
    }

    private void SyncGlobalProperties()
    {
        if (_originRT != null)
        {
            Shader.SetGlobalTexture(s_SourceTexID, _originRT);
        }
        // ⚠️ 其他全局参数：SetGlobalMatrix / SetGlobalVector / SetGlobalFloat
    }

    // ════════════════════════════════════════════════════════════
    //  Inspector — 两种组织任选其一
    // ════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    [CustomEditor(typeof(YourManager))]
    public class YourManagerEditor : Editor // ⚠️ 同文件嵌套（InteractionManager.cs 先例）
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (YourManager)target;
            EditorGUILayout.Space();
            // ⚠️ 初始化状态 HelpBox + 场景搭建按钮（Create Framework 类动作）
        }
    }
#endif
}
