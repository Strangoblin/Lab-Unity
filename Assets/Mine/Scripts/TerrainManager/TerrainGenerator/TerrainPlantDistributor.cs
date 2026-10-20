// ════════════════════════════════════════════════════════════
//  TerrainPlantDistributor — 按地形特征与密度生成确定性的植被放置计划
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Mine.Terrain
{
    [Serializable]
    public sealed class TerrainPlantRule
    {
        [SerializeField, Min(0)] private int _region;
        [SerializeField, Min(0)] private int _plant;
        [SerializeField, Min(0f)] private float _densityPerSquareUnit;
        [SerializeField, Min(0f)] private float _footprintRadius;

        public int Region => _region;
        public int Plant => _plant;
        public float DensityPerSquareUnit => _densityPerSquareUnit;
        public float FootprintRadius => _footprintRadius;

        public TerrainPlantRule(int region, int plant, float density,
            float footprintRadius)
        {
            _region = region;
            _plant = plant;
            _densityPerSquareUnit = density;
            _footprintRadius = footprintRadius;
        }
    }

    [Serializable]
    public sealed class TerrainPlantSettings
    {
        [SerializeField, Min(1)] private int _maxPlants = 100000;
        [SerializeField] private TerrainPlantRule[] _rules = Array.Empty<TerrainPlantRule>();

        public int MaxPlants => _maxPlants;
        public TerrainPlantRule[] Rules => _rules;

        public TerrainPlantSettings() { }

        public TerrainPlantSettings(TerrainPlantRule[] rules, int maxPlants = 100000)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));
            _rules = (TerrainPlantRule[])rules.Clone();
            _maxPlants = maxPlants;
        }

        public void Validate()
        {
            if (_maxPlants < 1 || _rules == null)
                throw new ArgumentException("Plant limit and rules must be valid.");
            for (int i = 0; i < _rules.Length; i++)
            {
                TerrainPlantRule rule = _rules[i];
                if (rule == null || rule.Region < 0 || rule.Plant < 0 ||
                    !IsFiniteNonNegative(rule.DensityPerSquareUnit) ||
                    !IsFiniteNonNegative(rule.FootprintRadius))
                    throw new ArgumentException($"Invalid terrain plant rule at index {i}.");
            }
        }

        private static bool IsFiniteNonNegative(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    }

    public readonly struct TerrainPlantPlacement
    {
        public readonly int Type;
        public readonly int Region;
        public readonly Vector3 Position;
        public readonly float Yaw;
        public readonly float Scale;
        public readonly float Radius;

        public TerrainPlantPlacement(int type, int region, Vector3 position,
            float yaw, float scale, float radius)
        {
            Type = type;
            Region = region;
            Position = position;
            Yaw = yaw;
            Scale = scale;
            Radius = radius;
        }
    }

    public sealed class TerrainPlantPlan
    {
        private readonly List<TerrainPlantPlacement> _placements = new List<TerrainPlantPlacement>();

        public IReadOnlyList<TerrainPlantPlacement> Placements => _placements;

        public TerrainPlantPlan() { }

        public TerrainPlantPlan(IEnumerable<TerrainPlantPlacement> placements)
        {
            if (placements == null)
                throw new ArgumentNullException(nameof(placements));
            _placements.AddRange(placements);
        }

        internal void Add(TerrainPlantPlacement placement) => _placements.Add(placement);

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
    /// 只读取地图、建筑占地和植被配方；输出可供任意植被表现层消费的放置数据。
    /// </summary>
    public static class TerrainPlantDistributor
    {
        public static TerrainPlantPlan Distribute(TerrainMapData map, TerrainStuffPlan structures,
            TerrainPlantSettings settings)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (structures == null)
                throw new ArgumentNullException(nameof(structures));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            settings.Validate();

            TerrainPlantPlan plan = new TerrainPlantPlan();
            TerrainPlantRule[] rules = settings.Rules;
            for (int i = 0; i < rules.Length; i++)
                DistributeRule(map, structures, settings, rules[i], i, plan);
            return plan;
        }

        // ════════════════════════════════════════════════════════════
        //  密度采样 — 单元面积换算为候选数，规则使用独立随机序列
        // ════════════════════════════════════════════════════════════

        private static void DistributeRule(TerrainMapData map, TerrainStuffPlan structures,
            TerrainPlantSettings settings, TerrainPlantRule rule, int ruleIndex,
            TerrainPlantPlan plan)
        {
            System.Random random = new System.Random(unchecked(map.Seed * 7919 ^ (ruleIndex + 1) * 104729));
            float expected = rule.DensityPerSquareUnit * map.CellSize * map.CellSize;
            int whole = Mathf.FloorToInt(expected);
            float fractional = expected - whole;
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    if (map.GetRegion(x, z) != rule.Region)
                        continue;
                    int trials = whole + (random.NextDouble() < fractional ? 1 : 0);
                    for (int trial = 0; trial < trials; trial++)
                    {
                        Vector2 point = map.Origin + new Vector2(
                            (x + (float)random.NextDouble()) * map.CellSize,
                            (z + (float)random.NextDouble()) * map.CellSize);
                        if (!IsClear(map, structures, rule, point))
                            continue;
                        if (plan.Placements.Count >= settings.MaxPlants)
                            throw new InvalidOperationException("Plant limit reached; lower density or raise MaxPlants.");

                        TerrainPlantPlacement placement = new TerrainPlantPlacement(
                            rule.Plant, rule.Region,
                            new Vector3(point.x, SampleSurfaceHeight(map, x, z, point), point.y),
                            (float)random.NextDouble() * 360f,
                            Mathf.Lerp(0.8f, 1.2f, (float)random.NextDouble()),
                            rule.FootprintRadius);
                        plan.Add(placement);
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  地形约束 — 区域、道路和建筑占地保持独立判定
        // ════════════════════════════════════════════════════════════

        private static bool IsClear(TerrainMapData map, TerrainStuffPlan structures,
            TerrainPlantRule rule, Vector2 point)
        {
            float radius = rule.FootprintRadius;
            int minX = Mathf.FloorToInt((point.x - radius - map.Origin.x) / map.CellSize);
            int maxX = Mathf.FloorToInt((point.x + radius - map.Origin.x) / map.CellSize);
            int minZ = Mathf.FloorToInt((point.y - radius - map.Origin.y) / map.CellSize);
            int maxZ = Mathf.FloorToInt((point.y + radius - map.Origin.y) / map.CellSize);
            if (minX < 0 || minZ < 0 || maxX >= map.WidthCells || maxZ >= map.HeightCells)
                return false;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float left = map.Origin.x + x * map.CellSize;
                    float bottom = map.Origin.y + z * map.CellSize;
                    float distanceX = point.x - Mathf.Clamp(point.x, left, left + map.CellSize);
                    float distanceZ = point.y - Mathf.Clamp(point.y, bottom, bottom + map.CellSize);
                    if (distanceX * distanceX + distanceZ * distanceZ > radius * radius)
                        continue;
                    if (map.GetRegion(x, z) != rule.Region || map.GetRoadCoverage(x, z) > 0.08f)
                        return false;
                }
            }

            for (int i = 0; i < structures.Placements.Count; i++)
            {
                TerrainStuffPlacement structure = structures.Placements[i];
                Vector2 center = new Vector2(structure.Position.x, structure.Position.z);
                float clearance = radius + structure.Radius + 0.25f;
                if ((point - center).sqrMagnitude < clearance * clearance)
                    return false;
            }
            return true;
        }

        private static float SampleSurfaceHeight(TerrainMapData map, int x, int z, Vector2 point)
        {
            float u = (point.x - map.Origin.x) / map.CellSize - x;
            float v = (point.y - map.Origin.y) / map.CellSize - z;
            float h00 = map.SampleHeightAtVertex(x, z);
            float h10 = map.SampleHeightAtVertex(x + 1, z);
            float h01 = map.SampleHeightAtVertex(x, z + 1);
            float h11 = map.SampleHeightAtVertex(x + 1, z + 1);
            return u + v <= 1f
                ? h00 + (h10 - h00) * u + (h01 - h00) * v
                : h11 + (h01 - h11) * (1f - u) + (h10 - h11) * (1f - v);
        }
    }
}
