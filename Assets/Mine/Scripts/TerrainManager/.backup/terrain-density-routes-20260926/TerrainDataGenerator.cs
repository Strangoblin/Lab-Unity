// ════════════════════════════════════════════════════════════
//  TerrainDataGenerator — 生成排斥地点、有限沃洛诺伊区域及下凹高度
// ════════════════════════════════════════════════════════════

using System;
using UnityEngine;

namespace Mine.Terrain
{
    /// <summary>
    /// 根据配置和种子生成原野地图数据，不创建任何场景对象。
    /// </summary>
    public static class TerrainDataGenerator
    {
        public static TerrainMapData Generate(TerrainGenerationSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            settings.Validate();

            TerrainMapData map = new TerrainMapData(settings);
            CreateSites(map, settings);
            FillRegions(map, settings);
            TerrainRoutePlanner.CreateRoutes(map, settings);
            RasterizeRoads(map, settings);
            return map;
        }

        // ════════════════════════════════════════════════════════════
        //  地点 — 候选点按距离排斥，四类特征共用相同选址规则
        // ════════════════════════════════════════════════════════════

        private static void CreateSites(TerrainMapData map, TerrainGenerationSettings settings)
        {
            System.Random random = new System.Random(unchecked(settings.Seed * 397 ^ 0x2B71));
            float shortSide = Mathf.Min(settings.Width, settings.Height);

            AddRepelledSite(map, settings, random, TerrainRegion.Village,
                new Vector2(0.29f, 0.50f), shortSide * 0.125f);
            AddRepelledSite(map, settings, random, TerrainRegion.WheatField,
                new Vector2(0.70f, 0.36f), shortSide * 0.16f);
            AddRepelledSite(map, settings, random, TerrainRegion.Forest,
                new Vector2(0.72f, 0.73f), shortSide * 0.18f);
            AddRepelledSite(map, settings, random, TerrainRegion.Water,
                new Vector2(0.22f, 0.22f), shortSide * 0.09f);
        }

        private static void AddRepelledSite(
            TerrainMapData map,
            TerrainGenerationSettings settings,
            System.Random random,
            TerrainRegion region,
            Vector2 anchor,
            float radius)
        {
            Vector2 preferred = settings.Origin + new Vector2(
                anchor.x * settings.Width, anchor.y * settings.Height);
            Vector2 bestPoint = preferred;
            float bestScore = float.NegativeInfinity;
            float margin = radius * 1.2f;

            for (int candidateIndex = 0; candidateIndex < 64; candidateIndex++)
            {
                float u = anchor.x + ((float)random.NextDouble() - 0.5f) * 0.36f;
                float v = anchor.y + ((float)random.NextDouble() - 0.5f) * 0.36f;
                Vector2 candidate = settings.Origin + new Vector2(
                    Mathf.Clamp(u * settings.Width, margin, settings.Width - margin),
                    Mathf.Clamp(v * settings.Height, margin, settings.Height - margin));
                float anchorDistance = Vector2.Distance(candidate, preferred);
                float clearance = float.MaxValue;

                for (int i = 0; i < map.Sites.Count; i++)
                {
                    TerrainSite other = map.Sites[i];
                    clearance = Mathf.Min(clearance,
                        Vector2.Distance(candidate, other.Center) - radius - other.Radius);
                }

                float score = map.Sites.Count == 0
                    ? -anchorDistance
                    : clearance - anchorDistance * 0.2f;
                if (score <= bestScore)
                    continue;
                bestScore = score;
                bestPoint = candidate;
            }

            map.AddSite(new TerrainSite(region, bestPoint, radius));
        }

        // ════════════════════════════════════════════════════════════
        //  区域 — 归一化距离决定归属，单位阈值外保留草地
        // ════════════════════════════════════════════════════════════

        private static void FillRegions(TerrainMapData map, TerrainGenerationSettings settings)
        {
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    Vector2 point = map.CellCenter(x, z);
                    float bestDistance = 1f;
                    TerrainRegion region = TerrainRegion.Grassland;
                    float waterDepth = 0f;

                    for (int i = 0; i < map.Sites.Count; i++)
                    {
                        TerrainSite site = map.Sites[i];
                        float edgeNoise = Mathf.PerlinNoise(
                            point.x * 0.042f + settings.Seed * 0.013f + i * 7.31f,
                            point.y * 0.042f + settings.Seed * 0.021f + i * 11.17f);
                        float distance = Vector2.Distance(point, site.Center) / site.Radius -
                            (edgeNoise - 0.5f) * 0.18f;
                        if (distance >= bestDistance)
                            continue;
                        bestDistance = distance;
                        region = site.Region;
                        float shoreWidth = Mathf.Max(map.CellSize * 2f, site.Radius * 0.24f);
                        waterDepth = site.Region == TerrainRegion.Water
                            ? -settings.DepressionDepth * Mathf.SmoothStep(0f, 1f,
                                Mathf.Clamp01((1f - distance) * site.Radius / shoreWidth))
                            : 0f;
                    }

                    map.SetRegion(x, z, region);
                    map.SetHeight(x, z, waterDepth);
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  道路栅格化 — 连续折线覆盖与路面类型独立于区域
        // ════════════════════════════════════════════════════════════

        private static void RasterizeRoads(TerrainMapData map, TerrainGenerationSettings settings)
        {
            float halfWidth = settings.RoadWidth * 0.5f;
            float feather = Mathf.Max(map.CellSize, 0.5f);

            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    Vector2 point = map.CellCenter(x, z);
                    float nearest = float.MaxValue;

                    for (int routeIndex = 0; routeIndex < map.Routes.Count; routeIndex++)
                    {
                        Vector2[] points = map.Routes[routeIndex].Points;
                        for (int i = 1; i < points.Length; i++)
                            nearest = Mathf.Min(nearest, DistanceToSegment(point, points[i - 1], points[i]));
                    }

                    float coverage = 1f - Mathf.SmoothStep(
                        0f, 1f, Mathf.InverseLerp(halfWidth - feather, halfWidth + feather, nearest));
                    if (map.GetRegion(x, z) == TerrainRegion.Water)
                        coverage = 0f;
                    TerrainRoadType type = map.GetRegion(x, z) == TerrainRegion.Village
                        ? TerrainRoadType.Stone
                        : TerrainRoadType.Dirt;
                    map.SetRoad(x, z, coverage, coverage > 0f ? type : TerrainRoadType.None);
                }
            }
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 0.0001f)
                return Vector2.Distance(point, start);

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }
    }
}
