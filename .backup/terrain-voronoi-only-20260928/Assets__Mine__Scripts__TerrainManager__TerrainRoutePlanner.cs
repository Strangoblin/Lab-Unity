// ════════════════════════════════════════════════════════════
//  TerrainRoutePlanner — 种子连线与沃洛诺伊胞边 SDF 道路
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

            if (settings.RoadSdfMode == TerrainRoadSdfMode.VoronoiCell)
                GenerateVoronoiSdfRoads(map, settings);
            else
                GenerateSiteLinkRoads(map, settings);
        }

        // ════════════════════════════════════════════════════════════
        //  标准道路 — 每个种子连接最近种子，以线段 SDF 着色
        // ════════════════════════════════════════════════════════════

        private static void GenerateSiteLinkRoads(TerrainMapData map,
            TerrainGenerationSettings settings)
        {
            bool[,] linked = new bool[map.Sites.Count, map.Sites.Count];
            for (int i = 0; i < map.Sites.Count; i++)
            {
                TerrainSite site = map.Sites[i];
                int nearest = -1;
                float nearestDistance = float.PositiveInfinity;
                for (int j = 0; j < map.Sites.Count; j++)
                {
                    if (i == j)
                        continue;
                    float distance = (site.Center - map.Sites[j].Center).sqrMagnitude;
                    if (distance >= nearestDistance)
                        continue;
                    nearestDistance = distance;
                    nearest = j;
                }
                if (nearest < 0 || linked[i, nearest])
                    continue;
                linked[i, nearest] = linked[nearest, i] = true;
                PaintSiteLink(map, settings, site, map.Sites[nearest]);
            }
        }

        private static void PaintSiteLink(TerrainMapData map,
            TerrainGenerationSettings settings, TerrainSite a, TerrainSite b)
        {
            TerrainRoadType type = RoadType(a, b);
            float halfWidth = settings.RoadWidth * 0.5f;
            float feather = Mathf.Max(map.CellSize, 0.5f);
            float reach = halfWidth + feather;
            int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.Center.x, b.Center.x) -
                reach - map.Origin.x) / map.CellSize));
            int maxX = Mathf.Min(map.WidthCells - 1,
                Mathf.FloorToInt((Mathf.Max(a.Center.x, b.Center.x) + reach -
                    map.Origin.x) / map.CellSize));
            int minZ = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.Center.y, b.Center.y) -
                reach - map.Origin.y) / map.CellSize));
            int maxZ = Mathf.Min(map.HeightCells - 1,
                Mathf.FloorToInt((Mathf.Max(a.Center.y, b.Center.y) + reach -
                    map.Origin.y) / map.CellSize));
            Vector2 segment = b.Center - a.Center;
            float segmentLengthSq = segment.sqrMagnitude;
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 point = map.CellCenter(x, z);
                    float t = Mathf.Clamp01(Vector2.Dot(point - a.Center, segment) /
                        segmentLengthSq);
                    float distance = Vector2.Distance(point, a.Center + segment * t);
                    float coverage = RoadCoverage(distance, halfWidth, feather);
                    if (coverage > map.GetRoadCoverage(x, z))
                        map.SetRoad(x, z, coverage, type);
                }
            }
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
                    TerrainSite a = map.Sites[owner];
                    TerrainSite b = map.Sites[neighbor];
                    float coverage = RoadCoverage(distance, halfWidth, feather);
                    if (coverage > 0f)
                        map.SetRoad(x, z, coverage, RoadType(a, b));
                }
            }
        }

        private static float RoadCoverage(float distance, float halfWidth, float feather)
        {
            return 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(halfWidth - feather, halfWidth + feather, distance));
        }

        private static TerrainRoadType RoadType(TerrainSite a, TerrainSite b)
        {
            return a.Region == TerrainRegion.Village || b.Region == TerrainRegion.Village
                ? TerrainRoadType.Stone : TerrainRoadType.Dirt;
        }
    }
}
