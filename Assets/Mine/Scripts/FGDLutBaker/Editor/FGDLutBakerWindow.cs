// ════════════════════════════════════════════════════════════
//  FGDLutBakerWindow — FGD LUT 烘焙窗口。
//  生产工具标准结构：固定资产路径 + Bake / Load / Save + 资产所有权。
//  烘焙逻辑在服务层 FGDLutBaker，本窗只做设置与预览。
// ════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Mine.FGDLutBaker;
using System.IO;

/// <summary>
/// FGD LUT 烘焙窗口 —— 固定落在 Assets/Textures/FGD_LUT.asset。
/// </summary>
public sealed class FGDLutBakerWindow : EditorWindow
{
    // ── 资产所有权 ──────────────────────────────────────────
    private UnityEngine.Object _asset;

    private bool _ownsAsset;

    // ── 烘焙设置 ────────────────────────────────────────────

    [SerializeField] private int _resolution = 128;
    [SerializeField] private int _sampleCount = 1024;

    // ── 固定资产路径（Save = Load）──────────────────────────

    [SerializeField] private string _assetPath = "Assets/Textures/FGD_LUT.asset";

    private Vector2 _scrollPos;

    private string AssetPath
    {
        get => _assetPath;
        set => _assetPath = value;
    }

    private string BakeLabel => "Bake";

    // ════════════════════════════════════════════════════════
    //  菜单入口
    // ════════════════════════════════════════════════════════

    [MenuItem("Tools/FGD Lut Baker...")]
    public static void ShowWindow()
    {
        var window = GetWindow<FGDLutBakerWindow>(false, "FGD Lut Baker", true);
        window.minSize = new Vector2(360f, 400f);
        window.Show();
    }

    // ════════════════════════════════════════════════════════
    //  GUI — 设置 → 路径行 → 行动行 → 预览
    // ════════════════════════════════════════════════════════

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.LabelField("FGD Lut Baker", EditorStyles.boldLabel);
        EditorGUILayout.Space(4f);

        _resolution = EditorGUILayout.IntSlider("Resolution", _resolution, 16, 512);
        _sampleCount = EditorGUILayout.IntSlider("Sample Count", _sampleCount, 64, 8192);

        EditorGUILayout.Space(4f);
        DrawAssetPathRow();

        EditorGUILayout.Space(4f);
        DrawActionRow();

        EditorGUILayout.Space(4f);
        DrawPreview();

