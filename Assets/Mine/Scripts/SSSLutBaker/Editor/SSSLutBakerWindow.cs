// ════════════════════════════════════════════════════════════
//  SSSLutBakerWindow — 烘焙、预览与保存 SSS LUT，更新资产时保留 GUID。
// ════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System;
using System.IO;

namespace Mine.SSSLutBaker
{
    /// <summary>SSS LUT 烘焙窗口；只拥有本窗口生成的临时纹理。</summary>
    public sealed class SSSLutBakerWindow : EditorWindow
    {
        [SerializeField] private int _resolution = 128;
        [SerializeField] private int _sampleCount = 256;
        [SerializeField] private Vector3 _profile = new Vector3(1f, 0.35f, 0.2f);
        [SerializeField] private string _assetPath = "Assets/Mine/Scripts/SSSLutBaker/SSS_LUT.asset";
        [SerializeField] private Vector2 _scrollPosition;

        private Texture2D _previewTexture;
        private bool _ownsTexture;

        // ════════════════════════════════════════════════════════════
        //  菜单与生命周期
        // ════════════════════════════════════════════════════════════

        [MenuItem("Tools/SSS Lut Baker...")]
        public static void ShowWindow()
        {
            var window = GetWindow<SSSLutBakerWindow>(false, "SSS Lut Baker", true);
            window.minSize = new Vector2(380f, 480f);
        }

        private void OnDisable()
        {
            ReleasePreview();
        }

        // ════════════════════════════════════════════════════════════
        //  GUI — 设置、行动行与预览
        // ════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            EditorGUILayout.LabelField("SSS Lut Baker", EditorStyles.boldLabel);
            _resolution = EditorGUILayout.IntSlider("Resolution", _resolution, 16, 512);
            _sampleCount = EditorGUILayout.IntSlider("Samples", _sampleCount, 32, 1024);
            _profile = EditorGUILayout.Vector3Field("Scatter Radius RGB", _profile);
            EditorGUILayout.HelpBox("RGB controls relative scatter width (0.02 to 2). Red spreads furthest in the default profile.", MessageType.Info);
            DrawPath();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Bake", GUILayout.Height(30))) BakePreview();
            if (GUILayout.Button("Load", GUILayout.Height(30))) LoadPreview();
            using (new EditorGUI.DisabledScope(_previewTexture == null))
            {
                if (GUILayout.Button("Save", GUILayout.Height(30))) SavePreview();
            }
            EditorGUILayout.EndHorizontal();

            if (_previewTexture != null)
            {
                float size = Mathf.Max(128f, position.width - 36f);
                Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(true));
                EditorGUI.DrawPreviewTexture(rect, _previewTexture, null, ScaleMode.ScaleToFit);
                EditorGUILayout.LabelField("X: NdotL (-1 to 1) | Y: Scatter x Curvature (0 to 2)");
                EditorGUILayout.LabelField($"{_previewTexture.width} x {_previewTexture.height} | Linear RGB response");
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawPath()
        {
            EditorGUILayout.BeginHorizontal();
            _assetPath = EditorGUILayout.TextField("Asset Path", _assetPath);
            if (GUILayout.Button("Browse", GUILayout.Width(64)))
            {
                string selected = EditorUtility.SaveFilePanelInProject("SSS LUT Asset",
                    Path.GetFileNameWithoutExtension(_assetPath), "asset", "Choose LUT path",
                    "Assets/Mine/Scripts/SSSLutBaker");
                if (!string.IsNullOrEmpty(selected)) _assetPath = selected;
            }
            EditorGUILayout.EndHorizontal();
        }

        // ════════════════════════════════════════════════════════════
        //  Bake / Load / Save — 委托烘焙并维护预览所有权
        // ════════════════════════════════════════════════════════════

        private void BakePreview()
        {
            EditorUtility.DisplayProgressBar("SSS Lut Baker", "Integrating diffusion profile...", 0.5f);
            try
            {
                Texture2D texture = SSSLutBaker.Bake(_resolution, _sampleCount, _profile);
                ReleasePreview();
                _previewTexture = texture;
                _ownsTexture = true;
            }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private void LoadPreview()
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(_assetPath);
            if (texture == null)
            {
                Debug.LogWarning($"No Texture2D at {_assetPath}");
                return;
            }
            ReleasePreview();
            _previewTexture = texture;
            _ownsTexture = false;
        }

        private void SavePreview()
        {
            try { EditorGUIUtility.PingObject(SaveTextureAsset(_previewTexture, _assetPath)); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        /// <summary>保存 LUT；已有 Texture2D 原位更新，避免材质引用失效。</summary>
        public static Texture2D SaveTextureAsset(Texture2D texture, string assetPath)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            string path = assetPath?.Replace('\\', '/');
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                || Array.Exists(path.Split('/'), part => part == ".." || part == "."))
                throw new ArgumentException("Use an .asset path inside Assets.", nameof(assetPath));

            UnityEngine.Object existingObject = AssetDatabase.LoadMainAssetAtPath(path);
            if (existingObject != null && !(existingObject is Texture2D))
                throw new InvalidOperationException("The selected path contains a different asset type.");
            EnsureFolders(Path.GetDirectoryName(path)?.Replace('\\', '/'));

            Texture2D existing = existingObject as Texture2D;
            if (existing != null)
            {
                if (existing != texture) EditorUtility.CopySerialized(texture, existing);
                existing.name = Path.GetFileNameWithoutExtension(path);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            Texture2D copy = Instantiate(texture);
            copy.name = Path.GetFileNameWithoutExtension(path);
            try { AssetDatabase.CreateAsset(copy, path); }
            catch { DestroyImmediate(copy); throw; }
            AssetDatabase.SaveAssets();
            return copy;
        }

        private static void EnsureFolders(string directory)
        {
            if (string.IsNullOrEmpty(directory) || AssetDatabase.IsValidFolder(directory)) return;
            string parent = Path.GetDirectoryName(directory)?.Replace('\\', '/');
            EnsureFolders(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(directory));
        }

        private void ReleasePreview()
        {
            if (_ownsTexture && _previewTexture != null) DestroyImmediate(_previewTexture);
            _previewTexture = null;
            _ownsTexture = false;
        }
    }
}
