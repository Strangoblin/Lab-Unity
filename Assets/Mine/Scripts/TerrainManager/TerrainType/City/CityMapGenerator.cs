// ════════════════════════════════════════════════════════════
//  CityMapGenerator — 将城市街区用途和道路走廊写入通用地图契约
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 建立零高度的城市基础层、街区子类型层及车行道/人行道覆盖层。
    /// </summary>
    public static class CityMapGenerator
    {
        public static TerrainMapData Generate(TerrainGenerationSettings mapSettings, CitySettings settings)
        {
            CityLayout layout = CityLayoutGenerator.Generate(mapSettings, settings);
            var builder = new TerrainMapBuilder(mapSettings);
            int[] owners = AddBlockRecords(builder, layout);
            WriteCells(builder, layout, owners);
            return builder.Map;
        }

        // ════════════════════════════════════════════════════════════
        //  街区归属 — 先统计实际逻辑格，再为有样本的街区建立密集地点/分组
        // ════════════════════════════════════════════════════════════

        private static int[] AddBlockRecords(TerrainMapBuilder builder, CityLayout layout)
        {
            TerrainMapData map = builder.Map;
            var counts = new int[layout.Blocks.Count];
            var centers = new Vector2[layout.Blocks.Count];
            var owners = new int[layout.Blocks.Count];
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    Vector2 center = map.CellCenter(x, z);
                    int block = layout.Grid.FindBlockIndex(center);
                    if (block < 0)
                        continue;
                    counts[block]++;
                    centers[block] += center;
                }
            }
            for (int i = 0; i < layout.Blocks.Count; i++)
            {
                owners[i] = -1;
                if (counts[i] == 0)
                    continue;
                CityBlock block = layout.Blocks[i];
                int id = map.Sites.Count;
                float radius = Mathf.Min(block.Bounds.width, block.Bounds.height) * 0.5f;
                builder.AddSite(new TerrainSite(id, block.Region, block.Bounds.center, radius));
                builder.AddRegionGroup(new TerrainRegionGroup(id, block.Region, counts[i], centers[i] / counts[i]));
                owners[i] = id;
            }
            return owners;
        }

        private static void WriteCells(TerrainMapBuilder builder, CityLayout layout, int[] owners)
        {
            TerrainMapData map = builder.Map;
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    Vector2 center = map.CellCenter(x, z);
                    if (layout.Grid.TrySampleRoad(center, out bool carriageway))
                    {
                        builder.SetRegion(x, z, CityRegion.UrbanBase);
                        builder.SetRoad(x, z, 1f, carriageway ? CityRoadType.Asphalt : CityRoadType.Sidewalk);
                        continue;
                    }
                    int block = layout.Grid.FindBlockIndex(center);
                    if (block < 0 || owners[block] < 0)
                        throw new InvalidOperationException("A non-road city cell has no street block owner.");
                    builder.SetRegion(x, z, layout.Blocks[block].Region, owners[block]);
                    builder.SetRegionGroup(x, z, owners[block]);
                }
            }
        }
    }
}
