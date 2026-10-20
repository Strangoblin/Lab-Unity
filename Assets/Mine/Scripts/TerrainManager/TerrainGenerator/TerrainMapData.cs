// ════════════════════════════════════════════════════════════
//  TerrainMapData — 俯视地图的逻辑网格、地点和道路数据契约
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Mine.Terrain
{
    public static class TerrainRoadType
    {
        public const int None = 0;
    }

    public readonly struct TerrainSite
    {
        public readonly int Id;
        public readonly int Region;
        public readonly Vector2 Center;
        public readonly float Radius;

        public TerrainSite(int id, int region, Vector2 center, float radius)
        {
            Id = id;
            Region = region;
            Center = center;
            Radius = radius;
        }
    }

    public readonly struct TerrainRegionGroup
    {
        public readonly int Id;
        public readonly int Region;
        public readonly int CellCount;
        public readonly Vector2 Center;

        public TerrainRegionGroup(int id, int region, int cellCount, Vector2 center)
        {
            Id = id;
            Region = region;
            CellCount = cellCount;
            Center = center;
        }
    }

    /// <summary>
    /// 地形的唯一逻辑数据源；区域与道路为独立层，地表和内容生成器只读取它。
    /// </summary>
    public sealed class TerrainMapData
    {
        private readonly int[] _regions;
        private readonly int[] _ownerSiteIds;
        private readonly int[] _regionGroupIds;
        private readonly int[] _roadTypes;
        private readonly float[] _roadCoverage;
        private readonly float[] _heights;
        private readonly List<TerrainSite> _sites = new List<TerrainSite>();
        private readonly List<TerrainRegionGroup> _regionGroups = new List<TerrainRegionGroup>();

        public int WidthCells { get; }
        public int HeightCells { get; }
        public float CellSize { get; }
        public Vector2 Origin { get; }
        public int Seed { get; }
        public IReadOnlyList<TerrainSite> Sites => _sites;
        public IReadOnlyList<TerrainRegionGroup> RegionGroups => _regionGroups;

        internal TerrainMapData(TerrainGenerationSettings settings)
        {
            WidthCells = Mathf.RoundToInt(settings.Width / settings.CellSize);
            HeightCells = Mathf.RoundToInt(settings.Height / settings.CellSize);
            CellSize = settings.CellSize;
            Origin = settings.Origin;
            Seed = settings.Seed;

            int count = WidthCells * HeightCells;
            _regions = new int[count];
            _ownerSiteIds = new int[count];
            _regionGroupIds = new int[count];
            _roadTypes = new int[count];
            _roadCoverage = new float[count];
            _heights = new float[count];
            for (int i = 0; i < count; i++)
            {
                _ownerSiteIds[i] = -1;
                _regionGroupIds[i] = -1;
            }
        }

        public int GetRegion(int x, int z) => _regions[Index(x, z)];
        public int GetOwnerSiteId(int x, int z) => _ownerSiteIds[Index(x, z)];
        public int GetRegionGroupId(int x, int z) => _regionGroupIds[Index(x, z)];
        public int GetRoadType(int x, int z) => _roadTypes[Index(x, z)];
        public float GetRoadCoverage(int x, int z) => _roadCoverage[Index(x, z)];
        public float GetHeight(int x, int z) => _heights[Index(x, z)];

        public Vector2 CellCenter(int x, int z)
        {
            Index(x, z);
            return Origin + new Vector2((x + 0.5f) * CellSize, (z + 0.5f) * CellSize);
        }

        public float SampleHeightAtVertex(int x, int z)
        {
            float sum = 0f;
            for (int dz = -1; dz <= 0; dz++)
            {
                for (int dx = -1; dx <= 0; dx++)
                {
                    int sx = Mathf.Clamp(x + dx, 0, WidthCells - 1);
                    int sz = Mathf.Clamp(z + dz, 0, HeightCells - 1);
                    sum += GetHeight(sx, sz);
                }
            }
            return sum * 0.25f;
        }

        internal void SetRegion(int x, int z, int value, int ownerSiteId)
        {
            int index = Index(x, z);
            _regions[index] = value;
            _ownerSiteIds[index] = ownerSiteId;
        }
        internal void SetRegionGroup(int x, int z, int groupId) =>
            _regionGroupIds[Index(x, z)] = groupId;
        internal void SetHeight(int x, int z, float value) => _heights[Index(x, z)] = value;

        internal void SetRoad(int x, int z, float coverage, int type)
        {
            int index = Index(x, z);
            _roadCoverage[index] = Mathf.Clamp01(coverage);
            _roadTypes[index] = type;
        }

        internal void AddSite(TerrainSite site) => _sites.Add(site);
        internal void AddRegionGroup(TerrainRegionGroup group) => _regionGroups.Add(group);

        private int Index(int x, int z)
        {
            if (x < 0 || x >= WidthCells || z < 0 || z >= HeightCells)
                throw new ArgumentOutOfRangeException($"Terrain cell ({x}, {z}) is outside {WidthCells} x {HeightCells}.");
            return x + z * WidthCells;
        }
    }
}
