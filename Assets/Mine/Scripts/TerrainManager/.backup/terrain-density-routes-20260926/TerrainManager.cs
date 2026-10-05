// ════════════════════════════════════════════════════════════
//  TerrainManager — 编排地图数据、地表和实验占位物的场景生命周期
// ════════════════════════════════════════════════════════════

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.Terrain
{
    /// <summary>
    /// 原野实验入口：生成全部结果后一次提交，重复生成会替换旧场景对象和资源。
    /// </summary>
    public sealed class TerrainManager : MonoBehaviour
    {
        [Header("Generation")]
        [SerializeField] private TerrainGenerationSettings _settings = new TerrainGenerationSettings();
        [SerializeField] private bool _generateOnStart = true;
        [SerializeField] private Shader _surfaceShader;

        private GameObject _generatedRoot;
        private TerrainSurfaceResult _surface;
        private Material _surfaceMaterial;
        private Material _markerMaterial;

        public TerrainMapData CurrentMap { get; private set; }
        public TerrainStuffPlan CurrentStuff { get; private set; }
        public int SurfaceChunkCount => _surface?.Chunks.Count ?? 0;
        public Transform GeneratedRoot => _generatedRoot != null ? _generatedRoot.transform : null;

        // ════════════════════════════════════════════════════════════
        //  生命周期 — Play Mode 自动生成，退出时释放生成资源
        // ════════════════════════════════════════════════════════════

        private void Start()
        {
            if (_generateOnStart)
                Generate();
        }

        private void OnDisable()
        {
            Release();
        }

        private void OnDestroy()
        {
            Release();
        }

        [ContextMenu("Generate Terrain Experiment")]
        public void Generate()
        {
            Shader surfaceShader = _surfaceShader != null
                ? _surfaceShader
                : Shader.Find("Render/TerrainSurface");
            Shader markerShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (surfaceShader == null || markerShader == null)
                throw new InvalidOperationException("Terrain surface or URP marker shader is missing.");

            TerrainMapData map = null;
            TerrainSurfaceResult surface = null;
            TerrainStuffPlan stuff = null;
            GameObject root = null;
            Material surfaceMaterial = null;
            Material markerMaterial = null;
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            long dataMilliseconds = 0;
            long meshMilliseconds = 0;
            long stuffMilliseconds = 0;

            try
            {
                map = TerrainDataGenerator.Generate(_settings);
                dataMilliseconds = timer.ElapsedMilliseconds;
                surface = TerrainMeshGenerator.Generate(map, _settings.ChunkCells);
                meshMilliseconds = timer.ElapsedMilliseconds - dataMilliseconds;
                stuff = TerrainStuffDistributor.Distribute(map, _settings);
                stuffMilliseconds = timer.ElapsedMilliseconds - dataMilliseconds - meshMilliseconds;

                surfaceMaterial = new Material(surfaceShader) { name = "Terrain_Experiment_Surface" };
                surfaceMaterial.SetTexture("_ColorMap", surface.ColorMap);
                markerMaterial = new Material(markerShader) { name = "Terrain_Experiment_Markers" };
                root = new GameObject("Generated Terrain Experiment");
                root.transform.SetParent(transform, false);
                CreateSurfaceObjects(root.transform, surface, surfaceMaterial);
                CreateStuffObjects(root.transform, stuff, markerMaterial);
            }
            catch
            {
                DisposeGenerated(root, surface, surfaceMaterial, markerMaterial);
                throw;
            }

            Release();
            _generatedRoot = root;
            _surface = surface;
            _surfaceMaterial = surfaceMaterial;
            _markerMaterial = markerMaterial;
            CurrentMap = map;
            CurrentStuff = stuff;

            Debug.Log($"Terrain experiment generated: {map.WidthCells}x{map.HeightCells} cells, " +
                      $"{surface.Chunks.Count} chunks, {stuff.Placements.Count} markers; " +
                      $"data {dataMilliseconds} ms, mesh {meshMilliseconds} ms, " +
                      $"distribution {stuffMilliseconds} ms, total {timer.ElapsedMilliseconds} ms.", this);
        }

        // ════════════════════════════════════════════════════════════
        //  表现提交 — Mesh 与几何占位物消费生成结果
        // ════════════════════════════════════════════════════════════

        private static void CreateSurfaceObjects(
            Transform root,
            TerrainSurfaceResult surface,
            Material material)
        {
            GameObject group = new GameObject("Surface Chunks");
            group.transform.SetParent(root, false);
            for (int i = 0; i < surface.Chunks.Count; i++)
            {
                TerrainSurfaceChunk chunk = surface.Chunks[i];
                GameObject child = new GameObject(chunk.Mesh.name);
                child.transform.SetParent(group.transform, false);
                child.transform.position = chunk.Position;
                child.AddComponent<MeshFilter>().sharedMesh = chunk.Mesh;
                MeshRenderer renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private static void CreateStuffObjects(
            Transform root,
            TerrainStuffPlan plan,
            Material material)
        {
            GameObject group = new GameObject("Stuff Markers");
            group.transform.SetParent(root, false);
            MaterialPropertyBlock block = new MaterialPropertyBlock();

            for (int i = 0; i < plan.Placements.Count; i++)
            {
                TerrainStuffPlacement placement = plan.Placements[i];
                PrimitiveType primitive = PrimitiveFor(placement.Type);
                GameObject marker = GameObject.CreatePrimitive(primitive);
                marker.name = $"{placement.Type}_{i}";
                marker.transform.SetParent(group.transform, false);
                marker.transform.localScale = ScaleFor(placement.Type);
                marker.transform.position = placement.Position + Vector3.up * marker.transform.localScale.y * 0.5f;
                marker.transform.rotation = Quaternion.Euler(0f, placement.Yaw, 0f);

                Collider collider = marker.GetComponent<Collider>();
                if (collider != null)
                    collider.enabled = false;
                MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                block.SetColor("_BaseColor", ColorFor(placement.Type));
                renderer.SetPropertyBlock(block);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private static PrimitiveType PrimitiveFor(TerrainStuffType type)
        {
            switch (type)
            {
                case TerrainStuffType.Windmill: return PrimitiveType.Cylinder;
                case TerrainStuffType.Tree: return PrimitiveType.Capsule;
                case TerrainStuffType.Wheat: return PrimitiveType.Cylinder;
                default: return PrimitiveType.Cube;
            }
        }

        private static Vector3 ScaleFor(TerrainStuffType type)
        {
            switch (type)
            {
                case TerrainStuffType.House: return new Vector3(6f, 4f, 6f);
                case TerrainStuffType.Windmill: return new Vector3(4f, 8f, 4f);
                case TerrainStuffType.Tree: return new Vector3(2.2f, 5f, 2.2f);
                case TerrainStuffType.Wheat: return new Vector3(0.35f, 1.1f, 0.35f);
                default: return new Vector3(0.55f, 0.65f, 0.55f);
            }
        }

        private static Color ColorFor(TerrainStuffType type)
        {
            switch (type)
            {
                case TerrainStuffType.House: return new Color(0.78f, 0.33f, 0.25f);
                case TerrainStuffType.Windmill: return new Color(0.95f, 0.91f, 0.76f);
                case TerrainStuffType.Tree: return new Color(0.08f, 0.28f, 0.12f);
                case TerrainStuffType.Wheat: return new Color(0.92f, 0.75f, 0.31f);
                default: return new Color(0.19f, 0.76f, 0.20f);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  资源清理 — 编辑模式与播放模式分别销毁实例
        // ════════════════════════════════════════════════════════════

        private void Release()
        {
            DisposeGenerated(_generatedRoot, _surface, _surfaceMaterial, _markerMaterial);
            _generatedRoot = null;
            _surface = null;
            _surfaceMaterial = null;
            _markerMaterial = null;
            CurrentMap = null;
            CurrentStuff = null;
        }

        private static void DisposeGenerated(
            GameObject root,
            TerrainSurfaceResult surface,
            Material surfaceMaterial,
            Material markerMaterial)
        {
            if (root != null)
            {
                root.SetActive(false);
                DestroyGeneratedObject(root);
            }
            if (surface != null)
            {
                for (int i = 0; i < surface.Chunks.Count; i++)
                    DestroyGeneratedObject(surface.Chunks[i].Mesh);
                DestroyGeneratedObject(surface.ColorMap);
            }
            DestroyGeneratedObject(surfaceMaterial);
            DestroyGeneratedObject(markerMaterial);
        }

        private static void DestroyGeneratedObject(UnityEngine.Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
