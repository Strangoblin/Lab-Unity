// ════════════════════════════════════════════════════════════
//  CityLayout — 城市路网与街区用途的只读布局
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Mine.Terrain
{
    public readonly struct CityBlock
    {
        public int Id { get; }
        public int Region { get; }
        public CityZone Zone => CityContent.ZoneFor(Region);
        public Rect Bounds { get; }

        public CityBlock(int id, int region, Rect bounds)
        {
            Id = id;
            Region = region;
            Bounds = bounds;
        }
    }

    /// <summary>
    /// 某次生成的只读布局；由分布器按地图种子重建，不写入共享方案资产。
    /// </summary>
    public sealed class CityLayout
    {
        private readonly IReadOnlyList<CityBlock> _blocks;

        public TerrainGridRoadLayout Grid { get; }
        public IReadOnlyList<CityBlock> Blocks => _blocks;

        public CityLayout(TerrainGridRoadLayout grid, CityBlock[] blocks)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (blocks == null || blocks.Length != grid.Blocks.Count)
                throw new ArgumentException("City block count must match the grid layout.", nameof(blocks));
            _blocks = Array.AsReadOnly((CityBlock[])blocks.Clone());
        }
    }
}
