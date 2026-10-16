#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using Mine.HDRMaps;

namespace Mine.HDRMaps.Editor
{
    /// <summary>
    /// HDR Map Converter Window — 独立窗口：把一张 HDR 图在投影之间转换并预览 / 落盘。
    /// 生产工具标准结构：固定资产路径 + Convert / Load / Save + 资产所有权。
    /// </summary>
    public sealed class HDRMapConverterWindow : EditorWindow
    {
        // ── 资产所有权 ──────────────────────────────────────────
        private UnityEngine.Object _asset;

        private bool _ownsAsset;

        // ── 源与目标设置 ────────────────────────────────────────

        [SerializeField] private Texture2D _source;
        private Texture2D _ownedSource;
        [SerializeField] private HDRMapProjection _sourceProjection = HDRMapProjection.Equirectangular;
        [SerializeField] private HDRMapProjection _targetProjection = HDRMapProjection.Equirectangular;
        [SerializeField] private HDRMapCoverage _coverage = HDRMapCoverage.Panorama;
        [SerializeField] private int _resolution = 512;

        // ── 固定资产路径（Save = Load）──────────────────────────

        [SerializeField] private string _assetPath = "Assets/Textures/HDRSamplingMap.asset";

        private Vector2 _scrollPosition;

        private static readonly int[] ResolutionOptions = { 64, 128, 256, 512, 1024, 2048, 4096 };

        private static readonly string[] ResolutionLabels = { "64", "128", "256", "512", "1024", "2048", "4096" };

        private string AssetPath
        {
            get => _assetPath;
            set => _assetPath = value;
        }

        private string ConvertLabel => "Convert";

        /// <summary>转换是否显示进度条（逐像素服务可能耗时）。</summary>
        private bool ShowConvertProgress => true;

        /// <summary>没有源纹理时不允许转换。</summary>
        private bool CanConvert => _source != null;

        /// <summary>当前结果是否可落盘（仅接受 Texture2D）。</summary>
        private bool HasSaveableAsset => Asset is Texture2D;

        // ════════════════════════════════════════════════════════
        //  菜单入口
        // ════════════════════════════════════════════════════════

        [MenuItem("Tools/HDR Map Converter...")]
        public static void ShowWindow()
        {
            var window = GetWindow<HDRMapConverterWindow>(false, "HDR Map Converter", true);
            window.minSize = new Vector2(400f, 580f);
            window.Show();
        }

        /// <summary>打开窗口并把 source 设为源；不克隆、不销毁 source，也不改变其所有权。</summary>
        public static void OpenWithSource(Texture2D source)
        {
            var window = GetWindow<HDRMapConverterWindow>(false, "HDR Map Converter", true);
            window.minSize = new Vector2(400f, 580f);

            window.AssignSource(source);

            window._sourceProjection = HDRMapProjection.Equirectangular;
            window.Show();
            window.Repaint();
        }

        // ════════════════════════════════════════════════════════
        //  GUI — 源 → 目标 → 路径行 → 行动行 → 预览
        // ════════════════════════════════════════════════════════

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.LabelField("HDR Map Converter", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            DrawSourceSection();

            EditorGUILayout.Space(6f);
            DrawTargetSection();

            EditorGUILayout.Space(4f);
            DrawAssetPathRow();

            EditorGUILayout.Space(4f);
            DrawActionRow();

            DrawPreview();

            EditorGUILayout.EndScrollView();
        }

        // ════════════════════════════════════════════════════════
        //  源分区 — 源纹理与源投影
        // ════════════════════════════════════════════════════════

        private void DrawSourceSection()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);

            Texture2D assigned = (Texture2D)EditorGUILayout.ObjectField(
                "Source Texture",
                _source,
                typeof(Texture2D),
                false);

            if (assigned != _source)
            {
                if (_ownsAsset && assigned == (_asset as Texture2D))
                {
                    Debug.LogWarning(
                        $"{titleContent.text}: the converter's own generated output cannot be used as its source.");
                }
                else
                {
                    AssignSource(assigned);
                }
            }

            _sourceProjection = (HDRMapProjection)EditorGUILayout.EnumPopup(
                "Source Projection",
                _sourceProjection);

