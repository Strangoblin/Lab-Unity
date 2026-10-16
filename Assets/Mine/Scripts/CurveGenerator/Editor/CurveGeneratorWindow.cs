#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// Curve Generator 窗口 —— 把父物体子节点作为控制点烘焙成 CurveAsset。
/// 生产工具标准结构：固定资产路径 + Generate / Load / Save + 资产所有权。
/// 数学部分全部委托 <see cref="CurveBake"/>，本窗只做设置、储存与 Scene 预览。
/// </summary>
public sealed class CurveGeneratorWindow : EditorWindow
{
    // ── 资产所有权 ──────────────────────────────────────────
    private UnityEngine.Object _asset;

    private bool _ownsAsset;

    // ── 输入 ────────────────────────────────────────────────

    [SerializeField] private Transform _controlParent;          // 空物体：读取其直接子节点
    [SerializeField] private CurveAsset.CurveType _curveType = CurveAsset.CurveType.CatmullRom;
    [SerializeField] private CurveAsset.CurveDimension _dimension = CurveAsset.CurveDimension.XYZ;
    [SerializeField] private int _sampleCount = 256;
    [SerializeField] private bool _loop;

    // ── 固定资产路径（Save = Load）──────────────────────────

    [SerializeField] private string _assetPath = "Assets/Mine/Data/Curves/NewCurve.asset";

    // ── 运行时状态 ──────────────────────────────────────────

    private CurveBake.Result _result;
    private bool _hasData;
    private Vector2 _scrollPos;

    private static readonly string[] DimensionLabels = { "XYZ", "XY", "XZ", "YZ" };

    private string AssetPath
    {
        get => _assetPath;
        set => _assetPath = value;
    }

    private string BakeLabel => "Generate";

    /// <summary>曲线生成为瞬时操作，不需要进度条。</summary>
    private bool ShowBakeProgress => false;

    // ════════════════════════════════════════════════════════
    //  菜单入口与生命周期
    // ════════════════════════════════════════════════════════

