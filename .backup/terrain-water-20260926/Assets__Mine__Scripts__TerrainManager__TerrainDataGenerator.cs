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
                    float depression = 0f;

                    for (int i = 0; i < map.Sites.Count; i++)
                    {
                        TerrainSite site = map.Sites[i];
                        float radius = site.Radius * Mathf.Lerp(0.84f, 1.16f, edgeNoise);
                        float distance = Vector2.Distance(point, site.Center) / radius;
                        if (distance >= bestDistance)
                            continue;
                        bestDistance = distance;
                        region = site.Region;
                        depression = site.Region == TerrainRegion.Forest
                            ? -settings.DepressionDepth * Mathf.SmoothStep(0f, 1f, 1f - distance)
                            : 0f;
                    }

                    map.SetRegion(x, z, region);
                    map.SetHeight(x, z, depression);
                }
            }
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
