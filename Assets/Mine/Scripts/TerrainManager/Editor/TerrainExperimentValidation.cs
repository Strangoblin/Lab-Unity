// ════════════════════════════════════════════════════════════
//  TerrainExperimentValidation — 验证种子、区域、道路、占地与网格契约
// ════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System;

namespace Mine.Terrain.Editor
{
    /// <summary>
    /// 郊野方案的无场景 EditMode 回归；通用方案隔离另由 RunTypeIsolation 验证。
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

        [MenuItem("Tools/Terrain/Validate Terrain Type Isolation")]
        public static void ValidateTypeIsolationFromMenu()
        {
            Debug.Log(RunTypeIsolation());
        }

        public static string Run()
        {
            using CountrysideFixture fixture = new CountrysideFixture();
            TerrainGenerationSettings settings = fixture.MapSettings;
            TerrainMapData first = fixture.Generate();
            TerrainMapData second = fixture.Generate();
            TerrainGenerationSettings changedSettings = new TerrainGenerationSettings
            {
                Seed = settings.Seed + 1
            };
            TerrainMapData changed = fixture.Generate(changedSettings);
            using CountrysideFixture sparseFixture = new CountrysideFixture();
            sparseFixture.Settings.FeatureRules = new[]
                {
                    new TerrainFeatureRule(CountrysideRegion.Village, 1, 17f, 22f),
                    new TerrainFeatureRule(CountrysideRegion.WheatField, 1, 22f, 32f),
                    new TerrainFeatureRule(CountrysideRegion.Forest, 1, 23f, 32f),
                    new TerrainFeatureRule(CountrysideRegion.Water, 1, 13f, 19f)
                };
            TerrainMapData sparse = sparseFixture.Generate();
            TerrainGenerationSettings invalidSettings = new TerrainGenerationSettings
            {
                Width = 0
            };
            bool invalidRejected = false;
            try
            {
                fixture.Generate(invalidSettings);
            }
            catch (ArgumentException)
            {
                invalidRejected = true;
            }
            TerrainStuffPlan firstStuff = fixture.Type.DistributeStuff(first);
            TerrainStuffPlan secondStuff = fixture.Type.DistributeStuff(second);
            TerrainPlantPlan firstPlants = fixture.Type.DistributePlants(first, firstStuff);
            TerrainPlantPlan secondPlants = fixture.Type.DistributePlants(second, secondStuff);
            TerrainSurfaceResult surface = TerrainMeshGenerator.Generate(first, settings.ChunkCells,
                fixture.Type);

            try
            {
                int[] regions = new int[CountrysideRegion.Count];
                int dirtCells = 0;
                int roadOnFeatures = 0;
                int roadOnWater = 0;
                float lowestWater = 0f;
                int waterX = -1;
                int waterZ = -1;
                for (int z = 0; z < first.HeightCells; z++)
                {
                    for (int x = 0; x < first.WidthCells; x++)
                    {
                        int region = first.GetRegion(x, z);
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
                        if (region == CountrysideRegion.Water)
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
                            if (first.GetRoadType(x, z) == CountrysideRoadType.Dirt) dirtCells++;
                            if (region != CountrysideRegion.Grassland) roadOnFeatures++;
                            if (region == CountrysideRegion.Water) roadOnWater++;
                        }
                    }
                }

                for (int i = 0; i < regions.Length; i++)
                    Require(regions[i] > 0, $"Region {i} is missing.");
                Require(first.Sites.Count == 28 && sparse.Sites.Count == 4,
                    "Map recipe did not control the number of feature instances.");
                Require(regions[(int)CountrysideRegion.Grassland] > 20000 &&
                        regions[(int)CountrysideRegion.Grassland] < 70000,
                    "Grassland share is outside the experiment's broad coverage range.");
                ValidateSites(first);
                ValidateRegionConnectivity(first, regions);
                Require(lowestWater <= -0.999f && lowestWater >= -1.001f,
                    "Water floor does not reach one world unit below ground.");
                Require(invalidRejected, "Invalid terrain dimensions were accepted.");
                Require(first.Sites[0].Center != changed.Sites[0].Center,
                    "Changing the seed did not change the layout.");
                Require(dirtCells > 0, "Voronoi boundaries produced no roads.");
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

                return $"Countryside validation passed: {first.WidthCells}x{first.HeightCells}, " +
                       $"regions {string.Join("/", regions)}, roads dirt={dirtCells} " +
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
            using CountrysideFixture fixture = new CountrysideFixture();
            for (int seed = 0; seed < seedCount; seed++)
            {
                TerrainMapData map = fixture.Generate(new TerrainGenerationSettings { Seed = seed });
                int[] regions = new int[CountrysideRegion.Count];
                for (int z = 0; z < map.HeightCells; z++)
                {
                    for (int x = 0; x < map.WidthCells; x++)
                    {
                        int region = map.GetRegion(x, z);
                        regions[(int)region]++;
                    }
                }

                for (int i = 0; i < regions.Length; i++)
                    Require(regions[i] > 0, $"Seed {seed} is missing region {i}.");
                ValidateSites(map);
                ValidateRegionConnectivity(map, regions);
                ValidateRoadLayers(map);
            }

            return $"Countryside seed sweep passed: {seedCount} seeds, valid regions and layered roads.";
        }

