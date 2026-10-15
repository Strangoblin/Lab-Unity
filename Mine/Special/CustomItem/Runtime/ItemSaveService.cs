using System;
using System.IO;
using System.Text;
using CelToon.CustomItem.Contracts;
using UnityEngine;

namespace CelToon.CustomItem.Runtime
{
    public static class ItemSaveService
    {
        public static string ToJson(ItemStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            return JsonUtility.ToJson(store.ExportSnapshot(), true);
        }

        public static ItemStore FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("An item save cannot be empty.");
            ItemStoreSnapshot snapshot;
            try { snapshot = JsonUtility.FromJson<ItemStoreSnapshot>(json); }
            catch (ArgumentException exception) { throw new InvalidDataException("The item save is not valid JSON.", exception); }
            return ItemStore.FromSnapshot(snapshot);
        }

        public static void Save(string path, ItemStore store)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A save path is required.", nameof(path));
            string json = ToJson(store);
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(directory);
            string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }
                // Same-directory replacement keeps the existing complete save if writing fails.
                if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
                else File.Move(temporaryPath, fullPath);
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }

        public static ItemStore Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A save path is required.", nameof(path));
            return FromJson(File.ReadAllText(path, Encoding.UTF8));
        }
    }
}
