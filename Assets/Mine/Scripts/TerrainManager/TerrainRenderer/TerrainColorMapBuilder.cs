// ════════════════════════════════════════════════════════════
//  TerrainColorMapBuilder — 把逐格地表层烘焙成颜色图
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Mine.Terrain
{
    /// <summary>
    /// 颜色图尺寸等于格数，UV 与网格顶点共用整图映射。
    /// </summary>
    public static class TerrainColorMapBuilder
    {
        public static Texture2D Build(TerrainGrid grid)
        {
            var texture = new Texture2D(grid.WidthCells, grid.HeightCells,
                TextureFormat.RGBA32, false)
            {
                name = "Terrain Color Map",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };

            var pixels = new Color[grid.CellCount];
            for (int z = 0; z < grid.HeightCells; z++)
                for (int x = 0; x < grid.WidthCells; x++)
                    pixels[z * grid.WidthCells + x] =
                        TerrainSurfacePalette.GetColor(grid.GetLayer(x, z));

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
