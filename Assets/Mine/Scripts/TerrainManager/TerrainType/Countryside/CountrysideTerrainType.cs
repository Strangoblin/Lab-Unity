// ════════════════════════════════════════════════════════════
//  CountrysideTerrainType — 郊野生成步骤、内容配方与占位外观
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 原野场景方案；可复用通用算法生成草地、村落、麦田、森林等特征。
    /// </summary>
    [CreateAssetMenu(menuName = "Terrain/Types/Countryside", fileName = "CountrysideTerrain")]
    public sealed class CountrysideTerrainType : TerrainTypeDefinition
    {
        [SerializeField] private CountrysideSettings _settings = new CountrysideSettings();
        [SerializeField] private TerrainPlantSettings _plantSettings = DefaultPlants();

        public CountrysideSettings Settings => _settings;
        public TerrainPlantSettings PlantSettings => _plantSettings;

        // ════════════════════════════════════════════════════════════
        //  数据与分布 — 方案选择操作顺序，通用生成器执行计算
        // ════════════════════════════════════════════════════════════

        public override void Validate()
        {
            if (_settings == null || _plantSettings == null)
                throw new ArgumentException("Countryside settings are missing.");
            _settings.Validate();
            _plantSettings.Validate();
            for (int i = 0; i < _plantSettings.Rules.Length; i++)
            {
                TerrainPlantRule rule = _plantSettings.Rules[i];
                if (rule.Region >= CountrysideRegion.Count || rule.Plant >= CountrysidePlantType.Count)
                    throw new ArgumentException($"Unknown countryside plant rule at index {i}.");
            }
        }

        public override TerrainMapData GenerateMap(TerrainGenerationSettings settings)
        {
            Validate();
            TerrainMapData map = TerrainVoronoiGenerator.Generate(settings, _settings);
            TerrainHeightGenerator.ApplyDepression(map, CountrysideRegion.Water, _settings.DepressionDepth);
            return map;
        }

        public override TerrainStuffPlan DistributeStuff(TerrainMapData map)
        {
            TerrainStuffSettings rules = new TerrainStuffSettings(new[]
            {
                new TerrainStuffRule(CountrysideStuffType.Windmill, CountrysideRegion.WheatField, 1, 5f),
                new TerrainStuffRule(CountrysideStuffType.House, CountrysideRegion.Village, _settings.HouseCount, 4f),
                new TerrainStuffRule(CountrysideStuffType.Tree, CountrysideRegion.Forest, _settings.TreeCount, 1.5f),
                new TerrainStuffRule(CountrysideStuffType.OrchardTree, CountrysideRegion.Orchard,
                    _settings.TreeCount == 0 ? 0 : Mathf.Max(15, _settings.TreeCount / 2), 1.5f)
            });
            return TerrainStuffDistributor.Distribute(map, rules);
        }

        public override TerrainPlantPlan DistributePlants(TerrainMapData map, TerrainStuffPlan stuff) =>
            TerrainPlantDistributor.Distribute(map, stuff, _plantSettings);

        private static TerrainPlantSettings DefaultPlants() => new TerrainPlantSettings(new[]
        {
            new TerrainPlantRule(CountrysideRegion.Grassland, CountrysidePlantType.ShortGrass, 0.45f, 0.12f),
            new TerrainPlantRule(CountrysideRegion.Grassland, CountrysidePlantType.TallGrass, 0.07f, 0.18f),
            new TerrainPlantRule(CountrysideRegion.Village, CountrysidePlantType.ShortGrass, 0.05f, 0.12f),
            new TerrainPlantRule(CountrysideRegion.WheatField, CountrysidePlantType.Wheat, 1.1f, 0.12f),
            new TerrainPlantRule(CountrysideRegion.Forest, CountrysidePlantType.ShortGrass, 0.12f, 0.12f),
            new TerrainPlantRule(CountrysideRegion.Meadow, CountrysidePlantType.TallGrass, 0.35f, 0.18f),
            new TerrainPlantRule(CountrysideRegion.Meadow, CountrysidePlantType.Flower, 0.15f, 0.12f),
            new TerrainPlantRule(CountrysideRegion.Orchard, CountrysidePlantType.ShortGrass, 0.15f, 0.12f)
        });

        // ════════════════════════════════════════════════════════════
        //  实验外观 — 颜色和几何规格随方案输出，不进入通用渲染器
        // ════════════════════════════════════════════════════════════

        public override Color RegionColor(int region)
        {
            switch (region)
            {
                case CountrysideRegion.Grassland: return new Color(0.37f, 0.59f, 0.28f);
                case CountrysideRegion.Village: return new Color(0.50f, 0.62f, 0.38f);
                case CountrysideRegion.WheatField: return new Color(0.78f, 0.66f, 0.30f);
                case CountrysideRegion.Forest: return new Color(0.18f, 0.38f, 0.21f);
                case CountrysideRegion.Water: return new Color(0.10f, 0.36f, 0.65f);
                case CountrysideRegion.Meadow: return new Color(0.52f, 0.69f, 0.31f);
                case CountrysideRegion.Orchard: return new Color(0.34f, 0.49f, 0.23f);
                default: throw new ArgumentOutOfRangeException(nameof(region));
            }
        }

        public override Color RoadColor(int roadType) =>
            roadType == CountrysideRoadType.Dirt ? new Color(0.70f, 0.55f, 0.28f) : Color.clear;

        public override TerrainStuffVisual StuffVisual(int type)
        {
            switch (type)
            {
                case CountrysideStuffType.House:
                    return new TerrainStuffVisual(PrimitiveType.Cube, new Vector3(6f, 4f, 6f), new Color(0.78f, 0.33f, 0.25f));
                case CountrysideStuffType.Windmill:
                    return new TerrainStuffVisual(PrimitiveType.Cylinder, new Vector3(4f, 8f, 4f), new Color(0.95f, 0.91f, 0.76f));
                case CountrysideStuffType.Tree:
                    return new TerrainStuffVisual(PrimitiveType.Capsule, new Vector3(2.2f, 5f, 2.2f), new Color(0.08f, 0.28f, 0.12f));
                case CountrysideStuffType.OrchardTree:
                    return new TerrainStuffVisual(PrimitiveType.Capsule, new Vector3(2.5f, 4f, 2.5f), new Color(0.40f, 0.63f, 0.18f));
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public override TerrainPlantVisual PlantVisual(int type)
        {
            switch (type)
            {
                case CountrysidePlantType.ShortGrass:
                    return new TerrainPlantVisual(new Vector3(0.45f, 0.5f, 0.45f), new Color(0.19f, 0.76f, 0.20f));
                case CountrysidePlantType.TallGrass:
                    return new TerrainPlantVisual(new Vector3(0.7f, 1.1f, 0.7f), new Color(0.22f, 0.58f, 0.17f));
                case CountrysidePlantType.Wheat:
                    return new TerrainPlantVisual(new Vector3(0.45f, 1.2f, 0.45f), new Color(0.91f, 0.75f, 0.30f));
                case CountrysidePlantType.Flower:
                    return new TerrainPlantVisual(new Vector3(0.4f, 0.6f, 0.4f), new Color(0.94f, 0.34f, 0.62f));
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }
    }
}
