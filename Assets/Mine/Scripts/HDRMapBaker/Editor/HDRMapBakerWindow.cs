#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using Mine.HDRMaps;

namespace Mine.HDRMaps.Editor
{
    /// <summary>
    /// HDR Map Baker Window — 独立窗口：选择场景框架并烘焙 2:1 HDR 全景图。
    /// 生产工具标准结构：固定资产路径 + Bake / Load / Save + 资产所有权。
    /// </summary>
    public sealed class HDRMapBakerWindow : EditorWindow
    {
        // ── 资产所有权 ──────────────────────────────────────────
        private UnityEngine.Object _asset;

        private bool _ownsAsset;

        // ── 场景框架 ────────────────────────────────────────────

        [SerializeField] private HDRMapBaker _framework;

        // ── 烘焙设置 ────────────────────────────────────────────

        [SerializeField] private int _faceResolution = 256;

        // ── 固定资产路径（Save = Load）──────────────────────────

        [SerializeField] private string _assetPath = "Assets/Textures/HDRPanorama.asset";

        private Vector2 _scrollPosition;

        private static readonly int[] FaceResolutionOptions = { 64, 128, 256, 512, 1024, 2048 };

        private static readonly string[] FaceResolutionLabels = { "64", "128", "256", "512", "1024", "2048" };

        private string AssetPath
        {
            get => _assetPath;
            set => _assetPath = value;
        }

        private string BakeLabel => "Bake";

        /// <summary>烘焙是否显示进度条（Baker 型需要；Generator 型瞬时完成）。</summary>
        private bool ShowBakeProgress => true;

        /// <summary>框架缺失或未初始化时不允许烘焙。</summary>
        private bool CanBake => _framework != null && _framework.IsInitialized;

        /// <summary>当前资产是否可落盘（多输出模式的窗口按当前模式判断）。</summary>
        private bool HasSaveableAsset => Asset is Texture2D;

        // ════════════════════════════════════════════════════════
        //  菜单入口
        // ════════════════════════════════════════════════════════

        [MenuItem("Tools/HDR Map Baker...")]
        public static void ShowWindow()
        {
            var window = GetWindow<HDRMapBakerWindow>(false, "HDR Map Baker", true);
            window.minSize = new Vector2(380f, 540f);
            window.Show();
        }

        // ════════════════════════════════════════════════════════
        //  GUI — 框架 → 设置 → 路径行 → 行动行 → 预览
        // ════════════════════════════════════════════════════════

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.LabelField("HDR Map Baker", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            DrawFrameworkSection();

            EditorGUILayout.Space(6f);
            DrawTextureSettings();

            EditorGUILayout.Space(4f);
            DrawAssetPathRow();

            EditorGUILayout.Space(4f);
            DrawActionRow();

            EditorGUILayout.Space(4f);
            DrawOpenConverterRow();

            DrawPreview();

            EditorGUILayout.EndScrollView();
        }

        // ════════════════════════════════════════════════════════
        //  框架分区 — 选择 / 新建 / 修复场景框架
        // ════════════════════════════════════════════════════════

        private void DrawFrameworkSection()
        {
            _framework = (HDRMapBaker)EditorGUILayout.ObjectField(
                "Framework",
                _framework,
                typeof(HDRMapBaker),
                true);

            if (GUILayout.Button("Create Standard Framework", GUILayout.Height(26f)))
            {
                _framework = HDRMapBakerEditorUtility.CreateStandardFramework();
                Repaint();
            }

            DrawFrameworkStatus();
            DrawFrameworkSerializedSettings();
        }

        private void DrawFrameworkStatus()
        {
            if (_framework == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign an existing framework here, or click Create Standard Framework above.",
                    MessageType.Info);
                return;
            }

            if (!_framework.IsInitialized)
            {
                EditorGUILayout.HelpBox(
                    "The selected framework is missing a volume, direction, or bake camera. Click Initialize Framework to repair it.",
                    MessageType.Warning);
                if (GUILayout.Button("Initialize Framework"))
                {
                    HDRMapBakerEditorUtility.InitializeFramework(_framework);
                    Repaint();
                }
                return;
            }

