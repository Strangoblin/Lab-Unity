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

        [MenuItem("Tools/Terrain/Validate Voronoi Distribution")]
        public static void ValidateVoronoiDistributionFromMenu()
        {
            Debug.Log(RunVoronoiDistribution());
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
                    new TerrainFeatureRule(TerrainRegion.Water, 1, 13f, 19f, false)
                }
            };
            TerrainMapData sparse = TerrainDataGenerator.Generate(sparseSettings);
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
            TerrainPlantSettings plantSettings = new TerrainPlantSettings();
            ValidateLegacyPlantRules();
            TerrainPlantPlan firstPlants = TerrainPlantDistributor.Distribute(first, firstStuff, plantSettings);
            TerrainPlantPlan secondPlants = TerrainPlantDistributor.Distribute(second, secondStuff, plantSettings);
            TerrainSurfaceResult surface = TerrainMeshGenerator.Generate(first, settings.ChunkCells);

            try
            {
                int[] regions = new int[Enum.GetValues(typeof(TerrainRegion)).Length];
                int dirtCells = 0;
                int stoneCells = 0;
                int roadOnFeatures = 0;
                int roadOnWater = 0;
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
                        }
                        if (first.GetRoadCoverage(x, z) > 0.5f)
                        {
                            if (first.GetRoadType(x, z) == TerrainRoadType.Stone) stoneCells++;
                            if (first.GetRoadType(x, z) == TerrainRoadType.Dirt) dirtCells++;
                            if (region != TerrainRegion.Grassland) roadOnFeatures++;
                            if (region == TerrainRegion.Water) roadOnWater++;
                        }
                    }
                }

                for (int i = 0; i < regions.Length; i++)
                    Require(regions[i] > 0, $"Region {(TerrainRegion)i} is missing.");
                Require(first.Sites.Count == 28 && sparse.Sites.Count == 4,
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
                Require(dirtCells + stoneCells > 0, "Voronoi boundaries produced no roads.");
                Require(roadOnFeatures > 0 && roadOnWater > 0,
                    "Roads did not overlay feature regions and water.");
                ValidateRoadLayers(first);
                RunVoronoiDistribution();
                ValidateStuff(first, firstStuff, secondStuff);
                ValidatePlants(first, firstStuff, firstPlants, secondPlants);
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
                ValidateRoadPalette(first, surface.ColorMap);
                Shader shader = Shader.Find("TerrainSurface/TerrainSurface");
                Require(shader != null && shader.isSupported, "TerrainSurface shader is unavailable.");
                Require(ShaderUtil.GetShaderMessages(shader).Length == 0,
                    "TerrainSurface shader has compilation messages.");

                return $"Terrain validation passed: {first.WidthCells}x{first.HeightCells}, " +
                       $"regions {string.Join("/", regions)}, roads dirt={dirtCells} stone={stoneCells} " +
                       $"feature/water={roadOnFeatures}/{roadOnWater}, " +
                       $"chunks={surface.Chunks.Count}, structures={firstStuff.Placements.Count}, " +
                       $"plants={firstPlants.Placements.Count}.";
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
                    }
                }

                for (int i = 0; i < regions.Length; i++)
                    Require(regions[i] > 0, $"Seed {seed} is missing region {(TerrainRegion)i}.");
                ValidateSites(map);
                ValidateRegionConnectivity(map, regions);
                ValidateRoadLayers(map);
            }

            return $"Terrain seed sweep passed: {seedCount} seeds, valid regions and layered roads.";
        }

        public static string RunSizeSweep()
        {
            int[] sizes = { 32, 64, 128, 300 };
            for (int i = 0; i < sizes.Length; i++)
            {
                TerrainGenerationSettings settings = new TerrainGenerationSettings
                {
                    Width = sizes[i],
                    Height = sizes[i],
                    BroadWarpDistance = 0f,
                    DetailWarpDistance = 0f
                };
                TerrainMapData map = TerrainDataGenerator.Generate(settings);
                Require(map.WidthCells == sizes[i] && map.HeightCells == sizes[i],
                    "Map size was not applied.");
                ValidateSites(map);
                ValidateRoadLayers(map);
                ValidatePolygonRegions(map, settings);
            }
            return "Terrain size sweep passed: 32, 64, 128, 300.";
        }

        public static string RunVoronoiDistribution()
        {
            TerrainGenerationSettings settings = new TerrainGenerationSettings
            {
                BroadWarpDistance = 0f,
                DetailWarpDistance = 0f
            };
            TerrainMapData unwarped = TerrainDataGenerator.Generate(settings);
            ValidatePolygonRegions(unwarped, settings);
            ValidateVoronoiRoadSdf(unwarped, settings.VoronoiRoadWidth,
                settings.VoronoiCornerSoftness);
            ValidateVariableVoronoiRoadWidth(unwarped, settings.VoronoiRoadWidth);
            settings.BroadWarpDistance = 18f;
            settings.DetailWarpDistance = 5f;
            TerrainMapData warped = TerrainDataGenerator.Generate(settings);

            int warpedFeatures = 0;
            int warpedRoads = 0;
            for (int z = 0; z < unwarped.HeightCells; z++)
            {
                for (int x = 0; x < unwarped.WidthCells; x++)
                {
                    if (unwarped.GetRegion(x, z) != warped.GetRegion(x, z))
                        warpedFeatures++;
                    if (Mathf.Abs(unwarped.GetRoadCoverage(x, z) -
                        warped.GetRoadCoverage(x, z)) > 0.01f)
                        warpedRoads++;
                }
            }
            Require(warpedFeatures > 0 && warpedRoads > 0,
                "Final noise did not affect both feature and road data.");
            ValidateWidthMonotonicity();
            ValidateCornerSoftness();
            ValidateRegionSoftness();
            ValidateRoadLayers(unwarped);
            ValidateRoadLayers(warped);
            TerrainStuffPlan stuff = TerrainStuffDistributor.Distribute(warped, settings);
            TerrainPlantPlan plants = TerrainPlantDistributor.Distribute(warped, stuff,
                new TerrainPlantSettings());
            TerrainSurfaceResult surface = TerrainMeshGenerator.Generate(warped,
                settings.ChunkCells);
            try
            {
                Require(surface.Chunks.Count > 0 && stuff.Placements.Count > 0 &&
                    plants.Placements.Count > 0,
                    "Polygon SDF output was not consumed by mesh and content layers.");
            }
            finally
            {
                for (int i = 0; i < surface.Chunks.Count; i++)
                    UnityEngine.Object.DestroyImmediate(surface.Chunks[i].Mesh);
                UnityEngine.Object.DestroyImmediate(surface.ColorMap);
            }
            return $"Voronoi distribution passed: noise feature/road=" +
                $"{warpedFeatures}/{warpedRoads}.";
        }

        private static void ValidateSites(TerrainMapData map)
        {
            for (int i = 0; i < map.Sites.Count; i++)
            {
                TerrainSite site = map.Sites[i];
                Require(site.Region != TerrainRegion.Grassland,
                    "The Grassland base layer created an unnecessary site.");
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

        private static void ValidatePolygonRegions(TerrainMapData map,
            TerrainGenerationSettings settings)
        {
            float width = settings.VoronoiRegionWidth *
                Mathf.Min(settings.Width, settings.Height) / 300f;
            float[] centerEdges = new float[map.Sites.Count];
            for (int i = 0; i < map.Sites.Count; i++)
                centerEdges[i] = TerrainVoronoiSdf.DistanceGap(map,
                    map.Sites[i].Center, i, out _, settings.VoronoiRegionSoftness);
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    TerrainSite owner = map.Sites[map.GetOwnerSiteId(x, z)];
                    Vector2 point = map.CellCenter(x, z);
                    float edge = TerrainVoronoiSdf.DistanceGap(map, point, owner.Id,
                        out _, settings.VoronoiRegionSoftness);
                    float threshold = centerEdges[owner.Id] - width;
                    TerrainRegion expected = edge > threshold ? owner.Region : TerrainRegion.Grassland;
                    Require(map.GetRegion(x, z) == expected,
                        "Region differs from its Voronoi seed-distance threshold.");
                }
            }
        }

        private static void ValidateWidthMonotonicity()
        {
            TerrainGenerationSettings settings = new TerrainGenerationSettings
            {
                BroadWarpDistance = 0f,
                DetailWarpDistance = 0f,
                VoronoiRegionWidth = 12f
            };
            TerrainMapData narrowRegions = TerrainDataGenerator.Generate(settings);
            settings.VoronoiRegionWidth = 30f;
            TerrainMapData wideRegions = TerrainDataGenerator.Generate(settings);
            int expandedCells = 0;
            for (int z = 0; z < narrowRegions.HeightCells; z++)
            {
                for (int x = 0; x < narrowRegions.WidthCells; x++)
                {
                    TerrainRegion before = narrowRegions.GetRegion(x, z);
                    TerrainRegion after = wideRegions.GetRegion(x, z);
                    if (before == TerrainRegion.Grassland && after != TerrainRegion.Grassland)
                        expandedCells++;
                    if (before != TerrainRegion.Grassland)
                        Require(before == after, "Increasing Voronoi region width removed a feature cell.");
                }
            }
            Require(expandedCells > 0, "Voronoi region width does not expand regions.");

            settings.VoronoiRoadWidth = 3f;
            TerrainMapData narrowRoads = TerrainDataGenerator.Generate(settings);
            settings.VoronoiRoadWidth = 9f;
            TerrainMapData wideRoads = TerrainDataGenerator.Generate(settings);
            int expandedRoadCells = 0;
            for (int z = 0; z < narrowRoads.HeightCells; z++)
            {
                for (int x = 0; x < narrowRoads.WidthCells; x++)
                {
                    float before = narrowRoads.GetRoadCoverage(x, z);
                    float after = wideRoads.GetRoadCoverage(x, z);
                    Require(after + 0.0001f >= before,
                        "Increasing Voronoi road width reduced road coverage.");
                    if (after > before + 0.01f)
                        expandedRoadCells++;
                }
            }
            Require(expandedRoadCells > 0, "Voronoi road width does not expand roads.");

        }

        private static void ValidateCornerSoftness()
        {
            TerrainGenerationSettings settings = new TerrainGenerationSettings
            {
                BroadWarpDistance = 0f,
                DetailWarpDistance = 0f,
                VoronoiCornerSoftness = 0f
            };
            TerrainMapData original = TerrainDataGenerator.Generate(settings);
            settings.VoronoiCornerSoftness = 3f;
            TerrainMapData smoothed = TerrainDataGenerator.Generate(settings);
            int widenedCells = 0;
            for (int z = 0; z < original.HeightCells; z++)
            {
                for (int x = 0; x < original.WidthCells; x++)
                {
                    float before = original.GetRoadCoverage(x, z);
                    float after = smoothed.GetRoadCoverage(x, z);
                    Require(after + 0.0001f >= before,
                        "Increasing corner softness reduced Voronoi road coverage.");
                    if (after > before + 0.01f)
                        widenedCells++;
                }
            }
            Require(widenedCells > 0,
                "Voronoi corner softness did not round any road junctions.");
        }

        private static void ValidateRegionSoftness()
        {
            TerrainGenerationSettings settings = new TerrainGenerationSettings
            {
                BroadWarpDistance = 0f,
                DetailWarpDistance = 0f,
                VoronoiRegionSoftness = 0f
            };
            TerrainMapData original = TerrainDataGenerator.Generate(settings);
            settings.VoronoiRegionSoftness = 8f;
            TerrainMapData smoothed = TerrainDataGenerator.Generate(settings);
            int changedCells = 0;
            for (int z = 0; z < original.HeightCells; z++)
            {
                for (int x = 0; x < original.WidthCells; x++)
                {
                    Require(original.GetOwnerSiteId(x, z) == smoothed.GetOwnerSiteId(x, z),
                        "Region softness changed nearest-site ownership.");
                    if (original.GetRegion(x, z) != smoothed.GetRegion(x, z))
                        changedCells++;
                }
            }
            Require(changedCells > 0, "Region softness did not change any region boundaries.");
            ValidateSites(original);
            ValidateSites(smoothed);
        }

        private static void ValidateVoronoiRoadSdf(TerrainMapData map, float roadWidth,
            float cornerSoftness)
        {
            float halfWidth = roadWidth * 0.5f;
            float feather = Mathf.Max(map.CellSize, 0.5f);
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    float expected = 0f;
                    int owner = map.GetOwnerSiteId(x, z);
                    float gap = TerrainVoronoiSdf.DistanceGap(map,
                        map.CellCenter(x, z), owner, out int neighbor, cornerSoftness);
                    if (neighbor >= 0)
                        expected = 1f - Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(halfWidth - feather,
                                halfWidth + feather, gap));
                    Require(Mathf.Abs(map.GetRoadCoverage(x, z) - expected) < 0.0001f,
                        "Voronoi road coverage differs from its seed-distance gap.");
                }
            }
        }

        private static void ValidateVariableVoronoiRoadWidth(TerrainMapData map,
            float roadWidth)
        {
            float halfWidth = roadWidth * 0.5f;
            float feather = Mathf.Max(map.CellSize, 0.5f);
            int widerCells = 0;
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    int owner = map.GetOwnerSiteId(x, z);
                    float geometric = TerrainVoronoiSdf.DistanceToEdge(map,
                        map.CellCenter(x, z), owner, out _);
                    float equalWidthCoverage = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(halfWidth - feather,
                            halfWidth + feather, geometric));
                    if (map.GetRoadCoverage(x, z) > 0.5f && equalWidthCoverage < 0.1f)
                        widerCells++;
                }
            }
            Require(widerCells > 0,
                "Voronoi roads still have the same width as geometric cell-edge roads.");
        }

        private static void ValidateRegionConnectivity(TerrainMapData map, int[] regions)
        {
            int width = map.WidthCells;
            int groupCount = map.RegionGroups.Count;
            int[] firstCell = new int[groupCount];
            int[] cellCounts = new int[groupCount];
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
                    Require(map.RegionGroups[groupId].Region == region,
                        "Cell and region group disagree.");
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

        private static void ValidateRoadLayers(TerrainMapData map)
        {
            int roadCells = 0;
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    if (map.GetRoadCoverage(x, z) <= 0f)
                        continue;
                    roadCells++;
                    Require(map.GetRoadCoverage(x, z) <= 1f,
                        "Road coverage exceeds one.");
                    Require(map.GetRoadType(x, z) != TerrainRoadType.None,
                        "A road cell has no surface type.");
                }
            }
            if (map.WidthCells * map.CellSize >= 300f)
                Require(roadCells > 0, "The map has no roads.");
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

        private static void ValidateRoadPalette(TerrainMapData map, Texture2D colorMap)
        {
            int dirt = 0;
            int stone = 0;
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    if (map.GetRoadCoverage(x, z) < 0.99f)
                        continue;
                    TerrainRoadType type = map.GetRoadType(x, z);
                    if (type == TerrainRoadType.None)
                        continue;
                    Color color = colorMap.GetPixel(x, z);
                    Require(color.r > color.g && color.g > color.b && color.r < 0.85f,
                        "Road palette is not earth yellow.");
                    if (type == TerrainRoadType.Dirt) dirt++;
                    if (type == TerrainRoadType.Stone) stone++;
                }
            }
            Require(dirt > 0 && stone > 0, "A road palette category was not sampled.");
        }

        private static void ValidateStuff(TerrainMapData map, TerrainStuffPlan first, TerrainStuffPlan second)
        {
            Require(first.Placements.Count == second.Placements.Count, "Stuff count is not deterministic.");
            Require(first.Count(TerrainStuffType.House) > 0, "No house was placed.");
            Require(first.Count(TerrainStuffType.Windmill) == 1, "Expected one windmill.");
            Require(first.Count(TerrainStuffType.Tree) > 0 &&
                    first.Count(TerrainStuffType.OrchardTree) > 0,
                "A tree category is missing from stuff.");

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

                for (int j = 0; j < i; j++)
                {
                    TerrainStuffPlacement other = first.Placements[j];
                    Vector2 a = new Vector2(current.Position.x, current.Position.z);
                    Vector2 b = new Vector2(other.Position.x, other.Position.z);
                    Require(Vector2.Distance(a, b) >= current.Radius + other.Radius,
                        "Stuff footprints overlap.");
                }
            }
        }

        private static TerrainRegion RegionFor(TerrainStuffType type)
        {
            switch (type)
            {
                case TerrainStuffType.House: return TerrainRegion.Village;
                case TerrainStuffType.Tree: return TerrainRegion.Forest;
                case TerrainStuffType.OrchardTree: return TerrainRegion.Orchard;
                default: return TerrainRegion.WheatField;
            }
        }

        private static void ValidatePlants(TerrainMapData map, TerrainStuffPlan structures,
            TerrainPlantPlan first, TerrainPlantPlan second)
        {
            Require(first.Placements.Count == second.Placements.Count,
                "Plant count is not deterministic.");
            Require(first.Placements.Count > structures.Placements.Count * 20,
                "Plant distribution is not dense enough for the experiment.");
            foreach (TerrainPlantType type in Enum.GetValues(typeof(TerrainPlantType)))
                Require(first.Count(type) > 0, $"Plant type {type} is missing.");

            for (int i = 0; i < first.Placements.Count; i++)
            {
                TerrainPlantPlacement plant = first.Placements[i];
                TerrainPlantPlacement repeated = second.Placements[i];
                Require(plant.Type == repeated.Type && plant.Region == repeated.Region &&
                        plant.Position == repeated.Position && plant.Yaw == repeated.Yaw &&
                        plant.Scale == repeated.Scale, "Plant placement is not deterministic.");

                int x = Mathf.FloorToInt((plant.Position.x - map.Origin.x) / map.CellSize);
                int z = Mathf.FloorToInt((plant.Position.z - map.Origin.y) / map.CellSize);
                Require(x >= 0 && x < map.WidthCells && z >= 0 && z < map.HeightCells,
                    "Plant is outside the map.");
                Require(map.GetRegion(x, z) == plant.Region &&
                        map.GetRoadCoverage(x, z) <= 0.08f,
                    "Plant overlaps a road or has the wrong region.");

                Vector2 point = new Vector2(plant.Position.x, plant.Position.z);
                for (int j = 0; j < structures.Placements.Count; j++)
                {
                    TerrainStuffPlacement structure = structures.Placements[j];
                    Vector2 center = new Vector2(structure.Position.x, structure.Position.z);
                    Require(Vector2.Distance(point, center) >=
                            plant.Radius + structure.Radius + 0.25f,
                        "Plant overlaps a structure footprint.");
                }
            }
        }

        private static void ValidateLegacyPlantRules()
        {
            TerrainPlantSettings legacy = new TerrainPlantSettings();
            int originalCount = legacy.Rules.Length;
            legacy.Rules[0] = new TerrainPlantRule(TerrainRegion.Forest,
                (TerrainPlantType)4, 0.006f, 1.4f);
            legacy.Rules[1] = new TerrainPlantRule(TerrainRegion.Orchard,
                (TerrainPlantType)5, 0.008f, 1.4f);
            legacy.Validate();
            Require(legacy.Rules.Length == originalCount - 2,
                "Legacy tree plant rules were not migrated out of the dense plant settings.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
