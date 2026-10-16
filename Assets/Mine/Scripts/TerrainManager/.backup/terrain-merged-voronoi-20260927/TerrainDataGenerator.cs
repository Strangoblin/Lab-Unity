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
        //  地点 — 配方决定多实例数量与尺度，候选点保持最小间距
        // ════════════════════════════════════════════════════════════

        private static void CreateSites(TerrainMapData map, TerrainGenerationSettings settings)
        {
            System.Random random = new System.Random(unchecked(settings.Seed * 397 ^ 0x2B71));
            TerrainFeatureRule[] rules = settings.FeatureRules;
            float areaScale = settings.Width * settings.Height / 90000f;
            float radiusScale = Mathf.Min(settings.Width, settings.Height) / 300f;
            int[] counts = new int[rules.Length];
            int maxCount = 0;
            for (int i = 0; i < rules.Length; i++)
            {
                counts[i] = Mathf.Max(1, Mathf.RoundToInt(rules[i].SitesAtReferenceSize * areaScale));
                maxCount = Mathf.Max(maxCount, counts[i]);
            }

            for (int instance = 0; instance < maxCount; instance++)
            {
                for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
                {
                    if (instance >= counts[ruleIndex])
                        continue;
                    TerrainFeatureRule rule = rules[ruleIndex];
                    float radius = Mathf.Lerp(rule.MinRadiusAtReferenceSize,
                        rule.MaxRadiusAtReferenceSize, (float)random.NextDouble()) * radiusScale;
                    AddRepelledSite(map, settings, random, rule, radius, instance == 0);
                }
            }
        }

        private static void AddRepelledSite(
            TerrainMapData map,
            TerrainGenerationSettings settings,
            System.Random random,
            TerrainFeatureRule rule,
            float radius,
            bool useAnchor)
        {
            Vector2 anchor = PreferredAnchor(rule.Region);
            Vector2 preferred = settings.Origin + new Vector2(anchor.x * settings.Width,
                anchor.y * settings.Height);
            Vector2 bestPoint = Vector2.zero;
            bool found = false;
            float minRadius = rule.MinRadiusAtReferenceSize *
                Mathf.Min(settings.Width, settings.Height) / 300f;
            for (int sizeAttempt = 0; sizeAttempt < 4 && !found; sizeAttempt++)
            {
                float trialRadius = Mathf.Lerp(radius, minRadius, sizeAttempt / 3f);
                float margin = trialRadius + settings.CellSize * 2f;
                float bestScore = float.NegativeInfinity;
                for (int candidateIndex = 0; candidateIndex < 512; candidateIndex++)
                {
                    float u = useAnchor
                        ? anchor.x + ((float)random.NextDouble() - 0.5f) * 0.30f
                        : (float)random.NextDouble();
                    float v = useAnchor
                        ? anchor.y + ((float)random.NextDouble() - 0.5f) * 0.30f
                        : (float)random.NextDouble();
                    Vector2 candidate = settings.Origin + new Vector2(
                        Mathf.Clamp(u * settings.Width, margin, settings.Width - margin),
                        Mathf.Clamp(v * settings.Height, margin, settings.Height - margin));
                    float clearance = float.MaxValue;
                    for (int i = 0; i < map.Sites.Count; i++)
                    {
                        TerrainSite other = map.Sites[i];
                        clearance = Mathf.Min(clearance,
                            Vector2.Distance(candidate, other.Center) -
                            (trialRadius + other.Radius) * 0.8f);
                    }
                    if (map.Sites.Count > 0 && clearance <= 0f)
                        continue;
                    float score = map.Sites.Count == 0 ?
                        -Vector2.Distance(candidate, preferred) : clearance - (useAnchor
                            ? Vector2.Distance(candidate, preferred) * 0.35f : 0f);
                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    bestPoint = candidate;
                    found = true;
                }
                if (found)
                    radius = trialRadius;
            }
            if (!found)
                throw new InvalidOperationException($"Cannot place {rule.Region} site {map.Sites.Count}; " +
                    "reduce the feature count or radius in the map recipe.");
            map.AddSite(new TerrainSite(map.Sites.Count, rule.Region, bestPoint, radius,
                rule.ConnectRoad));
        }

        private static Vector2 PreferredAnchor(TerrainRegion region)
        {
            switch (region)
            {
                case TerrainRegion.Village: return new Vector2(0.29f, 0.50f);
                case TerrainRegion.WheatField: return new Vector2(0.70f, 0.36f);
                case TerrainRegion.Forest: return new Vector2(0.72f, 0.73f);
                case TerrainRegion.Water: return new Vector2(0.22f, 0.22f);
                case TerrainRegion.Meadow: return new Vector2(0.47f, 0.20f);
                case TerrainRegion.Orchard: return new Vector2(0.43f, 0.78f);
                default: return new Vector2(0.5f, 0.5f);
            }
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
            float reach = halfWidth + feather;
            float[] coverage = new float[map.WidthCells * map.HeightCells];
            for (int routeIndex = 0; routeIndex < map.Routes.Count; routeIndex++)
            {
                Vector2[] points = map.Routes[routeIndex].Points;
                for (int i = 1; i < points.Length; i++)
                {
                    Vector2 start = points[i - 1];
                    Vector2 end = points[i];
                    int minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(start.x, end.x) - reach - map.Origin.x) / map.CellSize));
                    int maxX = Mathf.Min(map.WidthCells - 1, Mathf.FloorToInt((Mathf.Max(start.x, end.x) + reach - map.Origin.x) / map.CellSize));
                    int minZ = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(start.y, end.y) - reach - map.Origin.y) / map.CellSize));
                    int maxZ = Mathf.Min(map.HeightCells - 1, Mathf.FloorToInt((Mathf.Max(start.y, end.y) + reach - map.Origin.y) / map.CellSize));
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            Vector2 point = map.CellCenter(x, z);
                            float distance = DistanceToSegment(point, start, end);
                            float value = 1f - Mathf.SmoothStep(0f, 1f,
                                Mathf.InverseLerp(halfWidth - feather, halfWidth + feather, distance));
                            int index = x + z * map.WidthCells;
                            coverage[index] = Mathf.Max(coverage[index], value);
                        }
                    }
                }
            }
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    float value = coverage[x + z * map.WidthCells];
                    if (map.GetRegion(x, z) == TerrainRegion.Water)
                        value = 0f;
                    TerrainRoadType type = map.GetRegion(x, z) == TerrainRegion.Village
                        ? TerrainRoadType.Stone
                        : TerrainRoadType.Dirt;
                    map.SetRoad(x, z, value, value > 0f ? type : TerrainRoadType.None);
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
