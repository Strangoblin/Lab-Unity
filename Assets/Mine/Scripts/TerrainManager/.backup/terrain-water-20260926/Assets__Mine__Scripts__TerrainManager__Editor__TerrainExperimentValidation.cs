// ════════════════════════════════════════════════════════════
//  TerrainExperimentValidation — 验证种子、区域、道路、占地与网格契约
// ════════════════════════════════════════════════════════════

using System;
using UnityEngine;
using UnityEditor;

namespace Mine.Terrain.Editor
{
    /// <summary>
    /// 无场景依赖的 EditMode 验证，可从菜单或 unityctl script eval 调用。
    /// </summary>
    public static class TerrainExperimentValidation
    {
        [MenuItem("Tools/Terrain/Validate Experiment Data")]
        public static void ValidateFromMenu()
        {
            Debug.Log(Run());
        }

        public static string Run()
        {
            TerrainGenerationSettings settings = new TerrainGenerationSettings();
            TerrainMapData first = TerrainDataGenerator.Generate(settings);
            TerrainMapData second = TerrainDataGenerator.Generate(settings);
            TerrainGenerationSettings changedSettings = new TerrainGenerationSettings
            {
                Seed = settings.Seed + 1
            };
            TerrainMapData changed = TerrainDataGenerator.Generate(changedSettings);
            TerrainGenerationSettings invalidSettings = new TerrainGenerationSettings
            {
                Width = 0
            };
            bool invalidRejected = false;
            try
            {
                TerrainDataGenerator.Generate(invalidSettings);
            }
            catch (ArgumentException)
            {
                invalidRejected = true;
            }
            TerrainStuffPlan firstStuff = TerrainStuffDistributor.Distribute(first, settings);
            TerrainStuffPlan secondStuff = TerrainStuffDistributor.Distribute(second, settings);
            TerrainSurfaceResult surface = TerrainMeshGenerator.Generate(first, settings.ChunkCells);

            try
            {
                int[] regions = new int[4];
                int dirtCells = 0;
                int stoneCells = 0;
                for (int z = 0; z < first.HeightCells; z++)
                {
                    for (int x = 0; x < first.WidthCells; x++)
                    {
                        TerrainRegion region = first.GetRegion(x, z);
                        regions[(int)region]++;
                        Require(region == second.GetRegion(x, z), "Region generation is not deterministic.");
                        Require(first.GetRoadCoverage(x, z) == second.GetRoadCoverage(x, z),
                            "Road generation is not deterministic.");
                        if (first.GetRoadCoverage(x, z) > 0.5f)
                        {
                            if (first.GetRoadType(x, z) == TerrainRoadType.Stone) stoneCells++;
                            if (first.GetRoadType(x, z) == TerrainRoadType.Dirt) dirtCells++;
                        }
                    }
                }

                for (int i = 0; i < regions.Length; i++)
                    Require(regions[i] > 0, $"Region {(TerrainRegion)i} is missing.");
                Require(invalidRejected, "Invalid terrain dimensions were accepted.");
                Require(first.Sites[0].Center != changed.Sites[0].Center,
                    "Changing the seed did not change the layout.");
                Require(dirtCells > 0 && stoneCells > 0, "Both dirt and stone road segments are required.");
                Require(first.Routes.Count == 3, "Expected three connected site routes.");
                ValidateRoutes(first);
                ValidateStuff(first, firstStuff, secondStuff);
                Require(surface.Chunks.Count == 36, "Expected 36 surface chunks for the default map.");
                Require(surface.ColorMap.width == first.WidthCells &&
                        surface.ColorMap.height == first.HeightCells, "Color map size differs from logical grid.");
                Shader shader = Shader.Find("Render/TerrainSurface");
                Require(shader != null && shader.isSupported, "TerrainSurface shader is unavailable.");
                Require(ShaderUtil.GetShaderMessages(shader).Length == 0,
                    "TerrainSurface shader has compilation messages.");

                return $"Terrain validation passed: {first.WidthCells}x{first.HeightCells}, " +
                       $"regions {string.Join("/", regions)}, roads dirt={dirtCells} stone={stoneCells}, " +
                       $"chunks={surface.Chunks.Count}, markers={firstStuff.Placements.Count}.";
            }
            finally
            {
                for (int i = 0; i < surface.Chunks.Count; i++)
                    UnityEngine.Object.DestroyImmediate(surface.Chunks[i].Mesh);
                UnityEngine.Object.DestroyImmediate(surface.ColorMap);
            }
        }

