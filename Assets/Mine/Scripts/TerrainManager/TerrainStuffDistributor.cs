// ════════════════════════════════════════════════════════════
//  TerrainStuffDistributor — 根据区域、道路和占地约束生成物体放置计划
// ════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mine.Terrain
{
    public enum TerrainStuffType
    {
        House,
        Windmill,
        Tree,
        OrchardTree
    }

    public readonly struct TerrainStuffPlacement
    {
        public readonly TerrainStuffType Type;
        public readonly Vector3 Position;
        public readonly float Yaw;
        public readonly float Radius;

        public TerrainStuffPlacement(TerrainStuffType type, Vector3 position, float yaw, float radius)
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

        internal void Add(TerrainStuffPlacement placement) => _placements.Add(placement);

        public int Count(TerrainStuffType type)
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
        public static TerrainStuffPlan Distribute(TerrainMapData map, TerrainGenerationSettings settings)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            TerrainStuffPlan plan = new TerrainStuffPlan();
            List<TerrainStuffPlacement> occupied = new List<TerrainStuffPlacement>();
            System.Random random = new System.Random(unchecked(map.Seed * 1319 ^ 0x512D));

            PlaceMany(map, plan, occupied, random, TerrainStuffType.Windmill, TerrainRegion.WheatField, 1, 5f);
            PlaceMany(map, plan, occupied, random, TerrainStuffType.House, TerrainRegion.Village, settings.HouseCount, 4f);
            PlaceMany(map, plan, occupied, random, TerrainStuffType.Tree, TerrainRegion.Forest,
                settings.TreeCount, 1.5f);
            PlaceMany(map, plan, occupied, random, TerrainStuffType.OrchardTree, TerrainRegion.Orchard,
                settings.TreeCount == 0 ? 0 : Mathf.Max(15, settings.TreeCount / 2), 1.5f);
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
            TerrainStuffType type,
            TerrainRegion region,
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
            TerrainRegion region)
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
