using System.IO;
using UnityEditor;

namespace CelToon.CustomEquipment.Editor
{
    // Content paths remain stable; scene construction now lives in CombatRangeSceneBuilder.
    public static class StorageSceneBuilder
    {
        public const string RootPath = "Assets/Mine/Special/CustomEquipment";
        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
