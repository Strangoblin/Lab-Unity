// ════════════════════════════════════════════════════════════
//  TerrainHeightGenerator — 按指定区域边界生成浅下凹高度
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 从指定区域的最终边界向内部测距，生成不依赖具体场景语义的浅下凹。
    /// </summary>
    public static class TerrainHeightGenerator
    {
        public static void ApplyDepression(TerrainMapData map, int region, float depth)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (region < 0 || float.IsNaN(depth) || float.IsInfinity(depth) || depth < 0f)
                throw new ArgumentException("Depression region or depth is invalid.");
            int width = map.WidthCells;
            int height = map.HeightCells;
            int[] distance = new int[width * height];
            int[] queue = new int[distance.Length];
            int head = 0;
            int tail = 0;
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = x + z * width;
                    distance[index] = -1;
                    if (map.GetRegion(x, z) != region ||
                        !IsRegionBoundary(map, x, z, region))
                        continue;
                    distance[index] = 0;
                    queue[tail++] = index;
                }
            }

            float shoreWidth = Mathf.Max(map.CellSize * 2f, 4f);
            while (head < tail)
            {
                int index = queue[head++];
                int x = index % width;
                int z = index / width;
                float amount = Mathf.Clamp01(distance[index] * map.CellSize / shoreWidth);
                map.SetHeight(x, z, -depth * Mathf.SmoothStep(0f, 1f, amount));
                TryAddRegionCell(map, x - 1, z, region, distance[index] + 1, distance, queue, ref tail);
                TryAddRegionCell(map, x + 1, z, region, distance[index] + 1, distance, queue, ref tail);
                TryAddRegionCell(map, x, z - 1, region, distance[index] + 1, distance, queue, ref tail);
                TryAddRegionCell(map, x, z + 1, region, distance[index] + 1, distance, queue, ref tail);
            }
        }

        private static bool IsRegionBoundary(TerrainMapData map, int x, int z, int region)
        {
            return x == 0 || z == 0 || x == map.WidthCells - 1 ||
                z == map.HeightCells - 1 ||
                map.GetRegion(x - 1, z) != region ||
                map.GetRegion(x + 1, z) != region ||
                map.GetRegion(x, z - 1) != region ||
                map.GetRegion(x, z + 1) != region;
        }

        private static void TryAddRegionCell(TerrainMapData map, int x, int z,
            int region, int cellDistance, int[] distance, int[] queue, ref int tail)
        {
            if (x < 0 || z < 0 || x >= map.WidthCells || z >= map.HeightCells)
                return;
            int index = x + z * map.WidthCells;
            if (distance[index] >= 0 || map.GetRegion(x, z) != region)
                return;
            distance[index] = cellDistance;
            queue[tail++] = index;
        }

    }
}
