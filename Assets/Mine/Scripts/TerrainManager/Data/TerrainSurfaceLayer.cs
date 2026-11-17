// ════════════════════════════════════════════════════════════
//  TerrainSurfaceLayer — 地表层的离散语义与默认配色
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Mine.Terrain
{
    /// <summary>
    /// 地表层标识：数据层按格存储，渲染层按层解析默认颜色。
    /// </summary>
    public enum TerrainSurfaceLayer : byte
    {
        Grass = 0,
        Dirt = 1,
        Rock = 2,
        Sand = 3,
        Snow = 4
    }

    /// <summary>
    /// 地表层到基础色的映射；后续可由材质或调色板资产替换。
    /// </summary>
    public static class TerrainSurfacePalette
    {
        private static readonly Color Grass = new(0.28f, 0.46f, 0.20f);
        private static readonly Color Dirt = new(0.42f, 0.32f, 0.20f);
        private static readonly Color Rock = new(0.42f, 0.42f, 0.44f);
        private static readonly Color Sand = new(0.72f, 0.64f, 0.42f);
        private static readonly Color Snow = new(0.90f, 0.92f, 0.95f);

        public static Color GetColor(TerrainSurfaceLayer layer)
        {
            switch (layer)
            {
                case TerrainSurfaceLayer.Dirt:
                    return Dirt;
                case TerrainSurfaceLayer.Rock:
                    return Rock;
                case TerrainSurfaceLayer.Sand:
                    return Sand;
                case TerrainSurfaceLayer.Snow:
                    return Snow;
                default:
                    return Grass;
            }
        }
    }
}
