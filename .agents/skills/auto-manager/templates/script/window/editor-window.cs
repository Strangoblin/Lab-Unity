// ═══════════════════════════════════════════════════════════════
//  EditorWindow 通用壳模板（window 家族 · 可编译骨架）
//
//  家族定位：编辑器工具窗口 —— 只做 GUI 壳与资产落盘，不做生成逻辑。
//    逻辑在家族服务层（baker-service.cs / generator-facade.cs）。
//
//  使用方式：
//    1. 复制到 <category.Script>/<YourTool>/Editor/，类名改 YourToolWindow
//    2. 替换 ⚠️ 标记；按消费族改写行动行：
//       Baker 型 → [Bake][Load][Save]（Bake 带进度条）
//       Generator 型 → [Generate][Save]（预览 hash 缓存）
//    3. UI 文案英文；序列化字段 _ 前缀
//
//  消费差异表 + 实源：本目录 README.md
// ═══════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// YourToolWindow — thin GUI shell over the YourTool service layer.
/// Generates, previews and saves YourTool output. // ⚠️ 类名改为 <YourTool>Window
/// </summary>
public sealed class YourToolWindow : EditorWindow
{
    // ── Serialized state（Inspector 持久化，_ 前缀）───────────────
    [SerializeField] private string _outputPath = string.Empty; // ⚠️ 由用户选择已授权的资产输出路径
    [SerializeField] private Vector2 _scrollPos;
    // ⚠️ 设置参数按需分组：private [System.Serializable] class YourSettings { ... }

    // ── Runtime preview textures（窗口拥有 → OnDisable 释放）──────
    private Texture2D _previewTexture;
    private int _previewHash;            // ⚠️ Generator 型：参数不变不重建
    private bool _ownsTexture;           // 本窗烘焙=true; AssetDatabase 加载=false

    // ════════════════════════════════════════════════════════════
    //  菜单入口
    // ════════════════════════════════════════════════════════════

    [MenuItem("Tools/YourTool")] // ⚠️ 菜单路径
    private static void OpenWindow()
    {
        var window = GetWindow<YourToolWindow>(false, "YourTool", true); // ⚠️ 窗口标题
        window.minSize = new Vector2(360, 480);
    }

    private void OnEnable()
    {
        // ⚠️ Baker 型：框架就绪检查（ObjectField 引用非空）影响 Bake 禁用态
    }

    private void OnDisable()
    {
        ReleasePreview();
    }

    // ════════════════════════════════════════════════════════════
    //  GUI — 标题 → 设置 → 输出路径+Browse → 行动行 → 预览
    // ════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        EditorGUILayout.LabelField("YourTool", EditorStyles.boldLabel); // ⚠️ 窗口内标题
        EditorGUILayout.Space(4);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        DrawSettings();
        EditorGUILayout.Space(8);
        DrawOutputPath();
        EditorGUILayout.Space(8);
        DrawActions();
        EditorGUILayout.Space(8);
        DrawPreview();

