// ════════════════════════════════════════════════════════════
//  NormalSmootherWindow — 平滑法线 Baker。
//  输入固定为单个 Mesh；固定资产路径 + Bake / Load / Save + 资产所有权。
//  烘焙逻辑在 NormalSmoother，本窗只做设置、预览与落盘。
// ════════════════════════════════════════════════════════════

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Mine.NormalSmoother
{
    /// <summary>
    /// 法线平滑烘焙窗口：把单个网格转换为平滑法线网格资产。
    /// 结果写入顶点法线或顶点色，保存到窗口持有的固定资产路径。
    /// </summary>
    ///
    /// <remarks>
    /// 输入只接受 Mesh 资产（FBX 内的网格同样以 Mesh 引用传入）。
    /// Bake 产物由窗口接管，换源或关闭时释放；Load / Save 后的资产归 AssetDatabase 管理。
    /// </remarks>
    public sealed class NormalSmootherWindow : EditorWindow
    {
        // ── 资产所有权 ──────────────────────────────────────────

        private UnityEngine.Object _asset;

        private bool _ownsAsset;

        // ── 输入（固定为单个 Mesh）───────────────────────────────

        [SerializeField] private Mesh _sourceMesh;

        // ── 烘焙设置 ────────────────────────────────────────────

        [SerializeField] private float _angleThreshold = 60f;
        [SerializeField] private float _positionEpsilon = 0.0001f;
        [SerializeField] private NormalSmoother.StorageMode _storageMode = NormalSmoother.StorageMode.VertexColors;

        // ── 固定资产路径（Save = Load）──────────────────────────

        [SerializeField] private string _assetPath = "Assets/Mine/Generated/NormalSmoother/SmoothedNormal.asset";

        private Vector2 _scrollPosition;

        private PreviewRenderUtility _previewUtility;
        private Material _previewMaterial;

        private string AssetPath
        {
            get => _assetPath;
            set => _assetPath = value;
        }

        private string BakeLabel => "Bake";

        /// <summary>Baker 型：大网格平滑耗时不可忽略，显示进度条。</summary>
        private bool ShowBakeProgress => true;

        /// <summary>源网格就绪且可读时才允许烘焙。</summary>
        private bool CanBake => _sourceMesh != null && _sourceMesh.isReadable;

        /// <summary>只有 Mesh 资产可落盘。</summary>
        private bool HasSaveableAsset => _asset is Mesh;

        // ════════════════════════════════════════════════════════
        //  菜单与生命周期
        // ════════════════════════════════════════════════════════

        [MenuItem("Tools/Normal Smoother...")]
        public static void ShowWindow()
        {
            var window = GetWindow<NormalSmootherWindow>(false, "Normal Smoother", true);
            window.minSize = new Vector2(420f, 520f);
        }

        private void OnDisable()
        {
            ReleaseAsset();
            ReleasePreviewUtility();
        }

        // ════════════════════════════════════════════════════════
        //  GUI — 标题 → 输入 → 设置 → 路径行 → 行动行 → 预览
        // ════════════════════════════════════════════════════════

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.LabelField("Normal Smoother", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            DrawInput();
            EditorGUILayout.Space(8f);

            DrawSettings();
            EditorGUILayout.Space(8f);

            DrawAssetPathRow();
            EditorGUILayout.Space(4f);

            DrawActionRow();
            EditorGUILayout.Space(8f);

            DrawPreview();

            EditorGUILayout.EndScrollView();
        }

        private void DrawInput()
        {
            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            _sourceMesh = (Mesh)EditorGUILayout.ObjectField("Source Mesh", _sourceMesh, typeof(Mesh), false);

            if (_sourceMesh == null)
            {
                EditorGUILayout.HelpBox("Assign a mesh asset to bake.", MessageType.Info);
            }
            else if (!_sourceMesh.isReadable)
            {
                EditorGUILayout.HelpBox(
                    $"Mesh '{_sourceMesh.name}' is not readable. Enable Read/Write on the importer.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField(
                    $"{_sourceMesh.vertexCount} vertices | {_sourceMesh.subMeshCount} submeshes");
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("Smoothing Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            _angleThreshold = EditorGUILayout.Slider("Angle Threshold", _angleThreshold, 0f, 180f);
            _positionEpsilon = EditorGUILayout.FloatField("Position Epsilon", Mathf.Max(1e-6f, _positionEpsilon));
            _storageMode = (NormalSmoother.StorageMode)EditorGUILayout.EnumPopup("Storage Mode", _storageMode);

            EditorGUILayout.HelpBox(
                "Vertex Color stores the smoothed normal as n * 0.5 + 0.5. "
                + "Angle 180 merges every face sharing a position into one normal.",
                MessageType.Info);

            EditorGUILayout.EndVertical();
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            Mesh previewMesh = Asset as Mesh;
            if (previewMesh == null)
            {
                EditorGUILayout.HelpBox("Bake or load a mesh to preview.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            Rect rect = GUILayoutUtility.GetRect(256f, 256f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

            if (Event.current.type == EventType.Repaint)
            {
                RenderPreview(rect, previewMesh);
            }

            EditorGUILayout.LabelField(
                $"{previewMesh.vertexCount} vertices | {previewMesh.subMeshCount} submeshes"
                + $" | {_storageMode}");

            EditorGUILayout.EndVertical();
        }

        // ════════════════════════════════════════════════════════
        //  烘焙 — 委托 NormalSmoother 服务层，结果所有权移交窗口
        // ════════════════════════════════════════════════════════

        private UnityEngine.Object BakeCore()
        {
            NormalSmoother.Settings settings = new NormalSmoother.Settings
            {
                angleThreshold = _angleThreshold,
                positionEpsilon = _positionEpsilon,
                storageMode = _storageMode
            };

            return NormalSmoother.CreateSmoothedMesh(_sourceMesh, settings);
        }

        // ════════════════════════════════════════════════════════
        //  工具可调整
        // ════════════════════════════════════════════════════════

        /// <summary>烘焙完成 / 载入完成后的回调：只需重绘预览。</summary>
        private void OnAssetChanged()
        {
            Repaint();
        }

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
                if (string.IsNullOrEmpty(directory))
                {
                    directory = "Assets";
                }

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

            if (!(loaded is Mesh))
            {
                Debug.LogWarning($"{titleContent.text}: \"{AssetPath}\" does not hold a Mesh.");
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
                    Debug.LogError($"NormalSmoother: invalid save request (source={source}, path=\"{path}\").");
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
                    Debug.LogError($"NormalSmoother: \"{normalized}\" already holds a {existing.GetType().Name}, "
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

        // ════════════════════════════════════════════════════════
        //  预览 — PreviewRenderUtility，窗口自建并负责释放
        // ════════════════════════════════════════════════════════

        private void EnsurePreviewUtility()
        {
            if (_previewUtility == null)
            {
                _previewUtility = new PreviewRenderUtility(true);
                _previewUtility.cameraFieldOfView = 30f;
            }

            if (_previewMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                _previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        private void ReleasePreviewUtility()
        {
            if (_previewUtility != null)
            {
                _previewUtility.Cleanup();
                _previewUtility = null;
            }

            if (_previewMaterial != null)
            {
                DestroyImmediate(_previewMaterial);
                _previewMaterial = null;
            }
        }

        private void RenderPreview(Rect rect, Mesh mesh)
        {
            EnsurePreviewUtility();

            Bounds bounds = mesh.bounds;
            if (bounds.size.sqrMagnitude < 1e-8f)
            {
                bounds = new Bounds(Vector3.zero, Vector3.one);
            }

            _previewUtility.BeginPreview(rect, GUIStyle.none);

            float distance = Mathf.Max(0.01f, bounds.extents.magnitude * 2f);
            Transform cameraTransform = _previewUtility.camera.transform;
            cameraTransform.position = bounds.center + new Vector3(0f, 0f, -distance);
            cameraTransform.rotation = Quaternion.LookRotation(bounds.center - cameraTransform.position, Vector3.up);

            _previewUtility.camera.nearClipPlane = distance * 0.01f;
            _previewUtility.camera.farClipPlane = distance * 10f;

            _previewUtility.lights[0].intensity = 1f;
            _previewUtility.lights[0].transform.rotation = Quaternion.Euler(30f, 30f, 0f);
            _previewUtility.lights[1].intensity = 1f;

            Matrix4x4 matrix = Matrix4x4.TRS(-bounds.center, Quaternion.identity, Vector3.one);
            _previewUtility.DrawMesh(mesh, matrix, _previewMaterial, 0);
            _previewUtility.Render(true);

            Texture texture = _previewUtility.EndPreview();
            if (texture != null)
            {
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
            }
        }
    }
}
