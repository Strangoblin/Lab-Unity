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
        //  颜色图 — 逻辑区域保持离散，表现颜色在边界附近连续混合
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

            Color[] groundColors = BuildSmoothedRegionColors(map);
            Color32[] pixels = new Color32[groundColors.Length];
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    int index = x + z * map.WidthCells;
                    Color ground = groundColors[index];
                    Color road = RoadColor(map.GetRoadType(x, z), ground);
                    pixels[index] = Color.Lerp(ground, road, map.GetRoadCoverage(x, z));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Color[] BuildSmoothedRegionColors(TerrainMapData map)
        {
            int width = map.WidthCells;
            int height = map.HeightCells;
            int radius = Mathf.Clamp(Mathf.CeilToInt(3f / map.CellSize), 1, 12);
            float sigma = Mathf.Max(1f, radius * 0.55f);
            Color[] source = new Color[width * height];
            Color[] horizontal = new Color[source.Length];
            Color[] result = new Color[source.Length];
            float[] weights = new float[radius * 2 + 1];

            for (int offset = -radius; offset <= radius; offset++)
                weights[offset + radius] = Mathf.Exp(-offset * offset / (2f * sigma * sigma));
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                    source[x + z * width] = RegionColor(map.GetRegion(x, z));

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color sum = Color.clear;
                    float total = 0f;
                    for (int offset = -radius; offset <= radius; offset++)
                    {
                        int sampleX = Mathf.Clamp(x + offset, 0, width - 1);
                        float weight = weights[offset + radius];
                        sum += source[sampleX + z * width] * weight;
                        total += weight;
                    }
                    horizontal[x + z * width] = sum / total;
                }
            }

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color sum = Color.clear;
                    float total = 0f;
                    for (int offset = -radius; offset <= radius; offset++)
                    {
                        int sampleZ = Mathf.Clamp(z + offset, 0, height - 1);
                        float weight = weights[offset + radius];
                        sum += horizontal[x + sampleZ * width] * weight;
                        total += weight;
                    }
                    result[x + z * width] = sum / total;
                }
            }

            return result;
        }

        private static Color RegionColor(TerrainRegion region)
        {
            switch (region)
            {
                case TerrainRegion.Village: return new Color(0.50f, 0.62f, 0.38f);
                case TerrainRegion.WheatField: return new Color(0.78f, 0.66f, 0.30f);
                case TerrainRegion.Forest: return new Color(0.18f, 0.38f, 0.21f);
                case TerrainRegion.Water: return new Color(0.10f, 0.36f, 0.65f);
                case TerrainRegion.Meadow: return new Color(0.52f, 0.69f, 0.31f);
                case TerrainRegion.Orchard: return new Color(0.34f, 0.49f, 0.23f);
                default: return new Color(0.37f, 0.59f, 0.28f);
            }
        }

        private static Color RoadColor(TerrainRoadType type, Color ground)
        {
            switch (type)
            {
                case TerrainRoadType.Stone: return Color.Lerp(ground, Color.white, 0.30f);
                case TerrainRoadType.Dirt: return Color.Lerp(ground, Color.white, 0.17f);
                default: return ground;
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