        EditorGUILayout.EndScrollView();
    }

    private void DrawPreview()
    {
        Texture2D lut = Asset as Texture2D;
        if (lut == null)
        {
            EditorGUILayout.HelpBox("Bake or load a FGD LUT to preview.", MessageType.Info);
            return;
        }

        float size = EditorGUIUtility.currentViewWidth - 32f;
        Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawPreviewTexture(rect, lut, null, ScaleMode.ScaleToFit);
        }
    }

    // ════════════════════════════════════════════════════════
    //  烘焙 — 委托 FGDLutBaker 服务层，结果所有权移交基类
    // ════════════════════════════════════════════════════════

    private UnityEngine.Object BakeCore()
    {
        return FGDLutBaker.Bake(_resolution, _sampleCount);
    }




    // ════════════════════════════════════════════════════════
    //  工具可调整
    // ════════════════════════════════════════════════════════

    /// <summary>当前是否允许烘焙（如场景框架未就绪时为 false）。</summary>
    private bool CanBake => true;

    /// <summary>烘焙是否显示进度条（Baker 型需要；Generator 型瞬时完成）。</summary>
    private bool ShowBakeProgress => true;

    /// <summary>当前资产是否可落盘（多输出模式的窗口按当前模式判断）。</summary>
    private bool HasSaveableAsset => _asset != null;

    /// <summary>资产变化（Bake 完成 / Load 完成）后的回调：工具据此刷新自己的预览与状态。</summary>
    private void OnAssetChanged() { }

    // ── 工具可读 ────────────────────────────────────────────

    /// <summary>当前资产：预览来源与待保存对象。</summary>
    private UnityEngine.Object Asset => _asset;

    // ════════════════════════════════════════════════════════
    //  生命周期 — 窗口关闭时释放自产资产
    // ════════════════════════════════════════════════════════

    private void OnDisable()
    {
        ReleaseAsset();
    }

    // ════════════════════════════════════════════════════════
    //  共享 UI — 路径行与行动行
    // ════════════════════════════════════════════════════════

    /// <summary>固定资产路径行：可直接编辑 + Browse 选择。</summary>
    private void DrawAssetPathRow(string label = "Asset Path")
    {
        EditorGUILayout.BeginHorizontal();
        AssetPath = EditorGUILayout.TextField(label, AssetPath);
        if (GUILayout.Button("Browse", GUILayout.Width(64f)))
        {
            string directory = Path.GetDirectoryName(AssetPath);
            if (string.IsNullOrEmpty(directory)) directory = "Assets";

            string chosen = EditorUtility.SaveFilePanelInProject(
                titleContent.text, Path.GetFileName(AssetPath), "asset", "Choose asset path", directory);
            if (!string.IsNullOrEmpty(chosen))
            {
                AssetPath = chosen;
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>标准行动行：[Bake / Generate] [Load] [Save]。</summary>
    private void DrawActionRow()
    {
        EditorGUILayout.BeginHorizontal();

        using (new EditorGUI.DisabledScope(!CanBake))
        {
            if (GUILayout.Button(BakeLabel, GUILayout.Height(30f)))
            {
                Bake();
            }
        }

        if (GUILayout.Button("Load", GUILayout.Height(30f)))
        {
            LoadFromAsset();
        }

        using (new EditorGUI.DisabledScope(!HasSaveableAsset))
        {
            if (GUILayout.Button("Save", GUILayout.Height(30f)))
            {
                SaveToAsset();
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    // ════════════════════════════════════════════════════════
    //  行动实现 — 烘焙 / 回看 / 落盘
    // ════════════════════════════════════════════════════════

    private void Bake()
    {
        UnityEngine.Object baked = null;
        if (ShowBakeProgress)
        {
            EditorUtility.DisplayProgressBar(titleContent.text, "Baking...", 0.5f);
        }

        try
        {
            baked = BakeCore();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (ShowBakeProgress)
            {
                EditorUtility.ClearProgressBar();
            }
        }

        if (baked != null)
        {
            SetAsset(baked, true);
        }
    }

    private void LoadFromAsset()
    {
        if (!AssetFiles.IsValidAssetPath(AssetPath))
        {
            Debug.LogWarning($"{titleContent.text}: \"{AssetPath}\" is not a valid asset path.");
            return;
        }

        UnityEngine.Object loaded = AssetFiles.Load(AssetPath);
        if (loaded == null)
        {
            Debug.LogWarning($"{titleContent.text}: no asset at \"{AssetPath}\".");
            return;
        }

        SetAsset(loaded, false);
    }

    private void SaveToAsset()
    {
        UnityEngine.Object saved = AssetFiles.Save(_asset, AssetPath);
        if (saved == null)
        {
            return;
        }

        SetAsset(saved, false);     // 落盘后资产归 AssetDatabase 管理，窗口不再销毁
        EditorGUIUtility.PingObject(saved);
        Debug.Log($"{titleContent.text}: saved to {AssetPath}");
    }

    // ════════════════════════════════════════════════════════
    //  所有权 — 自产资产由窗口接管，落盘 / 载入的资产不销毁
    // ════════════════════════════════════════════════════════

    /// <summary>替换当前资产。owned = true 表示工具移交所有权，窗口负责销毁。</summary>
    private void SetAsset(UnityEngine.Object asset, bool owned)
    {
        ReleaseAsset();
        _asset = asset;
        _ownsAsset = owned;
        OnAssetChanged();
        Repaint();
    }

    private void ReleaseAsset()
    {
        if (_ownsAsset && _asset != null)
        {
            DestroyImmediate(_asset);
        }

        _asset = null;
        _ownsAsset = false;
    }

    private static class AssetFiles
    {
        // ════════════════════════════════════════════════════════
        //  路径校验 — Assets/ 下的 .asset，且不含 . / .. 段
        // ════════════════════════════════════════════════════════

        /// <summary>资产路径是否可安全落盘 / 回看。</summary>
        public static bool IsValidAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalized = Normalize(path);
            if (!normalized.StartsWith("Assets/", System.StringComparison.Ordinal))
            {
                return false;
            }

            if (!normalized.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (string segment in normalized.Split('/'))
            {
                if (segment == "." || segment == "..")
                {
                    return false;
                }
            }

            return true;
        }

        // ════════════════════════════════════════════════════════
        //  回看 / 落盘
        // ════════════════════════════════════════════════════════

        /// <summary>按路径回看已落盘资产；路径非法或资产不存在返回 null。</summary>
        public static UnityEngine.Object Load(string path)
        {
            return IsValidAssetPath(path) ? AssetDatabase.LoadMainAssetAtPath(Normalize(path)) : null;
        }

        /// <summary>
        /// 落盘资产并返回磁盘上的对象实例。
        /// 路径上已有同类型资产时原位更新以保留 GUID；类型不符、路径非法或用户取消覆盖返回 null。
        /// </summary>
        public static UnityEngine.Object Save(UnityEngine.Object source, string path)
        {
            if (source == null || !IsValidAssetPath(path))
            {
                Debug.LogError($"ProductionTools: invalid save request (source={source}, path=\"{path}\").");
                return null;
            }

            string normalized = Normalize(path);
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(normalized);

            if (existing == source)
            {
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }

            if (existing != null && !existing.GetType().IsInstanceOfType(source))
            {
                Debug.LogError($"ProductionTools: \"{normalized}\" already holds a {existing.GetType().Name}, "
                    + $"cannot save a {source.GetType().Name} there.");
                return null;
            }

            if (existing != null && !EditorUtility.DisplayDialog(
                    "Overwrite Asset",
                    $"The file already exists. Overwrite it?\n{normalized}",
                    "Overwrite",
                    "Cancel"))
            {
                return null;
            }

            EnsureFolders(Path.GetDirectoryName(normalized));

            if (existing != null)
            {
                EditorUtility.CopySerialized(source, existing);
                existing.name = Path.GetFileNameWithoutExtension(normalized);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }

            UnityEngine.Object copy = UnityEngine.Object.Instantiate(source);
            copy.name = Path.GetFileNameWithoutExtension(normalized);
            AssetDatabase.CreateAsset(copy, normalized);
            AssetDatabase.SaveAssetIfDirty(copy);
            return copy;
        }

        // ════════════════════════════════════════════════════════
        //  目录 — 逐级补齐 Assets 下缺失目录
        // ════════════════════════════════════════════════════════

        /// <summary>补齐目录及其缺失父级；越出 Assets 的路径直接忽略。</summary>
        public static void EnsureFolders(string directory)
        {
            if (string.IsNullOrEmpty(directory) || AssetDatabase.IsValidFolder(directory))
            {
                return;
            }

            string parent = Normalize(Path.GetDirectoryName(directory));
            if (string.IsNullOrEmpty(parent) || parent == directory
                || !parent.StartsWith("Assets", System.StringComparison.Ordinal))
            {
                return;
            }

            EnsureFolders(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(directory));
        }

        // ════════════════════════════════════════════════════════
        //  私有辅助
        // ════════════════════════════════════════════════════════

        private static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }
    }
}
#endif