    [MenuItem("Tools/Curve Generator...")]
    public static void ShowWindow()
    {
        var window = GetWindow<CurveGeneratorWindow>(false, "Curve Generator", true);
        window.minSize = new Vector2(380f, 480f);
        window.Show();
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        ReleaseAsset();
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    // ════════════════════════════════════════════════════════
    //  GUI
    // ════════════════════════════════════════════════════════

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.LabelField("Curve Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space(4f);
        DrawControlPoints();
        EditorGUILayout.Space(6f);
        DrawSettings();
        EditorGUILayout.Space(8f);
        DrawOutput();
        EditorGUILayout.Space(8f);
        DrawInfo();

        EditorGUILayout.EndScrollView();
    }

    private void DrawControlPoints()
    {
        EditorGUILayout.LabelField("Control Points", EditorStyles.boldLabel);

        _controlParent = (Transform)EditorGUILayout.ObjectField(
            "Parent", _controlParent, typeof(Transform), true);

        if (_controlParent != null)
        {
            int childCount = _controlParent.childCount;
            EditorGUILayout.LabelField($"  → {childCount} direct children");
            if (childCount < 2)
            {
                EditorGUILayout.HelpBox("Need at least 2 children as control points.", MessageType.Warning);
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Assign an empty GameObject whose direct children are the control points.", MessageType.Info);
        }
    }

    private void DrawSettings()
    {
        EditorGUILayout.LabelField("Curve Settings", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Dimension", GUILayout.Width(72));
        int dimIndex = GUILayout.Toolbar((int)_dimension, DimensionLabels);
        _dimension = (CurveAsset.CurveDimension)dimIndex;
        EditorGUILayout.EndHorizontal();

        _curveType = (CurveAsset.CurveType)EditorGUILayout.EnumPopup("Type", _curveType);
        _sampleCount = EditorGUILayout.IntSlider("Samples", Mathf.Clamp(_sampleCount, 16, 4096), 16, 4096);
        _loop = EditorGUILayout.Toggle("Loop", _loop);
    }

    private void DrawOutput()
    {
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        DrawAssetPathRow();

        EditorGUILayout.Space(4f);
        DrawActionRow();
    }

    private void DrawInfo()
    {
        if (!_hasData)
        {
            return;
        }

        EditorGUILayout.LabelField("Preview Info", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"  Type: {_curveType}   Dim: {_dimension}   Loop: {_loop}");
        EditorGUILayout.LabelField($"  Samples: {_result.sampleCount}   Length: {_result.totalLength:F2}");
        EditorGUILayout.HelpBox("Curve preview is drawn in the Scene view.", MessageType.Info);
    }

    // ════════════════════════════════════════════════════════
    //  生成 — 烘焙曲线并组装待保存的 CurveAsset
    // ════════════════════════════════════════════════════════

    private UnityEngine.Object BakeCore()
    {
        Vector3[] controlPoints = GatherControlPointPositions();
        if (controlPoints.Length < 2)
        {
            Debug.LogWarning("CurveGenerator: Need at least 2 valid control points.");
            return null;
        }

        _result = CurveBake.Bake(controlPoints, _curveType, _dimension, _sampleCount, _loop);
        Debug.Log($"Curve baked [{_curveType}/{_dimension}]: {_result.sampleCount} samples, length={_result.totalLength:F2}");

        return BuildCurveAsset(controlPoints);
    }

    private CurveAsset BuildCurveAsset(Vector3[] controlPoints)
    {
        var asset = CreateInstance<CurveAsset>();
        asset.curveType = _curveType;
        asset.dimension = _dimension;
        asset.loop = _loop;
        asset.positions = _result.positions;
        asset.tangents = _result.tangents;
        asset.arcLengths = _result.arcLengths;
        asset.normals = _result.normals;
        asset.curvatures = _result.curvatures;
        asset.totalLength = _result.totalLength;
        asset.sampleCount = _result.sampleCount;
        asset.controlPointCount = controlPoints.Length;
        asset.controlPointPositions = controlPoints;
        return asset;
    }

    /// <summary>载入 / 生成后让窗口状态跟随当前资产，Scene 预览据 _result 重绘。</summary>
    private void OnAssetChanged()
    {
        CurveAsset asset = Asset as CurveAsset;
        if (asset == null)
        {
            _hasData = false;
            _result = default;
            return;
        }

        _curveType = asset.curveType;
        _dimension = asset.dimension;
        _loop = asset.loop;
        _result = new CurveBake.Result
        {
            positions = asset.positions,
            tangents = asset.tangents,
            arcLengths = asset.arcLengths,
            normals = asset.normals,
            curvatures = asset.curvatures,
            totalLength = asset.totalLength,
        };
        _hasData = true;
    }

    /// <summary>读取父物体的直接子节点世界坐标作为控制点。</summary>
    private Vector3[] GatherControlPointPositions()
    {
        var points = new List<Vector3>();
        if (_controlParent != null)
        {
            for (int i = 0; i < _controlParent.childCount; i++)
            {
                points.Add(_controlParent.GetChild(i).position);
            }
        }
        return points.ToArray();
    }

    // ════════════════════════════════════════════════════════
    //  Scene 预览 — 控制点、曲线与切线
    // ════════════════════════════════════════════════════════

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!_hasData || _result.positions == null || _result.positions.Length < 2)
        {
            return;
        }

        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        Handles.color = Color.yellow;
        if (_controlParent != null)
        {
            for (int i = 0; i < _controlParent.childCount; i++)
            {
                Transform child = _controlParent.GetChild(i);
                float radius = HandleUtility.GetHandleSize(child.position) * 0.1f;
                Handles.SphereHandleCap(0, child.position, Quaternion.identity, radius, EventType.Repaint);
                Handles.Label(child.position + Vector3.up * radius * 2, $"CP{i}");
            }
        }

        Handles.color = new Color(0, 1, 1, 0.9f);
        for (int i = 0; i < _result.positions.Length - 1; i++)
        {
            Handles.DrawLine(_result.positions[i], _result.positions[i + 1], 1.5f);
        }
        if (_loop)
        {
            Handles.DrawLine(_result.positions[_result.positions.Length - 1], _result.positions[0], 1.5f);
        }

        Handles.color = new Color(1, 0.5f, 0, 0.6f);
        int stride = Mathf.Max(1, _result.positions.Length / 32);
        for (int i = 0; i < _result.positions.Length; i += stride)
        {
            Vector3 point = _result.positions[i];
            Vector3 tangent = _result.tangents[i];
            float length = HandleUtility.GetHandleSize(point) * 0.15f;
            Handles.DrawLine(point, point + tangent * length, 1f);
        }
    }




    // ════════════════════════════════════════════════════════
    //  工具可调整
    // ════════════════════════════════════════════════════════

    /// <summary>当前是否允许烘焙（如场景框架未就绪时为 false）。</summary>
    private bool CanBake => true;

    /// <summary>当前资产是否可落盘（多输出模式的窗口按当前模式判断）。</summary>
    private bool HasSaveableAsset => _asset != null;

    // ── 工具可读 ────────────────────────────────────────────

    /// <summary>当前资产：预览来源与待保存对象。</summary>
    private UnityEngine.Object Asset => _asset;

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
