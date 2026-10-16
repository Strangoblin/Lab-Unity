// ════════════════════════════════════════════════════════════
//  TerrainTypeDefinition — 地图方案的生成策略与外观契约
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Mine.Terrain
{
    /// <summary>
    /// 以统一地图与内容计划输出不同场景方案；内容 ID 的含义由各方案自行定义。
    /// </summary>
    public abstract class TerrainTypeDefinition : ScriptableObject, ITerrainAppearance
    {
        public abstract void Validate();
        public abstract TerrainMapData GenerateMap(TerrainGenerationSettings settings);
        public abstract TerrainStuffPlan DistributeStuff(TerrainMapData map);
        public abstract TerrainPlantPlan DistributePlants(TerrainMapData map, TerrainStuffPlan stuff);
        public abstract Color RegionColor(int region);
        public abstract Color RoadColor(int roadType);
        public abstract TerrainStuffVisual StuffVisual(int type);
        public abstract TerrainPlantVisual PlantVisual(int type);
    }
}
