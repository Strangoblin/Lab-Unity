using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Mine.Grass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
            // Exercise the same cleanup callback without closing the validation scene.
            typeof(GrassChunkManager).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
            Require(manager.ActiveChunkCount == 0, "Manager cleanup did not release chunks");
            typeof(ProceduralGrassTerrain).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(terrain, null);
            Require(terrain.SurfaceChunkCount == 0, "Terrain cleanup did not release surface chunks");
            Debug.Log("GRASS_DEMO_VALIDATION_PASS: 25 terrain chunks; 3x3 residency, movement, edges, deterministic rebuild, sampling, zero density and cleanup.");
        }

        /// <summary>Exercises the actual visible-index buffers without opening or changing a user scene.</summary>
        public static void RunFrustumInIsolation()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo update = typeof(GrassChunkManager).GetMethod("UpdateChunks", flags);
            MethodInfo cull = typeof(GrassChunkManager).GetMethod("RefreshVisibility", flags);
            Require(update != null && cull != null, "Chunk update or visibility refresh is missing");
            Require(Attribute.IsDefined(typeof(GrassChunkManager), typeof(ExecuteAlways)),
                "Grass manager must run in Edit Mode");
            Require(Attribute.IsDefined(typeof(ProceduralGrassTerrain), typeof(ExecuteAlways)),
                "Terrain must run in Edit Mode");

            Mesh mesh = AssetDatabase.LoadAllAssetsAtPath("Assets/Mine/Shaders/Render/SimpleGrass/Material/Normalized Grass.fbx")
                .OfType<Mesh>().FirstOrDefault(candidate => candidate.vertexCount > 0);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Mine/Shaders/Render/SimpleGrass/Material/GrassInstance.mat");
            Require(mesh != null && material != null, "Grass mesh or material missing");

            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            GameObject cameraObject = null;
            try
            {
                root = new GameObject("Grass Validation Root") { hideFlags = HideFlags.HideAndDontSave };
                cameraObject = new GameObject("Grass Validation Camera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, preview);
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.fieldOfView = 60f;
                camera.aspect = 1.5f;
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 100f;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 4f, 0f), Quaternion.identity);
                ProceduralGrassTerrain terrain = root.AddComponent<ProceduralGrassTerrain>();
                GrassChunkManager manager = root.AddComponent<GrassChunkManager>();
                manager.Configure(terrain, camera, mesh, material);
                manager.Density = 0.8f;
                manager.Size = 4f;
                Require((bool)update.Invoke(manager, null), "Initial chunk build failed");
                cull.Invoke(manager, null);
                Require(manager.ActiveChunkCount == 9, "Center must activate 3x3 chunks");
                int forward = CheckVisibleBuffers(manager, camera);
                camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                cull.Invoke(manager, null);
                int backward = CheckVisibleBuffers(manager, camera);
                Require(forward > 0 && backward > 0, "Camera rotation must reveal grass in both directions");
                camera.transform.position = new Vector3(25f, 4f, 0f);
                Require((bool)update.Invoke(manager, null), "Moving camera must update chunks");
                cull.Invoke(manager, null);
                Require(manager.ActiveChunkCount == 9 && !manager.IsChunkActive(1, 1) && manager.IsChunkActive(4, 1),
                    "Moving one chunk did not update residency");
                CheckVisibleBuffers(manager, camera);
                manager.Density = 0f;
                update.Invoke(manager, null);
                cull.Invoke(manager, null);
                Require(manager.ActiveInstanceCount == 0 && manager.VisibleInstanceCount == 0,
                    "Zero density retained visible instances");
                camera.transform.SetPositionAndRotation(new Vector3(0f, 3.5f, -8f), Quaternion.Euler(12f, 0f, 0f));
                Color32[] withoutGrass = Capture(camera, "/tmp/grass-frustum-empty.png");
                manager.Density = 0.8f;
                Require((bool)update.Invoke(manager, null), "Grass rebuild for camera render failed");
                cull.Invoke(manager, null);
                Color32[] withGrass = Capture(camera, "/tmp/grass-frustum-render.png");
                int changed = 0;
                for (int i = 0; i < withoutGrass.Length; i++)
                {
                    int delta = Math.Abs(withoutGrass[i].r - withGrass[i].r)
                        + Math.Abs(withoutGrass[i].g - withGrass[i].g)
                        + Math.Abs(withoutGrass[i].b - withGrass[i].b);
                    if (delta > 6)
                        changed++;
                }
                Require(changed > 10, "Main Camera.Render did not draw grass via the render callback");
                Debug.Log($"GRASS_FRUSTUM_VALIDATION_PASS: forward={forward}, backward={backward}; " +
                    $"camera render changed {changed} pixels; visible indices, camera rotation, " +
                    "chunk movement and zero density verified.");
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
                if (cameraObject != null)
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static int CheckVisibleBuffers(GrassChunkManager manager, Camera camera)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            IDictionary chunks = (IDictionary)typeof(GrassChunkManager).GetField("_chunks", flags).GetValue(manager);
            int visible = 0;
            int obviousFront = 0;
            int obviousBack = 0;
            foreach (DictionaryEntry entry in chunks)
            {
                object chunk = entry.Value;
                Type type = chunk.GetType();
                ComputeBuffer matricesBuffer = (ComputeBuffer)type.GetField("_matrices", flags).GetValue(chunk);
                if (matricesBuffer == null)
                    continue;
                ComputeBuffer indicesBuffer = (ComputeBuffer)type.GetField("_indices", flags).GetValue(chunk);
                ComputeBuffer argsBuffer = (ComputeBuffer)type.GetField("_arguments", flags).GetValue(chunk);
                Matrix4x4[] matrices = new Matrix4x4[matricesBuffer.count];
                matricesBuffer.GetData(matrices);
                uint[] args = new uint[5];
                argsBuffer.GetData(args);
                int drawn = checked((int)args[1]);
                Require(drawn <= matrices.Length, "Indirect draw count exceeds instance count");
                uint[] indices = new uint[drawn];
                if (drawn > 0)
                    indicesBuffer.GetData(indices, 0, 0, drawn);
                bool[] selected = new bool[matrices.Length];
                foreach (uint index in indices)
                {
                    Require(index < matrices.Length && !selected[index], "Visible index is invalid or duplicated");
                    selected[index] = true;
                }
                visible += drawn;
                for (int i = 0; i < matrices.Length; i++)
                {
                    Vector3 local = camera.transform.InverseTransformPoint(matrices[i].GetColumn(3));
                    if (local.z >= 10f && local.z <= 18f && Mathf.Abs(local.x) < 2f && local.y < -2f && local.y > -6f)
                    {
                        Require(selected[i], "Clearly visible front instance was culled");
                        obviousFront++;
                    }
                    if (local.z < -4f)
                    {
                        Require(!selected[i], "Instance well behind camera was retained");
                        obviousBack++;
                    }
                }
            }
            Require(visible == manager.VisibleInstanceCount, "Reported visible count disagrees with draw buffers");
            Require(visible > 0 && visible < manager.ActiveInstanceCount, "Expected both visible and culled instances");
            Require(obviousFront > 0 && obviousBack > 0, "Test positions did not cover clear inside/outside cases");
            return visible;
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
