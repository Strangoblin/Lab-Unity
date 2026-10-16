// ════════════════════════════════════════════════════════════
//  CountrysideSettings — 郊野配方、水域下凹与稀疏内容参数
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 郊野方案持有 Voronoi 操作参数和特有规则；地图尺寸与种子独立配置。
    /// </summary>
    [Serializable]
    public sealed class CountrysideSettings : TerrainVoronoiSettings
    {
        [SerializeField, Min(0f)] private float _depressionDepth = 1f;
        [SerializeField, Min(0)] private int _houseCount = 10;
        [SerializeField, Min(0)] private int _treeCount = 90;

        public float DepressionDepth { get => _depressionDepth; set => _depressionDepth = value; }
        public int HouseCount { get => _houseCount; set => _houseCount = value; }
        public int TreeCount { get => _treeCount; set => _treeCount = value; }

        public CountrysideSettings()
        {
            BaseRegion = CountrysideRegion.Grassland;
            RoadType = CountrysideRoadType.Dirt;
            FeatureRules = new[]
            {
                new TerrainFeatureRule(CountrysideRegion.Village, 3, 17f, 22f, new Vector2(0.29f, 0.50f)),
                new TerrainFeatureRule(CountrysideRegion.WheatField, 7, 22f, 32f, new Vector2(0.70f, 0.36f)),
                new TerrainFeatureRule(CountrysideRegion.Forest, 8, 23f, 32f, new Vector2(0.72f, 0.73f)),
                new TerrainFeatureRule(CountrysideRegion.Water, 4, 13f, 19f, new Vector2(0.22f, 0.22f)),
                new TerrainFeatureRule(CountrysideRegion.Meadow, 3, 15f, 23f, new Vector2(0.47f, 0.20f)),
                new TerrainFeatureRule(CountrysideRegion.Orchard, 3, 16f, 23f, new Vector2(0.43f, 0.78f))
            };
        }

        public override void Validate()
        {
            base.Validate();
            if (float.IsNaN(_depressionDepth) || float.IsInfinity(_depressionDepth) ||
                _depressionDepth < 0f || _houseCount < 0 || _treeCount < 0 ||
                BaseRegion != CountrysideRegion.Grassland || RoadType != CountrysideRoadType.Dirt)
                throw new ArgumentException("Countryside height, content counts, or base IDs are invalid.");
            for (int i = 0; i < FeatureRules.Length; i++)
                if (FeatureRules[i].Region <= CountrysideRegion.Grassland ||
                    FeatureRules[i].Region >= CountrysideRegion.Count)
                    throw new ArgumentException($"Unknown countryside feature at index {i}.");
        }
    }
}
