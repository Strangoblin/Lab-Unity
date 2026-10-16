// ════════════════════════════════════════════════════════════
//  CityTerrainType — 城市路网、园区配方与完整设施的方案入口
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Mine.Terrain
{
    /// <summary>
    /// 正交城市实验方案；每次生成独立布局，输出通用地图与内容计划。
    /// </summary>
    [CreateAssetMenu(menuName = "Terrain/Types/City", fileName = "CityTerrain")]
    public sealed class CityTerrainType : TerrainTypeDefinition
    {
        [SerializeField] private CitySettings _settings = new CitySettings();

        public CitySettings Settings => _settings;

        public override void Validate()
        {
            if (_settings == null)
                throw new InvalidOperationException("City settings are missing.");
            _settings.Validate();
        }

        public override TerrainMapData GenerateMap(TerrainGenerationSettings settings)
        {
            Validate();
            return CityMapGenerator.Generate(settings, _settings);
        }

        public override TerrainStuffPlan DistributeStuff(TerrainMapData map) =>
            CityStuffDistributor.Distribute(map, _settings);

        public override TerrainPlantPlan DistributePlants(TerrainMapData map, TerrainStuffPlan stuff)
        {
            Validate();
            List<TerrainPlantRule> rules = new List<TerrainPlantRule>();
            AddGreenRule(rules, (int)CitySubtype.Park, CityPlantType.Lawn, 0.12f);
            AddGreenRule(rules, (int)CitySubtype.Undeveloped, CityPlantType.WildGrass, 0.18f);
            return TerrainPlantDistributor.Distribute(map, stuff,
                new TerrainPlantSettings(rules.ToArray()));
        }

        // ════════════════════════════════════════════════════════════
        //  方案外观 — 设施为完整占位几何，尺寸同时供布局验证使用
        // ════════════════════════════════════════════════════════════

        public override Color RegionColor(int region)
        {
            switch (region)
            {
                case CityRegion.UrbanBase: return new Color(0.40f, 0.42f, 0.43f);
                case (int)CitySubtype.Factory: return new Color(0.48f, 0.43f, 0.34f);
                case (int)CitySubtype.ConstructionSite: return new Color(0.68f, 0.54f, 0.34f);
                case (int)CitySubtype.Villa: return new Color(0.61f, 0.60f, 0.47f);
                case (int)CitySubtype.Slum: return new Color(0.47f, 0.48f, 0.44f);
                case (int)CitySubtype.Office: return new Color(0.43f, 0.53f, 0.61f);
                case (int)CitySubtype.ResearchBase: return new Color(0.53f, 0.61f, 0.65f);
                case (int)CitySubtype.Park: return new Color(0.25f, 0.54f, 0.29f);
                case (int)CitySubtype.Undeveloped: return new Color(0.37f, 0.47f, 0.23f);
                default: throw new ArgumentOutOfRangeException(nameof(region));
            }
        }

        public override Color RoadColor(int roadType)
        {
            switch (roadType)
            {
                case TerrainRoadType.None: return Color.clear;
                case CityRoadType.Asphalt: return new Color(0.15f, 0.17f, 0.19f);
                case CityRoadType.Sidewalk: return new Color(0.72f, 0.73f, 0.70f);
                default: throw new ArgumentOutOfRangeException(nameof(roadType));
            }
        }

        public override TerrainStuffVisual StuffVisual(int type) => VisualForStuff(type);

        public static TerrainStuffVisual VisualForStuff(int type)
        {
            switch (type)
            {
                case CityStuffType.Factory:
                    return Cube(new Vector3(15f, 6f, 12f), new Color(0.72f, 0.47f, 0.25f));
                case CityStuffType.ConstructionSite:
                    return Cube(new Vector3(12f, 1.5f, 12f), new Color(0.89f, 0.67f, 0.28f));
                case CityStuffType.Villa:
                    return Cube(new Vector3(8f, 4f, 8f), new Color(0.91f, 0.85f, 0.67f));
                case CityStuffType.SlumHouse:
                    return Cube(new Vector3(4f, 3f, 4f), new Color(0.61f, 0.49f, 0.42f));
                case CityStuffType.OfficeTower:
                    return Cube(new Vector3(10f, 24f, 10f), new Color(0.36f, 0.63f, 0.78f));
                case CityStuffType.ResearchBuilding:
                    return Cube(new Vector3(14f, 7f, 10f), new Color(0.72f, 0.85f, 0.88f));
                case CityStuffType.ParkPavilion:
                    return Cube(new Vector3(5f, 3f, 5f), new Color(0.77f, 0.55f, 0.36f));
                case CityStuffType.Tree:
                    return Cube(new Vector3(2f, 5f, 2f), new Color(0.13f, 0.37f, 0.19f));
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public override TerrainPlantVisual PlantVisual(int type)
        {
            switch (type)
            {
                case CityPlantType.Lawn:
                    return new TerrainPlantVisual(new Vector3(0.45f, 0.5f, 0.45f),
                        new Color(0.25f, 0.67f, 0.28f));
                case CityPlantType.WildGrass:
                    return new TerrainPlantVisual(new Vector3(0.7f, 1.1f, 0.7f),
                        new Color(0.42f, 0.59f, 0.23f));
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private void AddGreenRule(List<TerrainPlantRule> rules, int region, int plant, float radius)
        {
            foreach (CitySubtypeRule rule in _settings.SubtypeRules)
            {
                if (rule.Region != region)
                    continue;
                if (rule.GrassDensity > 0f)
                    rules.Add(new TerrainPlantRule(region, plant, rule.GrassDensity, radius));
                return;
            }
        }

        private static TerrainStuffVisual Cube(Vector3 scale, Color color) =>
            new TerrainStuffVisual(PrimitiveType.Cube, scale, color);
    }
}
