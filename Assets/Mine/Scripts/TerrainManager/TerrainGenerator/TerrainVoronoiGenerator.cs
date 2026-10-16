// ════════════════════════════════════════════════════════════
//  TerrainVoronoiGenerator — 基础区域、特征 SDF、道路、整体扰动与派生数据
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 依据通用配方生成 Voronoi 地图数据，不依赖具体场景特征或创建场景对象。
    /// </summary>
    public static class TerrainVoronoiGenerator
    {
        public static TerrainMapData Generate(TerrainGenerationSettings settings,
            TerrainVoronoiSettings profile)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            settings.Validate();
            profile.Validate();

            TerrainMapData map = new TerrainMapData(settings);
            InitializeBaseRegion(map, profile.BaseRegion);
            CreateSites(map, settings, profile);
            FillFeatureRegions(map, settings, profile);
            if (profile.RoadType != TerrainRoadType.None)
                TerrainRoadGenerator.GenerateRoads(map, profile.VoronoiRoadWidth,
                    profile.VoronoiCornerSoftness, profile.RoadType);
            TerrainDataNoise.Apply(map, settings, profile);
            BuildRegionGroups(map);
            return map;
        }

        private static void InitializeBaseRegion(TerrainMapData map, int baseRegion)
        {
            for (int z = 0; z < map.HeightCells; z++)
                for (int x = 0; x < map.WidthCells; x++)
                    map.SetRegion(x, z, baseRegion, -1);
        }

        // ════════════════════════════════════════════════════════════
        //  地点 — 配方决定多实例数量与尺度，候选点保持最小间距
        // ════════════════════════════════════════════════════════════

        private static void CreateSites(TerrainMapData map, TerrainGenerationSettings settings,
            TerrainVoronoiSettings profile)
        {
            System.Random random = new System.Random(unchecked(settings.Seed * 397 ^ 0x2B71));
            TerrainFeatureRule[] rules = profile.FeatureRules;
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
            Vector2 anchor = rule.PreferredAnchor;
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

        // ════════════════════════════════════════════════════════════
        //  区域 — 最近种子硬归属，平滑距离差阈值决定特征覆盖
        // ════════════════════════════════════════════════════════════

        private static void FillFeatureRegions(TerrainMapData map, TerrainGenerationSettings settings,
            TerrainVoronoiSettings profile)
        {
            if (map.Sites.Count == 0)
                return;
            float[] centerGaps = new float[map.Sites.Count];
            for (int i = 0; i < map.Sites.Count; i++)
                centerGaps[i] = TerrainVoronoiSdf.DistanceGap(map,
                    map.Sites[i].Center, i, out _, profile.VoronoiRegionSoftness);

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

                    int region = SampleVoronoiRegion(map, settings, profile, point,
                        nearestSite, centerGaps[nearestSite]);
                    map.SetRegion(x, z, region, nearestSite);
                }
            }
        }

        private static int SampleVoronoiRegion(TerrainMapData map,
            TerrainGenerationSettings settings, TerrainVoronoiSettings profile,
            Vector2 point, int owner,
            float centerGap)
        {
            TerrainSite site = map.Sites[owner];
            if (map.Sites.Count == 1)
                return site.Region;
            float gap = TerrainVoronoiSdf.DistanceGap(map, point, owner, out _,
                profile.VoronoiRegionSoftness);
            float width = profile.VoronoiRegionWidth *
                Mathf.Min(settings.Width, settings.Height) / 300f;
            float threshold = centerGap - width;
            return gap > threshold
                ? site.Region : profile.BaseRegion;
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
                int region = map.GetRegion(startX, startZ);
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
            int region, int groupId, int[] queue, ref int tail)
        {
            if (x < 0 || z < 0 || x >= map.WidthCells || z >= map.HeightCells ||
                map.GetRegionGroupId(x, z) >= 0 || map.GetRegion(x, z) != region)
                return;
            map.SetRegionGroup(x, z, groupId);
            queue[tail++] = x + z * map.WidthCells;
        }

    }
}
