#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// Noise Generator 窗口 —— 按输出模式生成程序化噪声纹理。
/// 生产工具标准结构：固定资产路径 + Generate / Load / Save + 资产所有权。
/// 生成逻辑在 facade <see cref="NoiseGenerator"/>，本窗负责设置分区、hash 缓存预览与输出。
/// </summary>
public sealed class NoiseGeneratorWindow : EditorWindow
{
    // ── 资产所有权 ──────────────────────────────────────────
    private UnityEngine.Object _asset;

    private bool _ownsAsset;

    private enum OutputMode
    {
        Texture3D,
        Texture2D
    }

    [System.Serializable]
    private class Texture3DSettings
    {
        public int size = 32;
        public float scale = 4f;
        public bool seamless = true;
        public NoiseGenerator.NoiseType noiseType = NoiseGenerator.NoiseType.Perlin;
    }

    [System.Serializable]
    private class ChannelSettings
    {
        public int resolution = 32;
        public float scale = 4f;
        public bool seamless = true;
        public NoiseGenerator.NoiseType noiseType = NoiseGenerator.NoiseType.Perlin;
        public float randomSeed = 0f;
        public int previewHash;
        [System.NonSerialized] public Texture2D previewTexture;   // 运行时缓存，hash 不符时重建
    }

    // ── 序列化状态 ──────────────────────────────────────────

    [SerializeField] private OutputMode _outputMode;
    [SerializeField] private Texture3DSettings _texture3DSettings = new Texture3DSettings();
    [SerializeField] private ChannelSettings[] _channelSettings = new ChannelSettings[4];
    [SerializeField] private int _activeChannelCount = 4;
    [SerializeField] private int[] _previewSlices3D = new int[3];
    [SerializeField] private int _selectedChannel2D;
    [SerializeField] private int _selectedAxis3D;
    [SerializeField] private Vector2 _scrollPos;

    // ── 固定资产路径（Save = Load，按输出模式各一条）─────────

    [SerializeField] private string _assetPath2D = "Assets/Textures/Noise2D.asset";
    [SerializeField] private string _assetPath3D = "Assets/Textures/Noise3D.asset";

    // ── 运行时预览纹理（窗口自有，OnDisable 释放）────────────

    private Texture2D[] _sliceTextures3D = new Texture2D[3];
    private int[] _sliceHashes3D = new int[3];
    private int _packedHash;

    private static readonly string[] AxisLabels = { "X", "Y", "Z" };
    private static readonly string[] ChannelLabels = { "R", "G", "B", "A" };

    // ── 当前资产按模式取用 ──────────────────────────────────

    private Texture3D Current3D => Asset as Texture3D;
    private Texture2D Current2D => Asset as Texture2D;

    private string AssetPath
    {
        get => _outputMode == OutputMode.Texture3D ? _assetPath3D : _assetPath2D;
        set
        {
            if (_outputMode == OutputMode.Texture3D) _assetPath3D = value;
            else _assetPath2D = value;
        }
    }

    private string BakeLabel => "Generate";

    /// <summary>生成为瞬时操作，不需要进度条。</summary>
    private bool ShowBakeProgress => false;

    /// <summary>只有当前模式下产出的资产可落盘。</summary>
    private bool HasSaveableAsset =>
        _outputMode == OutputMode.Texture3D ? Current3D != null : Current2D != null;

    // ═══════════════════════════════════════════════════════
    //  菜单与生命周期
    // ═══════════════════════════════════════════════════════

    [MenuItem("Tools/Noise Generator...")]
    public static void ShowWindow()
    {
        var window = GetWindow<NoiseGeneratorWindow>(false, "Noise Generator", true);
        window.minSize = new Vector2(440, 520);
        window.Show();
    }

    private void OnEnable()
    {
        EnsureChannelSettings();
        EnsureSliceState();
    }

    private void OnDisable()
    {
        ReleaseAsset();
        ReleaseTexture(ref _sliceTextures3D);
        ReleaseChannelPreviews();
    }

