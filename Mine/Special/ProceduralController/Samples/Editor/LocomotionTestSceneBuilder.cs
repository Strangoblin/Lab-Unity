using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CelToon.ProceduralController.Samples;

namespace CelToon.ProceduralController.Editor
{
    public static class LocomotionTestSceneBuilder
    {
        private const string ScenePath = "Assets/Mine/Special/ProceduralController/Samples/LocomotionIOTest.unity";

        [MenuItem("CelToon/Locomotion/Rebuild Controllable Test Scene")]
        public static void CreateScene()
        {
            EnsureFolder("Assets/Mine/Special/ProceduralController/Samples");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("LocomotionIOTest");
            root.AddComponent<DemoSceneBootstrap>();
            CreatePrimitive(root.transform, PrimitiveType.Plane, "Ground_Flat", Vector3.zero, Vector3.one * 5f, Quaternion.identity);
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Ground_Slope", new Vector3(0f, -0.1f, 5f), new Vector3(8f, 0.2f, 3f), Quaternion.Euler(-15f, 0f, 0f));
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Ground_Step", new Vector3(4f, 0.25f, 3f), new Vector3(2f, 0.5f, 2f), Quaternion.identity).layer = 10;
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Obstacle_Block", new Vector3(-3f, 0.5f, 3.5f), new Vector3(1.5f, 1f, 1.5f), Quaternion.identity).layer = 10;
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Obstacle_Ramp", new Vector3(2f, 0.35f, 6f), new Vector3(2f, 0.7f, 3f), Quaternion.Euler(0f, 0f, -12f)).layer = 10;
            CreateCamera(root.transform);
            var light = new GameObject("Test_KeyLight"); light.transform.SetParent(root.transform); light.transform.position = new Vector3(-4f, 8f, -4f); var sun = light.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            Selection.activeGameObject = root;
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new System.InvalidOperationException("Could not save test scene");
            ValidateSavedScene();
            Debug.Log("Created seven-morphology single-input locomotion test scene: " + ScenePath);
        }

        [MenuItem("CelToon/Locomotion/Validate Saved Test Scene")]
        public static void ValidateSavedScene()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForValidation = !scene.IsValid() || !scene.isLoaded;
            if (openedForValidation) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (scene.isDirty) throw new System.InvalidOperationException("Save the test scene before validation");
            try
            {
            bool bootstrap = false, ground = false, obstacle = false, camera = false;
            int missingScripts = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    bootstrap |= transform.GetComponent<DemoSceneBootstrap>() != null;
                    ground |= transform.name == "Ground_Flat";
                    obstacle |= transform.name == "Obstacle_Block" && transform.gameObject.layer == 10;
                    camera |= transform.GetComponent<Camera>() != null;
                    foreach (var script in transform.GetComponents<MonoBehaviour>()) if (script == null) missingScripts++;
                }
            }
            if (!bootstrap || !ground || !obstacle || !camera || missingScripts != 0)
                throw new System.InvalidOperationException("Test scene validation failed: bootstrap=" + bootstrap +
                    " ground=" + ground + " obstacle=" + obstacle + " camera=" + camera + " missingScripts=" + missingScripts);
            Debug.Log("Validated saved locomotion scene: bootstrap, ground, obstacle, camera; missingScripts=0.");
            }
            finally { if (openedForValidation) EditorSceneManager.CloseScene(scene, true); }
        }

        private static GameObject CreatePrimitive(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Quaternion rotation)
        { var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent); go.transform.position = position; go.transform.localScale = scale; go.transform.rotation = rotation; return go; }

        private static void CreateCamera(Transform parent)
        { var go = new GameObject("Test_Camera"); go.transform.SetParent(parent); go.transform.position = new Vector3(0f, 19f, -26f); go.transform.LookAt(new Vector3(0f, 0f, 1f)); var camera = go.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 19f; go.tag = "MainCamera"; }

        private static void EnsureFolder(string path)
        { if (!AssetDatabase.IsValidFolder(path)) { string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/'); string name = System.IO.Path.GetFileName(path); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, name); } }
    }
}
