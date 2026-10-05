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

        [MenuItem("Tools/Terrain/Validate Seed Sweep")]
        public static void ValidateSeedSweepFromMenu()
        {
            Debug.Log(RunSeedSweep());
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
                int[] regions = new int[5];
                int dirtCells = 0;
                int stoneCells = 0;
                float lowestWater = 0f;
                int waterX = -1;
                int waterZ = -1;
                for (int z = 0; z < first.HeightCells; z++)
                {
                    for (int x = 0; x < first.WidthCells; x++)
                    {
                        TerrainRegion region = first.GetRegion(x, z);
                        regions[(int)region]++;
                        Require(region == second.GetRegion(x, z), "Region generation is not deterministic.");
                        Require(first.GetRoadCoverage(x, z) == second.GetRoadCoverage(x, z),
                            "Road generation is not deterministic.");
                        Require(first.GetHeight(x, z) == second.GetHeight(x, z),
                            "Height generation is not deterministic.");
                        if (region == TerrainRegion.Water)
                        {
                            float height = first.GetHeight(x, z);
                            if (height < lowestWater)
                            {
                                lowestWater = height;
                                waterX = x;
                                waterZ = z;
                            }
                            Require(first.GetRoadCoverage(x, z) <= 0.08f,
                                "A road crosses the water region.");
                        }
                        if (first.GetRoadCoverage(x, z) > 0.5f)
                        {
                            if (first.GetRoadType(x, z) == TerrainRoadType.Stone) stoneCells++;
                            if (first.GetRoadType(x, z) == TerrainRoadType.Dirt) dirtCells++;
                        }
                    }
                }

                for (int i = 0; i < regions.Length; i++)
                    Require(regions[i] > 0, $"Region {(TerrainRegion)i} is missing.");
                Require(first.Sites.Count == 4, "Expected four peer terrain sites.");
                ValidateSites(first);
                ValidateRegionConnectivity(first, regions);
                Require(lowestWater <= -0.999f && lowestWater >= -1.001f,
                    "Water floor does not reach one world unit below ground.");
                Require(invalidRejected, "Invalid terrain dimensions were accepted.");
                Require(first.Sites[0].Center != changed.Sites[0].Center,
                    "Changing the seed did not change the layout.");
                Require(dirtCells > 0 && stoneCells > 0, "Both dirt and stone road segments are required.");
                Require(first.Routes.Count == 2, "Expected the two edges of a minimal connected road network.");
                ValidateRoutes(first);
                ValidateStuff(first, firstStuff, secondStuff);
                Require(surface.Chunks.Count == 36, "Expected 36 surface chunks for the default map.");
                float lowestVertex = 0f;
                for (int i = 0; i < surface.Chunks.Count; i++)
                {
                    Vector3[] vertices = surface.Chunks[i].Mesh.vertices;
                    for (int j = 0; j < vertices.Length; j++)
                        lowestVertex = Mathf.Min(lowestVertex, vertices[j].y);
                }
                Require(lowestVertex <= -0.999f && lowestVertex >= -1.001f,
                    "Surface mesh does not contain the one-unit water basin.");
                Require(surface.ColorMap.width == first.WidthCells &&
                        surface.ColorMap.height == first.HeightCells, "Color map size differs from logical grid.");
                Color waterColor = surface.ColorMap.GetPixel(waterX, waterZ);
                Require(waterColor.b > waterColor.g && waterColor.g > waterColor.r,
                    "Water region is not blue in the surface color map.");
                ValidateVisualBlend(first, surface.ColorMap);
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

        public static string RunSeedSweep()
        {
            const int seedCount = 20;
            for (int seed = 0; seed < seedCount; seed++)
            {
                TerrainMapData map = TerrainDataGenerator.Generate(
                    new TerrainGenerationSettings { Seed = seed });
                int[] regions = new int[5];
                for (int z = 0; z < map.HeightCells; z++)
                {
                    for (int x = 0; x < map.WidthCells; x++)
                    {
                        TerrainRegion region = map.GetRegion(x, z);
                        regions[(int)region]++;
                        if (region == TerrainRegion.Water)
                            Require(map.GetRoadCoverage(x, z) <= 0.08f,
                                $"Seed {seed} has a road crossing water.");
                    }
                }

                for (int i = 0; i < regions.Length; i++)
                    Require(regions[i] > 0, $"Seed {seed} is missing region {(TerrainRegion)i}.");
                Require(map.Routes.Count == 2, $"Seed {seed} has the wrong road-tree size.");
                ValidateSites(map);
                ValidateRegionConnectivity(map, regions);
                ValidateRoutes(map);
            }

            return $"Terrain seed sweep passed: {seedCount} seeds, connected regions and water-free roads.";
        }

        private static void ValidateSites(TerrainMapData map)
        {
            for (int i = 0; i < map.Sites.Count; i++)
            {
                TerrainSite site = map.Sites[i];
                int x = Mathf.FloorToInt((site.Center.x - map.Origin.x) / map.CellSize);
                int z = Mathf.FloorToInt((site.Center.y - map.Origin.y) / map.CellSize);
                Require(map.GetRegion(x, z) == site.Region, "A site center lost its region.");
                for (int j = 0; j < i; j++)
                {
                    TerrainSite other = map.Sites[j];
                    Require(Vector2.Distance(site.Center, other.Center) >
                            (site.Radius + other.Radius) * 0.8f,
                        "Terrain sites are too close together.");
                }
            }
        }

        private static void ValidateRegionConnectivity(TerrainMapData map, int[] regions)
        {
            int width = map.WidthCells;
            bool[] visited = new bool[width * map.HeightCells];
            int[] queue = new int[visited.Length];
            for (int regionIndex = 1; regionIndex < regions.Length; regionIndex++)
            {
                TerrainRegion region = (TerrainRegion)regionIndex;
                int start = -1;
                for (int z = 0; z < map.HeightCells && start < 0; z++)
                    for (int x = 0; x < width; x++)
                        if (map.GetRegion(x, z) == region)
                        {
                            start = x + z * width;
                            break;
                        }

                Array.Clear(visited, 0, visited.Length);
                int head = 0;
                int tail = 0;
                queue[tail++] = start;
                visited[start] = true;
                while (head < tail)
                {
                    int cell = queue[head++];
                    int x = cell % width;
                    int z = cell / width;
                    TryVisit(x - 1, z);
                    TryVisit(x + 1, z);
                    TryVisit(x, z - 1);
                    TryVisit(x, z + 1);
                }
                Require(tail == regions[regionIndex], $"Region {region} has disconnected islands.");

                void TryVisit(int x, int z)
                {
                    if (x < 0 || z < 0 || x >= width || z >= map.HeightCells)
                        return;
                    int index = x + z * width;
                    if (visited[index] || map.GetRegion(x, z) != region)
                        return;
                    visited[index] = true;
                    queue[tail++] = index;
                }
            }
        }

        private static void ValidateRoutes(TerrainMapData map)
        {
            bool[,] connected = new bool[3, 3];
            for (int i = 0; i < map.Routes.Count; i++)
            {
                Vector2[] points = map.Routes[i].Points;
                Require(points.Length > 2, "Route has too few points.");
                int startSite = RouteEndpointSite(map, points[0]);
                int endSite = RouteEndpointSite(map, points[points.Length - 1]);
                Require(startSite >= 0 && endSite >= 0 && startSite != endSite,
                    "Route endpoints must be distinct land sites.");
                connected[startSite, endSite] = true;
                connected[endSite, startSite] = true;
                for (int j = 0; j < points.Length; j++)
                {
                    int x = Mathf.Clamp(Mathf.FloorToInt((points[j].x - map.Origin.x) / map.CellSize), 0, map.WidthCells - 1);
                    int z = Mathf.Clamp(Mathf.FloorToInt((points[j].y - map.Origin.y) / map.CellSize), 0, map.HeightCells - 1);
                    Require(map.GetRoadCoverage(x, z) > 0.5f, "Route raster has a gap at a route point.");
                    Require(map.GetRegion(x, z) != TerrainRegion.Water, "Route point lies in water.");
                }
            }
            Require((connected[0, 1] || connected[0, 2]) &&
                    (connected[1, 0] || connected[1, 2]) &&
                    (connected[2, 0] || connected[2, 1]),
                "Land sites are not connected by the road tree.");
        }

        private static int RouteEndpointSite(TerrainMapData map, Vector2 point)
        {
            for (int i = 0; i < map.Sites.Count; i++)
                if (map.Sites[i].Region != TerrainRegion.Water &&
                    Vector2.Distance(point, map.Sites[i].Center) < 0.001f)
                    return i;
            return -1;
        }

        private static void ValidateVisualBlend(TerrainMapData map, Texture2D colorMap)
        {
            bool blendedBoundary = false;
            for (int z = 1; z < map.HeightCells - 1 && !blendedBoundary; z++)
            {
                for (int x = 1; x < map.WidthCells - 1; x++)
                {
                    if (map.GetRegion(x, z) != TerrainRegion.Water ||
                        map.GetRegion(x + 1, z) != TerrainRegion.Grassland)
                        continue;
                    Color edge = colorMap.GetPixel(x, z);
                    blendedBoundary = edge.r > 0.11f && edge.b < 0.64f;
                    if (blendedBoundary)
                        break;
                }
            }
            Require(blendedBoundary, "Water boundary did not blend into neighboring grassland.");
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
