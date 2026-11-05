// Production Tool — 独立 EditorWindow 完整模板。
// 复制到已授权的 <Tool>/Editor/，替换 YourTool、资产路径及 BakeCore 服务调用。
// Bake/Load/Save、原位保存与资产所有权在当前类内；不依赖 agent 或项目通用基类。
// Baker 保留进度条，Generator 将 ShowBakeProgress 改为 false。
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// YourToolWindow — 独立的资产生产窗口，路径与生成服务由具体工具配置。
/// </summary>
public sealed class YourToolWindow : EditorWindow
{
    // ── 资产所有权 ──────────────────────────────────────────
    private UnityEngine.Object _asset;

    private bool _ownsAsset;

    // ── 设置（按需分区，⚠️ 换成真实参数）─────────────────────

    [SerializeField] private int _resolution = 128;

    // ── 固定资产路径（Save = Load）───────────────────────────

    [SerializeField] private string _assetPath = "YOUR_AUTHORIZED_ASSET_PATH"; // ⚠️ 替换为当前工具获准的 Assets/... .asset 路径

    private string AssetPath
    {
        get => _assetPath;
        set => _assetPath = value;
    }

    private string BakeLabel => "Bake";        // ⚠️ Generator 型改 "Generate"

    private bool ShowBakeProgress => true;     // ⚠️ Generator 型改 false

    // ════════════════════════════════════════════════════════
    //  菜单入口
    // ════════════════════════════════════════════════════════

    [MenuItem("Tools/Your Tool...")]                      // ⚠️ 菜单路径
    public static void ShowWindow()
    {
        var window = GetWindow<YourToolWindow>(false, "Your Tool", true); // ⚠️ 窗口标题
        window.minSize = new Vector2(360f, 480f);
        window.Show();
    }

    // ════════════════════════════════════════════════════════
    //  GUI — 标题 → 设置 → 路径行 → 行动行 → 预览
    // ════════════════════════════════════════════════════════

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Your Tool", EditorStyles.boldLabel); // ⚠️ 窗口内标题
        EditorGUILayout.Space(4f);

        _resolution = EditorGUILayout.IntSlider("Resolution", _resolution, 16, 512); // ⚠️ 真实参数

        EditorGUILayout.Space(4f);
        DrawAssetPathRow();

        EditorGUILayout.Space(4f);
        DrawActionRow();

        EditorGUILayout.Space(8f);
        DrawPreview();
    }

    private void DrawPreview()
    {
        Texture2D baked = Asset as Texture2D;             // ⚠️ 资产类型按工具替换
        if (baked == null)
        {
            EditorGUILayout.HelpBox("Bake or load a texture to preview.", MessageType.Info);
            return;
        }

        Rect rect = GUILayoutUtility.GetAspectRect(1f, GUILayout.ExpandWidth(true)); // ⚠️ 换真实宽高比
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawPreviewTexture(rect, baked, null, ScaleMode.ScaleToFit);
        }
    }

    // ════════════════════════════════════════════════════════
    //  烘焙 — 委托服务层，产物所有权交给生成后的窗口
    // ════════════════════════════════════════════════════════

    private UnityEngine.Object BakeCore()
    {
        return YourToolBaker.Bake(_resolution);           // ⚠️ 换真实服务层入口
    }




    // ════════════════════════════════════════════════════════
    //  工具可调整
    // ════════════════════════════════════════════════════════

    /// <summary>当前是否允许烘焙（如场景框架未就绪时为 false）。</summary>
    private bool CanBake => true;

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
