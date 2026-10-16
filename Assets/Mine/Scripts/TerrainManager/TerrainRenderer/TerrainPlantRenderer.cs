// ════════════════════════════════════════════════════════════
//  TerrainPlantRenderer — 将植被计划按地表块和类型批量绘制为实验几何
// ════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.Terrain
{
    /// <summary>
    /// 只消费植被放置计划；实验阶段用交叉面片实例化，后续可替换为专用植被渲染器。
    /// </summary>
    public sealed class TerrainPlantRenderer : IDisposable
    {
        private const int MaxInstancesPerDraw = 1023;

        private readonly struct PlantBatch
        {
            public readonly Matrix4x4[] Matrices;
            public readonly MaterialPropertyBlock Properties;

            public PlantBatch(Matrix4x4[] matrices, MaterialPropertyBlock properties)
            {
                Matrices = matrices;
                Properties = properties;
            }
        }

        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly List<PlantBatch> _batches = new List<PlantBatch>();
        private bool _disposed;

        public int BatchCount => _batches.Count;

        public TerrainPlantRenderer(TerrainMapData map, TerrainPlantPlan plan,
            int chunkCells, Material material, ITerrainAppearance appearance)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (material == null)
                throw new ArgumentNullException(nameof(material));
            if (chunkCells < 1)
                throw new ArgumentOutOfRangeException(nameof(chunkCells));
            if (appearance == null)
                throw new ArgumentNullException(nameof(appearance));

            _material = material;
            _material.enableInstancing = true;
            BuildBatches(map, plan, chunkCells, appearance);
            _mesh = CreateCrossMesh();
        }

        public void Draw(int layer)
        {
            if (_disposed)
                return;
            for (int i = 0; i < _batches.Count; i++)
            {
                PlantBatch batch = _batches[i];
                Graphics.DrawMeshInstanced(_mesh, 0, _material, batch.Matrices,
                    batch.Matrices.Length, batch.Properties, ShadowCastingMode.Off, false, layer);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _batches.Clear();
            TerrainSurfaceResult.DestroyResource(_mesh);
        }

        // ════════════════════════════════════════════════════════════
        //  批次构建 — 地图块内同类植被共享一次实例绘制
        // ════════════════════════════════════════════════════════════

        private void BuildBatches(TerrainMapData map, TerrainPlantPlan plan,
            int chunkCells, ITerrainAppearance appearance)
        {
            int chunksX = Mathf.CeilToInt(map.WidthCells / (float)chunkCells);
            Dictionary<(int chunkId, int type), List<Matrix4x4>> groups =
                new Dictionary<(int chunkId, int type), List<Matrix4x4>>();

            for (int i = 0; i < plan.Placements.Count; i++)
            {
                TerrainPlantPlacement plant = plan.Placements[i];
                int cellX = Mathf.FloorToInt((plant.Position.x - map.Origin.x) / map.CellSize);
                int cellZ = Mathf.FloorToInt((plant.Position.z - map.Origin.y) / map.CellSize);
                int chunkX = cellX / chunkCells;
                int chunkZ = cellZ / chunkCells;
                (int chunkId, int type) key = (chunkZ * chunksX + chunkX, plant.Type);
                if (!groups.TryGetValue(key, out List<Matrix4x4> matrices))
                {
                    matrices = new List<Matrix4x4>();
                    groups.Add(key, matrices);
                }

                Vector3 scale = appearance.PlantVisual(plant.Type).Size * plant.Scale;
                matrices.Add(Matrix4x4.TRS(plant.Position,
                    Quaternion.Euler(0f, plant.Yaw, 0f), scale));
            }

            foreach (KeyValuePair<(int chunkId, int type), List<Matrix4x4>> pair in groups)
            {
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", appearance.PlantVisual(pair.Key.type).Color);
                List<Matrix4x4> matrices = pair.Value;
                for (int start = 0; start < matrices.Count; start += MaxInstancesPerDraw)
                {
                    int count = Mathf.Min(MaxInstancesPerDraw, matrices.Count - start);
                    Matrix4x4[] batch = new Matrix4x4[count];
                    matrices.CopyTo(start, batch, 0, count);
                    _batches.Add(new PlantBatch(batch, properties));
                }
            }
        }

        private static Mesh CreateCrossMesh()
        {
            Mesh mesh = new Mesh { name = "Terrain_Experiment_PlantCross" };
            try
            {
                mesh.vertices = new[]
                {
                    new Vector3(-0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f),
                    new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f),
                    new Vector3(0f, 0f, -0.5f), new Vector3(0f, 1f, -0.5f),
                    new Vector3(0f, 0f, 0.5f), new Vector3(0f, 1f, 0.5f)
                };
                mesh.triangles = new[]
                {
                    0, 1, 2, 2, 1, 3, 2, 1, 0, 3, 1, 2,
                    4, 5, 6, 6, 5, 7, 6, 5, 4, 7, 5, 6
                };
                mesh.RecalculateBounds();
                return mesh;
            }
            catch
            {
                TerrainSurfaceResult.DestroyResource(mesh);
                throw;
            }
        }
    }
}
