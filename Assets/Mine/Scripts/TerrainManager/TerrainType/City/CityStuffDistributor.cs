// ════════════════════════════════════════════════════════════
//  CityStuffDistributor — 沿街矩形地块放置设施并保留入口走廊
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Mine.Terrain
{
    /// <summary>
    /// 有限候选放置报告；不足的设施或树木被跳过，不缩小原始设施尺寸。
    /// </summary>
    public sealed class CityStuffResult
    {
        public TerrainStuffPlan Plan { get; }
        public int RequestedFacilities { get; }
        public int PlacedFacilities { get; }
        public int SkippedFacilities => RequestedFacilities - PlacedFacilities;
        public int RequestedTrees { get; }
        public int PlacedTrees { get; }
        public int SkippedTrees => RequestedTrees - PlacedTrees;

        internal CityStuffResult(TerrainStuffPlan plan, int requestedFacilities,
            int placedFacilities, int requestedTrees, int placedTrees)
        {
            Plan = plan;
            RequestedFacilities = requestedFacilities;
            PlacedFacilities = placedFacilities;
            RequestedTrees = requestedTrees;
            PlacedTrees = placedTrees;
        }

        public override string ToString() =>
            $"Facilities {PlacedFacilities}/{RequestedFacilities} (skipped {SkippedFacilities}), " +
            $"trees {PlacedTrees}/{RequestedTrees} (skipped {SkippedTrees}).";
    }

    /// <summary>
    /// 城市专用设施布局；只输出通用放置计划，不创建对象或缓存共享方案状态。
    /// </summary>
    public static class CityStuffDistributor
    {
        private const float FacilityGap = 1.5f;
        private const float EntryWidth = 2.5f;
        private const float FootprintTolerance = 0.001f;
        private const int TreeAttemptsPerTarget = 30;
        private const int TreeMinimumAttempts = 64;

        public static TerrainStuffPlan Distribute(TerrainMapData map, CitySettings settings) =>
            DistributeDetailed(map, settings).Plan;

        public static CityStuffResult DistributeDetailed(TerrainMapData map, CitySettings settings)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            settings.Validate();

            CityLayout layout = CityLayoutGenerator.CreateForMap(map, settings);
            TerrainStuffPlan plan = new TerrainStuffPlan();
            System.Random random = new System.Random(unchecked(map.Seed * 1319 ^ 0xC175));
            List<Rect> occupied = new List<Rect>();
            List<CityBlock> blocks = OrderedBlocks(layout);
            int requestedFacilities = 0;
            int placedFacilities = 0;
            int requestedTrees = 0;
            int placedTrees = 0;

            for (int i = 0; i < blocks.Count; i++)
            {
                CityBlock block = blocks[i];
                CitySubtypeRule rule = settings.RuleFor(block.Region);
                requestedFacilities += rule.MaxFacilitiesPerBlock;
                placedFacilities += PlaceFacilities(map, block, settings.BuildingSetback,
                    rule.MaxFacilitiesPerBlock, random, occupied, plan);
            }
            for (int i = 0; i < blocks.Count; i++)
            {
                CityBlock block = blocks[i];
                int target = settings.RuleFor(block.Region).TreesPerBlock;
                requestedTrees += target;
                placedTrees += PlaceTrees(map, block, settings.BuildingSetback,
                    target, random, occupied, plan);
            }
            return new CityStuffResult(plan, requestedFacilities, placedFacilities,
                requestedTrees, placedTrees);
        }

        // ════════════════════════════════════════════════════════════
        //  沿街地块 — 按完整占地生成边缘槽位，并预留通向道路的入口
        // ════════════════════════════════════════════════════════════

        private static int PlaceFacilities(TerrainMapData map, CityBlock block, float setback,
            int target, System.Random random, List<Rect> occupied, TerrainStuffPlan plan)
        {
            if (target == 0)
                return 0;
            int type = CityContent.MainStuffFor(block.Region);
            if (type < 0)
                return 0;
            Vector3 size = CityTerrainType.VisualForStuff(type).Scale;
            List<ParcelCandidate> candidates = Candidates(block.Bounds, setback, size);
            Shuffle(candidates, random);
            float footprintRadius = new Vector2(size.x, size.z).magnitude * 0.5f;
            int placed = 0;
            for (int i = 0; i < candidates.Count && placed < target; i++)
            {
                ParcelCandidate candidate = candidates[i];
                Vector2 center = candidate.Footprint.center;
                Rect reserved = Expand(candidate.Footprint, FacilityGap * 0.5f);
                if (!CircleInsideMap(map, center, footprintRadius) ||
                    !IsClearFootprint(map, candidate.Footprint, block.Region) ||
                    IntersectsAny(reserved, occupied) || IntersectsAny(candidate.Entrance, occupied))
                    continue;
                float plantClearance = Mathf.Max(footprintRadius,
                    FarthestCornerDistance(center, candidate.Entrance));
                plan.Add(new TerrainStuffPlacement(type, Position(map, center), candidate.Yaw,
                    plantClearance));
                occupied.Add(reserved);
                occupied.Add(candidate.Entrance);
                placed++;
            }
            return placed;
        }

        private static List<ParcelCandidate> Candidates(Rect block, float setback, Vector3 size)
        {
            List<ParcelCandidate> candidates = new List<ParcelCandidate>();
            Rect buildable = Inset(block, setback);
            if (buildable.width <= 0f || buildable.height <= 0f)
                return candidates;
            for (int side = 0; side < 4; side++)
                AddSideCandidates(candidates, block, buildable, size, side);
            return candidates;
        }

        private static void AddSideCandidates(List<ParcelCandidate> result, Rect block,
            Rect buildable, Vector3 size, int side)
        {
            bool horizontal = side < 2;
            float front = size.x;
            float depth = size.z;
            float edgeLength = horizontal ? buildable.width : buildable.height;
            float depthAvailable = horizontal ? buildable.height : buildable.width;
            if (edgeLength < front || depthAvailable < depth)
                return;
            int slots = Mathf.FloorToInt((edgeLength + FacilityGap) / (front + FacilityGap));
            float start = (horizontal ? buildable.xMin : buildable.yMin) +
                (edgeLength - (slots * front + (slots - 1) * FacilityGap)) * 0.5f;
            for (int slot = 0; slot < slots; slot++)
            {
                float along = start + slot * (front + FacilityGap);
                Rect footprint;
                Rect entrance;
                float entryWidth = Mathf.Min(EntryWidth, front);
                float entryAlong = along + (front - entryWidth) * 0.5f;
                float yaw;
                if (side == 0)
                {
                    footprint = new Rect(along, buildable.yMin, front, depth);
                    entrance = new Rect(entryAlong, block.yMin, entryWidth, buildable.yMin - block.yMin);
                    yaw = 180f;
                }
                else if (side == 1)
                {
                    footprint = new Rect(along, buildable.yMax - depth, front, depth);
                    entrance = new Rect(entryAlong, buildable.yMax, entryWidth, block.yMax - buildable.yMax);
                    yaw = 0f;
                }
                else if (side == 2)
                {
                    footprint = new Rect(buildable.xMin, along, depth, front);
                    entrance = new Rect(block.xMin, entryAlong, buildable.xMin - block.xMin, entryWidth);
                    yaw = 270f;
                }
                else
                {
                    footprint = new Rect(buildable.xMax - depth, along, depth, front);
                    entrance = new Rect(buildable.xMax, entryAlong, block.xMax - buildable.xMax, entryWidth);
                    yaw = 90f;
                }
                result.Add(new ParcelCandidate(footprint, entrance, yaw));
            }
        }

        // ════════════════════════════════════════════════════════════
        //  树木填空 — 主设施之后有限采样，尊重完整占地与入口预留
        // ════════════════════════════════════════════════════════════

        private static int PlaceTrees(TerrainMapData map, CityBlock block, float setback,
            int target, System.Random random, List<Rect> occupied, TerrainStuffPlan plan)
        {
            if (target == 0)
                return 0;
            Vector3 size = CityTerrainType.VisualForStuff(CityStuffType.Tree).Scale;
            Rect available = Inset(block.Bounds, setback + Mathf.Max(size.x, size.z) * 0.5f);
            if (available.width <= 0f || available.height <= 0f)
                return 0;
            float radius = new Vector2(size.x, size.z).magnitude * 0.5f;
            int attempts = Mathf.Max(TreeMinimumAttempts, target * TreeAttemptsPerTarget);
            int placed = 0;
            for (int attempt = 0; attempt < attempts && placed < target; attempt++)
            {
                Vector2 center = new Vector2(
                    Mathf.Lerp(available.xMin, available.xMax, (float)random.NextDouble()),
                    Mathf.Lerp(available.yMin, available.yMax, (float)random.NextDouble()));
                Rect footprint = new Rect(center.x - size.x * 0.5f, center.y - size.z * 0.5f,
                    size.x, size.z);
                Rect reserved = Expand(footprint, FacilityGap * 0.5f);
                if (!CircleInsideMap(map, center, radius) ||
                    !IsClearFootprint(map, footprint, block.Region) || IntersectsAny(reserved, occupied))
                    continue;
                plan.Add(new TerrainStuffPlacement(CityStuffType.Tree, Position(map, center), 0f, radius));
                occupied.Add(reserved);
                placed++;
            }
            return placed;
        }

        // ════════════════════════════════════════════════════════════
        //  约束与排序 — 实际矩形检查占地，保守避让圆覆盖设施与完整入口
        // ════════════════════════════════════════════════════════════

        private static bool IsClearFootprint(TerrainMapData map, Rect footprint, int region)
        {
            int minX = Mathf.FloorToInt((footprint.xMin - map.Origin.x + FootprintTolerance) / map.CellSize);
            int maxX = Mathf.FloorToInt((footprint.xMax - map.Origin.x - FootprintTolerance) / map.CellSize);
            int minZ = Mathf.FloorToInt((footprint.yMin - map.Origin.y + FootprintTolerance) / map.CellSize);
            int maxZ = Mathf.FloorToInt((footprint.yMax - map.Origin.y - FootprintTolerance) / map.CellSize);
            if (minX < 0 || minZ < 0 || maxX >= map.WidthCells || maxZ >= map.HeightCells)
                return false;
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                    if (map.GetRegion(x, z) != region || map.GetRoadCoverage(x, z) > 0.08f)
                        return false;
            return true;
        }

        private static bool CircleInsideMap(TerrainMapData map, Vector2 center, float radius) =>
            center.x - radius >= map.Origin.x && center.y - radius >= map.Origin.y &&
            center.x + radius <= map.Origin.x + map.WidthCells * map.CellSize &&
            center.y + radius <= map.Origin.y + map.HeightCells * map.CellSize;

        private static float FarthestCornerDistance(Vector2 center, Rect rectangle)
        {
            float x = Mathf.Max(Mathf.Abs(rectangle.xMin - center.x),
                Mathf.Abs(rectangle.xMax - center.x));
            float z = Mathf.Max(Mathf.Abs(rectangle.yMin - center.y),
                Mathf.Abs(rectangle.yMax - center.y));
            return Mathf.Sqrt(x * x + z * z);
        }

        private static Vector3 Position(TerrainMapData map, Vector2 center)
        {
            int x = Mathf.FloorToInt((center.x - map.Origin.x) / map.CellSize);
            int z = Mathf.FloorToInt((center.y - map.Origin.y) / map.CellSize);
            return new Vector3(center.x, map.GetHeight(x, z), center.y);
        }

        private static bool IntersectsAny(Rect candidate, List<Rect> occupied)
        {
            for (int i = 0; i < occupied.Count; i++)
                if (candidate.Overlaps(occupied[i]))
                    return true;
            return false;
        }

        private static Rect Inset(Rect rectangle, float amount) =>
            new Rect(rectangle.xMin + amount, rectangle.yMin + amount,
                rectangle.width - 2f * amount, rectangle.height - 2f * amount);

        private static Rect Expand(Rect rectangle, float amount) => Inset(rectangle, -amount);

        private static List<CityBlock> OrderedBlocks(CityLayout layout)
        {
            List<CityBlock> blocks = new List<CityBlock>(layout.Blocks);
            blocks.Sort((first, second) =>
            {
                int comparison = MainFootprintArea(second.Region).CompareTo(MainFootprintArea(first.Region));
                return comparison != 0 ? comparison : first.Id.CompareTo(second.Id);
            });
            return blocks;
        }

        private static float MainFootprintArea(int region)
        {
            int type = CityContent.MainStuffFor(region);
            if (type < 0)
                return 0f;
            Vector3 size = CityTerrainType.VisualForStuff(type).Scale;
            return size.x * size.z;
        }

        private static void Shuffle(List<ParcelCandidate> candidates, System.Random random)
        {
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                ParcelCandidate swap = candidates[i];
                candidates[i] = candidates[other];
                candidates[other] = swap;
            }
        }

        private readonly struct ParcelCandidate
        {
            public readonly Rect Footprint;
            public readonly Rect Entrance;
            public readonly float Yaw;

            public ParcelCandidate(Rect footprint, Rect entrance, float yaw)
            {
                Footprint = footprint;
                Entrance = entrance;
                Yaw = yaw;
            }
        }
    }
}
