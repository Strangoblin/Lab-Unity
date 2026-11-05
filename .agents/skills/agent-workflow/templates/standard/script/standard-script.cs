// ═══════════════════════════════════════════════════════════════
//  普通 C# 脚本标准骨架（函数无关 · 可编译 · 双形态）
//
//  结构规范: references/standard/script/script-structure.md
//  家族骨架: Baker/Generator/Manager/Controller → templates/script/
//
//  使用方式:
//    1. 复制到 <category.Script>/<YourArea>/，改名 YourXxx.cs
//    2. ⚠️ 双形态二选一: 静态工具类（形态 A）或 MonoBehaviour（形态 B），
//       删除不需要的一段；保留 MonoBehaviour 时建议文件名 = 类名
//    3. 替换所有 ⚠️ 标记
// ═══════════════════════════════════════════════════════════════

using UnityEngine;

// ═══════════════════════════════════════════════════════════════
//  形态 A — 静态工具类（无状态函数库）
//
//  先例: NoiseGenerator / CurveBake / InteriorMapTextureBaker 的
//    生成函数部分。公开 API 全部在上，私有 helper 全部在下。
//    跨类共享的纯函数保持 public（Repeat01/Lerp 先例）。
// ═══════════════════════════════════════════════════════════════

public static class YourUtility
{
    /// <summary>
    /// ⚠️ 公开 API — 一句话说明作用与所有权（创建的纹理/对象由调用方销毁）。
    /// </summary>
    public static Texture2D GenerateTexture(int resolution)
    {
        // ⚠️ 参数 clamp 先例: resolution = Mathf.Max(resolution, 1);

        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            name = "YourOutput", // ⚠️ 命名即资产名
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        // ⚠️ 主体计算 … 返回自建对象，所有权移交调用方

        return texture;
    }

    // ════════════════════════════════════════════════════════════
    //  私有 helper — 公开 API 之下，非本类不得调用
    // ════════════════════════════════════════════════════════════

    private static float Clamp01(float value)
    {
        return Mathf.Clamp01(value);
    }
}

// ═══════════════════════════════════════════════════════════════
//  形态 B — MonoBehaviour（场景内持有状态的行为组件）
//
//  先例: 普通单组件脚本。场景引用用 [SerializeField] + _ 前缀；
//    OnEnable/OnDisable 对称；编辑模式销毁必须 DestroyImmediate。
//    复杂系统编排 → Manager 家族模板（templates/script/manager/）。
// ═══════════════════════════════════════════════════════════════

public class YourBehaviour : MonoBehaviour // ⚠️ 保留本段时建议独立成 YourBehaviour.cs
{
    [Header("⚙️ 引用")]
    [SerializeField] private Camera _targetCamera; // ⚠️ 场景引用示例

    // ── 运行时状态 ──────────────────────────────────────────────
    private RenderTexture _workRT;
    private bool _initialized;

    // ════════════════════════════════════════════════════════════
    //  生命周期 — OnEnable/OnDisable 对称注册与释放
    // ════════════════════════════════════════════════════════════

    private void OnEnable()
    {
        EnsureResources();
        _initialized = true;
    }

    private void OnDisable()
    {
        ReleaseResources();
        _initialized = false;
    }

    private void Update()
    {
        if (!_initialized) return;
        // ⚠️ 每帧行为
    }

    // ════════════════════════════════════════════════════════════
    //  资源生命周期 — 释放分支区分播放/编辑模式
    // ════════════════════════════════════════════════════════════

    private void EnsureResources()
    {
        if (_workRT != null) return;
        _workRT = new RenderTexture(256, 256, 0)
        {
            name = "YourWorkRT",
            filterMode = FilterMode.Bilinear
        };
        _workRT.Create();
    }

    private void ReleaseResources()
    {
        if (_workRT == null) return;

        _workRT.Release();
        // ⚠️ 铁律: 编辑模式 DestroyImmediate，播放模式 Destroy（实坑注记见 manager/）
        if (Application.isPlaying)
        {
            Destroy(_workRT);
        }
        else
        {
            DestroyImmediate(_workRT);
        }
        _workRT = null;
    }
}