    // ═══════════════════════════════════════════════════════
    //  窗口布局
    // ═══════════════════════════════════════════════════════

    private void OnGUI()
    {
        EnsureChannelSettings();
        EnsureSliceState();

        EditorGUILayout.LabelField("Noise Generator", EditorStyles.boldLabel);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        _outputMode = (OutputMode)GUILayout.Toolbar((int)_outputMode, new[] { "Texture3D", "Texture2D" });

        EditorGUILayout.Space(6);
        if (_outputMode == OutputMode.Texture3D)
        {
            DrawTexture3DSettings();
        }
        else
        {
            DrawTexture2DSettings();
        }

        EditorGUILayout.Space(8);
        DrawOutputSection();

        EditorGUILayout.Space(8);
        DrawPreviewSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawTexture3DSettings()
    {
        EditorGUILayout.LabelField("Texture3D Settings", EditorStyles.boldLabel);
        _texture3DSettings.size = EditorGUILayout.IntSlider("Size", Mathf.Clamp(_texture3DSettings.size, 4, 256), 4, 256);
        _texture3DSettings.scale = EditorGUILayout.FloatField("Scale", Mathf.Max(0.0001f, _texture3DSettings.scale));
        _texture3DSettings.seamless = EditorGUILayout.Toggle("Seamless", _texture3DSettings.seamless);
        _texture3DSettings.noiseType = (NoiseGenerator.NoiseType)EditorGUILayout.EnumPopup("Noise Type", _texture3DSettings.noiseType);
    }

    private void DrawTexture2DSettings()
    {
        EditorGUILayout.LabelField("Texture2D Settings", EditorStyles.boldLabel);
        _activeChannelCount = EditorGUILayout.IntSlider("Channels", Mathf.Clamp(_activeChannelCount, 1, 4), 1, 4);
        EditorGUILayout.HelpBox("Final packed output uses the max resolution of active channels.", MessageType.Info);

        _selectedChannel2D = DrawChannelSelector(_selectedChannel2D, _activeChannelCount);
        DrawChannelSettings(_selectedChannel2D);
    }

    private void DrawChannelSettings(int index)
    {
        ChannelSettings settings = _channelSettings[index];

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Channel {ChannelLabels[index]}", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Randomize", GUILayout.Width(88)))
        {
            settings.randomSeed = Random.value * 100000f;
        }
        EditorGUILayout.EndHorizontal();

        settings.resolution = EditorGUILayout.IntSlider("Resolution", Mathf.Clamp(settings.resolution, 8, 2048), 8, 2048);
        settings.scale = EditorGUILayout.FloatField("Scale", Mathf.Max(0.0001f, settings.scale));
        settings.seamless = EditorGUILayout.Toggle("Seamless", settings.seamless);
        settings.noiseType = (NoiseGenerator.NoiseType)EditorGUILayout.EnumPopup("Noise Type", settings.noiseType);
        settings.randomSeed = EditorGUILayout.FloatField("Random Seed", settings.randomSeed);

        UpdateChannelPreview(index);
        DrawTexturePreview(settings.previewTexture);