        EditorGUILayout.EndScrollView();
    }

    private void DrawSettings()
    {
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");
        // ⚠️ 参数控件：IntSlider / FloatField / EnumPopup / Toggle，见 NoiseGeneratorWindow 标准形
        EditorGUILayout.HelpBox("⚠️ 参数说明", MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    private void DrawOutputPath()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Output", GUILayout.Width(52));
        _outputPath = EditorGUILayout.TextField(_outputPath);
        if (GUILayout.Button("Browse", GUILayout.Width(64)))
        {
            string directory = Path.GetDirectoryName(_outputPath);
            string fileName = Path.GetFileName(_outputPath);
            string selected = EditorUtility.SaveFilePanelInProject(
                "Save Output", fileName, "asset", "Choose output path", directory);
            if (!string.IsNullOrEmpty(selected))
            {
                _outputPath = selected;
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawActions()
    {
        EditorGUILayout.BeginHorizontal();

        // ⚠️ Baker 型行动行：[Bake][Load][Save]
        //   Bake 禁用态 = 框架未就绪；烘焙用 DisplayProgressBar + try/finally
        // ⚠️ Generator 型行动行：[Generate][Save]，Generate 始终可用
        bool canGenerate = true; // ⚠️ Bake 型：_bakeReady
        EditorGUI.BeginDisabledGroup(!canGenerate);
        if (GUILayout.Button("Generate", GUILayout.Height(30))) // ⚠️ Bake 型改 [Bake][Load][Save]
        {
            ReleasePreview();
            _previewTexture = YourService.Generate(); // ⚠️ 委托服务层：YourService.Generate(...)
            _ownsTexture = _previewTexture != null;
            _previewHash = 0; // ⚠️ 强制重建预览
        }
        EditorGUI.EndDisabledGroup();

        // ⚠️ Generator 型可省 Load（可再生）；Baker 型必须 Load 回看已存资产
        if (GUILayout.Button("Load", GUILayout.Height(30)))
        {
            LoadFromAsset();
        }

        bool hasPreview = _previewTexture != null && HasOutputPath();
        EditorGUI.BeginDisabledGroup(!hasPreview);
        if (GUILayout.Button("Save", GUILayout.Height(30)))
        {
            SaveAsset();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawPreview()
    {
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");
        if (_previewTexture != null)
        {
            // ⚠️ 预览尺寸换算：纹理 w/h → 控件矩形（2:1 / 等比）
            Rect rect = GUILayoutUtility.GetRect(256, 256);
            GUI.DrawTexture(rect, _previewTexture, ScaleMode.ScaleToFit);
            EditorGUILayout.LabelField($"{_previewTexture.width}×{_previewTexture.height}"); // ⚠️ Info 行
        }
        else
        {
            EditorGUILayout.HelpBox("No preview yet — Generate first.", MessageType.Info);
        }
        EditorGUILayout.EndVertical();
    }

    // ════════════════════════════════════════════════════════════
    //  落盘与所有权
    // ════════════════════════════════════════════════════════════

    private void LoadFromAsset()
    {
        if (!HasOutputPath()) return;
        // AssetDatabase 加载 → 窗口永不销毁（_ownsTexture = false）
        var loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(_outputPath);
        if (loaded == null)
        {
            Debug.LogWarning($"No asset at {_outputPath}");
            return;
        }
        ReleasePreview(); // 只销毁自建预览，AssetDatabase 资产不受影响
        _previewTexture = loaded;
        _ownsTexture = false;
    }

    private void SaveAsset()
    {
        if (_previewTexture == null || !HasOutputPath()) return;

        if (File.Exists(_outputPath) &&
            !EditorUtility.DisplayDialog("Overwrite?",
                $"Replace existing asset?\n{_outputPath}", "Overwrite", "Cancel"))
        {
            return;
        }

        EnsureFolders(_outputPath);
        var copy = Object.Instantiate(_previewTexture);
        AssetDatabase.DeleteAsset(_outputPath);
        AssetDatabase.CreateAsset(copy, _outputPath);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(copy);
    }

    private bool HasOutputPath()
    {
        return !string.IsNullOrWhiteSpace(_outputPath)
            && _outputPath.StartsWith("Assets/", System.StringComparison.Ordinal)
            && !_outputPath.Contains("..")
            && _outputPath.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureFolders(string assetPath)
    {
        // "Assets/A/B/C.asset" → 逐级 CreateFolder 直到目录存在
        string directory = Path.GetDirectoryName(assetPath);
        if (string.IsNullOrEmpty(directory) || AssetDatabase.IsValidFolder(directory)) return;

        string parent = Path.GetDirectoryName(directory);
        EnsureFolders(parent); // ⚠️ 父级可能也不存在 → 先递归创建
        AssetDatabase.CreateFolder(parent, Path.GetFileName(directory));
    }

    private void ReleasePreview()
    {
        if (_previewTexture == null) return;
        // 铁律：只销毁本窗生成的预览；AssetDatabase 加载的由 Unity 资产系统管理
        if (_ownsTexture)
        {
            DestroyImmediate(_previewTexture);
        }
        _previewTexture = null;
        _ownsTexture = false;
    }
}
#endif
