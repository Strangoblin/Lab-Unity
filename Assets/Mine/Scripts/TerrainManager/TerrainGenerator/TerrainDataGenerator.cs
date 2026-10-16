// ════════════════════════════════════════════════════════════
//  TerrainDataGenerator — 将通用地图配置分派给所选地图方案
// ════════════════════════════════════════════════════════════

using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 统一数据生成入口；算法和场景配方由 TerrainTypeDefinition 实现。
    /// </summary>
    public static class TerrainDataGenerator
    {
        public static TerrainMapData Generate(TerrainGenerationSettings settings,
            TerrainTypeDefinition terrainType)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (terrainType == null)
                throw new ArgumentNullException(nameof(terrainType));
            settings.Validate();
            terrainType.Validate();
            TerrainMapData map = terrainType.GenerateMap(settings);
            if (map == null)
                throw new InvalidOperationException(
                    $"Terrain type {terrainType.GetType().Name} returned no map data.");
            return map;
        }
    }
}
