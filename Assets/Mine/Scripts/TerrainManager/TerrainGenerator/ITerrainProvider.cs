// ════════════════════════════════════════════════════════════
//  ITerrainProvider — 数据生成策略与工厂
// ════════════════════════════════════════════════════════════

namespace Mine.Terrain
{
    /// <summary>
    /// 向空网格写入高度与地表层；提供者不持有渲染资源。
    /// </summary>
    public interface ITerrainProvider
    {
        void Generate(TerrainGrid grid, TerrainSettings settings);
    }

    /// <summary>
    /// 按枚举创建内置提供者；后续可扩展为资产驱动的注册表。
    /// </summary>
    public static class TerrainProviderFactory
    {
        public static ITerrainProvider Create(TerrainProviderKind kind)
        {
            switch (kind)
            {
                case TerrainProviderKind.Flat:
                    return new FlatTerrainProvider();
                case TerrainProviderKind.Noise:
                    return new NoiseTerrainProvider();
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(kind), kind,
                        "Unsupported terrain provider.");
            }
        }
    }
}
