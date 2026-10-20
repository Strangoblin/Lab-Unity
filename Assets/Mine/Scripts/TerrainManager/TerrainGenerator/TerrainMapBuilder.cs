// ════════════════════════════════════════════════════════════
//  TerrainMapBuilder — 地图方案构建数据时的公共写入入口
// ════════════════════════════════════════════════════════════

using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 为自定义地图方案提供写入接口；表现层继续通过 TerrainMapData 只读契约消费数据。
    /// </summary>
    /// <remarks>
    /// 内容类型 ID 可稀疏；地点和分组 ID 是密集列表索引，写入格子归属前需先添加对应记录。
    /// </remarks>
    public sealed class TerrainMapBuilder
    {
        public TerrainMapData Map { get; }

        public TerrainMapBuilder(TerrainGenerationSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            Map = new TerrainMapData(settings);
        }

        public void SetRegion(int x, int z, int region, int ownerSiteId = -1)
        {
            if (region < 0)
                throw new ArgumentOutOfRangeException(nameof(region), "Region type ID must not be negative.");
            if (ownerSiteId < -1 || ownerSiteId >= Map.Sites.Count)
                throw new ArgumentOutOfRangeException(nameof(ownerSiteId),
                    "Owner site must be an existing site index, or -1 for no owner.");
            Map.SetRegion(x, z, region, ownerSiteId);
        }

        public void SetRegionGroup(int x, int z, int groupId)
        {
            if (groupId < -1 || groupId >= Map.RegionGroups.Count)
                throw new ArgumentOutOfRangeException(nameof(groupId),
                    "Region group must be an existing group index, or -1 for no group.");
            Map.SetRegionGroup(x, z, groupId);
        }

        public void SetHeight(int x, int z, float height)
        {
            if (!IsFinite(height))
                throw new ArgumentOutOfRangeException(nameof(height), "Terrain height must be finite.");
            Map.SetHeight(x, z, height);
        }

        public void SetRoad(int x, int z, float coverage, int roadType)
        {
            if (!IsFinite(coverage))
                throw new ArgumentOutOfRangeException(nameof(coverage), "Road coverage must be finite.");
            if (roadType < 0)
                throw new ArgumentOutOfRangeException(nameof(roadType), "Road type ID must not be negative.");
            Map.SetRoad(x, z, coverage, roadType);
        }

        public void AddSite(TerrainSite site)
        {
            if (site.Id != Map.Sites.Count)
                throw new ArgumentException("Site ID must equal the next dense site index.", nameof(site));
            if (site.Region < 0 || !IsFinite(site.Center.x) || !IsFinite(site.Center.y) ||
                !IsFinite(site.Radius) || site.Radius <= 0f)
                throw new ArgumentException("Site region, center, or radius is invalid.", nameof(site));
            Map.AddSite(site);
        }

        public void AddRegionGroup(TerrainRegionGroup group)
        {
            if (group.Id != Map.RegionGroups.Count)
                throw new ArgumentException("Group ID must equal the next dense group index.", nameof(group));
            if (group.Region < 0 || group.CellCount < 1 ||
                !IsFinite(group.Center.x) || !IsFinite(group.Center.y))
                throw new ArgumentException("Region group type, count, or center is invalid.", nameof(group));
            Map.AddRegionGroup(group);
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
