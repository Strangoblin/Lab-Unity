// ════════════════════════════════════════════════════════════
//  TerrainDataGenerator — 草地底层、特征 SDF、道路、整体扰动与派生数据
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
            InitializeGrassland(map);
            CreateSites(map, settings);
            FillFeatureRegions(map, settings);
            TerrainRoutePlanner.GenerateRoads(map, settings);
            TerrainDataNoise.Apply(map, settings);
            BuildRegionGroups(map);
            BuildWaterDepression(map, settings.DepressionDepth);
            return map;
        }

        private static void InitializeGrassland(TerrainMapData map)
        {
            for (int z = 0; z < map.HeightCells; z++)
                for (int x = 0; x < map.WidthCells; x++)
                    map.SetRegion(x, z, TerrainRegion.Grassland, -1);
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
            map.AddSite(new TerrainSite(map.Sites.Count, rule.Region, bestPoint, radius));
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
        //  区域 — 最近种子硬归属，平滑距离差阈值决定特征覆盖
        // ════════════════════════════════════════════════════════════

        private static void FillFeatureRegions(TerrainMapData map, TerrainGenerationSettings settings)
        {
            float[] centerGaps = new float[map.Sites.Count];
            for (int i = 0; i < map.Sites.Count; i++)
                centerGaps[i] = TerrainVoronoiSdf.DistanceGap(map,
                    map.Sites[i].Center, i, out _, settings.VoronoiRegionSoftness);

            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    Vector2 point = map.CellCenter(x, z);
                    float nearestDistance = float.PositiveInfinity;
                    int nearestSite = -1;
                    for (int i = 0; i < map.Sites.Count; i++)
                    {
                        TerrainSite site = map.Sites[i];
                        float ownerDistance = Vector2.SqrMagnitude(point - site.Center);
                        if (ownerDistance < nearestDistance)
                        {
                            nearestDistance = ownerDistance;
                            nearestSite = i;
                        }
                    }

                    TerrainRegion region = SampleVoronoiRegion(map, settings, point,
                        nearestSite, centerGaps[nearestSite]);
                    map.SetRegion(x, z, region, nearestSite);
                }
            }
        }

        private static TerrainRegion SampleVoronoiRegion(TerrainMapData map,
            TerrainGenerationSettings settings, Vector2 point, int owner,
            float centerGap)
        {
            TerrainSite site = map.Sites[owner];
            float gap = TerrainVoronoiSdf.DistanceGap(map, point, owner, out _,
                settings.VoronoiRegionSoftness);
            float width = settings.VoronoiRegionWidth *
                Mathf.Min(settings.Width, settings.Height) / 300f;
            float threshold = centerGap - width;
            return gap > threshold
                ? site.Region : TerrainRegion.Grassland;
        }

        // ════════════════════════════════════════════════════════════
        //  融合区域 — 同类相邻格归为一组
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
                map.AddRegionGroup(new TerrainRegionGroup(groupId, region, tail, center));
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

    }
}
