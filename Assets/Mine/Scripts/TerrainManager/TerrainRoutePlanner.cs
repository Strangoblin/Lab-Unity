// ════════════════════════════════════════════════════════════
//  TerrainRoutePlanner — 沃洛诺伊种子距离差道路
// ════════════════════════════════════════════════════════════

using System;
using UnityEngine;

namespace Mine.Terrain
{
    /// <summary>
    /// 在特征 SDF 完成后生成道路；最终坐标扰动由 TerrainDataNoise 统一处理。
    /// </summary>
    public static class TerrainRoutePlanner
    {
        public static void GenerateRoads(TerrainMapData map, TerrainGenerationSettings settings)
        {
            if (map == null || settings == null)
                throw new ArgumentNullException(map == null ? nameof(map) : nameof(settings));

            GenerateVoronoiSdfRoads(map, settings);
        }

        // ════════════════════════════════════════════════════════════
        //  多边道路 — 由最近与次近种子的距离差取阈值
        // ════════════════════════════════════════════════════════════

        private static void GenerateVoronoiSdfRoads(TerrainMapData map,
            TerrainGenerationSettings settings)
        {
            float halfWidth = settings.VoronoiRoadWidth * 0.5f;
            float feather = Mathf.Max(map.CellSize, 0.5f);
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    int owner = map.GetOwnerSiteId(x, z);
                    float distance = TerrainVoronoiSdf.DistanceGap(map, map.CellCenter(x, z),
                        owner, out int neighbor, settings.VoronoiCornerSoftness);
                    if (neighbor < 0)
                        continue;
                    float coverage = RoadCoverage(distance, halfWidth, feather);
                    if (coverage > 0f)
                        map.SetRoad(x, z, coverage, TerrainRoadType.Dirt);
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
