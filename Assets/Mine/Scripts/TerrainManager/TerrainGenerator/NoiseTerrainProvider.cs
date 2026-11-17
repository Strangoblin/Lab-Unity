// ════════════════════════════════════════════════════════════
//  NoiseTerrainProvider — 多倍频 Perlin 高度与简单分层
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 用种子偏移的多倍频 Perlin 噪声填充高度，并按高度/坡度写入地表层。
    /// </summary>
    public sealed class NoiseTerrainProvider : ITerrainProvider
    {
        private const float SnowHeightFactor = 0.55f;
        private const float SandHeightFactor = -0.55f;
        private const float RockSlope = 0.25f;

        public void Generate(TerrainGrid grid, TerrainSettings settings)
        {
            var random = new System.Random(settings.Seed);
            float offsetX = (float)random.NextDouble() * 1000f;
            float offsetZ = (float)random.NextDouble() * 1000f;
            float heightScale = settings.HeightScale;

            for (int z = 0; z <= grid.HeightCells; z++)
            {
                float worldZ = grid.Origin.z + z * grid.CellSize;
                for (int x = 0; x <= grid.WidthCells; x++)
                {
                    float worldX = grid.Origin.x + x * grid.CellSize;
                    grid.SetHeight(x, z, SampleHeight(worldX, worldZ, settings,
                        offsetX, offsetZ) * heightScale);
                }
            }

            grid.RecalculateHeightRange();
            AssignLayers(grid, settings, heightScale);
        }

        // ════════════════════════════════════════════════════════════
        //  高度 — 幅度逐层减半、频率逐层翻倍
        // ════════════════════════════════════════════════════════════

        private static float SampleHeight(float worldX, float worldZ, TerrainSettings settings,
            float offsetX, float offsetZ)
        {
            float amplitude = 1f;
            float frequency = settings.NoiseScale;
            float sum = 0f;
            float total = 0f;

            for (int octave = 0; octave < settings.NoiseOctaves; octave++)
            {
                sum += Mathf.PerlinNoise(worldX * frequency + offsetX,
                    worldZ * frequency + offsetZ) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }

            return total > 0f ? (sum / total - 0.5f) * 2f : 0f;
        }

        // ════════════════════════════════════════════════════════════
        //  地表层 — 高度决定雪/沙，格内坡度决定岩石
        // ════════════════════════════════════════════════════════════

        private static void AssignLayers(TerrainGrid grid, TerrainSettings settings,
            float heightScale)
        {
            for (int z = 0; z < grid.HeightCells; z++)
            {
                for (int x = 0; x < grid.WidthCells; x++)
                {
                    Vector3 p00 = grid.GetVertexPosition(x, z);
                    Vector3 p10 = grid.GetVertexPosition(x + 1, z);
                    Vector3 p11 = grid.GetVertexPosition(x + 1, z + 1);
                    Vector3 normal = Vector3.Cross(p11 - p00, p10 - p00).normalized;
                    float slope = 1f - Mathf.Abs(normal.y);
                    float heightFactor = heightScale > 0f
                        ? (grid.GetHeight(x, z) + grid.GetHeight(x + 1, z) +
                           grid.GetHeight(x + 1, z + 1)) / (3f * heightScale)
                        : 0f;

                    TerrainSurfaceLayer layer = settings.DefaultLayer;
                    if (heightFactor > SnowHeightFactor)
                        layer = TerrainSurfaceLayer.Snow;
                    else if (heightFactor < SandHeightFactor)
                        layer = TerrainSurfaceLayer.Sand;
                    else if (slope > RockSlope)
                        layer = TerrainSurfaceLayer.Rock;

                    grid.SetLayer(x, z, layer);
                }
            }
        }
    }
}