        private static void ValidateRoutes(TerrainMapData map)
        {
            int[,] connections = { { 0, 1 }, { 0, 2 }, { 1, 2 } };
            for (int i = 0; i < map.Routes.Count; i++)
            {
                Vector2[] points = map.Routes[i].Points;
                Require(points.Length > 2, "Route has too few points.");
                Require(Vector2.Distance(points[0], map.Sites[connections[i, 0]].Center) < 0.001f,
                    "Route does not begin at its site.");
                Require(Vector2.Distance(points[points.Length - 1], map.Sites[connections[i, 1]].Center) < 0.001f,
                    "Route does not end at its site.");
                for (int j = 0; j < points.Length; j++)
                {
                    int x = Mathf.Clamp(Mathf.FloorToInt((points[j].x - map.Origin.x) / map.CellSize), 0, map.WidthCells - 1);
                    int z = Mathf.Clamp(Mathf.FloorToInt((points[j].y - map.Origin.y) / map.CellSize), 0, map.HeightCells - 1);
                    Require(map.GetRoadCoverage(x, z) > 0.5f, "Route raster has a gap at a route point.");
                }
            }
        }

        private static void ValidateStuff(TerrainMapData map, TerrainStuffPlan first, TerrainStuffPlan second)
        {
            Require(first.Placements.Count == second.Placements.Count, "Stuff count is not deterministic.");
            Require(first.Count(TerrainStuffType.House) > 0, "No house was placed.");
            Require(first.Count(TerrainStuffType.Windmill) == 1, "Expected one windmill.");
            Require(first.Count(TerrainStuffType.Tree) > 0 &&
                    first.Count(TerrainStuffType.Grass) > 0 &&
                    first.Count(TerrainStuffType.Wheat) > 0, "A plant marker category is missing.");

            for (int i = 0; i < first.Placements.Count; i++)
            {
                TerrainStuffPlacement current = first.Placements[i];
                TerrainStuffPlacement repeated = second.Placements[i];
                Require(current.Type == repeated.Type && current.Position == repeated.Position &&
                        current.Yaw == repeated.Yaw, "Stuff placement is not deterministic.");

                int x = Mathf.FloorToInt((current.Position.x - map.Origin.x) / map.CellSize);
                int z = Mathf.FloorToInt((current.Position.z - map.Origin.y) / map.CellSize);
                Require(map.GetRoadCoverage(x, z) <= 0.08f, "Marker overlaps a road.");
                Require(map.GetRegion(x, z) == RegionFor(current.Type), "Marker has the wrong region.");

                if (current.Type != TerrainStuffType.House && current.Type != TerrainStuffType.Windmill)
                    continue;
                for (int j = 0; j < i; j++)
                {
                    TerrainStuffPlacement other = first.Placements[j];
                    if (other.Type != TerrainStuffType.House && other.Type != TerrainStuffType.Windmill)
                        continue;
                    Vector2 a = new Vector2(current.Position.x, current.Position.z);
                    Vector2 b = new Vector2(other.Position.x, other.Position.z);
                    Require(Vector2.Distance(a, b) >= current.Radius + other.Radius,
                        "Building footprints overlap.");
                }
            }
        }

        private static TerrainRegion RegionFor(TerrainStuffType type)
        {
            switch (type)
            {
                case TerrainStuffType.House: return TerrainRegion.Village;
                case TerrainStuffType.Windmill:
                case TerrainStuffType.Wheat: return TerrainRegion.WheatField;
                case TerrainStuffType.Tree: return TerrainRegion.Forest;
                default: return TerrainRegion.Grassland;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