        public static string RunSizeSweep()
        {
            int[] sizes = { 32, 64, 128, 300 };
            using CountrysideFixture fixture = new CountrysideFixture();
            fixture.Settings.BroadWarpDistance = 0f;
            fixture.Settings.DetailWarpDistance = 0f;
            for (int i = 0; i < sizes.Length; i++)
            {
                TerrainGenerationSettings settings = new TerrainGenerationSettings
                {
                    Width = sizes[i],
                    Height = sizes[i]
                };
                TerrainMapData map = fixture.Generate(settings);
                Require(map.WidthCells == sizes[i] && map.HeightCells == sizes[i],
                    "Map size was not applied.");
                ValidateSites(map);
                ValidateRoadLayers(map);
                ValidatePolygonRegions(map, fixture.Settings, settings);
            }
            return "Countryside size sweep passed: 32, 64, 128, 300.";
        }

        public static string RunVoronoiDistribution()
        {
            using CountrysideFixture fixture = new CountrysideFixture();
            CountrysideSettings settings = fixture.Settings;
            settings.BroadWarpDistance = 0f;
            settings.DetailWarpDistance = 0f;
            TerrainMapData unwarped = fixture.Generate();
            ValidatePolygonRegions(unwarped, settings, fixture.MapSettings);
            ValidateVoronoiRoadSdf(unwarped, settings.VoronoiRoadWidth,
                settings.VoronoiCornerSoftness);
            ValidateVariableVoronoiRoadWidth(unwarped, settings.VoronoiRoadWidth);
            settings.BroadWarpDistance = 18f;
            settings.DetailWarpDistance = 5f;
            TerrainMapData warped = fixture.Generate();

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
            TerrainStuffPlan stuff = fixture.Type.DistributeStuff(warped);
            TerrainPlantPlan plants = fixture.Type.DistributePlants(warped, stuff);
            TerrainSurfaceResult surface = TerrainMeshGenerator.Generate(warped,
                fixture.MapSettings.ChunkCells, fixture.Type);
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
            return $"Countryside Voronoi distribution passed: noise feature/road=" +
                $"{warpedFeatures}/{warpedRoads}.";
        }

        // ════════════════════════════════════════════════════════════
        //  方案隔离 — 非郊野 ID 验证公共构建、空配方及表现资源归属
        // ════════════════════════════════════════════════════════════

