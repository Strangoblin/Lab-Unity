// ════════════════════════════════════════════════════════════
//  FlatTerrainProvider — 全零高度与统一地表层
// ════════════════════════════════════════════════════════════

namespace Mine.Terrain
{
    /// <summary>
    /// 最简提供者：用于验证渲染层、采样契约与消费者接入。
    /// </summary>
    public sealed class FlatTerrainProvider : ITerrainProvider
    {
        public void Generate(TerrainGrid grid, TerrainSettings settings)
        {
            for (int z = 0; z <= grid.HeightCells; z++)
                for (int x = 0; x <= grid.WidthCells; x++)
                    grid.SetHeight(x, z, 0f);

            for (int z = 0; z < grid.HeightCells; z++)
                for (int x = 0; x < grid.WidthCells; x++)
                    grid.SetLayer(x, z, settings.DefaultLayer);

            grid.RecalculateHeightRange();
        }
    }
}
