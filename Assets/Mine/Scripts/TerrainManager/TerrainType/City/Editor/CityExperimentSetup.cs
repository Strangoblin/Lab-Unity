// ════════════════════════════════════════════════════════════
//  CityExperimentSetup — 创建独立城市实验场景，不改动已经打开的场景
// ════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.IO;

namespace Mine.Terrain.Editor
{
    /// <summary>
    /// 以 Additive 方式创建城市实验入口，仅保存新场景及其新建方案资产。
    /// </summary>
    public static class CityExperimentSetup
    {
        public const string ScenePath = "Assets/Scenes/CityScene.unity";
        public const string TypeAssetPath =
            "Assets/Mine/Scripts/TerrainManager/TerrainType/City/CityExperiment.asset";

        [MenuItem("Tools/Terrain/City/Create CityScene")]
        public static void CreateFromMenu() => Debug.Log(Create());

        public static string Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before creating CityScene.");
            RequireNewPath(ScenePath);
            RequireNewPath(TypeAssetPath);
            EnsureAssetFolder("Assets/Scenes");
            EnsureAssetFolder("Assets/Mine/Scripts/TerrainManager/TerrainType/City");

            CityTerrainType type = ScriptableObject.CreateInstance<CityTerrainType>();
            type.name = "City Experiment";
            type.Validate();
            AssetDatabase.CreateAsset(type, TypeAssetPath);
            AssetDatabase.SaveAssetIfDirty(type);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            if (SceneManager.GetActiveScene() != scene && !SceneManager.SetActiveScene(scene))
                throw new InvalidOperationException("Could not activate the new CityScene.");
            CreateCamera(scene);
            CreateLight(scene);
            CreateManager(scene, type);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Could not save the new CityScene.");

            return $"Created {ScenePath} with {TypeAssetPath}; existing scenes were not saved or closed.";
        }

        // ════════════════════════════════════════════════════════════
        //  实验入口 — 在新场景创建相机、方向光与使用城市方案的 Manager
        // ════════════════════════════════════════════════════════════

        private static void CreateCamera(Scene scene)
        {
            GameObject root = CreateObject(scene, "Main Camera");
            root.tag = "MainCamera";
            Camera camera = root.AddComponent<Camera>();
            root.AddComponent<AudioListener>();
            camera.transform.position = new Vector3(150f, 240f, -100f);
            camera.transform.LookAt(new Vector3(150f, 0f, 150f));
            camera.orthographic = true;
            camera.orthographicSize = 190f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.18f);
        }

        private static void CreateLight(Scene scene)
        {
            GameObject root = CreateObject(scene, "Directional Light");
            root.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = root.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
        }

        private static void CreateManager(Scene scene, CityTerrainType type)
        {
            GameObject root = CreateObject(scene, "City Experiment");
            TerrainManager manager = root.AddComponent<TerrainManager>();
            SerializedObject serialized = new SerializedObject(manager);
            serialized.FindProperty("_terrainType").objectReferenceValue = type;
            serialized.FindProperty("_generateOnStart").boolValue = true;
            SerializedProperty settings = serialized.FindProperty("_settings");
            settings.FindPropertyRelative("_width").intValue = 300;
            settings.FindPropertyRelative("_height").intValue = 300;
            settings.FindPropertyRelative("_cellSize").floatValue = 1f;
            settings.FindPropertyRelative("_chunkCells").intValue = 50;
            settings.FindPropertyRelative("_origin").vector2Value = Vector2.zero;
            settings.FindPropertyRelative("_seed").intValue = 42;
            SerializedProperty render = serialized.FindProperty("_renderSettings");
            render.FindPropertyRelative("_surfaceShader").objectReferenceValue = null;
            render.FindPropertyRelative("_colorBlendWidth").floatValue = 3f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Selection.activeGameObject = root;
        }

        private static GameObject CreateObject(Scene scene, string name)
        {
            GameObject root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        // ════════════════════════════════════════════════════════════
        //  资产边界 — 只允许创建新路径，拒绝覆盖已存在或尚未导入的文件
        // ════════════════════════════════════════════════════════════

        private static void RequireNewPath(string path)
        {
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)) || File.Exists(path) ||
                File.Exists(path + ".meta") || Directory.Exists(path))
                throw new InvalidOperationException($"{path} already exists; open it instead of overwriting it.");
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