        public static string RunTypeIsolation()
        {
            TerrainIsolationTestType type = ScriptableObject.CreateInstance<TerrainIsolationTestType>();
            type.hideFlags = HideFlags.HideAndDontSave;
            GameObject parent = null;
            TerrainSurfaceResult surface = null;
            TerrainSceneRenderer renderer = null;
            try
            {
                TerrainGenerationSettings settings = new TerrainGenerationSettings
                {
                    Width = 32,
                    Height = 32,
                    Seed = 73
                };
                TerrainMapData map = TerrainDataGenerator.Generate(settings, type);
                Require(map.GetRegion(0, 0) == TerrainIsolationTestType.RegionId &&
                    map.GetHeight(0, 0) == -0.5f && map.GetOwnerSiteId(0, 0) == 0 &&
                    map.GetRegionGroupId(0, 0) == 0 && map.Sites.Count == 1 &&
                    map.RegionGroups.Count == 1,
                    "Custom terrain type did not retain its public map-builder output.");
                Require(map.GetRoadType(16, 16) == TerrainIsolationTestType.RoadId &&
                    map.GetRoadCoverage(16, 16) == 1f,
                    "Custom road ID was changed by the shared data entry point.");

                TerrainVoronoiSettings emptyProfile = new TerrainVoronoiSettings
                {
                    BaseRegion = TerrainIsolationTestType.RegionId,
                    BroadWarpDistance = 0f,
                    DetailWarpDistance = 0f
                };
                TerrainMapData emptyMap = TerrainVoronoiGenerator.Generate(settings, emptyProfile);
                TerrainPlantSettings emptyPlants = new TerrainPlantSettings();
                Require(emptyProfile.FeatureRules.Length == 0 && emptyPlants.Rules.Length == 0 &&
                    emptyMap.Sites.Count == 0 &&
                    emptyMap.GetRegion(0, 0) == TerrainIsolationTestType.RegionId &&
                    TerrainStuffDistributor.Distribute(emptyMap,
                        new TerrainStuffSettings(Array.Empty<TerrainStuffRule>())).Placements.Count == 0 &&
                    TerrainPlantDistributor.Distribute(emptyMap,
                        new TerrainStuffPlan(), emptyPlants).Placements.Count == 0,
                    "Shared generators introduced an implicit countryside recipe.");

                TerrainStuffPlan stuff = type.DistributeStuff(map);
                TerrainPlantPlan plants = type.DistributePlants(map, stuff);
                Require(stuff.Count(TerrainIsolationTestType.StuffId) == 1 &&
                    plants.Count(TerrainIsolationTestType.PlantId) == 2 &&
                    plants.Count(TerrainIsolationTestType.SecondPlantId) == 1,
                    "Sparse content IDs were not preserved by public placement plans.");
                surface = TerrainMeshGenerator.Generate(map, 16, type, 0f);
                Require(surface.Chunks.Count == 4 &&
                    ColorsMatch(surface.ColorMap.GetPixel(0, 0), type.RegionColor(TerrainIsolationTestType.RegionId)) &&
                    ColorsMatch(surface.ColorMap.GetPixel(16, 16), type.RoadColor(TerrainIsolationTestType.RoadId)),
                    "Shared mesh generation did not consume the custom type palette.");

                parent = new GameObject("Terrain Type Isolation Validation");
                parent.hideFlags = HideFlags.HideAndDontSave;
                renderer = new TerrainSceneRenderer(parent.transform, map, surface, stuff, plants,
                    type, new TerrainRenderSettings(), 16);
                Require(renderer.SurfaceChunkCount == 4 && renderer.PlantBatchCount == 3 &&
                    renderer.GeneratedRoot != null,
                    "Renderer did not support sparse plant IDs grouped by chunk and type.");
                Mesh ownedMesh = surface.Chunks[0].Mesh;
                Texture2D ownedColors = surface.ColorMap;
                Transform generatedRoot = renderer.GeneratedRoot;
                renderer.Dispose();
                renderer.Dispose();
                Require(renderer.GeneratedRoot == null && renderer.PlantBatchCount == 0,
                    "Renderer disposal did not clear its scene and plant batches.");
                if (!Application.isPlaying)
                    Require(ownedMesh == null && ownedColors == null && generatedRoot == null,
                        "Renderer did not release the surface resources it adopted.");
                return "Terrain type isolation passed: region=101, road=9, stuff=42, " +
                    "plants=70/71, empty shared recipes, chunks=4, plant batches=3, disposal valid.";
            }
            finally
            {
                renderer?.Dispose();
                surface?.Dispose();
                if (parent != null)
                    UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(type);
            }
        }

        private static bool ColorsMatch(Color first, Color second) =>
            Mathf.Abs(first.r - second.r) < 0.01f &&
            Mathf.Abs(first.g - second.g) < 0.01f &&
            Mathf.Abs(first.b - second.b) < 0.01f;

        /// <summary>
        /// 每个回归独立拥有临时郊野方案，测试参数不会写回项目中的方案资产。
        /// </summary>
        private sealed class CountrysideFixture : IDisposable
        {
            public TerrainGenerationSettings MapSettings { get; } = new TerrainGenerationSettings();
            public CountrysideTerrainType Type { get; }
            public CountrysideSettings Settings => Type.Settings;

            public CountrysideFixture()
            {
                Type = ScriptableObject.CreateInstance<CountrysideTerrainType>();
                Type.hideFlags = HideFlags.HideAndDontSave;
            }

            public TerrainMapData Generate(TerrainGenerationSettings settings = null) =>
                TerrainDataGenerator.Generate(settings ?? MapSettings, Type);

            public void Dispose()
            {
                if (Type != null)
                    UnityEngine.Object.DestroyImmediate(Type);
            }
        }

