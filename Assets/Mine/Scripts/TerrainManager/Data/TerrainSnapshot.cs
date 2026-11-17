// ════════════════════════════════════════════════════════════
//  TerrainSnapshot — 数据层的一次不可变发布
// ════════════════════════════════════════════════════════════

namespace Mine.Terrain
{
    /// <summary>
    /// 消费者持有的数据快照；Version 变化即代表网格与采样都已更新。
    /// </summary>
    public sealed class TerrainSnapshot
    {
        public TerrainGrid Grid { get; }
        public int Version { get; }

        public TerrainSnapshot(TerrainGrid grid, int version)
        {
            Grid = grid;
            Version = version;
        }
    }
}
