using System;
using System.Collections;
using System.Collections.Generic;
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
        // Use the isolated fixture for both entry points: never replace the user's open scene.
        public static void Run() => RunFrustumInIsolation();

        /// <summary>Verifies real scene save and reload in a dedicated batch Editor only.</summary>
        public static void RunHeightScenePersistenceInBatch()
        {
            Require(Application.isBatchMode, "Height scene persistence requires a separate batch Editor process");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Mine/Scripts/GrassManager/GrassHeightValidation.unity");
            bool created = false;
            try
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject root = new GameObject("Grass Height Persistence Fixture");
                ProceduralGrassTerrain terrain = root.AddComponent<ProceduralGrassTerrain>();
                GrassChunkManager manager = root.AddComponent<GrassChunkManager>();
                manager.Configure(terrain, null, null, null);
                terrain.Initialize();
                Require(terrain.TrySample(0f, 0f, out Vector3 center, out _), "Persistence fixture terrain sample failed");
                Require(manager.PaintHeight(center, 8f, -0.8f), "Persistence fixture failed to paint");
                float expected = manager.SamplePaintedHeight(center);
                Require(expected < 0.25f, "Persistence fixture did not retain painted height");
                EditorUtility.SetDirty(manager);
                created = EditorSceneManager.SaveScene(scene, path);
                Require(created, "Could not save the temporary height fixture scene");
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                GrassChunkManager restored = UnityEngine.Object.FindFirstObjectByType<GrassChunkManager>();
                Require(restored != null && restored.Terrain != null, "Reloaded scene lost the grass manager or terrain");
                restored.Terrain.Initialize();
                Require(Mathf.Abs(restored.SamplePaintedHeight(center) - expected) < 0.001f,
                    "Height data did not survive saving and reopening the scene");
                Debug.Log("GRASS_HEIGHT_SCENE_PERSISTENCE_PASS: painted scene saved and reloaded.");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (created) AssetDatabase.DeleteAsset(path);
            }
        }

        /// <summary>Exercises editor Undo/Redo on a normal, temporary scene in a separate batch process.</summary>
        public static void RunHeightUndoInBatch()
        {
            Require(Application.isBatchMode, "Height undo validation requires a separate batch Editor process");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject root = new GameObject("Grass Undo Validation");
            try
            {
                ProceduralGrassTerrain terrain = root.AddComponent<ProceduralGrassTerrain>();
                GrassChunkManager manager = root.AddComponent<GrassChunkManager>();
                manager.Configure(terrain, null, null, null);
                terrain.Initialize();
                Require(terrain.TrySample(0f, 0f, out Vector3 center, out _), "Undo fixture terrain sample failed");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(manager, "Validate Grass Height Undo");
                Require(manager.PaintHeight(center, 8f, -0.8f), "Undo fixture failed to paint");
                EditorUtility.SetDirty(manager);
                EditorSceneManager.MarkSceneDirty(root.scene);
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                long paintMs = timer.ElapsedMilliseconds;
                float painted = manager.SamplePaintedHeight(center);
                timer.Restart();
                Undo.PerformUndo();
                long undoMs = timer.ElapsedMilliseconds;
                terrain.Initialize();
                float undone = manager.SamplePaintedHeight(center);
                timer.Restart();
                Undo.PerformRedo();
                long redoMs = timer.ElapsedMilliseconds;
                terrain.Initialize();
                float redone = manager.SamplePaintedHeight(center);
                Require(Mathf.Approximately(undone, 1f) && Mathf.Abs(redone - painted) < 0.001f,
                    $"Undo/redo failed: painted={painted}, undo={undone}, redo={redone}");
                Vector3 secondCenter = center + Vector3.right * 25f;
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(manager, "Validate Second Grass Height Undo");
                Require(manager.PaintHeight(secondCenter, 8f, -0.6f), "Second undo fixture stroke failed");
                EditorUtility.SetDirty(manager);
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                float secondPainted = manager.SamplePaintedHeight(secondCenter);
                Undo.PerformUndo();
                terrain.Initialize();
                float secondUndone = manager.SamplePaintedHeight(secondCenter);
                Undo.PerformRedo();
                terrain.Initialize();
                float secondRedone = manager.SamplePaintedHeight(secondCenter);
                Require(Mathf.Approximately(secondUndone, 1f) && Mathf.Abs(secondRedone - secondPainted) < 0.001f,
                    $"Second undo/redo failed: painted={secondPainted}, undo={secondUndone}, redo={secondRedone}");
                Debug.Log($"GRASS_HEIGHT_UNDO_TIMING: paintAndRecord={paintMs}ms, undo={undoMs}ms, redo={redoMs}ms");
                Debug.Log("GRASS_HEIGHT_UNDO_VALIDATION_PASS: painted height undo and redo.");
            }
            finally
            {
                GrassChunkManager manager = root.GetComponent<GrassChunkManager>();
                if (manager != null) Undo.ClearUndo(manager);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>Exercises independent height painting without modifying a saved scene.</summary>
        public static void RunHeightPaintingInIsolation()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo update = typeof(GrassChunkManager).GetMethod("UpdateChunks", flags);
            MethodInfo cull = typeof(GrassChunkManager).GetMethod("RefreshVisibility", flags);
            Require(update != null && cull != null, "Chunk update or visibility refresh is missing");
            Mesh mesh = AssetDatabase.LoadAllAssetsAtPath("Assets/Mine/Scripts/GrassManager/Grass/Normalized Grass.fbx")
                .OfType<Mesh>().FirstOrDefault(candidate => candidate.vertexCount > 0);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Mine/Scripts/GrassManager/Grass/GrassWind.mat");
            Require(mesh != null && material != null, "Grass mesh or material missing");

            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            GameObject cameraObject = null;
            GameObject serializedCopy = null;
            try
            {
                root = new GameObject("Grass Paint Validation Root");
                cameraObject = new GameObject("Grass Paint Validation Camera");
                SceneManager.MoveGameObjectToScene(root, preview);
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.fieldOfView = 60f;
                camera.aspect = 1.5f;
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 100f;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 4f, -5f), Quaternion.Euler(12f, 0f, 0f));
                ProceduralGrassTerrain terrain = root.AddComponent<ProceduralGrassTerrain>();
                GrassChunkManager manager = root.AddComponent<GrassChunkManager>();
                manager.Configure(terrain, camera, mesh, material);
                manager.Density = 0.8f;
                manager.Size = 4f;
                manager.MeshRotation = new Vector3(-90f, 0f, 0f);
                Require((bool)update.Invoke(manager, null), "Paint fixture failed to generate grass");
                cull.Invoke(manager, null);
                Dictionary<Vector2Int, Matrix4x4[]> before = CaptureMatrices(manager);
                Require(terrain.TrySample(0f, 0f, out Vector3 center, out _), "Paint center is outside terrain");
                CheckPaintTerrainPicking(manager, terrain, center);
                Vector3 middle = center + Vector3.right * 4f;
                Vector3 outside = center + Vector3.right * 12f;
                Require(Mathf.Approximately(manager.SamplePaintedHeight(center), 1f) &&
                    Mathf.Approximately(manager.SamplePaintedHeight(outside), 1f), "Unpainted height must default to one");
                Require(!manager.PaintHeight(center, 0f, -1f), "Zero-radius brush must not paint");

                Require(manager.PaintHeight(center, 8f, -0.8f), "Erase stroke did not change the height map");
                float paintedCenter = manager.SamplePaintedHeight(center);
                float paintedMiddle = manager.SamplePaintedHeight(middle);
                Require(paintedCenter > 0.18f && paintedCenter < 0.23f && paintedMiddle > 0.57f && paintedMiddle < 0.64f &&
                    Mathf.Approximately(manager.SamplePaintedHeight(outside), 1f), "Brush falloff or outside range is incorrect");
                float leftOfBoundary = manager.SamplePaintedHeight(center + Vector3.left * 0.3f);
                float rightOfBoundary = manager.SamplePaintedHeight(center + Vector3.right * 0.3f);
                Require(leftOfBoundary < 0.25f && Mathf.Abs(leftOfBoundary - rightOfBoundary) < 0.01f,
                    "Sampling across a painted tile boundary has a discontinuity");

                string serialized = EditorJsonUtility.ToJson(manager);
                serializedCopy = new GameObject("Grass Paint Serialization Copy") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(serializedCopy, preview);
                GrassChunkManager restored = serializedCopy.AddComponent<GrassChunkManager>();
                EditorJsonUtility.FromJsonOverwrite(serialized, restored);
                restored.Configure(terrain, camera, mesh, material);
                Require(Mathf.Abs(restored.SamplePaintedHeight(center) - paintedCenter) < 0.001f &&
                    Mathf.Abs(restored.SamplePaintedHeight(middle) - paintedMiddle) < 0.001f,
                    "Serialized height data did not survive component reload");

                Require(manager.PaintHeight(center, 8f, 1f), "Positive brush did not restore height");
                Require(manager.SamplePaintedHeight(center) > 0.99f, "Positive brush did not restore center height");
                Require(manager.PaintHeight(center, 8f, -1f), "First erase stroke failed");
                Require(manager.PaintHeight(center, 8f, -1f), "Second erase stroke failed");
                Require(manager.SamplePaintedHeight(center) < 0.001f, "Brush center must clamp at zero");
                Require((bool)update.Invoke(manager, null), "Painted grass update failed");
                cull.Invoke(manager, null);
                CheckPaintedMatrices(manager, before);

                camera.transform.position = new Vector3(49f, 4f, 49f);
                Require((bool)update.Invoke(manager, null), "Moving away from painted chunks failed");
                camera.transform.position = new Vector3(0f, 4f, -5f);
                Require((bool)update.Invoke(manager, null), "Returning to painted chunks failed");
                cull.Invoke(manager, null);
                CheckPaintedMatrices(manager, before);
                terrain.ChunkSize = 30f;
                Require((bool)update.Invoke(manager, null), "Changing chunk size after paint failed");
                Require(manager.SamplePaintedHeight(center) < 0.001f &&
                    Mathf.Approximately(manager.SamplePaintedHeight(outside), 1f),
                    "Changing logical chunk size changed the painted data");
                manager.Density = 2f;
                Require((bool)update.Invoke(manager, null) && manager.ActiveInstanceCount > 0 &&
                    manager.SamplePaintedHeight(center) < 0.001f,
                    "Increasing grass density must retain the painted data");
                Debug.Log("GRASS_HEIGHT_PAINT_VALIDATION_PASS: brush falloff, serialization, " +
                    "clamping, matrix height and zero culling, stable positions, chunk reload and resizing.");
            }
            finally
            {
                if (serializedCopy != null) UnityEngine.Object.DestroyImmediate(serializedCopy);
                if (root != null)
                {
                    GrassChunkManager manager = root.GetComponent<GrassChunkManager>();
                    if (manager != null) Undo.ClearUndo(manager);
                    UnityEngine.Object.DestroyImmediate(root);
                }
                if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static void CheckPaintTerrainPicking(GrassChunkManager manager, ProceduralGrassTerrain terrain, Vector3 center)
        {
            Require(terrain.GetComponentInChildren<Collider>() == null, "Paint fixture unexpectedly has a collider");
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(manager, typeof(GrassHeightPainterEditor));
            try
            {
                MethodInfo onScene = typeof(GrassHeightPainterEditor).GetMethod("OnSceneGUI", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo pick = typeof(GrassHeightPainterEditor).GetMethod("TryPickTerrain", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(editor != null && onScene != null && pick != null, "Grass Scene painter is not available");
                object[] args = { terrain, new Ray(center + Vector3.up * 10f, Vector3.down), Vector3.zero, Vector3.zero };
                Require((bool)pick.Invoke(editor, args), "Scene painter failed to pick the terrain mesh without a collider");
                Vector3 point = (Vector3)args[2];
                Vector3 normal = (Vector3)args[3];
                Require((point - center).sqrMagnitude < 0.0001f && normal.y > 0f,
                    "Scene painter hit does not match the terrain sample");
                args = new object[] { terrain, new Ray(center + new Vector3(1000f, 10f, 0f), Vector3.down), Vector3.zero, Vector3.zero };
                Require(!(bool)pick.Invoke(editor, args), "Scene painter selected terrain outside its bounds");
            }
            finally
            {
                if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        private static Dictionary<Vector2Int, Matrix4x4[]> CaptureMatrices(GrassChunkManager manager)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            IDictionary chunks = (IDictionary)typeof(GrassChunkManager).GetField("_chunks", flags).GetValue(manager);
            var result = new Dictionary<Vector2Int, Matrix4x4[]>();
            foreach (DictionaryEntry entry in chunks)
            {
                object chunk = entry.Value;
                ComputeBuffer buffer = (ComputeBuffer)chunk.GetType().GetField("_matrices", flags).GetValue(chunk);
                if (buffer == null) continue;
                Matrix4x4[] matrices = new Matrix4x4[buffer.count];
                buffer.GetData(matrices);
                result.Add((Vector2Int)entry.Key, matrices);
            }
            return result;
        }

        private static void CheckPaintedMatrices(GrassChunkManager manager, Dictionary<Vector2Int, Matrix4x4[]> before)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            IDictionary chunks = (IDictionary)typeof(GrassChunkManager).GetField("_chunks", flags).GetValue(manager);
            int lowered = 0, zero = 0, unchanged = 0;
            foreach (DictionaryEntry entry in chunks)
            {
                object chunk = entry.Value;
                Type type = chunk.GetType();
                ComputeBuffer matricesBuffer = (ComputeBuffer)type.GetField("_matrices", flags).GetValue(chunk);
                if (matricesBuffer == null) continue;
                Matrix4x4[] matrices = new Matrix4x4[matricesBuffer.count];
                matricesBuffer.GetData(matrices);
                Require(before.TryGetValue((Vector2Int)entry.Key, out Matrix4x4[] baseline) && baseline.Length == matrices.Length,
                    "Painting changed the number of generated instances");
                uint[] args = new uint[5];
                ((ComputeBuffer)type.GetField("_arguments", flags).GetValue(chunk)).GetData(args);
                uint[] indices = new uint[checked((int)args[1])];
                if (indices.Length > 0)
                    ((ComputeBuffer)type.GetField("_indices", flags).GetValue(chunk)).GetData(indices, 0, 0, indices.Length);
                foreach (uint index in indices)
                {
                    Vector3 point = matrices[index].GetColumn(3);
                    Require(manager.SamplePaintedHeight(point) > 0f, "Zero-height grass must not be rendered");
                }
                for (int i = 0; i < matrices.Length; i++)
                {
                    Vector3 point = matrices[i].GetColumn(3);
                    Require(((Vector3)baseline[i].GetColumn(3) - point).sqrMagnitude < 0.000001f,
                        "Painting changed deterministic instance positions");
                    float height = manager.SamplePaintedHeight(point);
                    float originalAxis = baseline[i].MultiplyVector(Vector3.forward).magnitude;
                    float paintedAxis = matrices[i].MultiplyVector(Vector3.forward).magnitude;
                    Require(Mathf.Abs(paintedAxis - originalAxis * height) < 0.0001f,
                        "Grass growth axis does not match painted height");
                    if (height < 0.001f) zero++;
                    else if (height < 0.99f) lowered++;
                    else unchanged++;
                }
            }
            Require(zero > 0 && lowered > 0 && unchanged > 0,
                "Paint fixture must cover zero, partially painted and untouched instances");
        }

        /// <summary>Checks the saved demo in a dedicated batch Editor process without saving it.</summary>
        public static void RunSavedSceneInBatch()
        {
            Require(Application.isBatchMode, "Saved-scene validation must run in a separate batch Editor process");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            EditorSceneManager.OpenScene(GrassManagerDemoSetup.ScenePath, OpenSceneMode.Single);
            GrassChunkManager manager = UnityEngine.Object.FindFirstObjectByType<GrassChunkManager>();
            ProceduralGrassTerrain terrain = UnityEngine.Object.FindFirstObjectByType<ProceduralGrassTerrain>();
            Camera camera = Camera.main;
            Require(manager != null && terrain != null && camera != null, "Saved scene components or Main Camera missing");
            Require(Mathf.Approximately(terrain.ChunkSize, 20f), "Saved demo must default to a 20-unit chunk");
            Require((GrassTerrainLayer)typeof(GrassChunkManager).GetField("_terrainLayers", flags).GetValue(manager) ==
                GrassTerrainLayer.Grass, "Saved demo must select the Grass terrain layer by default");
            Require((manager.MeshRotation - new Vector3(-90f, 0f, 0f)).sqrMagnitude < 0.0001f,
                "Saved demo must rotate its grass mesh -90 degrees about X");
            Require((GrassTerrainSource)typeof(GrassChunkManager).GetField("_terrain", flags).GetValue(manager) == terrain &&
                (Camera)typeof(GrassChunkManager).GetField("_camera", flags).GetValue(manager) == camera,
                "Saved demo must reference its terrain and Main Camera");
            MethodInfo update = typeof(GrassChunkManager).GetMethod("UpdateChunks", flags);
            MethodInfo cull = typeof(GrassChunkManager).GetMethod("RefreshVisibility", flags);
            Require(update != null && cull != null && (bool)update.Invoke(manager, null), "Saved scene chunk update failed");
            cull.Invoke(manager, null);
            Require(terrain.SurfaceMeshCount == 1 && terrain.ChunkCountX == 5 && terrain.ChunkCountZ == 5,
                "Saved scene must generate a single terrain mesh with 5x5 logical chunks");
            Require(manager.ActiveInstanceCount > 0 && manager.VisibleInstanceCount > 0,
                "Saved scene must generate visible grass from Main Camera");
            CheckDefaultDistribution(manager, terrain);
            Mesh mesh = (Mesh)typeof(GrassChunkManager).GetField("_grassMesh", flags).GetValue(manager);
            CheckMeshRotation(manager, terrain, mesh, Quaternion.Euler(-90f, 0f, 0f));
            Debug.Log($"GRASS_SAVED_SCENE_VALIDATION_PASS: mesh={terrain.SurfaceMeshCount}, " +
                $"chunkSize={terrain.ChunkSize}, instances={manager.ActiveInstanceCount}, visible={manager.VisibleInstanceCount}.");
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

            Mesh mesh = AssetDatabase.LoadAllAssetsAtPath("Assets/Mine/Scripts/GrassManager/Grass/Normalized Grass.fbx")
                .OfType<Mesh>().FirstOrDefault(candidate => candidate.vertexCount > 0);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Mine/Scripts/GrassManager/Grass/GrassWind.mat");
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
                manager.TerrainLayers = GrassTerrainLayer.Grass;
                manager.MeshRotation = new Vector3(-90f, 0f, 0f);
                Require((bool)update.Invoke(manager, null), "Initial chunk build failed");
                cull.Invoke(manager, null);
                Require(manager.ActiveChunkCount == 9, "Center must activate 3x3 chunks");
                Require(terrain.SurfaceMeshCount == 1 && terrain.ChunkCountX == 5 && terrain.ChunkCountZ == 5,
                    "Terrain must have one surface mesh and 5x5 logical chunks");
                CheckDefaultDistribution(manager, terrain);
                CheckMeshRotation(manager, terrain, mesh, Quaternion.Euler(-90f, 0f, 0f));
                manager.MeshRotation = Vector3.zero;
                Require((bool)update.Invoke(manager, null), "Mesh rotation change failed");
                CheckMeshRotation(manager, terrain, mesh, Quaternion.identity);
                manager.MeshRotation = new Vector3(-90f, 0f, 0f);
                Require((bool)update.Invoke(manager, null), "Restoring mesh rotation failed");
                cull.Invoke(manager, null);
                int forward = CheckVisibleBuffers(manager, camera, terrain.ChunkWidth);

                int originalVersion = terrain.DataVersion;
                int originalInstances = manager.ActiveInstanceCount;
                Vector3 originalOrigin = terrain.Origin;
                Require(terrain.TrySample(8f, 9f, out Vector3 originalSurface, out Vector3 originalNormal),
                    "Expected in-bounds terrain sample");
                terrain.ChunkSize = 30f;
                Require((bool)update.Invoke(manager, null), "Changing chunk size must rebuild the terrain and grass");
                cull.Invoke(manager, null);
                Require(Mathf.Approximately(terrain.ChunkWidth, 30f) && terrain.DataVersion != originalVersion,
                    "Chunk size change did not update terrain data");
                Require(terrain.SurfaceMeshCount == 1 && terrain.ChunkCountX == 4 && terrain.ChunkCountZ == 4 &&
                    manager.ActiveChunkCount == 9 && manager.ActiveInstanceCount > 0 &&
                    manager.ActiveInstanceCount != originalInstances,
                    "Chunk size change did not rebuild the 4x4 partition and 3x3 grass neighborhood");
                Require(terrain.Origin == originalOrigin && terrain.TrySample(8f, 9f, out Vector3 resizedSurface,
                    out Vector3 resizedNormal) && (resizedSurface - originalSurface).sqrMagnitude < 0.000001f &&
                    (resizedNormal - originalNormal).sqrMagnitude < 0.000001f,
                    "Changing partition size changed the terrain geometry");
                Require(terrain.TrySample(49.9f, 49.9f, out _, out _) &&
                    !terrain.TrySample(50.1f, 49.9f, out _, out _), "Fixed terrain boundary changed");
                CheckVisibleBuffers(manager, camera, terrain.ChunkWidth);
                camera.transform.position = new Vector3(49f, 4f, 49f);
                Require((bool)update.Invoke(manager, null), "Partial edge chunk update failed");
                cull.Invoke(manager, null);
                Require(manager.ActiveChunkCount == 4 && manager.IsChunkActive(3, 3),
                    "Partial edge chunks must be resident at the terrain corner");
                CheckInstancesWithinTerrain(manager, terrain);
                FieldInfo layers = typeof(GrassChunkManager).GetField("_terrainLayers", flags);
                Require(layers != null, "Terrain layer selector missing");
                layers.SetValue(manager, GrassTerrainLayer.Grass);
                manager.Density = 0.1f;
                Require((bool)update.Invoke(manager, null), "Partial chunk density rebuild failed");
                Require(Mathf.Abs(ChunkInstanceCount(manager, 3, 3) - 10) <= 1 &&
                    Mathf.Abs(ChunkInstanceCount(manager, 3, 2) - 30) <= 1,
                    "Partial chunks must use their actual 10x10 and 10x30 areas for density");
                terrain.ChunkSize = 200f;
                Require((bool)update.Invoke(manager, null), "Oversized chunk rebuild failed");
                Require(terrain.ChunkCountX == 1 && terrain.ChunkCountZ == 1 && manager.ActiveChunkCount == 1 &&
                    Mathf.Abs(ChunkInstanceCount(manager, 0, 0) - 1000) <= 1,
                    "An oversized chunk must use the terrain's 100x100 area for density");
                camera.transform.position = new Vector3(0f, 4f, 0f);
                layers.SetValue(manager, GrassTerrainLayer.Grass);
                manager.Density = 0.8f;
                terrain.ChunkSize = 20f;
                Require((bool)update.Invoke(manager, null), "Restoring chunk size failed");
                cull.Invoke(manager, null);
                Require(manager.ActiveInstanceCount == originalInstances && terrain.SurfaceMeshCount == 1,
                    "Restoring size should deterministically rebuild the original instances");

                layers.SetValue(manager, GrassTerrainLayer.None);
                Require((bool)update.Invoke(manager, null), "Terrain layer change failed");
                cull.Invoke(manager, null);
                Require(manager.ActiveInstanceCount == 0 && manager.VisibleInstanceCount == 0,
                    "Selecting no terrain layers must exclude all grass");
                layers.SetValue(manager, GrassTerrainLayer.Water | GrassTerrainLayer.Dirt);
                Require((bool)update.Invoke(manager, null), "Nonmatching terrain layer change failed");
                cull.Invoke(manager, null);
                Require(manager.ActiveInstanceCount == 0 && manager.VisibleInstanceCount == 0,
                    "Selecting only nonmatching terrain layers must exclude all grass");
                layers.SetValue(manager, GrassTerrainLayer.Grass);
                Require((bool)update.Invoke(manager, null), "Grass-only terrain layer change failed");
                CheckOnlyGrassDistribution(manager, terrain);
                cull.Invoke(manager, null);
                Require(manager.ActiveInstanceCount == originalInstances, "Restoring terrain layer failed to rebuild grass");
                camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                cull.Invoke(manager, null);
                int backward = CheckVisibleBuffers(manager, camera, terrain.ChunkWidth);
                Require(forward > 0 && backward > 0, "Camera rotation must reveal grass in both directions");
                camera.transform.position = new Vector3(25f, 4f, 0f);
                Require((bool)update.Invoke(manager, null), "Moving camera must update chunks");
                cull.Invoke(manager, null);
                Require(manager.ActiveChunkCount == 9 && !manager.IsChunkActive(1, 1) && manager.IsChunkActive(4, 1),
                    "Moving one chunk did not update residency");
                CheckVisibleBuffers(manager, camera, terrain.ChunkWidth);
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
                    "chunk size changes, distance and frustum culling, layer filtering, chunk movement and zero density verified.");
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

        private static int CheckVisibleBuffers(GrassChunkManager manager, Camera camera, float radius)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            IDictionary chunks = (IDictionary)typeof(GrassChunkManager).GetField("_chunks", flags).GetValue(manager);
            int visible = 0;
            int inside = 0;
            int outsideRadiusInFrustum = 0;
            int outsideFrustumInRadius = 0;
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            Vector3 cameraPosition = camera.transform.position;
            foreach (DictionaryEntry entry in chunks)
            {
                object chunk = entry.Value;
                Type type = chunk.GetType();
                ComputeBuffer matricesBuffer = (ComputeBuffer)type.GetField("_matrices", flags).GetValue(chunk);
                if (matricesBuffer == null)
                    continue;
                ComputeBuffer indicesBuffer = (ComputeBuffer)type.GetField("_indices", flags).GetValue(chunk);
                ComputeBuffer argsBuffer = (ComputeBuffer)type.GetField("_arguments", flags).GetValue(chunk);
                Bounds[] bounds = (Bounds[])type.GetField("_instanceBounds", flags).GetValue(chunk);
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
                    Vector3 position = matrices[i].GetColumn(3);
                    float dx = position.x - cameraPosition.x;
                    float dz = position.z - cameraPosition.z;
                    bool inRadius = dx * dx + dz * dz <= radius * radius;
                    bool inFrustum = GeometryUtility.TestPlanesAABB(planes, bounds[i]);
                    Require(selected[i] == (inRadius && inFrustum),
                        "Visible index does not match the camera frustum and horizontal chunk-size radius");
                    if (inRadius && inFrustum) inside++;
                    if (!inRadius && inFrustum) outsideRadiusInFrustum++;
                    if (inRadius && !inFrustum) outsideFrustumInRadius++;
                }
            }
            Require(visible == manager.VisibleInstanceCount, "Reported visible count disagrees with draw buffers");
            Require(visible > 0 && visible < manager.ActiveInstanceCount, "Expected both visible and culled instances");
            Require(inside > 0 && outsideRadiusInFrustum > 0 && outsideFrustumInRadius > 0,
                "Fixture did not cover radius and frustum exclusion independently");
            return visible;
        }

        private static int ChunkInstanceCount(GrassChunkManager manager, int x, int z)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            IDictionary chunks = (IDictionary)typeof(GrassChunkManager).GetField("_chunks", flags).GetValue(manager);
            object chunk = chunks[new Vector2Int(x, z)];
            Require(chunk != null, "Expected chunk was not generated");
            return (int)chunk.GetType().GetProperty("Count").GetValue(chunk);
        }

        private static void CheckInstancesWithinTerrain(GrassChunkManager manager, ProceduralGrassTerrain terrain)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            IDictionary chunks = (IDictionary)typeof(GrassChunkManager).GetField("_chunks", flags).GetValue(manager);
            int checkedInstances = 0;
            foreach (DictionaryEntry entry in chunks)
            {
                object chunk = entry.Value;
                ComputeBuffer buffer = (ComputeBuffer)chunk.GetType().GetField("_matrices", flags).GetValue(chunk);
                if (buffer == null) continue;
                Matrix4x4[] matrices = new Matrix4x4[buffer.count];
                buffer.GetData(matrices);
                foreach (Matrix4x4 matrix in matrices)
                {
                    Vector3 position = matrix.GetColumn(3);
                    Require(terrain.TrySample(position.x, position.z, out Vector3 surface, out Vector3 normal,
                        out GrassTerrainLayer layer) && layer == GrassTerrainLayer.Grass && normal.y > 0f &&
                        Mathf.Abs(position.y - surface.y) < 0.0001f,
                        "Grass in a partial chunk must be on an allowed terrain surface");
                    checkedInstances++;
                }
            }
            Require(checkedInstances > 0, "No instances available to test the partial boundary");
        }

        private static void CheckDefaultDistribution(GrassChunkManager manager, ProceduralGrassTerrain terrain)
        {
            const int cellsPerAxis = 40;
            for (int z = 0; z < cellsPerAxis; z++)
            for (int x = 0; x < cellsPerAxis; x++)
            {
                float worldX = terrain.Origin.x + (x + 0.5f) * terrain.Width / cellsPerAxis;
                float worldZ = terrain.Origin.z + (z + 0.5f) * terrain.Depth / cellsPerAxis;
                Require(terrain.TrySample(worldX, worldZ, out _, out _, out GrassTerrainLayer layer) &&
                    layer == GrassTerrainLayer.Grass, "Every demo terrain cell must be Grass");
            }
            CheckOnlyGrassDistribution(manager, terrain);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            IDictionary chunks = (IDictionary)typeof(GrassChunkManager).GetField("_chunks", flags).GetValue(manager);
            foreach (DictionaryEntry entry in chunks)
            {
                Vector2Int chunkPosition = (Vector2Int)entry.Key;
                object chunk = entry.Value;
                ComputeBuffer buffer = (ComputeBuffer)chunk.GetType().GetField("_matrices", flags).GetValue(chunk);
                Require(buffer != null, "Default demo must populate every active terrain chunk");
                Matrix4x4[] matrices = new Matrix4x4[buffer.count];
                buffer.GetData(matrices);
                bool[] quadrants = new bool[4];
                foreach (Matrix4x4 matrix in matrices)
                {
                    Vector3 position = matrix.GetColumn(3);
                    float localX = position.x - terrain.Origin.x - chunkPosition.x * terrain.ChunkWidth;
                    float localZ = position.z - terrain.Origin.z - chunkPosition.y * terrain.ChunkWidth;
                    int quadrant = (localX >= terrain.ChunkWidth * 0.5f ? 1 : 0) +
                        (localZ >= terrain.ChunkWidth * 0.5f ? 2 : 0);
                    quadrants[quadrant] = true;
                }
                Require(quadrants.All(populated => populated),
                    "Default demo must distribute grass across every quadrant of each active chunk");
            }
        }

        private static void CheckOnlyGrassDistribution(GrassChunkManager manager, ProceduralGrassTerrain terrain)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            IDictionary chunks = (IDictionary)typeof(GrassChunkManager).GetField("_chunks", flags).GetValue(manager);
            int count = 0;
            foreach (DictionaryEntry entry in chunks)
            {
                object chunk = entry.Value;
                ComputeBuffer buffer = (ComputeBuffer)chunk.GetType().GetField("_matrices", flags).GetValue(chunk);
                if (buffer == null) continue;
                Matrix4x4[] matrices = new Matrix4x4[buffer.count];
                buffer.GetData(matrices);
                foreach (Matrix4x4 matrix in matrices)
                {
                    Vector3 position = matrix.GetColumn(3);
                    Require(terrain.TrySample(position.x, position.z, out _, out _, out GrassTerrainLayer layer) &&
                        layer == GrassTerrainLayer.Grass, "Grass-only selection allowed another terrain category");
                    count++;
                }
            }
            Require(count > 0, "Grass-only selection unexpectedly removed every instance");
        }

        private static void CheckMeshRotation(GrassChunkManager manager, ProceduralGrassTerrain terrain,
            Mesh mesh, Quaternion meshRotation)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            IDictionary chunks = (IDictionary)typeof(GrassChunkManager).GetField("_chunks", flags).GetValue(manager);
            int checkedCount = 0;
            foreach (DictionaryEntry entry in chunks)
            {
                object chunk = entry.Value;
                ComputeBuffer buffer = (ComputeBuffer)chunk.GetType().GetField("_matrices", flags).GetValue(chunk);
                if (buffer == null) continue;
                Matrix4x4[] matrices = new Matrix4x4[buffer.count];
                buffer.GetData(matrices);
                foreach (Matrix4x4 matrix in matrices)
                {
                    Vector3 position = matrix.GetColumn(3);
                    Require(terrain.TrySample(position.x, position.z, out _, out Vector3 normal),
                        "Rotated instance must remain on the terrain");
                    Quaternion expected = Quaternion.FromToRotation(Vector3.up, normal) * meshRotation;
                    Require(Vector3.Dot(matrix.MultiplyVector(Vector3.up).normalized, expected * Vector3.up) > 0.999f &&
                        Vector3.Dot(matrix.MultiplyVector(Vector3.forward).normalized, expected * Vector3.forward) > 0.999f,
                        "Instance mesh rotation is not composed with the sampled terrain normal");
                    if (Quaternion.Angle(meshRotation, Quaternion.Euler(-90f, 0f, 0f)) < 0.01f)
                    {
                        Vector3 growthDirection = mesh.bounds.center.z < 0f ? Vector3.back : Vector3.forward;
                        Require(Vector3.Dot(matrix.MultiplyVector(growthDirection).normalized, normal) > 0.999f,
                            "Grass mesh growth axis does not point along the sampled surface normal");
                    }
                    checkedCount++;
                }
            }
            Require(checkedCount > 0, "No matrices available to check mesh rotation");
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

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Grass demo validation: " + message);
        }
    }
}
