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

        [MenuItem("Tools/Terrain/Validate Size Sweep")]
        public static void ValidateSizeSweepFromMenu()
        {
            Debug.Log(RunSizeSweep());
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
            TerrainGenerationSettings sparseSettings = new TerrainGenerationSettings
            {
                FeatureRules = new[]
                {
                    new TerrainFeatureRule(TerrainRegion.Village, 1, 17f, 22f, true),
                    new TerrainFeatureRule(TerrainRegion.WheatField, 1, 22f, 32f, true),
                    new TerrainFeatureRule(TerrainRegion.Forest, 1, 23f, 32f, true),
                    new TerrainFeatureRule(TerrainRegion.Water, 1, 13f, 19f, false),
                    new TerrainFeatureRule(TerrainRegion.Grassland, 1, 26f, 35f, false)
                }
            };
            TerrainMapData sparse = TerrainDataGenerator.Generate(sparseSettings);
            TerrainMapData tree = TerrainDataGenerator.Generate(new TerrainGenerationSettings
            {
                LoopRatio = 0f,
                RoadBend = 0f
            });
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
                int[] regions = new int[Enum.GetValues(typeof(TerrainRegion)).Length];
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
                        Require(first.GetOwnerSiteId(x, z) == second.GetOwnerSiteId(x, z),
                            "Site ownership is not deterministic.");
                        Require(first.GetRegionGroupId(x, z) == second.GetRegionGroupId(x, z),
                            "Region groups are not deterministic.");
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
                Require(first.Sites.Count == 48 && sparse.Sites.Count == 5,
                    "Map recipe did not control the number of feature instances.");
                Require(regions[(int)TerrainRegion.Grassland] > 20000 &&
                        regions[(int)TerrainRegion.Grassland] < 70000,
                    "Grassland share is outside the experiment's broad coverage range.");
                ValidateSites(first);
                ValidateRegionConnectivity(first, regions);
                Require(lowestWater <= -0.999f && lowestWater >= -1.001f,
                    "Water floor does not reach one world unit below ground.");
                Require(invalidRejected, "Invalid terrain dimensions were accepted.");
                Require(first.Sites[0].Center != changed.Sites[0].Center,
                    "Changing the seed did not change the layout.");
                Require(dirtCells > 0 && stoneCells > 0, "Both dirt and stone road segments are required.");
                int roadGroups = CountRoadGroups(first);
                Require(first.Routes.Count > roadGroups - 1,
                    "Expected a connected road network with loops.");
                Require(tree.Routes.Count == roadGroups - 1,
                    "Disabling loops did not leave a spanning tree.");
                bool bendChangesRoute = false;
                for (int i = 0; i < tree.Routes.Count && !bendChangesRoute; i++)
                {
                    Vector2[] curved = first.Routes[i].Points;
                    Vector2[] direct = tree.Routes[i].Points;
                    if (curved.Length != direct.Length)
                    {
                        bendChangesRoute = true;
                        continue;
                    }
                    for (int point = 0; point < curved.Length; point++)
                        if (curved[point] != direct[point])
                        {
                            bendChangesRoute = true;
                            break;
                        }
                }
                Require(bendChangesRoute, "RoadBend did not change the route geometry.");
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
                int[] regions = new int[Enum.GetValues(typeof(TerrainRegion)).Length];
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
                Require(map.Routes.Count >= CountRoadGroups(map) - 1,
                    $"Seed {seed} has too few road edges.");
                ValidateSites(map);
                ValidateRegionConnectivity(map, regions);
                ValidateRoutes(map);
            }

            return $"Terrain seed sweep passed: {seedCount} seeds, connected regions and water-free roads.";
        }

        public static string RunSizeSweep()
        {
            int[] sizes = { 32, 64, 128, 300 };
            for (int i = 0; i < sizes.Length; i++)
            {
                TerrainGenerationSettings settings = new TerrainGenerationSettings
                {
                    Width = sizes[i],
                    Height = sizes[i]
                };
                TerrainMapData map = TerrainDataGenerator.Generate(settings);
                Require(map.WidthCells == sizes[i] && map.HeightCells == sizes[i],
                    "Map size was not applied.");
                ValidateSites(map);
                ValidateRoutes(map);
            }
            return "Terrain size sweep passed: 32, 64, 128, 300.";
        }

        private static void ValidateSites(TerrainMapData map)
        {
            for (int i = 0; i < map.Sites.Count; i++)
            {
                TerrainSite site = map.Sites[i];
                int x = Mathf.FloorToInt((site.Center.x - map.Origin.x) / map.CellSize);
                int z = Mathf.FloorToInt((site.Center.y - map.Origin.y) / map.CellSize);
                Require(map.GetRegion(x, z) == site.Region, "A site center lost its region.");
                Require(map.GetOwnerSiteId(x, z) == site.Id, "A site center lost its ownership.");
                for (int j = 0; j < i; j++)
                {
                    TerrainSite other = map.Sites[j];
                    Require(Vector2.Distance(site.Center, other.Center) >
                            (site.Radius + other.Radius) * 0.42f,
                        "Terrain sites are too close together.");
                }
            }
        }

        private static void ValidateRegionConnectivity(TerrainMapData map, int[] regions)
        {
            int width = map.WidthCells;
            int groupCount = map.RegionGroups.Count;
            int[] firstCell = new int[groupCount];
            int[] cellCounts = new int[groupCount];
            int[] siteCounts = new int[groupCount];
            for (int i = 0; i < firstCell.Length; i++)
                firstCell[i] = -1;
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int owner = map.GetOwnerSiteId(x, z);
                    int groupId = map.GetRegionGroupId(x, z);
                    Require(owner >= 0 && owner < map.Sites.Count,
                        "A terrain cell has no valid owner site.");
                    Require(groupId >= 0 && groupId < groupCount,
                        "A terrain cell has no valid region group.");
                    TerrainRegion region = map.GetRegion(x, z);
                    Require(map.Sites[owner].Region == region &&
                            map.RegionGroups[groupId].Region == region,
                        "Cell, owner, and region group disagree.");
                    if (firstCell[groupId] < 0)
                        firstCell[groupId] = x + z * width;
                    cellCounts[groupId]++;
                    if (x + 1 < width && map.GetRegion(x + 1, z) == region)
                        Require(map.GetRegionGroupId(x + 1, z) == groupId,
                            "Adjacent same-type cells were not fused.");
                    if (z + 1 < map.HeightCells && map.GetRegion(x, z + 1) == region)
                        Require(map.GetRegionGroupId(x, z + 1) == groupId,
                            "Adjacent same-type cells were not fused.");
                }
            }

            bool[] visited = new bool[width * map.HeightCells];
            int[] queue = new int[visited.Length];
            int currentGroup = -1;
            int tail = 0;
            for (int groupId = 0; groupId < groupCount; groupId++)
            {
                TerrainRegionGroup group = map.RegionGroups[groupId];
                Require(group.Id == groupId && cellCounts[groupId] == group.CellCount &&
                        firstCell[groupId] >= 0, "Region group cell count is inconsistent.");
                Array.Clear(visited, 0, visited.Length);
                currentGroup = groupId;
                int head = 0;
                tail = 0;
                queue[tail++] = firstCell[groupId];
                visited[firstCell[groupId]] = true;
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
                Require(tail == group.CellCount, "A region group is disconnected.");
                if (group.AnchorSiteId >= 0)
                    Require(map.Sites[group.AnchorSiteId].ConnectRoad,
                        "Region group anchor is not road-eligible.");
            }

            for (int i = 0; i < map.Sites.Count; i++)
            {
                TerrainSite site = map.Sites[i];
                int x = Mathf.FloorToInt((site.Center.x - map.Origin.x) / map.CellSize);
                int z = Mathf.FloorToInt((site.Center.y - map.Origin.y) / map.CellSize);
                siteCounts[map.GetRegionGroupId(x, z)]++;
            }
            bool hasFusion = false;
            for (int i = 0; i < groupCount; i++)
                hasFusion |= siteCounts[i] > 1 &&
                    map.RegionGroups[i].Region != TerrainRegion.Grassland;
            if (map.WidthCells * map.CellSize >= 300f)
                Require(hasFusion, "No non-grass sites fused into a shared region.");
            int totalCells = 0;
            for (int i = 0; i < regions.Length; i++)
                totalCells += regions[i];
            Require(totalCells == visited.Length, "Region counts do not cover the whole map.");

            void TryVisit(int x, int z)
            {
                if (x < 0 || z < 0 || x >= width || z >= map.HeightCells)
                    return;
                int index = x + z * width;
                if (visited[index] || map.GetRegionGroupId(x, z) != currentGroup)
                    return;
                visited[index] = true;
                queue[tail++] = index;
            }
        }

        private static int CountRoadGroups(TerrainMapData map)
        {
            int count = 0;
            for (int i = 0; i < map.RegionGroups.Count; i++)
                if (map.RegionGroups[i].AnchorSiteId >= 0)
                    count++;
            return count;
        }

        private static void ValidateRoutes(TerrainMapData map)
        {
            int siteCount = map.Sites.Count;
            bool[,] connected = new bool[siteCount, siteCount];
            int[] degree = new int[siteCount];
            bool[] anchors = new bool[siteCount];
            int landCount = CountRoadGroups(map);
            for (int i = 0; i < map.RegionGroups.Count; i++)
                if (map.RegionGroups[i].AnchorSiteId >= 0)
                    anchors[map.RegionGroups[i].AnchorSiteId] = true;
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
                degree[startSite]++;
                degree[endSite]++;
                for (int j = 0; j < points.Length; j++)
                {
                    int x = Mathf.Clamp(Mathf.FloorToInt((points[j].x - map.Origin.x) / map.CellSize), 0, map.WidthCells - 1);
                    int z = Mathf.Clamp(Mathf.FloorToInt((points[j].y - map.Origin.y) / map.CellSize), 0, map.HeightCells - 1);
                    Require(map.GetRoadCoverage(x, z) > 0.5f, "Route raster has a gap at a route point.");
                    Require(map.GetRegion(x, z) != TerrainRegion.Water, "Route point lies in water.");
                    if (j == 0)
                        continue;
                    int steps = Mathf.CeilToInt(Vector2.Distance(points[j - 1], points[j]) /
                        (map.CellSize * 0.5f));
                    if (steps == 0)
                        continue;
                    for (int step = 0; step <= steps; step++)
                    {
                        Vector2 point = Vector2.Lerp(points[j - 1], points[j], step / (float)steps);
                        int sx = Mathf.Clamp(Mathf.FloorToInt((point.x - map.Origin.x) / map.CellSize), 0, map.WidthCells - 1);
                        int sz = Mathf.Clamp(Mathf.FloorToInt((point.y - map.Origin.y) / map.CellSize), 0, map.HeightCells - 1);
                        Require(map.GetRegion(sx, sz) != TerrainRegion.Water,
                            $"Route {i} segment {j} crosses water near ({sx}, {sz}).");
                    }
                }
            }
            Require(map.Routes.Count >= landCount - 1, "Road graph has too few edges.");
            bool[] reached = new bool[siteCount];
            int first = -1;
            for (int i = 0; i < siteCount; i++)
                if (anchors[i])
                {
                    first = i;
                    break;
                }
            if (first < 0)
                return;
            reached[first] = true;
            for (int pass = 0; pass < siteCount; pass++)
                for (int i = 0; i < siteCount; i++)
                    for (int j = 0; j < siteCount; j++)
                        if (reached[i] && connected[i, j])
                            reached[j] = true;
            bool hasJunction = false;
            for (int i = 0; i < siteCount; i++)
            {
                if (!anchors[i])
                    continue;
                Require(reached[i], "A road-connected site is disconnected from the network.");
                hasJunction |= degree[i] >= 3;
            }
            if (landCount >= 4 && map.WidthCells * map.CellSize >= 300f)
            {
                Require(map.Routes.Count > landCount - 1, "Road graph has no loop.");
                Require(hasJunction, "Road graph has no branching junction.");
            }
        }

        private static int RouteEndpointSite(TerrainMapData map, Vector2 point)
        {
            for (int i = 0; i < map.RegionGroups.Count; i++)
            {
                int siteId = map.RegionGroups[i].AnchorSiteId;
                if (siteId >= 0 && Vector2.Distance(point, map.Sites[siteId].Center) < 0.001f)
                    return siteId;
            }
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
                    first.Count(TerrainStuffType.Wheat) > 0 &&
                    first.Count(TerrainStuffType.Flower) > 0 &&
                    first.Count(TerrainStuffType.OrchardTree) > 0,
                "A plant marker category is missing.");

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
                case TerrainStuffType.OrchardTree: return TerrainRegion.Orchard;
                case TerrainStuffType.Flower: return TerrainRegion.Meadow;
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