            EditorGUILayout.HelpBox(
                $"Volume: {_framework.Volume.name}    Direction: {_framework.Direction.name}    Camera: {_framework.BakeCamera.name}",
                MessageType.Info);
        }

        /// <summary>通过 SerializedObject 编辑框架的序列化字段，保留 Undo 与持久化。</summary>
        private void DrawFrameworkSerializedSettings()
        {
            if (_framework == null)
            {
                return;
            }

            var serializedFramework = new SerializedObject(_framework);
            SerializedProperty renderLayers = serializedFramework.FindProperty("_renderLayers");
            SerializedProperty includeSky = serializedFramework.FindProperty("_includeSky");
            if (renderLayers == null || includeSky == null)
            {
                EditorGUILayout.HelpBox(
                    "The selected framework does not expose render layers or sky settings.",
                    MessageType.Warning);
                return;
            }

            serializedFramework.Update();
            EditorGUILayout.PropertyField(renderLayers);
            EditorGUILayout.PropertyField(includeSky);
            serializedFramework.ApplyModifiedProperties();
        }

        // ════════════════════════════════════════════════════════
        //  烘焙设置分区
        // ════════════════════════════════════════════════════════

        private void DrawTextureSettings()
        {
            EditorGUILayout.LabelField("Bake Settings", EditorStyles.boldLabel);

            int index = System.Array.IndexOf(FaceResolutionOptions, _faceResolution);
            if (index < 0)
            {
                index = System.Array.IndexOf(FaceResolutionOptions, 256);
            }

            index = EditorGUILayout.Popup("Face Resolution", index, FaceResolutionLabels);
            _faceResolution = FaceResolutionOptions[index];

            EditorGUILayout.HelpBox(
                $"Output texture uses a 2:1 equirectangular HDR panorama layout: {_faceResolution * 2} × {_faceResolution}.",
                MessageType.None);
        }

        // ════════════════════════════════════════════════════════
        //  预览 — 按真实纹理宽高显示
        // ════════════════════════════════════════════════════════

        private void DrawPreview()
        {
            EditorGUILayout.Space(6f);

            Texture2D baked = Asset as Texture2D;
            if (baked == null)
            {
                EditorGUILayout.HelpBox("Bake or load an HDR panorama to preview.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            float aspect = baked.height > 0 ? (float)baked.width / baked.height : 1f;
            Rect previewRect = GUILayoutUtility.GetAspectRect(aspect, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawPreviewTexture(previewRect, baked, null, ScaleMode.ScaleToFit);
            }
        }

        // ════════════════════════════════════════════════════════
        //  烘焙 — 委托 HDRMapCapture 服务层
        // ════════════════════════════════════════════════════════

        private UnityEngine.Object BakeCore()
        {
            return HDRMapCapture.Bake(_framework, _faceResolution);
        }

        // ════════════════════════════════════════════════════════
        //  资产变化回调 / 工具可读
        // ════════════════════════════════════════════════════════

        /// <summary>资产变化（Bake 完成 / Load 完成）后的回调：工具据此刷新自己的预览与状态。</summary>
        private void OnAssetChanged() { }

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

        /// <summary>标准行动行：[Bake] [Load] [Save]。</summary>
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

        /// <summary>把当前烘焙 / 载入的纹理交给转换窗口；Baker 所有权不变。</summary>
        private void DrawOpenConverterRow()
        {
            Texture2D source = Asset as Texture2D;
            using (new EditorGUI.DisabledScope(source == null))
            {
                if (GUILayout.Button("Open Converter", GUILayout.Height(24f)))
                {
                    HDRMapConverterWindow.OpenWithSource(source);
                }
            }
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
                SetAsset(baked, true);      // 新产物不自动落盘
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
                    Debug.LogError($"HDR Map Baker: invalid save request (source={source}, path=\"{path}\").");
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
                    Debug.LogError($"HDR Map Baker: \"{normalized}\" already holds a {existing.GetType().Name}, "
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
