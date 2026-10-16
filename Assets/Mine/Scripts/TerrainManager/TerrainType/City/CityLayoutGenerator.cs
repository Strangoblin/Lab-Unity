// ════════════════════════════════════════════════════════════
//  CityLayoutGenerator — 按园区配额及四邻接扩张分配街区用途
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Mine.Terrain
{
    /// <summary>
    /// 道路先行，园区轻聚类，再按权重分配子类型；不执行 WFC 或坐标扭曲。
    /// </summary>
    public static class CityLayoutGenerator
    {
        public static CityLayout Generate(TerrainGenerationSettings mapSettings, CitySettings settings)
        {
            if (mapSettings == null)
                throw new ArgumentNullException(nameof(mapSettings));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            mapSettings.Validate();
            settings.Validate();
            return Build(TerrainGridRoadGenerator.Generate(mapSettings, settings.Grid), mapSettings.Seed, settings);
        }

        public static CityLayout CreateForMap(TerrainMapData map, CitySettings settings)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            return Build(TerrainGridRoadGenerator.Generate(map, settings.Grid), map.Seed, settings);
        }

        // ════════════════════════════════════════════════════════════
        //  用途编排 — 大类配额保底后分配园区，再在园区内分配子类型
        // ════════════════════════════════════════════════════════════

        private static CityLayout Build(TerrainGridRoadLayout grid, int seed, CitySettings settings)
        {
            var random = new System.Random(unchecked(seed ^ 0x4D35A19B));
            var zones = EnabledZones(settings);
            var minimums = new int[zones.Count];
            var weights = new float[zones.Count];
            int enabledSubtypes = 0;
            for (int i = 0; i < zones.Count; i++)
            {
                weights[i] = zones[i].Weight;
                minimums[i] = EnabledSubtypes(settings, zones[i].Zone).Count;
                enabledSubtypes += minimums[i];
            }
            if (grid.Blocks.Count < enabledSubtypes)
            {
                for (int i = 0; i < minimums.Length; i++)
                    minimums[i] = grid.Blocks.Count >= zones.Count ? 1 : 0;
            }
            int[] quotas = AllocateQuota(grid.Blocks.Count, weights, minimums, random);
            int[] assignment = AssignZones(grid, zones, quotas, random);
            int[] regions = AssignSubtypes(assignment, zones, settings, random);
            var blocks = new CityBlock[grid.Blocks.Count];
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = new CityBlock(i, regions[i], grid.Blocks[i]);
            return new CityLayout(grid, blocks);
        }

        private static List<CityZoneRule> EnabledZones(CitySettings settings)
        {
            var result = new List<CityZoneRule>();
            foreach (CityZoneRule zone in settings.ZoneRules)
            {
                if (zone.Weight > 0f)
                    result.Add(zone);
            }
            return result;
        }

        private static List<CitySubtypeRule> EnabledSubtypes(CitySettings settings, CityZone zone)
        {
            var result = new List<CitySubtypeRule>();
            foreach (CitySubtypeRule subtype in settings.SubtypeRules)
            {
                if (subtype.Weight > 0f && CityContent.ZoneFor(subtype.Region) == zone)
                    result.Add(subtype);
            }
            return result;
        }

        private static int[] AssignZones(TerrainGridRoadLayout grid, List<CityZoneRule> zones,
            int[] quotas, System.Random random)
        {
            var assignment = new int[grid.Blocks.Count];
            var remaining = (int[])quotas.Clone();
            for (int i = 0; i < assignment.Length; i++)
                assignment[i] = -1;
            int unassigned = assignment.Length;
            for (int zone = 0; zone < zones.Count; zone++)
            {
                if (remaining[zone] == 0)
                    continue;
                int block = ChooseBlock(grid, assignment, zone, zones[zone].Zone, random);
                assignment[block] = zone;
                remaining[zone]--;
                unassigned--;
            }
            while (unassigned > 0)
            {
                int zone = ChooseRemaining(remaining, unassigned, random);
                int block = ChooseBlock(grid, assignment, zone, zones[zone].Zone, random);
                assignment[block] = zone;
                remaining[zone]--;
                unassigned--;
            }
            return assignment;
        }

        private static int ChooseBlock(TerrainGridRoadLayout grid, int[] assignment, int zoneIndex,
            CityZone zone, System.Random random)
        {
            int selected = -1;
            double best = double.NegativeInfinity;
            for (int block = 0; block < assignment.Length; block++)
            {
                if (assignment[block] >= 0)
                    continue;
                int x = block % grid.Columns;
                int z = block / grid.Columns;
                int matching = CountMatchingNeighbours(assignment, x, z, grid.Columns, grid.Rows, zoneIndex);
                bool edge = x == 0 || z == 0 || x == grid.Columns - 1 || z == grid.Rows - 1;
                double score = matching * 2d + random.NextDouble();
                if (zone == CityZone.Industrial && edge)
                    score += 0.5d;
                if (score > best)
                {
                    best = score;
                    selected = block;
                }
            }
            return selected;
        }

        private static int CountMatchingNeighbours(int[] assignment, int x, int z,
            int columns, int rows, int zone)
        {
            int count = 0;
            if (x > 0 && assignment[x - 1 + z * columns] == zone) count++;
            if (x + 1 < columns && assignment[x + 1 + z * columns] == zone) count++;
            if (z > 0 && assignment[x + (z - 1) * columns] == zone) count++;
            if (z + 1 < rows && assignment[x + (z + 1) * columns] == zone) count++;
            return count;
        }

        private static int ChooseRemaining(int[] remaining, int total, System.Random random)
        {
            int draw = random.Next(total);
            for (int i = 0; i < remaining.Length; i++)
            {
                draw -= remaining[i];
                if (draw < 0)
                    return i;
            }
            throw new InvalidOperationException("City quota allocation is inconsistent.");
        }

        private static int[] AssignSubtypes(int[] assignment, List<CityZoneRule> zones,
            CitySettings settings, System.Random random)
        {
            var regions = new int[assignment.Length];
            for (int zone = 0; zone < zones.Count; zone++)
            {
                var blockIds = new List<int>();
                for (int i = 0; i < assignment.Length; i++)
                {
                    if (assignment[i] == zone)
                        blockIds.Add(i);
                }
                var subtypes = EnabledSubtypes(settings, zones[zone].Zone);
                var weights = new float[subtypes.Count];
                var minimums = new int[subtypes.Count];
                for (int i = 0; i < subtypes.Count; i++)
                {
                    weights[i] = subtypes[i].Weight;
                    minimums[i] = blockIds.Count >= subtypes.Count ? 1 : 0;
                }
                int[] quotas = AllocateQuota(blockIds.Count, weights, minimums, random);
                Shuffle(blockIds, random);
                int cursor = 0;
                for (int i = 0; i < subtypes.Count; i++)
                {
                    for (int n = 0; n < quotas[i]; n++)
                        regions[blockIds[cursor++]] = subtypes[i].Region;
                }
            }
            return regions;
        }

        // ════════════════════════════════════════════════════════════
        //  数量分配 — 最大余数保留比例，必要时从有余量类别调入保底数量
        // ════════════════════════════════════════════════════════════

        private static int[] AllocateQuota(int total, float[] weights, int[] minimums, System.Random random)
        {
            var quotas = new int[weights.Length];
            var remainders = new double[weights.Length];
            double weightSum = 0d;
            foreach (float weight in weights)
                weightSum += weight;
            int assigned = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                double exact = total * ((double)weights[i] / weightSum);
                quotas[i] = (int)Math.Floor(exact);
                remainders[i] = exact - quotas[i] + random.NextDouble() * 0.000001d;
                assigned += quotas[i];
            }
            while (assigned < total)
            {
                int highest = 0;
                for (int i = 1; i < remainders.Length; i++)
                {
                    if (remainders[i] > remainders[highest])
                        highest = i;
                }
                quotas[highest]++;
                remainders[highest] = -1d;
                assigned++;
            }
            for (int i = 0; i < quotas.Length; i++)
            {
                while (quotas[i] < minimums[i])
                {
                    int donor = -1;
                    int surplus = 0;
                    for (int j = 0; j < quotas.Length; j++)
                    {
                        int available = quotas[j] - minimums[j];
                        if (available > surplus)
                        {
                            surplus = available;
                            donor = j;
                        }
                    }
                    if (donor < 0)
                        throw new InvalidOperationException("City minimum subtype quotas exceed available blocks.");
                    quotas[donor]--;
                    quotas[i]++;
                }
            }
            return quotas;
        }

        private static void Shuffle(List<int> values, System.Random random)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int value = values[i];
                values[i] = values[j];
                values[j] = value;
            }
        }
    }
}