        EditorGUILayout.EndVertical();
    }

    private int DrawChannelSelector(int currentIndex, int channelCount)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Channel", GUILayout.Width(52));

        int selected = currentIndex;
        for (int i = 0; i < 4; i++)
        {
            using (new EditorGUI.DisabledScope(i >= channelCount))
            {
                bool isSelected = selected == i;
                if (GUILayout.Toggle(isSelected, ChannelLabels[i], "Button", GUILayout.Height(22)))
                {
                    selected = i;
                }
            }
        }

        EditorGUILayout.EndHorizontal();
        return Mathf.Clamp(selected, 0, Mathf.Max(0, channelCount - 1));
    }

    private void DrawPreviewSection()
    {
        if (_outputMode == OutputMode.Texture3D)
        {
            DrawTexture3DPreviewSection();
        }
        else
        {
            DrawTexture2DPreviewSection();
        }
    }

    private void DrawTexture3DPreviewSection()
    {
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        _selectedAxis3D = GUILayout.Toolbar(_selectedAxis3D, AxisLabels);
        EditorGUILayout.Space(6);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField($"Axis {AxisLabels[_selectedAxis3D]} Preview", EditorStyles.boldLabel);

        if (Current3D != null && !Current3D.isReadable)
        {
            EditorGUILayout.HelpBox("This texture is not readable; slice preview is unavailable.", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        int maxSlice = GetSliceDimensions(_selectedAxis3D).z - 1;
        _previewSlices3D[_selectedAxis3D] = EditorGUILayout.IntSlider(
            "Slice", Mathf.Clamp(_previewSlices3D[_selectedAxis3D], 0, maxSlice), 0, maxSlice);

        UpdateSlicePreview3D(_selectedAxis3D);
        DrawTexturePreview(_sliceTextures3D[_selectedAxis3D]);

        EditorGUILayout.EndVertical();
    }

    private void DrawTexture2DPreviewSection()
    {
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Packed RGBA Preview", EditorStyles.boldLabel);
        UpdatePackedPreviewTexture();
        DrawTexturePreview(Current2D);
        EditorGUILayout.EndVertical();
    }

    private void DrawTexturePreview(Texture2D texture)
    {
        if (texture == null)
        {
            EditorGUILayout.HelpBox("Preview is not ready.", MessageType.None);
            return;
        }

        float width = EditorGUIUtility.currentViewWidth - 40f;
        Rect rect = GUILayoutUtility.GetRect(width, width, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
        {
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
        }
    }

    // ═══════════════════════════════════════════════════════
    //  输出分区 — 固定资产路径 + Generate / Load / Save
    // ═══════════════════════════════════════════════════════

    private void DrawOutputSection()
    {
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        DrawAssetPathRow();
        EditorGUILayout.Space(4);
        DrawActionRow();
    }

    // ═══════════════════════════════════════════════════════
    //  预览缓存更新
    // ═══════════════════════════════════════════════════════

    private void UpdateSlicePreview3D(int axis)
    {
        if (Current3D != null && !Current3D.isReadable)
        {
            ReleaseTexture(ref _sliceTextures3D[axis]);
            return;
        }

        Vector3Int dimensions = GetSliceDimensions(axis);
        int size = dimensions.x;
        int maxSlice = dimensions.z - 1;
        _previewSlices3D[axis] = Mathf.Clamp(_previewSlices3D[axis], 0, maxSlice);

        int hash = GetSlicePreviewHash(axis, size);
        if (_sliceTextures3D[axis] != null && _sliceTextures3D[axis].width == dimensions.x
            && _sliceTextures3D[axis].height == dimensions.y && _sliceHashes3D[axis] == hash)
        {
            return;
        }

        ReleaseTexture(ref _sliceTextures3D[axis]);
        _sliceTextures3D[axis] = new Texture2D(dimensions.x, dimensions.y, TextureFormat.R8, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat
        };

        Color[] colors = new Color[dimensions.x * dimensions.y];
        for (int y = 0; y < dimensions.y; y++)
        {
            for (int x = 0; x < dimensions.x; x++)
            {
                float value = Sample3DSlice(axis, x, y, _previewSlices3D[axis], size);
                colors[x + y * dimensions.x] = new Color(value, value, value, 1f);
            }
        }

        _sliceTextures3D[axis].SetPixels(colors);
        _sliceTextures3D[axis].Apply(false, false);
        _sliceHashes3D[axis] = hash;
    }

    private void UpdateChannelPreview(int index)
    {
        ChannelSettings settings = _channelSettings[index];
        int resolution = Mathf.Clamp(settings.resolution, 8, 2048);
        int hash = GetChannelPreviewHash(settings, resolution);

        if (settings.previewTexture != null && settings.previewTexture.width == resolution
            && settings.previewTexture.height == resolution && settings.previewHash == hash)
        {
            return;
        }

        ReleaseTexture(ref settings.previewTexture);
        settings.previewTexture = NoiseGenerator.GenerateChannelTexture(
            resolution, settings.scale, settings.seamless, settings.noiseType, settings.randomSeed);
        settings.previewHash = hash;
    }

    /// <summary>载入 / 保存的资产保持原样；仅未落盘的程序化预览随参数更新。</summary>
    private void UpdatePackedPreviewTexture()
    {
        if (Current2D != null && EditorUtility.IsPersistent(Current2D))
        {
            return;
        }

        int activeCount = Mathf.Clamp(_activeChannelCount, 1, 4);
        int outputResolution = GetPackedResolution(activeCount);
        int hash = GetPackedPreviewHash(activeCount, outputResolution);

        Texture2D packed = Current2D;
        if (packed != null && packed.width == outputResolution && _packedHash == hash)
        {
            return;
        }

        SetAsset(BuildPackedTexture(activeCount, outputResolution), true);
        _packedHash = hash;
    }

    private Texture2D BuildPackedTexture(int activeCount, int outputResolution)
    {
        var sources = new Texture2D[activeCount];
        for (int i = 0; i < activeCount; i++)
        {
            sources[i] = _channelSettings[i].previewTexture;
        }
        return NoiseGenerator.PackChannels(sources, outputResolution);
    }

    private float Sample3DSlice(int axis, int x, int y, int slice, int size)
    {
        int xi;
        int yi;
        int zi;

        switch (axis)
        {
            case 0:
                xi = slice;
                yi = y;
                zi = x;
                break;
            case 1:
                xi = x;
                yi = slice;
                zi = y;
                break;
            default:
                xi = x;
                yi = y;
                zi = slice;
                break;
        }

        if (Current3D != null)
        {
            return Current3D.GetPixel(xi, yi, zi).r;
        }

        float wx = (float)xi / size;
        float wy = (float)yi / size;
        float wz = (float)zi / size;
        return NoiseGenerator.Sample3D(wx, wy, wz, _texture3DSettings.scale,
            _texture3DSettings.seamless, _texture3DSettings.noiseType);
    }

    private Vector3Int GetSliceDimensions(int axis)
    {
        Texture3D texture = Current3D;
        int size = Mathf.Clamp(_texture3DSettings.size, 4, 256);
        int width = texture != null ? texture.width : size;
        int height = texture != null ? texture.height : size;
        int depth = texture != null ? texture.depth : size;

        switch (axis)
        {
            case 0: return new Vector3Int(depth, height, width);
            case 1: return new Vector3Int(width, depth, height);
            default: return new Vector3Int(width, height, depth);
        }
    }

    private void OnAssetChanged()
    {
        ReleaseTexture(ref _sliceTextures3D);
    }

    private int GetPackedResolution(int activeCount)
    {
        int outputResolution = 8;
        for (int i = 0; i < activeCount; i++)
        {
            outputResolution = Mathf.Max(outputResolution, Mathf.Clamp(_channelSettings[i].resolution, 8, 2048));
        }
        return outputResolution;
    }

    // ═══════════════════════════════════════════════════════
    //  预览 hash
    // ═══════════════════════════════════════════════════════

    private int GetSlicePreviewHash(int axis, int size)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + axis;
            hash = hash * 31 + size;
            hash = hash * 31 + _texture3DSettings.scale.GetHashCode();
            hash = hash * 31 + _texture3DSettings.seamless.GetHashCode();
            hash = hash * 31 + _texture3DSettings.noiseType.GetHashCode();
            hash = hash * 31 + _previewSlices3D[axis];
            return hash;
        }
    }

    private int GetChannelPreviewHash(ChannelSettings settings, int resolution)
    {
        unchecked
        {
            int hash = 23;
            hash = hash * 31 + resolution;
            hash = hash * 31 + settings.scale.GetHashCode();
            hash = hash * 31 + settings.seamless.GetHashCode();
            hash = hash * 31 + settings.noiseType.GetHashCode();
            hash = hash * 31 + settings.randomSeed.GetHashCode();
            return hash;
        }
    }

    private int GetPackedPreviewHash(int activeCount, int resolution)
    {
        unchecked
        {
            int hash = 29;
            hash = hash * 31 + activeCount;
            hash = hash * 31 + resolution;
            for (int i = 0; i < activeCount; i++)
            {
                hash = hash * 31 + _channelSettings[i].previewHash;
            }
            return hash;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  生成 — 委托 NoiseGenerator facade，结果所有权移交基类
    // ═══════════════════════════════════════════════════════

    private UnityEngine.Object BakeCore()
    {
        EnsureChannelSettings();

        if (_outputMode == OutputMode.Texture3D)
        {
            int size = Mathf.Clamp(_texture3DSettings.size, 4, 256);
            Texture3D texture = NoiseGenerator.Generate3DTexture(
                size, _texture3DSettings.scale, _texture3DSettings.seamless, _texture3DSettings.noiseType);
            Debug.Log($"3D {_texture3DSettings.noiseType} generated. Size: {size}");
            return texture;
        }

        int activeCount = Mathf.Clamp(_activeChannelCount, 1, 4);
        for (int i = 0; i < activeCount; i++)
        {
            UpdateChannelPreview(i);
        }

        int outputResolution = GetPackedResolution(activeCount);
        Texture2D packed = BuildPackedTexture(activeCount, outputResolution);
        _packedHash = GetPackedPreviewHash(activeCount, outputResolution);
        Debug.Log($"2D packed noise generated. Channels: {activeCount}, Resolution: {outputResolution}x{outputResolution}");
        return packed;
    }

    // ═══════════════════════════════════════════════════════
    //  运行时状态与纹理释放
    // ═══════════════════════════════════════════════════════

    private void EnsureChannelSettings()
    {
        if (_channelSettings == null || _channelSettings.Length != 4)
        {
            _channelSettings = new ChannelSettings[4];
        }

        for (int i = 0; i < _channelSettings.Length; i++)
        {
            if (_channelSettings[i] == null)
            {
                _channelSettings[i] = new ChannelSettings();
            }
        }

        _activeChannelCount = Mathf.Clamp(_activeChannelCount, 1, 4);
    }

    private void EnsureSliceState()
    {
        if (_previewSlices3D == null || _previewSlices3D.Length != 3)
        {
            _previewSlices3D = new int[3];
        }
        if (_sliceTextures3D == null || _sliceTextures3D.Length != 3)
        {
            _sliceTextures3D = new Texture2D[3];
        }
        if (_sliceHashes3D == null || _sliceHashes3D.Length != 3)
        {
            _sliceHashes3D = new int[3];
        }

        _selectedChannel2D = Mathf.Clamp(_selectedChannel2D, 0, 3);
        _selectedAxis3D = Mathf.Clamp(_selectedAxis3D, 0, 2);
    }

    private void ReleaseChannelPreviews()
    {
        if (_channelSettings == null)
        {
            return;
        }

        for (int i = 0; i < _channelSettings.Length; i++)
        {
            if (_channelSettings[i] != null)
            {
                ReleaseTexture(ref _channelSettings[i].previewTexture);
            }
        }
    }

    private static void ReleaseTexture(ref Texture2D texture)
    {
        if (texture != null)
        {
            DestroyImmediate(texture);
            texture = null;
        }
    }

    private static void ReleaseTexture(ref Texture2D[] textures)
    {
        if (textures == null)
        {
            return;
        }

        for (int i = 0; i < textures.Length; i++)
        {
            ReleaseTexture(ref textures[i]);
        }
    }




    // ════════════════════════════════════════════════════════
    //  工具可调整
    // ════════════════════════════════════════════════════════

    /// <summary>当前是否允许烘焙（如场景框架未就绪时为 false）。</summary>
    private bool CanBake => true;

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