        private static void ValidateSites(TerrainMapData map)
        {
            for (int i = 0; i < map.Sites.Count; i++)
            {
                TerrainSite site = map.Sites[i];
                Require(site.Region != CountrysideRegion.Grassland,
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
            CountrysideSettings settings, TerrainGenerationSettings mapSettings)
        {
            float width = settings.VoronoiRegionWidth *
                Mathf.Min(mapSettings.Width, mapSettings.Height) / 300f;
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
                    int expected = edge > threshold ? owner.Region : CountrysideRegion.Grassland;
                    Require(map.GetRegion(x, z) == expected,
                        "Region differs from its Voronoi seed-distance threshold.");
                }
            }
        }

        private static void ValidateWidthMonotonicity()
        {
            using CountrysideFixture fixture = new CountrysideFixture();
            CountrysideSettings settings = fixture.Settings;
            settings.BroadWarpDistance = 0f;
            settings.DetailWarpDistance = 0f;
            settings.VoronoiRegionWidth = 12f;
            TerrainMapData narrowRegions = fixture.Generate();
            settings.VoronoiRegionWidth = 30f;
            TerrainMapData wideRegions = fixture.Generate();
            int expandedCells = 0;
            for (int z = 0; z < narrowRegions.HeightCells; z++)
            {
                for (int x = 0; x < narrowRegions.WidthCells; x++)
                {
                    int before = narrowRegions.GetRegion(x, z);
                    int after = wideRegions.GetRegion(x, z);
                    if (before == CountrysideRegion.Grassland && after != CountrysideRegion.Grassland)
                        expandedCells++;
                    if (before != CountrysideRegion.Grassland)
                        Require(before == after, "Increasing Voronoi region width removed a feature cell.");
                }
            }
            Require(expandedCells > 0, "Voronoi region width does not expand regions.");

            settings.VoronoiRoadWidth = 3f;
            TerrainMapData narrowRoads = fixture.Generate();
            settings.VoronoiRoadWidth = 9f;
            TerrainMapData wideRoads = fixture.Generate();
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
            using CountrysideFixture fixture = new CountrysideFixture();
            CountrysideSettings settings = fixture.Settings;
            settings.BroadWarpDistance = 0f;
            settings.DetailWarpDistance = 0f;
            settings.VoronoiCornerSoftness = 0f;
            TerrainMapData original = fixture.Generate();
            settings.VoronoiCornerSoftness = 3f;
            TerrainMapData smoothed = fixture.Generate();
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
            using CountrysideFixture fixture = new CountrysideFixture();
            CountrysideSettings settings = fixture.Settings;
            settings.BroadWarpDistance = 0f;
            settings.DetailWarpDistance = 0f;
            settings.VoronoiRegionSoftness = 0f;
            TerrainMapData original = fixture.Generate();
            settings.VoronoiRegionSoftness = 8f;
            TerrainMapData smoothed = fixture.Generate();
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
                    int region = map.GetRegion(x, z);
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
                    Require(map.GetRoadType(x, z) == CountrysideRoadType.Dirt,
                        "A road cell is not the shared dirt type.");
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
                    if (map.GetRegion(x, z) != CountrysideRegion.Water ||
                        map.GetRegion(x + 1, z) != CountrysideRegion.Grassland)
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
            bool hasReference = false;
            bool crossesRegions = false;
            Color reference = Color.clear;
            int firstRegion = CountrysideRegion.Grassland;
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    if (map.GetRoadCoverage(x, z) < 0.99f)
                        continue;
                    Require(map.GetRoadType(x, z) == CountrysideRoadType.Dirt,
                        "Full-coverage road is not dirt.");
                    Color color = colorMap.GetPixel(x, z);
                    Require(color.r > color.g && color.g > color.b && color.r < 0.85f,
                        "Road palette is not earth yellow.");
                    if (hasReference)
                    {
                        Require(Mathf.Abs(color.r - reference.r) < 0.02f &&
                                Mathf.Abs(color.g - reference.g) < 0.02f &&
                                Mathf.Abs(color.b - reference.b) < 0.02f,
                            "Road color changes with region.");
                        crossesRegions |= map.GetRegion(x, z) != firstRegion;
                    }
                    else
                    {
                        reference = color;
                        firstRegion = map.GetRegion(x, z);
                        hasReference = true;
                    }
                    dirt++;
                }
            }
            Require(dirt > 0 && crossesRegions,
                "Uniform dirt roads were not sampled across multiple regions.");
        }

        private static void ValidateStuff(TerrainMapData map, TerrainStuffPlan first, TerrainStuffPlan second)
        {
            Require(first.Placements.Count == second.Placements.Count, "Stuff count is not deterministic.");
            Require(first.Count(CountrysideStuffType.House) > 0, "No house was placed.");
            Require(first.Count(CountrysideStuffType.Windmill) == 1, "Expected one windmill.");
            Require(first.Count(CountrysideStuffType.Tree) > 0 &&
                    first.Count(CountrysideStuffType.OrchardTree) > 0,
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

        private static int RegionFor(int type)
        {
            switch (type)
            {
                case CountrysideStuffType.House: return CountrysideRegion.Village;
                case CountrysideStuffType.Tree: return CountrysideRegion.Forest;
                case CountrysideStuffType.OrchardTree: return CountrysideRegion.Orchard;
                default: return CountrysideRegion.WheatField;
            }
        }

        private static void ValidatePlants(TerrainMapData map, TerrainStuffPlan structures,
            TerrainPlantPlan first, TerrainPlantPlan second)
        {
            Require(first.Placements.Count == second.Placements.Count,
                "Plant count is not deterministic.");
            Require(first.Placements.Count > structures.Placements.Count * 20,
                "Plant distribution is not dense enough for the experiment.");
            for (int type = 0; type < CountrysidePlantType.Count; type++)
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

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    /// 仅供隔离验证的临时方案；不引用郊野内容 ID 或其生成策略。
    /// </summary>
    public sealed class TerrainIsolationTestType : TerrainTypeDefinition
    {
        public const int RegionId = 101;
        public const int RoadId = 9;
        public const int StuffId = 42;
        public const int PlantId = 70;
        public const int SecondPlantId = 71;

        public override void Validate() { }

        public override TerrainMapData GenerateMap(TerrainGenerationSettings settings)
        {
            TerrainMapBuilder builder = new TerrainMapBuilder(settings);
            TerrainMapData map = builder.Map;
            Vector2 center = map.Origin + new Vector2(map.WidthCells, map.HeightCells) * map.CellSize * 0.5f;
            builder.AddSite(new TerrainSite(0, RegionId, center, 4f));
            builder.AddRegionGroup(new TerrainRegionGroup(0, RegionId,
                map.WidthCells * map.HeightCells, center));
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    builder.SetRegion(x, z, RegionId, 0);
                    builder.SetRegionGroup(x, z, 0);
                    builder.SetHeight(x, z, -0.5f);
                }
            }
            builder.SetRoad(16, 16, 1f, RoadId);
            return map;
        }

        public override TerrainStuffPlan DistributeStuff(TerrainMapData map) =>
            new TerrainStuffPlan(new[]
            {
                new TerrainStuffPlacement(StuffId, Position(map, 4, 4), 0f, 0.25f)
            });

        public override TerrainPlantPlan DistributePlants(TerrainMapData map, TerrainStuffPlan stuff) =>
            new TerrainPlantPlan(new[]
            {
                new TerrainPlantPlacement(PlantId, RegionId, Position(map, 2, 2), 0f, 1f, 0.1f),
                new TerrainPlantPlacement(SecondPlantId, RegionId, Position(map, 3, 3), 0f, 1f, 0.1f),
                new TerrainPlantPlacement(PlantId, RegionId, Position(map, 20, 2), 0f, 1f, 0.1f)
            });

        public override Color RegionColor(int region) => region == RegionId
            ? new Color(0.1f, 0.3f, 0.7f)
            : throw new ArgumentOutOfRangeException(nameof(region));

        public override Color RoadColor(int roadType) => roadType == RoadId
            ? new Color(0.7f, 0.2f, 0.6f)
            : throw new ArgumentOutOfRangeException(nameof(roadType));

        public override TerrainStuffVisual StuffVisual(int type) => type == StuffId
            ? new TerrainStuffVisual(PrimitiveType.Cube, Vector3.one, Color.red)
            : throw new ArgumentOutOfRangeException(nameof(type));

        public override TerrainPlantVisual PlantVisual(int type) =>
            type == PlantId || type == SecondPlantId
                ? new TerrainPlantVisual(Vector3.one, Color.green)
                : throw new ArgumentOutOfRangeException(nameof(type));

        private static Vector3 Position(TerrainMapData map, int x, int z)
        {
            Vector2 center = map.CellCenter(x, z);
            return new Vector3(center.x, map.GetHeight(x, z), center.y);
        }
    }
}
