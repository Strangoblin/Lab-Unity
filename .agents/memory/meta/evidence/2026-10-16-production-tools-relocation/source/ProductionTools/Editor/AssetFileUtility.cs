#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Mine.Tools
{
    /// <summary>
    /// 生产类工具共享的资产落盘 / 回看工具。
    /// </summary>
    ///
    /// <remarks>
    /// 保存策略：路径上已有同类型资产时**原位更新**（CopySerialized + SetDirty），
    ///   保留 GUID 与材质、场景引用，不采用 Delete + Create。
    /// 写入范围：只对目标资产调用 SaveAssetIfDirty，不调用全局 SaveAssets，
    ///   避免把整个会话的内存态（如 RendererData）顺带写回。
    /// </remarks>
    public static class AssetFileUtility
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
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return false;
            }

            if (!normalized.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
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
                || !parent.StartsWith("Assets", StringComparison.Ordinal))
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
