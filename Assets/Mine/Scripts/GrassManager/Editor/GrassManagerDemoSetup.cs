using System;
using System.Linq;
using Mine.Grass;
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
        private const string MeshPath = "Assets/Mine/Shaders/Render/SimpleGrass/Material/Normalized Grass.fbx";
        private const string MaterialPath = "Assets/Mine/Shaders/Render/SimpleGrass/Material/GrassInstance.mat";

        [MenuItem("Tools/Grass Manager/Create Demo Scene")]
        public static void CreateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Create();
        }

        // Also usable from Unity -batchmode -executeMethod.
        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                throw new InvalidOperationException($"Demo scene already exists: {ScenePath}");

            Mesh mesh = AssetDatabase.LoadAllAssetsAtPath(MeshPath).OfType<Mesh>()
                .FirstOrDefault(candidate => candidate.vertexCount > 0);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mesh == null || material == null)
                throw new InvalidOperationException("SimpleGrass demonstration mesh or material is missing.");

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
            ProceduralGrassTerrain terrain = root.AddComponent<ProceduralGrassTerrain>();
            GrassChunkManager manager = root.AddComponent<GrassChunkManager>();
            manager.Configure(terrain, camera, mesh, material);
            manager.Size = 4f; // The reference mesh is only 0.2 units tall.

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Could not save demo scene: {ScenePath}");
            Debug.Log($"Created Grass Manager demo scene: {ScenePath}");
        }
    }
}
