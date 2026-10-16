#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Mine.Tools
{
    /// <summary>
    /// 生产类工具（Generator / Baker）窗口的统一骨架。
    /// 只负责固定资产路径、Bake / Load / Save 行动与资产所有权。
    /// </summary>
    ///
    /// <remarks>
    /// 分工：
    ///   基类 —— 路径行、行动行、落盘 / 回看、自产资产的释放
    ///   子类 —— BakeCore() 调服务层烘焙，另有自己的设置分区与预览分区
    ///
    /// 所有权铁律：本窗 Bake 产生的资产归窗口，窗口关闭或换源时销毁；
    ///   经 AssetDatabase 载入或落盘的资产归资产系统，永不销毁。
    ///
    /// 子类若需覆写 OnDisable，必须 <c>protected override void OnDisable()</c> 并调用 <c>base.OnDisable()</c>，
    /// 否则自产资产不会被释放。
    /// </remarks>
    public abstract class ProductionToolWindow : EditorWindow
    {
        // ── 资产所有权 ──────────────────────────────────────────

        private UnityEngine.Object _asset;
        private bool _ownsAsset;

        // ════════════════════════════════════════════════════════
        //  子类契约
        // ════════════════════════════════════════════════════════

        /// <summary>固定资产路径；Save 落盘与 Load 回看共用同一来源。子类以 [SerializeField] 字段承载。</summary>
        protected abstract string AssetPath { get; set; }

        /// <summary>行动行首个按钮的文案：Baker 型 "Bake"，Generator 型 "Generate"。</summary>
        protected abstract string BakeLabel { get; }

        /// <summary>执行烘焙 / 生成并返回待保存资产；失败返回 null。设置与预览由子类在内部维护。</summary>
        protected abstract UnityEngine.Object BakeCore();

        // ════════════════════════════════════════════════════════
        //  子类可覆写
        // ════════════════════════════════════════════════════════

        /// <summary>当前是否允许烘焙（如场景框架未就绪时为 false）。</summary>
        protected virtual bool CanBake => true;

        /// <summary>烘焙是否显示进度条（Baker 型需要；Generator 型瞬时完成）。</summary>
        protected virtual bool ShowBakeProgress => true;

        /// <summary>当前资产是否可落盘（多输出模式的窗口按当前模式判断）。</summary>
        protected virtual bool HasSaveableAsset => _asset != null;

        /// <summary>资产变化（Bake 完成 / Load 完成）后的回调：子类据此刷新自己的预览与状态。</summary>
        protected virtual void OnAssetChanged() { }

        // ── 子类可读 ────────────────────────────────────────────

        /// <summary>当前资产：预览来源与待保存对象。</summary>
        protected UnityEngine.Object Asset => _asset;

        // ════════════════════════════════════════════════════════
        //  生命周期 — 窗口关闭时释放自产资产
        // ════════════════════════════════════════════════════════

        protected virtual void OnDisable()
        {
            ReleaseAsset();
        }

        // ════════════════════════════════════════════════════════
        //  共享 UI — 路径行与行动行
        // ════════════════════════════════════════════════════════

        /// <summary>固定资产路径行：可直接编辑 + Browse 选择。</summary>
        protected void DrawAssetPathRow(string label = "Asset Path")
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
        protected void DrawActionRow()
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
            catch (Exception exception)
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
            if (!AssetFileUtility.IsValidAssetPath(AssetPath))
            {
                Debug.LogWarning($"{titleContent.text}: \"{AssetPath}\" is not a valid asset path.");
                return;
            }

            UnityEngine.Object loaded = AssetFileUtility.Load(AssetPath);
            if (loaded == null)
            {
                Debug.LogWarning($"{titleContent.text}: no asset at \"{AssetPath}\".");
                return;
            }

            SetAsset(loaded, false);
        }

        private void SaveToAsset()
        {
            UnityEngine.Object saved = AssetFileUtility.Save(_asset, AssetPath);
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

        /// <summary>替换当前资产。owned = true 表示子类移交所有权，窗口负责销毁。</summary>
        protected void SetAsset(UnityEngine.Object asset, bool owned)
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
    }
}
#endif