            EditorGUILayout.HelpBox(
                "Select the source projection explicitly. Interior keeps the forward (+Z) half; Panorama keeps all available directions.",
                MessageType.None);
        }

        // ════════════════════════════════════════════════════════
        //  目标分区 — 目标投影 / 覆盖 / 输出分辨率
        // ════════════════════════════════════════════════════════

        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);

            _targetProjection = (HDRMapProjection)EditorGUILayout.EnumPopup(
                "Target Projection",
                _targetProjection);
            _coverage = (HDRMapCoverage)EditorGUILayout.EnumPopup("Coverage", _coverage);

            int index = System.Array.IndexOf(ResolutionOptions, _resolution);
            if (index < 0)
            {
                index = System.Array.IndexOf(ResolutionOptions, 512);
            }

            index = EditorGUILayout.Popup("Output Resolution", index, ResolutionLabels);
            _resolution = ResolutionOptions[index];

            DrawTargetNotes();
        }

        private void DrawTargetNotes()
        {
            string outputSize = _targetProjection == HDRMapProjection.Equirectangular
                ? $"{_resolution * 2} × {_resolution}"
                : $"{_resolution} × {_resolution}";
            EditorGUILayout.HelpBox(
                $"Resolution {_resolution} maps to a {outputSize} output for {_targetProjection}.",
                MessageType.None);

            if (_coverage == HDRMapCoverage.Interior)
            {
                EditorGUILayout.HelpBox(
                    "Interior coverage writes the +Z half sphere; pixels outside it are transparent with alpha 0.",
                    MessageType.None);
            }

            if (_targetProjection == HDRMapProjection.Hemisphere)
            {
                EditorGUILayout.HelpBox(
                    "Hemisphere represents only the +Z half sphere.",
                    MessageType.None);
            }
        }

        // ════════════════════════════════════════════════════════
        //  预览 — 按真实纹理宽高显示
        // ════════════════════════════════════════════════════════

        private void DrawPreview()
        {
            EditorGUILayout.Space(6f);

            Texture2D converted = Asset as Texture2D;
            if (converted == null)
            {
                EditorGUILayout.HelpBox("Convert or load an HDR map to preview.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            float aspect = converted.height > 0 ? (float)converted.width / converted.height : 1f;
            Rect previewRect = GUILayoutUtility.GetAspectRect(aspect, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawPreviewTexture(previewRect, converted, null, ScaleMode.ScaleToFit);
            }
        }

        // ════════════════════════════════════════════════════════
        //  转换 — 委托 HDRMapConverter 服务层
        // ════════════════════════════════════════════════════════

        private UnityEngine.Object ConvertCore()
        {
            return HDRMapConverter.Convert(
                _source,
                _sourceProjection,
                _targetProjection,
                _resolution,
                _coverage);
        }

        // ════════════════════════════════════════════════════════
        //  资产变化回调 / 工具可读
        // ════════════════════════════════════════════════════════

        /// <summary>资产变化（Convert 完成 / Load 完成）后的回调：工具据此刷新自己的预览与状态。</summary>
        private void OnAssetChanged() { }

        /// <summary>当前资产：预览来源与待保存对象。</summary>
        private UnityEngine.Object Asset => _asset;

        // ════════════════════════════════════════════════════════
        //  生命周期 — 窗口关闭时释放自产资产
        // ════════════════════════════════════════════════════════

        private void OnDisable()
        {
            ReleaseAsset();
            ReleaseSource();
        }

        private void AssignSource(Texture2D source)
        {
            if (source == _source) return;
            ReleaseSource();
            if (source != null && !EditorUtility.IsPersistent(source))
            {
                _ownedSource = Instantiate(source);
                _ownedSource.name = source.name + "_ConverterSource";
                _ownedSource.hideFlags = HideFlags.HideAndDontSave;
                _source = _ownedSource;
            }
            else _source = source;
        }

        private void ReleaseSource()
        {
            if (_ownedSource != null) DestroyImmediate(_ownedSource);
            _ownedSource = null;
            _source = null;
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

        /// <summary>标准行动行：[Convert] [Load] [Save]。</summary>
        private void DrawActionRow()
        {
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!CanConvert))
            {
                if (GUILayout.Button(ConvertLabel, GUILayout.Height(30f)))
                {
                    Convert();
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
        //  行动实现 — 转换 / 回看 / 落盘
        // ════════════════════════════════════════════════════════

        private void Convert()
        {
            UnityEngine.Object converted = null;
            if (ShowConvertProgress)
            {
                EditorUtility.DisplayProgressBar(titleContent.text, "Converting...", 0.5f);
            }

            try
            {
                converted = ConvertCore();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                if (ShowConvertProgress)
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            if (converted != null)
            {
                SetAsset(converted, true);      // 新产物不自动落盘
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
            if (!(loaded is Texture2D texture))
            {
                Debug.LogWarning($"{titleContent.text}: no Texture2D at \"{AssetPath}\".");
                return;
            }

            SetAsset(texture, false);
        }

        private void SaveToAsset()
        {
            if (!(Asset is Texture2D))
            {
                Debug.LogWarning($"{titleContent.text}: only a Texture2D result can be saved.");
                return;
            }

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

        // ════════════════════════════════════════════════════════
        //  资产文件 — 路径校验 / 回看 / 原位落盘
        // ════════════════════════════════════════════════════════

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
                    Debug.LogError($"HDR Map Converter: invalid save request (source={source}, path=\"{path}\").");
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
                    Debug.LogError($"HDR Map Converter: \"{normalized}\" already holds a {existing.GetType().Name}, "
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
}
#endif
