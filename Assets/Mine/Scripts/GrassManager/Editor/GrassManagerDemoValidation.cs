using System;
using System.Collections;
using System.Reflection;
using Mine.Grass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mine.Grass.Editor
{
    /// <summary>Checks the demo's data, residency and buffer generation in an Editor process.</summary>
    public static class GrassManagerDemoValidation
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(GrassManagerDemoSetup.ScenePath, OpenSceneMode.Single);
            GrassChunkManager manager = UnityEngine.Object.FindFirstObjectByType<GrassChunkManager>();
            ProceduralGrassTerrain terrain = UnityEngine.Object.FindFirstObjectByType<ProceduralGrassTerrain>();
            GrassDemoCamera camera = UnityEngine.Object.FindFirstObjectByType<GrassDemoCamera>();
            Require(manager != null && terrain != null && camera != null, "Scene components missing");
            MethodInfo update = typeof(GrassChunkManager).GetMethod("UpdateChunks", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(update != null, "Chunk update method missing");

            camera.transform.position = new Vector3(0f, 18f, 0f);
            Require((bool)update.Invoke(manager, null), "Initial chunk build failed");
            Require(terrain.SurfaceChunkCount == 25, "Expected 25 terrain chunks");
            Require(manager.ActiveChunkCount == 9 && manager.ActiveInstanceCount > 0, "Expected nine populated grass chunks");
            Matrix4x4 original = FirstMatrix(manager, 1, 1);
            Vector3 position = original.GetColumn(3);
            Require(terrain.TrySample(position.x, position.z, out Vector3 surface, out Vector3 normal), "Instance outside map");
            Require(Mathf.Abs(surface.y - position.y) < 0.0001f && normal.y > 0f, "Instance does not match sampled surface");

            camera.transform.position = new Vector3(25f, 18f, 0f);
            update.Invoke(manager, null);
            Require(manager.ActiveChunkCount == 9 && !manager.IsChunkActive(1, 1) && manager.IsChunkActive(4, 1),
                "Moving one chunk did not replace the departing column");
            camera.transform.position = new Vector3(49f, 18f, 49f);
            update.Invoke(manager, null);
            Require(manager.ActiveChunkCount == 4, "Map corner must clip the 3x3 neighborhood to four chunks");
            camera.transform.position = new Vector3(150f, 18f, 150f);
            update.Invoke(manager, null);
            Require(manager.ActiveChunkCount == 0 && manager.ActiveInstanceCount == 0, "Outside-map chunks were retained");

            camera.transform.position = new Vector3(0f, 18f, 0f);
            update.Invoke(manager, null);
            Matrix4x4 regenerated = FirstMatrix(manager, 1, 1);
            Require(original == regenerated, "Chunk rebuild is not deterministic");
            manager.Density = 0f;
            update.Invoke(manager, null);
            Require(manager.ActiveChunkCount == 9 && manager.ActiveInstanceCount == 0, "Zero density must generate no instances");
            CheckRender(manager, camera.GetComponent<Camera>());
            // In Edit Mode ordinary MonoBehaviours do not receive OnDisable automatically.
            typeof(GrassChunkManager).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
            Require(manager.ActiveChunkCount == 0, "Manager cleanup did not release chunks");
            typeof(ProceduralGrassTerrain).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(terrain, null);
            Require(terrain.SurfaceChunkCount == 0, "Terrain cleanup did not release surface chunks");
            Debug.Log("GRASS_DEMO_VALIDATION_PASS: 25 terrain chunks; 3x3 residency, movement, edges, deterministic rebuild, sampling, zero density and cleanup.");
        }

        private static void CheckRender(GrassChunkManager manager, Camera camera)
        {
            camera.transform.SetPositionAndRotation(new Vector3(0f, 3.5f, -8f), Quaternion.Euler(12f, 0f, 0f));
            Mesh mesh = (Mesh)typeof(GrassChunkManager).GetField("_grassMesh", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            Debug.Log($"GRASS_DEMO_MESH: {mesh.name}, vertices={mesh.vertexCount}, bounds={mesh.bounds}, cameraFov={camera.fieldOfView}");
            var update = typeof(GrassChunkManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            manager.Density = 0f;
            update.Invoke(manager, null);
            Color32[] empty = Capture(camera, "/tmp/grass-demo-empty.png");
            manager.Density = 0.8f;
            update.Invoke(manager, null);
            Color32[] grass = Capture(camera, "/tmp/grass-demo-grass.png");
            int changed = 0;
            for (int i = 0; i < empty.Length; i++)
            {
                int delta = Math.Abs(empty[i].r - grass[i].r) + Math.Abs(empty[i].g - grass[i].g) + Math.Abs(empty[i].b - grass[i].b);
                if (delta > 6)
                    changed++;
            }
            Debug.Log($"GRASS_DEMO_RENDER: {changed} of {empty.Length} pixels changed with grass on; /tmp/grass-demo-empty.png and /tmp/grass-demo-grass.png");
            Require(changed > 10, "Grass did not change the rendered image");
        }

        private static Color32[] Capture(Camera camera, string path)
        {
            RenderTexture target = RenderTexture.GetTemporary(640, 360, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            RenderTexture oldTarget = camera.targetTexture;
            Texture2D image = new Texture2D(640, 360, TextureFormat.RGBA32, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, 640f, 360f), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
                return image.GetPixels32();
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static Matrix4x4 FirstMatrix(GrassChunkManager manager, int x, int z)
        {
            FieldInfo chunksField = typeof(GrassChunkManager).GetField("_chunks", BindingFlags.Instance | BindingFlags.NonPublic);
            IDictionary chunks = (IDictionary)chunksField.GetValue(manager);
            object chunk = chunks[new Vector2Int(x, z)];
            Require(chunk != null, "Expected chunk not found");
            FieldInfo matrixField = chunk.GetType().GetField("_matrices", BindingFlags.Instance | BindingFlags.NonPublic);
            ComputeBuffer buffer = (ComputeBuffer)matrixField.GetValue(chunk);
            Require(buffer != null, "Expected instance buffer not found");
            Matrix4x4[] result = new Matrix4x4[1];
            buffer.GetData(result, 0, 0, 1);
            return result[0];
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Grass demo validation: " + message);
        }
    }
}
