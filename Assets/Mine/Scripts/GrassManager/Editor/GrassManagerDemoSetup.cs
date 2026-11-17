using System;
using System.Linq;
using Mine.Fields;
using Mine.Grass;
using Mine.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mine.Grass.Editor
{
    /// <summary>Creates the standalone demonstration without changing existing scenes.</summary>
    public static class GrassManagerDemoSetup
    {
        public const string ScenePath = "Assets/Mine/Scripts/GrassManager/GrassManagerDemo.unity";
        private const string MeshPath = "Assets/Mine/Scripts/GrassManager/Grass/Normalized Grass.fbx";
        private const string MaterialPath = "Assets/Mine/Scripts/GrassManager/Grass/GrassWind.mat";
        private const string WindComputePath = "Assets/Mine/Scripts/FieldManager/Wind/WindField.compute";

        // 地形由 TerrainManager 提供：100 x 100 格、1 m 一格，与原演示的 100 x 100 世界范围一致。
        private const int TerrainCells = 100;
        private const float TerrainCellSize = 1f;
        private const float TerrainHeightScale = 2f;
        private const float TerrainNoiseScale = 0.025f;
        private const int TerrainNoiseOctaves = 3;
        private const int TerrainSeed = 42;
        private static readonly Vector3 TerrainOrigin = new Vector3(-50f, 0f, -50f);

        // 草侧参数：逻辑地块边长、密度与逐株尺寸倍率。
        private const float DemoChunkSize = 20f;
        private const float DemoDensity = 10f;
        private const float DemoSize = 4f;

        [MenuItem("Tools/Grass Manager/Create Demo Scene")]
        public static void CreateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Create();
        }

        [MenuItem("Tools/Grass Manager/Rebuild Demo Scene")]
        public static void RebuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Rebuild();
        }

        // Also usable from Unity -batchmode -executeMethod.
        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                throw new InvalidOperationException($"Demo scene already exists: {ScenePath}");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.AddComponent<GrassDemoCamera>();
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, 3.5f, -8f), Quaternion.Euler(12f, 0f, 0f));
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.Skybox;

            GameObject lightObject = new GameObject("Directional Light", typeof(Light));
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            RenderSettings.sun = light;

            GameObject root = new GameObject("Grass Manager Demo");
            ConfigureRoot(root, camera);

            // Inactive until the wind provider is configured; AddComponent would otherwise
            // enable it with a missing compute shader.
            GameObject fieldObject = new GameObject("Field Manager");
            fieldObject.SetActive(false);
            fieldObject.AddComponent<FieldManager>();
            GameObject windObject = new GameObject("Wind");
            windObject.transform.SetParent(fieldObject.transform, false);
            WindFieldProvider wind = windObject.AddComponent<WindFieldProvider>();
            SerializedObject serializedWind = new SerializedObject(wind);
            serializedWind.FindProperty("_computeShader").objectReferenceValue = LoadWindCompute();
            serializedWind.FindProperty("_followTarget").objectReferenceValue = camera.transform;
            serializedWind.FindProperty("_lowerHeightGain").floatValue = 1f;
            serializedWind.FindProperty("_upperHeightGain").floatValue = 1f;
            serializedWind.ApplyModifiedPropertiesWithoutUndo();
            fieldObject.SetActive(true);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Could not save demo scene: {ScenePath}");
            Debug.Log($"Created Grass Manager demo scene: {ScenePath}");
        }

        /// <summary>Rewrites the terrain wiring of the saved demo while keeping its other tuning.</summary>
        public static void Rebuild()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                throw new InvalidOperationException($"Demo scene is missing: {ScenePath}");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GrassChunkManager manager = UnityEngine.Object.FindFirstObjectByType<GrassChunkManager>();
            Camera camera = Camera.main;
            if (manager == null || camera == null)
                throw new InvalidOperationException("Demo scene is missing its grass manager or Main Camera.");

            ConfigureRoot(manager.gameObject, camera);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Could not save demo scene: {ScenePath}");
            Debug.Log($"Rebuilt Grass Manager demo scene: {ScenePath}");
        }

        // ════════════════════════════════════════════════════════════
        //  场景内容 — 地形来源、草的配置与风场资源
        // ════════════════════════════════════════════════════════════

        private static void ConfigureRoot(GameObject root, Camera camera)
        {
            Mesh mesh = LoadGrassMesh();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mesh == null || material == null)
                throw new InvalidOperationException("Grass demonstration mesh or material is missing.");

            ProceduralGrassTerrain legacy = root.GetComponent<ProceduralGrassTerrain>();
            if (legacy != null)
                UnityEngine.Object.DestroyImmediate(legacy);

            TerrainManager terrain = GetOrAddComponent<TerrainManager>(root);
            ApplyDemoTerrain(terrain.Settings);

            TerrainGrassSource source = GetOrAddComponent<TerrainGrassSource>(root);
            source.ChunkSize = DemoChunkSize;
            source.Configure(terrain);

            GrassChunkManager manager = GetOrAddComponent<GrassChunkManager>(root);
            manager.Configure(source, camera, mesh, material);
            manager.TerrainLayers = GrassTerrainLayer.Grass;
            manager.MeshRotation = new Vector3(-90f, 0f, 0f);
            manager.Size = DemoSize;
            manager.Density = DemoDensity;

            EditorUtility.SetDirty(terrain);
            EditorUtility.SetDirty(source);
            EditorUtility.SetDirty(manager);
        }

        private static void ApplyDemoTerrain(TerrainSettings settings)
        {
            settings.WidthCells = TerrainCells;
            settings.HeightCells = TerrainCells;
            settings.CellSize = TerrainCellSize;
            settings.Origin = TerrainOrigin;
            settings.Seed = TerrainSeed;
            settings.Provider = TerrainProviderKind.Noise;
            settings.DefaultLayer = TerrainSurfaceLayer.Grass;
            settings.HeightScale = TerrainHeightScale;
            settings.NoiseScale = TerrainNoiseScale;
            settings.NoiseOctaves = TerrainNoiseOctaves;
        }

        private static Mesh LoadGrassMesh()
        {
            return AssetDatabase.LoadAllAssetsAtPath(MeshPath).OfType<Mesh>()
                .FirstOrDefault(candidate => candidate.vertexCount > 0);
        }

        private static ComputeShader LoadWindCompute()
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(WindComputePath);
            if (compute == null)
                throw new InvalidOperationException("Wind compute shader is missing.");
            return compute;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
