// ════════════════════════════════════════════════════════════
//  TerrainSceneRenderer — 场景提交与生成表现资源的所有权边界
// ════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 将地表、稀疏物体和密集植被计划提交到场景，统一拥有表现对象和生成资源。
    /// </summary>
    public sealed class TerrainSceneRenderer : IDisposable
    {
        private readonly TerrainSurfaceResult _surface;
        private GameObject _generatedRoot;
        private Material _surfaceMaterial;
        private Material _markerMaterial;
        private TerrainPlantRenderer _plantRenderer;
        private bool _disposed;

        public int SurfaceChunkCount => _surface?.Chunks.Count ?? 0;
        public int PlantBatchCount => _plantRenderer?.BatchCount ?? 0;
        public Transform GeneratedRoot => _generatedRoot != null ? _generatedRoot.transform : null;

        public TerrainSceneRenderer(Transform parent, TerrainMapData map,
            TerrainSurfaceResult surface, TerrainStuffPlan stuff, TerrainPlantPlan plants,
            ITerrainAppearance appearance, TerrainRenderSettings settings, int chunkCells)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (surface == null)
                throw new ArgumentNullException(nameof(surface));
            if (stuff == null)
                throw new ArgumentNullException(nameof(stuff));
            if (plants == null)
                throw new ArgumentNullException(nameof(plants));
            if (appearance == null)
                throw new ArgumentNullException(nameof(appearance));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (chunkCells < 1)
                throw new ArgumentOutOfRangeException(nameof(chunkCells));

            try
            {
                Shader surfaceShader = settings.SurfaceShader != null
                    ? settings.SurfaceShader
                    : Shader.Find("TerrainSurface/TerrainSurface");
                Shader markerShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (surfaceShader == null || markerShader == null)
                    throw new InvalidOperationException("Terrain surface or URP marker shader is missing.");

                _surfaceMaterial = new Material(surfaceShader) { name = "Terrain_Experiment_Surface" };
                _surfaceMaterial.SetTexture("_ColorMap", surface.ColorMap);
                _markerMaterial = new Material(markerShader) { name = "Terrain_Experiment_Markers" };
                _plantRenderer = new TerrainPlantRenderer(map, plants, chunkCells,
                    _markerMaterial, appearance);
                _generatedRoot = new GameObject("Generated Terrain Experiment");
                _generatedRoot.SetActive(false);
                _generatedRoot.transform.SetParent(parent, false);
                CreateSurfaceObjects(_generatedRoot.transform, surface, _surfaceMaterial);
                CreateStuffObjects(_generatedRoot.transform, stuff, _markerMaterial, appearance);
                _generatedRoot.SetActive(true);
                _surface = surface;
            }
            catch
            {
                ReleaseSceneObjects();
                throw;
            }
        }

        public void Draw(int layer)
        {
            if (!_disposed && _generatedRoot != null && _generatedRoot.activeInHierarchy)
                _plantRenderer?.Draw(layer);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            ReleaseSceneObjects();
            _surface?.Dispose();
        }

        // ════════════════════════════════════════════════════════════
        //  表现创建 — 消费类型外观与计划，保持网格及实例批次互相独立
        // ════════════════════════════════════════════════════════════

        private static void CreateSurfaceObjects(Transform root,
            TerrainSurfaceResult surface, Material material)
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

        private static void CreateStuffObjects(Transform root, TerrainStuffPlan plan,
            Material material, ITerrainAppearance appearance)
        {
            GameObject group = new GameObject("Stuff Markers");
            group.transform.SetParent(root, false);
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            for (int i = 0; i < plan.Placements.Count; i++)
            {
                TerrainStuffPlacement placement = plan.Placements[i];
                TerrainStuffVisual visual = appearance.StuffVisual(placement.Type);
                GameObject marker = GameObject.CreatePrimitive(visual.Primitive);
                marker.transform.SetParent(group.transform, false);
                marker.name = $"{placement.Type}_{i}";
                marker.transform.localScale = visual.Scale;
                marker.transform.position = placement.Position + Vector3.up * visual.Scale.y * 0.5f;
                marker.transform.rotation = Quaternion.Euler(0f, placement.Yaw, 0f);

                Collider collider = marker.GetComponent<Collider>();
                if (collider != null)
                    collider.enabled = false;
                MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                block.SetColor("_BaseColor", visual.Color);
                renderer.SetPropertyBlock(block);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  场景释放 — 构造失败只清理自产对象，成功后同时释放接管的地表结果
        // ════════════════════════════════════════════════════════════

        private void ReleaseSceneObjects()
        {
            if (_generatedRoot != null)
            {
                _generatedRoot.SetActive(false);
                TerrainSurfaceResult.DestroyResource(_generatedRoot);
                _generatedRoot = null;
            }
            _plantRenderer?.Dispose();
            _plantRenderer = null;
            TerrainSurfaceResult.DestroyResource(_surfaceMaterial);
            TerrainSurfaceResult.DestroyResource(_markerMaterial);
            _surfaceMaterial = null;
            _markerMaterial = null;
        }
    }
}
