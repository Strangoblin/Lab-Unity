// ════════════════════════════════════════════════════════════
//  GrassGroundColorChecks — 草地表色链路的自检
//  通过 unityctl script execute -f 运行，不参与 Unity 编译。
//  需在 GrassManagerDemo 播放中执行（依赖场景里的 TerrainManager）。
// ════════════════════════════════════════════════════════════

using System;
using UnityEngine;
using Mine.Grass;
using Mine.Terrain;

public static class Script
{
    public static void Main()
    {
        var terrain = UnityEngine.Object.FindFirstObjectByType<TerrainManager>();
        if (terrain == null)
            throw new InvalidOperationException("TerrainManager not found in the loaded scene.");
        if (!terrain.HasData || terrain.CurrentGrid == null)
            throw new InvalidOperationException("TerrainManager has no snapshot.");

        TerrainGrid grid = terrain.CurrentGrid;
        Texture2D colorMap = terrain.SurfaceColorMap;
        if (colorMap == null)
            throw new InvalidOperationException("TerrainManager.SurfaceColorMap is null.");

        if (colorMap.width != grid.WidthCells || colorMap.height != grid.HeightCells)
            throw new InvalidOperationException(
                $"Color map {colorMap.width}x{colorMap.height} does not match grid {grid.WidthCells}x{grid.HeightCells}.");

        var grass = UnityEngine.Object.FindFirstObjectByType<GrassChunkManager>();
        if (grass == null)
            throw new InvalidOperationException("GrassChunkManager not found in the loaded scene.");

        GrassTerrainSource source = grass.Terrain;
        if (source == null)
            throw new InvalidOperationException("GrassChunkManager has no terrain source.");
        if (!source.TryGetGroundColor(out GrassGroundColorSource groundColor))
            throw new InvalidOperationException("TryGetGroundColor returned false.");

        if (!ReferenceEquals(groundColor.ColorMap, colorMap))
            throw new InvalidOperationException("Ground color map is not the terrain color map.");

        var expectedOrigin = new Vector2(grid.Origin.x, grid.Origin.z);
        var expectedSize = new Vector2(grid.WorldWidth, grid.WorldDepth);
        if ((groundColor.Origin - expectedOrigin).sqrMagnitude > 1e-6f)
            throw new InvalidOperationException($"Ground color origin {groundColor.Origin} != {expectedOrigin}.");
        if ((groundColor.Size - expectedSize).sqrMagnitude > 1e-6f)
            throw new InvalidOperationException($"Ground color size {groundColor.Size} != {expectedSize}.");

        // 世界 XZ → UV：网格写入端与草采样端必须落到同一套映射。
        Vector2 ToUV(float worldX, float worldZ) =>
            new Vector2((worldX - groundColor.Origin.x) / groundColor.Size.x,
                (worldZ - groundColor.Origin.y) / groundColor.Size.y);

        Vector2 uvMin = ToUV(grid.Origin.x, grid.Origin.z);
        Vector2 uvMax = ToUV(grid.Origin.x + grid.WorldWidth, grid.Origin.z + grid.WorldDepth);
        if ((uvMin - Vector2.zero).sqrMagnitude > 1e-6f || (uvMax - Vector2.one).sqrMagnitude > 1e-6f)
            throw new InvalidOperationException($"World to UV mapping is off: {uvMin} .. {uvMax}.");

        Shader shader = Shader.Find("Render/GrassWind");
        if (shader == null)
            throw new InvalidOperationException("Shader 'Render/GrassWind' was not found.");
        Material material = new Material(shader);
        try
        {
            if (!material.HasProperty("_TerrainColorMap") || !material.HasProperty("_TerrainColorMapTransform"))
                throw new InvalidOperationException("Grass shader is missing the terrain color map properties.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(material);
        }

        if (grass.VisibleInstanceCount <= 0)
            throw new InvalidOperationException("No visible grass instances; the demo did not build any chunks.");

        Debug.Log($"GRASS_GROUND_COLOR_CHECK_PASS: map={colorMap.width}x{colorMap.height}, " +
                  $"origin={groundColor.Origin}, size={groundColor.Size}, " +
                  $"instances={grass.ActiveInstanceCount}, visible={grass.VisibleInstanceCount}");
    }
}
