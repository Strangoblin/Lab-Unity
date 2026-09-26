// ════════════════════════════════════════════════════════════
//  TerrainDataGenerator — 生成连续沃洛诺伊区域、融合区域组及岸线下凹
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
            BuildRegionGroups(map);
            BuildWaterDepression(map, settings.DepressionDepth);
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
                float margin = Mathf.Max(settings.CellSize * 2f, trialRadius * 0.35f);
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
                            (trialRadius + other.Radius) * 0.42f);
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
        //  区域 — 草地与其它地点共同竞争，双尺度坐标扭曲改变共享边界
        // ════════════════════════════════════════════════════════════

        private static void FillRegions(TerrainMapData map, TerrainGenerationSettings settings)
        {
            float shortSide = Mathf.Min(settings.Width, settings.Height);
            Vector2 frequencies = new Vector2(settings.BroadWarpCycles / shortSide,
                settings.DetailWarpCycles / shortSide);
            Vector2 amplitudes = new Vector2(settings.BroadWarpDistance,
                settings.DetailWarpDistance) * (shortSide / 300f);
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    Vector2 point = map.CellCenter(x, z);
                    Vector2 warped = Vector2.Lerp(point,
                        WarpPoint(point, settings.Seed, frequencies, amplitudes),
                        SiteCoreWarpWeight(map, point));
                    float bestDistance = float.PositiveInfinity;
                    int bestSite = -1;

                    for (int i = 0; i < map.Sites.Count; i++)
                    {
                        TerrainSite site = map.Sites[i];
                        float distance = Vector2.Distance(warped, site.Center) / site.Radius;
                        if (distance >= bestDistance)
                            continue;
                        bestDistance = distance;
                        bestSite = i;
                    }
                    map.SetRegion(x, z, map.Sites[bestSite].Region, bestSite);
                }
            }
        }

        private static Vector2 WarpPoint(Vector2 point, int seed,
            Vector2 frequencies, Vector2 amplitudes)
        {
            float broadX = Mathf.PerlinNoise(point.x * frequencies.x + seed * 0.017f,
                point.y * frequencies.x + seed * 0.029f);
            float broadY = Mathf.PerlinNoise(point.x * frequencies.x + seed * 0.043f + 31.7f,
                point.y * frequencies.x + seed * 0.011f + 47.3f);
            float detailX = Mathf.PerlinNoise(point.x * frequencies.y + seed * 0.067f + 71.9f,
                point.y * frequencies.y + seed * 0.053f + 13.1f);
            float detailY = Mathf.PerlinNoise(point.x * frequencies.y + seed * 0.037f + 19.3f,
                point.y * frequencies.y + seed * 0.079f + 83.7f);
            return point + new Vector2(broadX - 0.5f, broadY - 0.5f) * (amplitudes.x * 2f) +
                new Vector2(detailX - 0.5f, detailY - 0.5f) * (amplitudes.y * 2f);
        }

        private static float SiteCoreWarpWeight(TerrainMapData map, Vector2 point)
        {
            float nearestDistanceSquared = float.PositiveInfinity;
            for (int i = 0; i < map.Sites.Count; i++)
            {
                TerrainSite site = map.Sites[i];
                float distanceSquared = (point - site.Center).sqrMagnitude /
                    (site.Radius * site.Radius);
                nearestDistanceSquared = Mathf.Min(nearestDistanceSquared, distanceSquared);
            }
            return Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.12f * 0.12f, 0.4f * 0.4f, nearestDistanceSquared));
        }

        // ════════════════════════════════════════════════════════════
        //  融合区域 — 同类相邻格归为一组，并选择道路代表地点
        // ════════════════════════════════════════════════════════════

        private static void BuildRegionGroups(TerrainMapData map)
        {
            int width = map.WidthCells;
            int[] queue = new int[width * map.HeightCells];
            for (int start = 0; start < queue.Length; start++)
            {
                int startX = start % width;
                int startZ = start / width;
                if (map.GetRegionGroupId(startX, startZ) >= 0)
                    continue;

                int groupId = map.RegionGroups.Count;
                TerrainRegion region = map.GetRegion(startX, startZ);
                int head = 0;
                int tail = 1;
                float sumX = 0f;
                float sumZ = 0f;
                queue[0] = start;
                map.SetRegionGroup(startX, startZ, groupId);
                while (head < tail)
                {
                    int cell = queue[head++];
                    int x = cell % width;
                    int z = cell / width;
                    Vector2 point = map.CellCenter(x, z);
                    sumX += point.x;
                    sumZ += point.y;
                    TryAddGroupCell(map, x - 1, z, region, groupId, queue, ref tail);
                    TryAddGroupCell(map, x + 1, z, region, groupId, queue, ref tail);
                    TryAddGroupCell(map, x, z - 1, region, groupId, queue, ref tail);
                    TryAddGroupCell(map, x, z + 1, region, groupId, queue, ref tail);
                }

                Vector2 center = new Vector2(sumX / tail, sumZ / tail);
                int anchor = FindGroupAnchor(map, groupId, center);
                map.AddRegionGroup(new TerrainRegionGroup(groupId, region, tail, center, anchor));
            }
        }

        private static void TryAddGroupCell(TerrainMapData map, int x, int z,
            TerrainRegion region, int groupId, int[] queue, ref int tail)
        {
            if (x < 0 || z < 0 || x >= map.WidthCells || z >= map.HeightCells ||
                map.GetRegionGroupId(x, z) >= 0 || map.GetRegion(x, z) != region)
                return;
            map.SetRegionGroup(x, z, groupId);
            queue[tail++] = x + z * map.WidthCells;
        }

        private static int FindGroupAnchor(TerrainMapData map, int groupId, Vector2 center)
        {
            int bestSite = -1;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < map.Sites.Count; i++)
            {
                TerrainSite site = map.Sites[i];
                if (!site.ConnectRoad)
                    continue;
                int x = Mathf.Clamp(Mathf.FloorToInt((site.Center.x - map.Origin.x) /
                    map.CellSize), 0, map.WidthCells - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt((site.Center.y - map.Origin.y) /
                    map.CellSize), 0, map.HeightCells - 1);
                if (map.GetRegionGroupId(x, z) != groupId)
                    continue;
                float distance = Vector2.SqrMagnitude(site.Center - center);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                bestSite = i;
            }
            return bestSite;
        }

        // ════════════════════════════════════════════════════════════
        //  水域高度 — 从最终区域边界向内测距，不依赖圆形地点半径
        // ════════════════════════════════════════════════════════════

        private static void BuildWaterDepression(TerrainMapData map, float depth)
        {
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
                    if (map.GetRegion(x, z) != TerrainRegion.Water ||
                        !IsWaterBoundary(map, x, z))
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
                TryAddWaterCell(map, x - 1, z, distance[index] + 1, distance, queue, ref tail);
                TryAddWaterCell(map, x + 1, z, distance[index] + 1, distance, queue, ref tail);
                TryAddWaterCell(map, x, z - 1, distance[index] + 1, distance, queue, ref tail);
                TryAddWaterCell(map, x, z + 1, distance[index] + 1, distance, queue, ref tail);
            }
        }

        private static bool IsWaterBoundary(TerrainMapData map, int x, int z)
        {
            return x == 0 || z == 0 || x == map.WidthCells - 1 ||
                z == map.HeightCells - 1 ||
                map.GetRegion(x - 1, z) != TerrainRegion.Water ||
                map.GetRegion(x + 1, z) != TerrainRegion.Water ||
                map.GetRegion(x, z - 1) != TerrainRegion.Water ||
                map.GetRegion(x, z + 1) != TerrainRegion.Water;
        }

        private static void TryAddWaterCell(TerrainMapData map, int x, int z,
            int cellDistance, int[] distance, int[] queue, ref int tail)
        {
            if (x < 0 || z < 0 || x >= map.WidthCells || z >= map.HeightCells)
                return;
            int index = x + z * map.WidthCells;
            if (distance[index] >= 0 || map.GetRegion(x, z) != TerrainRegion.Water)
                return;
            distance[index] = cellDistance;
            queue[tail++] = index;
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
