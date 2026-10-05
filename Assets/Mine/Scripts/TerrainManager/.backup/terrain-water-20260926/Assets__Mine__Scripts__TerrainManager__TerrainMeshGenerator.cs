// ════════════════════════════════════════════════════════════
//  TerrainMeshGenerator — 从逻辑网格生成分块地表与颜色图
// ════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.Terrain
{
    public readonly struct TerrainSurfaceChunk
    {
        public readonly Mesh Mesh;
        public readonly Vector3 Position;

        public TerrainSurfaceChunk(Mesh mesh, Vector3 position)
        {
            Mesh = mesh;
            Position = position;
        }
    }

    public sealed class TerrainSurfaceResult
    {
        public readonly Texture2D ColorMap;
        public readonly IReadOnlyList<TerrainSurfaceChunk> Chunks;

        public TerrainSurfaceResult(Texture2D colorMap, IReadOnlyList<TerrainSurfaceChunk> chunks)
        {
            ColorMap = colorMap;
            Chunks = chunks;
        }
    }

    /// <summary>
    /// 只消费 TerrainMapData，生成可渲染地表及供 Shader 采样的地图颜色。
    /// </summary>
    public static class TerrainMeshGenerator
    {
        public static TerrainSurfaceResult Generate(TerrainMapData map, int chunkCells)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (chunkCells < 1)
                throw new ArgumentOutOfRangeException(nameof(chunkCells));

            Texture2D colorMap = BuildColorMap(map);
            List<TerrainSurfaceChunk> chunks = new List<TerrainSurfaceChunk>();

            for (int z = 0; z < map.HeightCells; z += chunkCells)
            {
                for (int x = 0; x < map.WidthCells; x += chunkCells)
                {
                    int cellsX = Mathf.Min(chunkCells, map.WidthCells - x);
                    int cellsZ = Mathf.Min(chunkCells, map.HeightCells - z);
                    Mesh mesh = BuildChunk(map, x, z, cellsX, cellsZ);
                    Vector3 position = new Vector3(
                        map.Origin.x + x * map.CellSize,
                        0f,
                        map.Origin.y + z * map.CellSize);
                    chunks.Add(new TerrainSurfaceChunk(mesh, position));
                }
            }

            return new TerrainSurfaceResult(colorMap, chunks);
        }

        // ════════════════════════════════════════════════════════════
        //  颜色图 — 区域和道路各自取自同一逻辑网格
        // ════════════════════════════════════════════════════════════

        private static Texture2D BuildColorMap(TerrainMapData map)
        {
            Texture2D texture = new Texture2D(
                map.WidthCells, map.HeightCells, TextureFormat.RGBA32, false, false)
            {
                name = "Terrain_Experiment_ColorMap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color32[] pixels = new Color32[map.WidthCells * map.HeightCells];
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    Color ground = RegionColor(map.GetRegion(x, z));
                    Color road = RoadColor(map.GetRoadType(x, z));
                    pixels[x + z * map.WidthCells] = Color.Lerp(
                        ground, road, map.GetRoadCoverage(x, z));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Color RegionColor(TerrainRegion region)
        {
            switch (region)
            {
                case TerrainRegion.Village: return new Color(0.50f, 0.62f, 0.38f);
                case TerrainRegion.WheatField: return new Color(0.78f, 0.66f, 0.30f);
                case TerrainRegion.Forest: return new Color(0.18f, 0.38f, 0.21f);
                default: return new Color(0.37f, 0.59f, 0.28f);
            }
        }

        private static Color RoadColor(TerrainRoadType type)
        {
            switch (type)
            {
                case TerrainRoadType.Stone: return new Color(0.52f, 0.52f, 0.50f);
                case TerrainRoadType.Dirt: return new Color(0.68f, 0.51f, 0.31f);
                default: return Color.clear;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  地表分块 — 世界 UV 与网格顶点在边界处共享采样规则
        // ════════════════════════════════════════════════════════════

        private static Mesh BuildChunk(TerrainMapData map, int startX, int startZ, int cellsX, int cellsZ)
        {
            int row = cellsX + 1;
            Vector3[] vertices = new Vector3[row * (cellsZ + 1)];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[cellsX * cellsZ * 6];

            for (int z = 0; z <= cellsZ; z++)
            {
                for (int x = 0; x <= cellsX; x++)
                {
                    int index = x + z * row;
                    int mapX = startX + x;
                    int mapZ = startZ + z;
                    vertices[index] = new Vector3(
                        x * map.CellSize,
                        map.SampleHeightAtVertex(mapX, mapZ),
                        z * map.CellSize);
                    uv[index] = new Vector2(
                        mapX / (float)map.WidthCells,
                        mapZ / (float)map.HeightCells);
                }
            }

            for (int z = 0; z < cellsZ; z++)
            {
                for (int x = 0; x < cellsX; x++)
                {
                    int vertex = x + z * row;
                    int offset = (x + z * cellsX) * 6;
                    triangles[offset] = vertex;
                    triangles[offset + 1] = vertex + row;
                    triangles[offset + 2] = vertex + 1;
                    triangles[offset + 3] = vertex + 1;
                    triangles[offset + 4] = vertex + row;
                    triangles[offset + 5] = vertex + row + 1;
                }
            }

            Mesh mesh = new Mesh
            {
                name = $"Terrain_Chunk_{startX}_{startZ}",
                indexFormat = IndexFormat.UInt32
            };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
