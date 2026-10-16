// ════════════════════════════════════════════════════════════
//  ITerrainAppearance — 地图方案向表现层提供外观规格的通用契约
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Mine.Terrain
{
    /// <summary>
    /// 提供类型对应的表现参数，渲染层只消费类型标识和生成结果。
    /// </summary>
    public interface ITerrainAppearance
    {
        Color RegionColor(int region);
        Color RoadColor(int roadType);
        TerrainStuffVisual StuffVisual(int type);
        TerrainPlantVisual PlantVisual(int type);
    }

    public readonly struct TerrainStuffVisual
    {
        public readonly PrimitiveType Primitive;
        public readonly Vector3 Scale;
        public readonly Color Color;

        public TerrainStuffVisual(PrimitiveType primitive, Vector3 scale, Color color)
        {
            Primitive = primitive;
            Scale = scale;
            Color = color;
        }
    }

    public readonly struct TerrainPlantVisual
    {
        public readonly Vector3 Size;
        public readonly Color Color;

        public TerrainPlantVisual(Vector3 size, Color color)
        {
            Size = size;
            Color = color;
        }
    }
}
