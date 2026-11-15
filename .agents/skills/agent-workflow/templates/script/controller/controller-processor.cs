// ═══════════════════════════════════════════════════════════════
//  Controller 模板（核心层）— 接口 + 实现双段
//
//  家族定位：单物体/单系统行为控制。命令式 public API，常被 Manager
//    多态引用（Manager 只依赖接口）；也可独立挂载。
//
//  历史实源（仅溯源）：
//    Assets/Mine/Scripts/InteractionManager/IInteractionProcessor.cs
//    Assets/Mine/Scripts/InteractionManager/Water/WaterInteractionProcessor.cs
//      —— 接口实现形态（被 UniversalInteractionManager 自动发现驱动）
//    Assets/Mine/Scripts/CamController/CamController.cs
//      —— 纯单物体控制形态：无接口，直接命令式 API（此文件不含该形态，见实码）
//
//  使用方式：
//    1. 复制接口段到独立文件 <YourCapability>.cs（一个接口一个文件，实源先例）
//    2. 复制实现段到 <YourProcessor>.cs，实现者挂 Manager 同物体（如需要）
//    3. 替换所有 ⚠️ 标记；接口方法按系统实际裁剪（Initialize/Bind/Release 按需）
//
//  结构规范：references/standard/script/script-structure.md
// ═══════════════════════════════════════════════════════════════

using UnityEngine;

/// <summary>
/// ⚠️<YourCapability> 处理器接口 — 由 Manager 自动发现并每帧驱动。
/// 实现者须为 MonoBehaviour，挂在与 Manager 相同的 GameObject 上
/// （Manager 通过 GetComponent&lt;接口&gt;() 发现，先例 IUniversalInteractionProcessor）。
/// ⚠️ 输出 RT 由实现者自行管理（创建/释放），Manager 只管理共享输入。
/// </summary>
public interface IYourProcessor
{
    /// <summary>初始化：创建自管输出 RT，绑定纹理到 Compute Shader，查找 kernel。</summary>
    /// <param name="resolution">分辨率（边长像素数）</param>
    /// <param name="sourceRT">Manager 管理的共享输入 RT</param>
    void Initialize(int resolution, RenderTexture sourceRT);

    /// <summary>每帧处理：设置参数并 ⚠️(派发 Compute / 写数据)。</summary>
    /// <param name="deltaTime">帧间隔时间</param>
    /// <param name="worldDelta">世界空间位移（RT 数据重投影用，无则传 default）</param>
    void Process(float deltaTime, Vector2 worldDelta);

    /// <summary>绑定自管输出为全局 Shader 属性，供渲染 Shader 采样。</summary>
    void BindGlobalTextures();

    /// <summary>释放 Compute Shader 引用与自管 RT（Manager.OnDisable 调用）。</summary>
    void Release();
}

/// <summary>
/// ⚠️<YourCapability> 处理器实现。实现者挂 Manager 同物体；
/// 单物体纯控制形态（无 RT/无接口）参考 CamController，不走此骨架。
/// </summary>
public class YourProcessor : MonoBehaviour, IYourProcessor
{
    // ── 序列化参数 ─────────────────────────────────────────────
    [SerializeField] private float _strength = 1f;   // ⚠️ 行为参数
    [SerializeField] private float _decay = 0.98f;   // ⚠️ 按需

    // ── 运行时 ─────────────────────────────────────────────────
    private RenderTexture _outputRT;
    private bool _initialized;

    private static readonly int s_OutputTexID = Shader.PropertyToID("_YourOutputTex"); // ⚠️

    // ════════════════════════════════════════════════════════════
    //  生命周期 — 由 Manager 调用，不自行 OnEnable 创建
    // ════════════════════════════════════════════════════════════

    public void Initialize(int resolution, RenderTexture sourceRT)
    {
        if (_initialized) return;

        _outputRT = CreateOutputRT(resolution); // ⚠️ 私有辅助：与 Manager 同款设置
        // ⚠️ 绑定到 Compute Shader / 查找 kernel / 存 sourceRT 引用
        _initialized = true;
    }

    public void Process(float deltaTime, Vector2 worldDelta)
    {
        if (!_initialized) return;

        // ⚠️ 行为实现：读 sourceRT 输入 → 计算 → 写 _outputRT → 全局绑定
    }

    public void BindGlobalTextures()
    {
        if (_outputRT != null)
        {
            Shader.SetGlobalTexture(s_OutputTexID, _outputRT);
        }
    }

    public void Release()
    {
        if (_outputRT == null) return;

        _outputRT.Release();
        // ⚠️ 与 Manager 同铁律：Application.isPlaying ? Destroy : DestroyImmediate
        if (Application.isPlaying) Destroy(_outputRT);
        else DestroyImmediate(_outputRT);
        _outputRT = null;
        _initialized = false;
    }
}
