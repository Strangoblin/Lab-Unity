// ════════════════════════════════════════════════════════════
//  TerrainStuffDistributor — 根据区域、道路和占地约束生成物体放置计划
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Mine.Terrain
{
    public readonly struct TerrainStuffRule
    {
        public readonly int Type;
        public readonly int Region;
        public readonly int Count;
        public readonly float Radius;

        public TerrainStuffRule(int type, int region, int count, float radius)
        {
            Type = type;
            Region = region;
            Count = count;
            Radius = radius;
        }
    }

    /// <summary>
    /// 地图方案提供的稀疏物体配方；规则顺序决定共享占地约束的放置顺序。
    /// </summary>
    public sealed class TerrainStuffSettings
    {
        private readonly TerrainStuffRule[] _rules;

        public IReadOnlyList<TerrainStuffRule> Rules => _rules;

        public TerrainStuffSettings(TerrainStuffRule[] rules)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));
            _rules = (TerrainStuffRule[])rules.Clone();
        }

        public void Validate()
        {
            for (int i = 0; i < _rules.Length; i++)
            {
                TerrainStuffRule rule = _rules[i];
                if (rule.Type < 0 || rule.Region < 0 || rule.Count < 0 ||
                    float.IsNaN(rule.Radius) || float.IsInfinity(rule.Radius) || rule.Radius < 0f)
                    throw new ArgumentException($"Invalid terrain stuff rule at index {i}.");
            }
        }
    }

    public readonly struct TerrainStuffPlacement
    {
        public readonly int Type;
        public readonly Vector3 Position;
        public readonly float Yaw;
        public readonly float Radius;

        public TerrainStuffPlacement(int type, Vector3 position, float yaw, float radius)
        {
            Type = type;
            Position = position;
            Yaw = yaw;
            Radius = radius;
        }
    }

    public sealed class TerrainStuffPlan
    {
        private readonly List<TerrainStuffPlacement> _placements = new List<TerrainStuffPlacement>();

        public IReadOnlyList<TerrainStuffPlacement> Placements => _placements;

        public TerrainStuffPlan() { }

        public TerrainStuffPlan(IEnumerable<TerrainStuffPlacement> placements)
        {
            if (placements == null)
                throw new ArgumentNullException(nameof(placements));
            _placements.AddRange(placements);
        }

        internal void Add(TerrainStuffPlacement placement) => _placements.Add(placement);

        public int Count(int type)
        {
            int count = 0;
            for (int i = 0; i < _placements.Count; i++)
                if (_placements[i].Type == type)
                    count++;
            return count;
        }
    }

    /// <summary>
    /// 输出建筑与树木等完整个体的放置结果；密集地被由 TerrainPlantDistributor 负责。
    /// </summary>
    public static class TerrainStuffDistributor
    {
        public static TerrainStuffPlan Distribute(TerrainMapData map, TerrainStuffSettings settings)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            settings.Validate();

            TerrainStuffPlan plan = new TerrainStuffPlan();
            List<TerrainStuffPlacement> occupied = new List<TerrainStuffPlacement>();
            System.Random random = new System.Random(unchecked(map.Seed * 1319 ^ 0x512D));

            for (int i = 0; i < settings.Rules.Count; i++)
            {
                TerrainStuffRule rule = settings.Rules[i];
                PlaceMany(map, plan, occupied, random, rule.Type, rule.Region, rule.Count, rule.Radius);
            }
            return plan;
        }

        // ════════════════════════════════════════════════════════════
        //  候选采样 — 固定种子、有限尝试，内容密度与基础地图解耦
        // ════════════════════════════════════════════════════════════

        private static void PlaceMany(
            TerrainMapData map,
            TerrainStuffPlan plan,
            List<TerrainStuffPlacement> occupied,
            System.Random random,
            int type,
            int region,
            int targetCount,
            float radius)
        {
            int placed = 0;
            int maxAttempts = Mathf.Max(500, targetCount * 150);

            for (int attempt = 0; attempt < maxAttempts && placed < targetCount; attempt++)
            {
                int x = random.Next(map.WidthCells);
                int z = random.Next(map.HeightCells);
                if (!IsValidFootprint(map, x, z, radius, region))
                    continue;

                Vector2 point = map.CellCenter(x, z);
                bool blocked = false;
                for (int i = 0; i < occupied.Count; i++)
                {
                    TerrainStuffPlacement other = occupied[i];
                    Vector2 occupiedPoint = new Vector2(other.Position.x, other.Position.z);
                    if (Vector2.Distance(point, occupiedPoint) < radius + other.Radius + 1f)
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked)
                    continue;

                TerrainStuffPlacement placement = new TerrainStuffPlacement(
                    type,
                    new Vector3(point.x, map.GetHeight(x, z), point.y),
                    (float)random.NextDouble() * 360f,
                    radius);
                plan.Add(placement);
                occupied.Add(placement);
                placed++;
            }
        }

        private static bool IsValidFootprint(
            TerrainMapData map,
            int centerX,
            int centerZ,
            float radius,
            int region)
        {
            int cellRadius = Mathf.CeilToInt(radius / map.CellSize);
            for (int dz = -cellRadius; dz <= cellRadius; dz++)
            {
                for (int dx = -cellRadius; dx <= cellRadius; dx++)
                {
                    if ((dx * dx + dz * dz) * map.CellSize * map.CellSize > radius * radius)
                        continue;

                    int x = centerX + dx;
                    int z = centerZ + dz;
                    if (x < 0 || x >= map.WidthCells || z < 0 || z >= map.HeightCells)
                        return false;
                    if (map.GetRegion(x, z) != region || map.GetRoadCoverage(x, z) > 0.08f)
                        return false;
                }
            }
            return true;
        }
    }
}
