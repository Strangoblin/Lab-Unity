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
            if (!root.TryGetComponent<TerrainManager>(out _))
                Undo.AddComponent<TerrainManager>(root);

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
    }
}
