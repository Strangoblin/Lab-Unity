// ════════════════════════════════════════════════════════════
//  TerrainExperimentSetup — 为 AlgorithmScene 配置原野实验入口与俯视相机
// ════════════════════════════════════════════════════════════

using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Mine.Terrain.Editor
{
    /// <summary>
    /// 仅修改当前打开的 AlgorithmScene；生成结果在 Play Mode 由 TerrainManager 创建。
    /// </summary>
    public static class TerrainExperimentSetup
    {
        private const string DefaultTypeAssetPath =
            "Assets/Mine/Scripts/TerrainManager/TerrainType/Countryside/AlgorithmCountryside.asset";

        [MenuItem("Tools/Terrain/Prepare AlgorithmScene")]
        public static void PrepareFromMenu()
        {
            Debug.Log(Prepare());
        }

        public static string Prepare()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != "AlgorithmScene")
                throw new InvalidOperationException("Open AlgorithmScene before preparing the terrain experiment.");

            GameObject root = GameObject.Find("Terrain Experiment");
            if (root == null)
            {
                root = new GameObject("Terrain Experiment");
                Undo.RegisterCreatedObjectUndo(root, "Create Terrain Experiment");
            }
            if (!root.TryGetComponent<TerrainManager>(out TerrainManager manager))
                manager = Undo.AddComponent<TerrainManager>(root);
            BindDefaultTypeIfMissing(manager);

            Camera camera = Camera.main;
            if (camera == null)
                throw new InvalidOperationException("AlgorithmScene needs a Main Camera.");
            Undo.RecordObject(camera.transform, "Frame Terrain Experiment");
            Undo.RecordObject(camera, "Frame Terrain Experiment");
            camera.transform.position = new Vector3(150f, 240f, -100f);
            camera.transform.LookAt(new Vector3(150f, 0f, 150f));
            camera.orthographic = true;
            camera.orthographicSize = 190f;
            camera.farClipPlane = 1000f;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Failed to save AlgorithmScene.");

            return $"Prepared {scene.path} with TerrainManager and top-down camera.";
        }

        // ════════════════════════════════════════════════════════════
        //  默认方案绑定 — 新实验入口使用郊野资产，已有方案配置保持其资产引用
        // ════════════════════════════════════════════════════════════

        private static void BindDefaultTypeIfMissing(TerrainManager manager)
        {
            SerializedObject serializedManager = new SerializedObject(manager);
            SerializedProperty terrainType = serializedManager.FindProperty("_terrainType");
            if (terrainType.objectReferenceValue != null)
                return;

            CountrysideTerrainType type =
                AssetDatabase.LoadAssetAtPath<CountrysideTerrainType>(DefaultTypeAssetPath);
            if (type == null)
            {
                EnsureAssetFolder("Assets/Mine/Scripts/TerrainManager/TerrainType/Countryside");
                type = ScriptableObject.CreateInstance<CountrysideTerrainType>();
                AssetDatabase.CreateAsset(type, DefaultTypeAssetPath);
                AssetDatabase.SaveAssets();
            }
            terrainType.objectReferenceValue = type;
            serializedManager.ApplyModifiedProperties();
        }

        private static void EnsureAssetFolder(string path)
        {
            string[] parts = path.Split('/');
            string parent = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string folder = parent + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder(parent, parts[i]);
                parent = folder;
            }
        }
    }
}
