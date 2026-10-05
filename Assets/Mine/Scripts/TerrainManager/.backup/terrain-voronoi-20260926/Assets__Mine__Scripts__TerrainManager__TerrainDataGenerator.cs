// ════════════════════════════════════════════════════════════
//  TerrainDataGenerator — 生成区域、地点、连续道路及可选下凹高度
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
            CreateRoutes(map, settings);
            FillWater(map, settings);
            RasterizeRoads(map, settings);
            return map;
        }

        // ════════════════════════════════════════════════════════════
        //  地点与区域 — 固定布局骨架加种子扰动，保证四类区域出现
        // ════════════════════════════════════════════════════════════

        private static void CreateSites(TerrainMapData map, TerrainGenerationSettings settings)
        {
            System.Random random = new System.Random(unchecked(settings.Seed * 397 ^ 0x2B71));
            float shortSide = Mathf.Min(settings.Width, settings.Height);
            Vector2 origin = settings.Origin;

            map.AddSite(new TerrainSite(
                TerrainRegion.Village,
                origin + JitteredPoint(random, settings.Width, settings.Height, 0.29f, 0.50f),
                shortSide * 0.125f));
            map.AddSite(new TerrainSite(
                TerrainRegion.WheatField,
                origin + JitteredPoint(random, settings.Width, settings.Height, 0.70f, 0.36f),
                shortSide * 0.16f));
            map.AddSite(new TerrainSite(
                TerrainRegion.Forest,
                origin + JitteredPoint(random, settings.Width, settings.Height, 0.72f, 0.73f),
                shortSide * 0.18f));
        }

        private static Vector2 JitteredPoint(System.Random random, float width, float height, float u, float v)
        {
            float jitterX = ((float)random.NextDouble() - 0.5f) * width * 0.08f;
            float jitterZ = ((float)random.NextDouble() - 0.5f) * height * 0.08f;
            return new Vector2(u * width + jitterX, v * height + jitterZ);
        }

        private static void FillRegions(TerrainMapData map, TerrainGenerationSettings settings)
        {
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    Vector2 point = map.CellCenter(x, z);
                    float edgeNoise = Mathf.PerlinNoise(
                        x * 0.046f + settings.Seed * 0.013f,
                        z * 0.046f + settings.Seed * 0.021f);
                    float bestDistance = 1f;
                    TerrainRegion region = TerrainRegion.Grassland;

                    for (int i = 0; i < map.Sites.Count; i++)
                    {
                        TerrainSite site = map.Sites[i];
                        float radius = site.Radius * Mathf.Lerp(0.84f, 1.16f, edgeNoise);
                        float distance = Vector2.Distance(point, site.Center) / radius;
                        if (distance >= bestDistance)
                            continue;
                        bestDistance = distance;
                        region = site.Region;
                    }

                    map.SetRegion(x, z, region);
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  水域 — 在远离地点和路径的草地生成连续下凹区域
        // ════════════════════════════════════════════════════════════

        private static void FillWater(TerrainMapData map, TerrainGenerationSettings settings)
        {
            if (settings.DepressionDepth <= 0f)
                return;

            float radius = Mathf.Min(settings.Width, settings.Height) * 0.075f;
            float bestClearance = float.NegativeInfinity;
            Vector2 center = Vector2.zero;

            for (int z = 0; z <= 8; z++)
            {
                for (int x = 0; x <= 8; x++)
                {
                    Vector2 candidate = settings.Origin + new Vector2(
                        settings.Width * (0.12f + x * 0.095f),
                        settings.Height * (0.12f + z * 0.095f));
                    float clearance = WaterClearance(map, settings, candidate);
                    if (clearance <= bestClearance)
                        continue;
                    bestClearance = clearance;
                    center = candidate;
                }
            }

            if (bestClearance < radius * 1.15f)
                return;

            float shoreWidth = Mathf.Max(map.CellSize * 2f, radius * 0.24f);
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    if (map.GetRegion(x, z) != TerrainRegion.Grassland)
                        continue;

                    Vector2 point = map.CellCenter(x, z);
                    float edgeNoise = Mathf.PerlinNoise(
                        point.x * 0.055f + settings.Seed * 0.031f,
                        point.y * 0.055f + settings.Seed * 0.047f);
                    float localRadius = radius * Mathf.Lerp(0.88f, 1.12f, edgeNoise);
                    float shoreDistance = localRadius - Vector2.Distance(point, center);
                    if (shoreDistance <= 0f)
                        continue;

                    float depth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(shoreDistance / shoreWidth));
                    map.SetRegion(x, z, TerrainRegion.Water);
                    map.SetHeight(x, z, -settings.DepressionDepth * depth);
                }
            }
        }

        private static float WaterClearance(
            TerrainMapData map,
            TerrainGenerationSettings settings,
            Vector2 candidate)
        {
            int cellX = Mathf.FloorToInt((candidate.x - map.Origin.x) / map.CellSize);
            int cellZ = Mathf.FloorToInt((candidate.y - map.Origin.y) / map.CellSize);
            if (map.GetRegion(cellX, cellZ) != TerrainRegion.Grassland)
                return float.NegativeInfinity;

            Vector2 relative = candidate - map.Origin;
            float clearance = Mathf.Min(relative.x, relative.y,
                settings.Width - relative.x, settings.Height - relative.y);

            for (int i = 0; i < map.Sites.Count; i++)
            {
                TerrainSite site = map.Sites[i];
                clearance = Mathf.Min(clearance,
                    Vector2.Distance(candidate, site.Center) - site.Radius * 1.16f);
            }

            for (int routeIndex = 0; routeIndex < map.Routes.Count; routeIndex++)
            {
                Vector2[] points = map.Routes[routeIndex].Points;
                for (int i = 1; i < points.Length; i++)
                {
                    float distance = DistanceToSegment(candidate, points[i - 1], points[i]);
                    clearance = Mathf.Min(clearance, distance -
                        settings.RoadWidth * 0.5f - Mathf.Max(map.CellSize, 0.5f));
                }
            }

            return clearance;
        }

        // ════════════════════════════════════════════════════════════
        //  道路 — 连续折线连接地点，覆盖与材质类型独立于区域
        // ════════════════════════════════════════════════════════════

        private static void CreateRoutes(TerrainMapData map, TerrainGenerationSettings settings)
        {
            System.Random random = new System.Random(unchecked(settings.Seed * 997 ^ 0x34A5));
            AddRoute(map, map.Sites[0].Center, map.Sites[1].Center, random, settings);
            AddRoute(map, map.Sites[0].Center, map.Sites[2].Center, random, settings);
            AddRoute(map, map.Sites[1].Center, map.Sites[2].Center, random, settings);
        }

        private static void AddRoute(
            TerrainMapData map,
            Vector2 start,
            Vector2 end,
            System.Random random,
            TerrainGenerationSettings settings)
        {
            const int segmentCount = 18;
            Vector2[] points = new Vector2[segmentCount + 1];
            Vector2 direction = (end - start).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            float phase = (float)random.NextDouble() * Mathf.PI * 2f;
            float amplitude = Mathf.Min(settings.Width, settings.Height) * 0.025f;

            for (int i = 0; i <= segmentCount; i++)
            {
                float t = i / (float)segmentCount;
                float offset = Mathf.Sin(t * Mathf.PI) * Mathf.Sin(t * Mathf.PI * 3f + phase) * amplitude;
                points[i] = Vector2.Lerp(start, end, t) + normal * offset;
            }

            map.AddRoute(new TerrainRoute(points));
        }

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
