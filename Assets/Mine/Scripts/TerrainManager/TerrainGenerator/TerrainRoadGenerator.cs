// ════════════════════════════════════════════════════════════
//  TerrainRoadGenerator — 沃洛诺伊种子距离差道路
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 在特征 SDF 完成后生成道路；最终坐标扰动由 TerrainDataNoise 统一处理。
    /// </summary>
    public static class TerrainRoadGenerator
    {
        public static void GenerateRoads(TerrainMapData map, float roadWidth,
            float cornerSoftness, int roadType)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (!(roadWidth >= 0f && roadWidth < float.PositiveInfinity) ||
                !(cornerSoftness >= 0f && cornerSoftness < float.PositiveInfinity) || roadType < 0)
                throw new ArgumentException("Road width, softness, or type is invalid.");
            if (roadType == TerrainRoadType.None || roadWidth == 0f)
                return;

            GenerateVoronoiSdfRoads(map, roadWidth, cornerSoftness, roadType);
        }

        // ════════════════════════════════════════════════════════════
        //  多边道路 — 由最近与次近种子的距离差取阈值
        // ════════════════════════════════════════════════════════════

        private static void GenerateVoronoiSdfRoads(TerrainMapData map,
            float roadWidth, float cornerSoftness, int roadType)
        {
            float halfWidth = roadWidth * 0.5f;
            float feather = Mathf.Max(map.CellSize, 0.5f);
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    int owner = map.GetOwnerSiteId(x, z);
                    float distance = TerrainVoronoiSdf.DistanceGap(map, map.CellCenter(x, z),
                        owner, out int neighbor, cornerSoftness);
                    if (neighbor < 0)
                        continue;
                    float coverage = RoadCoverage(distance, halfWidth, feather);
                    if (coverage > 0f)
                        map.SetRoad(x, z, coverage, roadType);
                }
            }
        }

        private static float RoadCoverage(float distance, float halfWidth, float feather)
        {
            return 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(halfWidth - feather, halfWidth + feather, distance));
        }

    }
}
