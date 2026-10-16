// ════════════════════════════════════════════════════════════
//  CityExperimentValidation — 城市路网、园区、设施与共享表现层的隔离回归
// ════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Mine.Terrain.Editor
{
    /// <summary>
    /// 使用临时城市方案执行 EditMode 验证，不修改场景、资产或构建列表。
    /// </summary>
    public static class CityExperimentValidation
    {
        private const int UrbanBase = 0;
        private const int Asphalt = 10;
        private const int Sidewalk = 11;
        private const float Tolerance = 0.0001f;

        [MenuItem("Tools/Terrain/City/Validate City Data")]
        public static void ValidateFromMenu() => Debug.Log(Run());

        [MenuItem("Tools/Terrain/City/Validate City Seeds")]
        public static void ValidateSeedsFromMenu() => Debug.Log(RunSeedSweep());

        [MenuItem("Tools/Terrain/City/Validate City Sizes")]
        public static void ValidateSizesFromMenu() => Debug.Log(RunSizeSweep());

        [MenuItem("Tools/Terrain/City/Validate City Configurations")]
        public static void ValidateConfigurationsFromMenu() => Debug.Log(RunConfigurationSweep());

        public static string Run()
        {
            RequireEditMode();
            using CityFixture fixture = new CityFixture();
            TerrainGenerationSettings settings = new TerrainGenerationSettings();
            TerrainMapData first = fixture.Generate(settings);
            TerrainMapData second = fixture.Generate(settings);
            CityLayout layout = CityLayoutGenerator.CreateForMap(first, fixture.Type.Settings);
            MapCounts counts = ValidateMap(first, layout, true);
            Require(layout.Blocks.Count == 36, "The default city must have 36 blocks.");
            CompareMaps(first, second);
            CityStuffResult detailed = CityStuffDistributor.DistributeDetailed(first, fixture.Type.Settings);
            TerrainStuffPlan repeatedStuff = fixture.Type.DistributeStuff(second);
            CompareStuff(detailed.Plan, repeatedStuff);
            ValidateStuff(first, layout, fixture.Type, detailed.Plan);
            Require(detailed.PlacedFacilities + detailed.SkippedFacilities == detailed.RequestedFacilities &&
                    detailed.PlacedTrees + detailed.SkippedTrees == detailed.RequestedTrees,
                "City distribution totals do not account for skipped facilities or trees.");
            Require(detailed.PlacedFacilities > 0, "The default city placed no facilities.");
            TerrainPlantPlan plants = fixture.Type.DistributePlants(first, detailed.Plan);
            TerrainPlantPlan repeatedPlants = fixture.Type.DistributePlants(second, repeatedStuff);
            ComparePlants(plants, repeatedPlants);
            Require(plants.Placements.Count > 0, "The default city placed no green-area plants.");
            ValidatePlants(first, layout, fixture.Type, detailed.Plan, plants);
            int batches = ValidatePresentation(first, detailed.Plan, plants, fixture.Type, settings);

            return $"City validation passed: {first.WidthCells}x{first.HeightCells}, " +
                   $"blocks={layout.Blocks.Count}, subtypes={counts.Subtypes}, " +
                   $"roads asphalt/sidewalk={counts.AsphaltCells}/{counts.SidewalkCells}, " +
                   $"facilities={detailed.PlacedFacilities}/{detailed.RequestedFacilities}, " +
                   $"trees={detailed.PlacedTrees}/{detailed.RequestedTrees}, " +
                   $"plants={plants.Placements.Count}, chunks=36, batches={batches}.";
        }

        public static string RunSeedSweep()
        {
            RequireEditMode();
            using CityFixture fixture = new CityFixture();
            int[] seeds = { 0, 1, 7, 42, 103, 211, 409, 853, 1009, 2026 };
            TerrainMapData reference = null;
            bool changed = false;
            int facilities = 0;
            int plants = 0;
            for (int i = 0; i < seeds.Length; i++)
            {
                TerrainGenerationSettings settings = new TerrainGenerationSettings { Seed = seeds[i] };
                TerrainMapData map = fixture.Generate(settings);
                CityLayout layout = CityLayoutGenerator.CreateForMap(map, fixture.Type.Settings);
                ValidateMap(map, layout, true);
                TerrainStuffPlan stuff = fixture.Type.DistributeStuff(map);
                TerrainPlantPlan plantPlan = fixture.Type.DistributePlants(map, stuff);
                ValidateStuff(map, layout, fixture.Type, stuff);
                ValidatePlants(map, layout, fixture.Type, stuff, plantPlan);
                facilities += stuff.Placements.Count;
                plants += plantPlan.Placements.Count;
                if (reference != null)
                    changed |= HasDifferentRegions(reference, map);
                else
                    reference = map;
            }
            Require(changed, "Changing the city seed did not change any district assignment.");
            return $"City seed sweep passed: {seeds.Length} seeds, " +
                   $"stuff={facilities}, plants={plants}.";
        }

        public static string RunSizeSweep()
        {
            RequireEditMode();
            using CityFixture fixture = new CityFixture();
            bool rejected = false;
            try
            {
                fixture.Generate(new TerrainGenerationSettings { Width = 32, Height = 32 });
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Require(rejected, "32x32 must reject the default minimum block and boundary roads.");
            int[] widths = { 64, 128, 300, 128 };
            int[] heights = { 64, 128, 300, 256 };
            List<string> summaries = new List<string>();
            for (int i = 0; i < widths.Length; i++)
            {
                TerrainGenerationSettings settings = new TerrainGenerationSettings
                {
                    Width = widths[i], Height = heights[i], Seed = 42
                };
                TerrainMapData map = fixture.Generate(settings);
                CityLayout layout = CityLayoutGenerator.CreateForMap(map, fixture.Type.Settings);
                ValidateMap(map, layout, false);
                TerrainStuffPlan stuff = fixture.Type.DistributeStuff(map);
                TerrainPlantPlan plants = fixture.Type.DistributePlants(map, stuff);
                ValidateStuff(map, layout, fixture.Type, stuff);
                ValidatePlants(map, layout, fixture.Type, stuff, plants);
                using TerrainSurfaceResult surface = TerrainMeshGenerator.Generate(map,
                    settings.ChunkCells, fixture.Type);
                int expected = Mathf.CeilToInt(map.WidthCells / (float)settings.ChunkCells) *
                               Mathf.CeilToInt(map.HeightCells / (float)settings.ChunkCells);
                Require(surface.Chunks.Count == expected, "City size sweep has an incorrect chunk count.");
                summaries.Add($"{widths[i]}x{heights[i]}:{layout.Grid.Columns}x{layout.Grid.Rows} blocks");
            }
            return $"City size sweep passed: 32x32 rejected; {string.Join(", ", summaries)}.";
        }

        public static string RunConfigurationSweep()
        {
            RequireEditMode();
            ValidateRestrictedRules(CityZone.Residential,
                new[] { CitySubtype.Villa, CitySubtype.Slum }, false);
            ValidateRestrictedRules(CityZone.Green, new[] { CitySubtype.Park }, true);
            using (CityFixture fixture = new CityFixture())
            {
                fixture.Type.Settings.Grid.SidewalkWidth = 0f;
                TerrainMapData map = fixture.Generate(new TerrainGenerationSettings());
                CityLayout layout = CityLayoutGenerator.CreateForMap(map, fixture.Type.Settings);
                MapCounts counts = ValidateMap(map, layout, true);
                Require(counts.SidewalkCells == 0, "Disabled sidewalks still generate sidewalk cells.");
                TerrainStuffPlan stuff = fixture.Type.DistributeStuff(map);
                ValidateStuff(map, layout, fixture.Type, stuff);
                ValidatePlants(map, layout, fixture.Type, stuff, fixture.Type.DistributePlants(map, stuff));
            }
            using (CityFixture fixture = new CityFixture())
            {
                fixture.Type.Settings.Grid.StreetWidth = 0.25f;
                RequireRejected(() => fixture.Generate(new TerrainGenerationSettings()),
                    "Sub-cell city streets must reject insufficient sampling precision.");
            }
            using (CityFixture fixture = new CityFixture())
            {
                fixture.Type.Settings.Grid.SidewalkWidth = 0.25f;
                RequireRejected(() => fixture.Generate(new TerrainGenerationSettings()),
                    "Sub-cell city sidewalks must reject insufficient sampling precision.");
            }
            using (CityFixture fixture = new CityFixture())
            {
                TerrainGenerationSettings settings = new TerrainGenerationSettings { Seed = 7 };
                typeof(TerrainGenerationSettings).GetField("_origin", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(settings, new Vector2(-120.5f, 37.25f));
                typeof(TerrainGenerationSettings).GetField("_cellSize", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(settings, 2f);
                fixture.Type.Settings.Grid.MainRoadEvery = 0;
                TerrainMapData map = fixture.Generate(settings);
                CityLayout layout = CityLayoutGenerator.CreateForMap(map, fixture.Type.Settings);
                ValidateMap(map, layout, true);
                TerrainStuffPlan stuff = fixture.Type.DistributeStuff(map);
                ValidateStuff(map, layout, fixture.Type, stuff);
                ValidatePlants(map, layout, fixture.Type, stuff, fixture.Type.DistributePlants(map, stuff));
            }
            return "City configuration sweep passed: residential-only, park-only, " +
                   "no sidewalks, rejected sub-cell street/sidewalk, shifted origin with 2-unit cells.";
        }

        private static void ValidateRestrictedRules(CityZone zone, CitySubtype[] subtypes, bool green)
        {
            using CityFixture fixture = new CityFixture();
            SerializedObject serialized = new SerializedObject(fixture.Type);
            SerializedProperty settings = serialized.FindProperty("_settings");
            SerializedProperty zones = settings.FindPropertyRelative("_zones");
            zones.arraySize = 1;
            zones.GetArrayElementAtIndex(0).FindPropertyRelative("_zone").intValue = (int)zone;
            zones.GetArrayElementAtIndex(0).FindPropertyRelative("_weight").floatValue = 1f;
            SerializedProperty rules = settings.FindPropertyRelative("_subtypes");
            rules.arraySize = subtypes.Length;
            for (int i = 0; i < subtypes.Length; i++)
            {
                SerializedProperty rule = rules.GetArrayElementAtIndex(i);
                rule.FindPropertyRelative("_subtype").intValue = (int)subtypes[i];
                rule.FindPropertyRelative("_weight").floatValue = 1f;
                rule.FindPropertyRelative("_maxFacilitiesPerBlock").intValue = 1;
                rule.FindPropertyRelative("_treesPerBlock").intValue = green ? 3 : 0;
                rule.FindPropertyRelative("_grassDensity").floatValue = green ? 0.3f : 0f;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            TerrainMapData map = fixture.Generate(new TerrainGenerationSettings());
            CityLayout layout = CityLayoutGenerator.CreateForMap(map, fixture.Type.Settings);
            MapCounts counts = ValidateMap(map, layout, false);
            Require(counts.Subtypes == subtypes.Length, "A restricted city scheme has incorrect subtype quotas.");
            for (int i = 0; i < layout.Blocks.Count; i++)
                Require(layout.Blocks[i].Zone == zone, "A disabled zone still occurs in a restricted city scheme.");
            TerrainStuffPlan stuff = fixture.Type.DistributeStuff(map);
            TerrainPlantPlan plants = fixture.Type.DistributePlants(map, stuff);
            ValidateStuff(map, layout, fixture.Type, stuff);
            ValidatePlants(map, layout, fixture.Type, stuff, plants);
            Require((plants.Placements.Count > 0) == green,
                "A restricted city scheme has incorrect green-area plant output.");
        }

        // ════════════════════════════════════════════════════════════
        //  地图契约 — 每个逻辑格属于道路或一个街区，道路与街区编号互不混用
        // ════════════════════════════════════════════════════════════

        private static MapCounts ValidateMap(TerrainMapData map, CityLayout layout, bool allSubtypes)
        {
            Require(layout.Blocks.Count == layout.Grid.Columns * layout.Grid.Rows,
                "City blocks do not match the rectangular grid.");
            HashSet<int> subtypes = new HashSet<int>();
            for (int i = 0; i < layout.Blocks.Count; i++)
            {
                CityBlock block = layout.Blocks[i];
                Require(block.Id == i && CityContent.KnownRegion(block.Region),
                    "City block IDs or subtype IDs are invalid.");
                Require(CityContent.ZoneFor(block.Region) == block.Zone,
                    "City subtype disagrees with its parent zone.");
                Require(block.Bounds.width > 0f && block.Bounds.height > 0f,
                    "A city block has no usable extent.");
                subtypes.Add(block.Region);
            }
            if (allSubtypes)
                Require(subtypes.Count == Enum.GetValues(typeof(CitySubtype)).Length,
                    "A default-city subtype is missing.");

            int asphalt = 0;
            int sidewalk = 0;
            int[] groupCells = new int[map.RegionGroups.Count];
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    Vector2 point = map.CellCenter(x, z);
                    Require(map.GetHeight(x, z) == 0f, "City ground must remain flat in this prototype.");
                    bool road = layout.Grid.TrySampleRoad(point, out bool carriageway);
                    float coverage = map.GetRoadCoverage(x, z);
                    if (road)
                    {
                        int expected = carriageway ? Asphalt : Sidewalk;
                        Require(coverage == 1f && map.GetRoadType(x, z) == expected,
                            "City road raster differs from its grid layout.");
                        Require(map.GetRegion(x, z) == UrbanBase && map.GetOwnerSiteId(x, z) == -1 &&
                                map.GetRegionGroupId(x, z) == -1,
                            "Road corridors incorrectly consume a district owner or group.");
                        if (carriageway) asphalt++; else sidewalk++;
                        continue;
                    }
                    int blockId = layout.Grid.FindBlockIndex(point);
                    Require(blockId >= 0 && blockId < layout.Blocks.Count,
                        "A city cell belongs to neither a block nor a road.");
                    CityBlock block = layout.Blocks[blockId];
                    int owner = map.GetOwnerSiteId(x, z);
                    int group = map.GetRegionGroupId(x, z);
                    Require(coverage == 0f && map.GetRoadType(x, z) == TerrainRoadType.None &&
                            map.GetRegion(x, z) == block.Region,
                        "City district cells have an incorrect subtype or road overlay.");
                    Require(owner >= 0 && owner < map.Sites.Count && group >= 0 &&
                            group < map.RegionGroups.Count, "District cells have an invalid owner or group.");
                    Require(map.Sites[owner].Region == block.Region &&
                            (map.Sites[owner].Center - block.Bounds.center).sqrMagnitude < Tolerance &&
                            map.RegionGroups[group].Region == block.Region,
                        "District site/group metadata disagrees with its block.");
                    groupCells[group]++;
                }
            }
            for (int i = 0; i < groupCells.Length; i++)
                Require(map.RegionGroups[i].Id == i && groupCells[i] == map.RegionGroups[i].CellCount &&
                        groupCells[i] > 0, "City group IDs or cell counts are inconsistent.");
            bool hasSidewalk = layout.Grid.XRoads[0].CarMin > layout.Grid.XRoads[0].Min;
            Require(asphalt > 0 && (hasSidewalk ? sidewalk > 0 : sidewalk == 0),
                "The city's carriageway or enabled sidewalk output is incorrect.");
            ValidateRoadConnectivity(map, layout.Grid, asphalt);
            return new MapCounts(asphalt, sidewalk, subtypes.Count);
        }

        private static void ValidateRoadConnectivity(TerrainMapData map,
            TerrainGridRoadLayout grid, int expected)
        {
            bool[] visited = new bool[map.WidthCells * map.HeightCells];
            Queue<int> queue = new Queue<int>();
            int start = -1;
            for (int z = 0; z < map.HeightCells && start < 0; z++)
                for (int x = 0; x < map.WidthCells && start < 0; x++)
                    if (map.GetRoadType(x, z) == Asphalt)
                        start = x + z * map.WidthCells;
            Require(start >= 0, "The city has no carriageway cell.");
            visited[start] = true;
            queue.Enqueue(start);
            int reached = 0;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int x = current % map.WidthCells;
                int z = current / map.WidthCells;
                reached++;
                Visit(x - 1, z);
                Visit(x + 1, z);
                Visit(x, z - 1);
                Visit(x, z + 1);
            }
            Require(reached == expected, "The city's carriageway network is disconnected.");
            for (int z = 0; z < grid.ZRoads.Count; z++)
            {
                for (int x = 0; x < grid.XRoads.Count; x++)
                {
                    float centerX = (grid.XRoads[x].CarMin + grid.XRoads[x].CarMax) * 0.5f;
                    float centerZ = (grid.ZRoads[z].CarMin + grid.ZRoads[z].CarMax) * 0.5f;
                    int cellX = Mathf.FloorToInt((centerX - map.Origin.x) / map.CellSize);
                    int cellZ = Mathf.FloorToInt((centerZ - map.Origin.y) / map.CellSize);
                    Require(map.GetRoadType(cellX, cellZ) == Asphalt,
                        "An orthogonal road intersection is missing its carriageway.");
                }
            }

            void Visit(int x, int z)
            {
                if (x < 0 || z < 0 || x >= map.WidthCells || z >= map.HeightCells)
                    return;
                int index = x + z * map.WidthCells;
                if (visited[index] || map.GetRoadType(x, z) != Asphalt)
                    return;
                visited[index] = true;
                queue.Enqueue(index);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  设施占地 — 按表现矩形检查街区边界、道路与同层设施，不以中心点代替占地
        // ════════════════════════════════════════════════════════════

        private static void ValidateStuff(TerrainMapData map, CityLayout layout,
            CityTerrainType type, TerrainStuffPlan plan)
        {
            Rect[] footprints = new Rect[plan.Placements.Count];
            for (int i = 0; i < plan.Placements.Count; i++)
            {
                TerrainStuffPlacement placement = plan.Placements[i];
                Require(Mathf.Abs(placement.Yaw / 90f - Mathf.Round(placement.Yaw / 90f)) < Tolerance,
                    "City facilities are not aligned to the orthogonal grid.");
                Vector2 center = new Vector2(placement.Position.x, placement.Position.z);
                int blockId = layout.Grid.FindBlockIndex(center);
                Require(blockId >= 0, "A city facility center lies outside the blocks.");
                TerrainStuffVisual visual = type.StuffVisual(placement.Type);
                bool quarterTurn = (Mathf.RoundToInt(placement.Yaw / 90f) & 1) != 0;
                Vector2 size = quarterTurn ? new Vector2(visual.Scale.z, visual.Scale.x) :
                    new Vector2(visual.Scale.x, visual.Scale.z);
                Rect footprint = new Rect(center - size * 0.5f, size);
                Rect bounds = layout.Blocks[blockId].Bounds;
                Require(Contains(bounds, footprint), "A complete facility footprint crosses its block boundary.");
                Require(placement.Radius + Tolerance >= size.magnitude * 0.5f,
                    "Facility radius does not conservatively contain its rendered rectangle.");
                ValidateClearRect(map, footprint, layout.Blocks[blockId].Region);
                for (int j = 0; j < i; j++)
                    Require(!footprint.Overlaps(footprints[j]), "Rendered city facility rectangles overlap.");
                footprints[i] = footprint;
            }
        }

        private static void ValidateClearRect(TerrainMapData map, Rect rectangle, int region)
        {
            int minX = Mathf.FloorToInt((rectangle.xMin - map.Origin.x) / map.CellSize);
            int maxX = Mathf.FloorToInt((rectangle.xMax - map.Origin.x - Tolerance) / map.CellSize);
            int minZ = Mathf.FloorToInt((rectangle.yMin - map.Origin.y) / map.CellSize);
            int maxZ = Mathf.FloorToInt((rectangle.yMax - map.Origin.y - Tolerance) / map.CellSize);
            Require(minX >= 0 && minZ >= 0 && maxX < map.WidthCells && maxZ < map.HeightCells,
                "A complete facility footprint crosses the map boundary.");
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                    Require(map.GetRoadCoverage(x, z) <= 0.08f && map.GetRegion(x, z) == region,
                        "A complete facility footprint overlaps a road or another subtype.");
        }

        // ════════════════════════════════════════════════════════════
        //  密集地被 — 只消费绿化用途，完整圆占地避让道路与设施的保守外接圆
        // ════════════════════════════════════════════════════════════

        private static void ValidatePlants(TerrainMapData map, CityLayout layout, CityTerrainType type,
            TerrainStuffPlan stuff, TerrainPlantPlan plants)
        {
            ValidateEntrances(layout, type, stuff, plants);
            for (int i = 0; i < plants.Placements.Count; i++)
            {
                TerrainPlantPlacement plant = plants.Placements[i];
                Require(CityContent.ZoneFor(plant.Region) == CityZone.Green,
                    "City dense plants were generated outside a green zone.");
                Vector2 point = new Vector2(plant.Position.x, plant.Position.z);
                ValidateClearCircle(map, point, plant.Radius, plant.Region);
                for (int j = 0; j < stuff.Placements.Count; j++)
                {
                    TerrainStuffPlacement other = stuff.Placements[j];
                    Vector2 center = new Vector2(other.Position.x, other.Position.z);
                    float clearance = plant.Radius + other.Radius + 0.25f;
                    Require((point - center).sqrMagnitude + Tolerance >= clearance * clearance,
                        "City plants overlap a conservative facility footprint.");
                }
            }
        }

        private static void ValidateEntrances(CityLayout layout, CityTerrainType type,
            TerrainStuffPlan stuff, TerrainPlantPlan plants)
        {
            for (int i = 0; i < stuff.Placements.Count; i++)
            {
                TerrainStuffPlacement facility = stuff.Placements[i];
                if (facility.Type == CityStuffType.Tree)
                    continue;
                Vector2 center = new Vector2(facility.Position.x, facility.Position.z);
                Rect block = layout.Blocks[layout.Grid.FindBlockIndex(center)].Bounds;
                Vector3 size = type.StuffVisual(facility.Type).Scale;
                Rect entrance = Entrance(center, size, facility.Yaw, block);
                Vector2 farthest = new Vector2(
                    Mathf.Max(Mathf.Abs(entrance.xMin - center.x), Mathf.Abs(entrance.xMax - center.x)),
                    Mathf.Max(Mathf.Abs(entrance.yMin - center.y), Mathf.Abs(entrance.yMax - center.y)));
                Require(facility.Radius + Tolerance >= farthest.magnitude,
                    "A facility's vegetation exclusion radius does not contain its entrance.");
                for (int j = 0; j < stuff.Placements.Count; j++)
                {
                    if (j == i)
                        continue;
                    TerrainStuffPlacement other = stuff.Placements[j];
                    Vector3 scale = type.StuffVisual(other.Type).Scale;
                    bool quarterTurn = (Mathf.RoundToInt(other.Yaw / 90f) & 1) != 0;
                    Vector2 footprintSize = quarterTurn ? new Vector2(scale.z, scale.x) :
                        new Vector2(scale.x, scale.z);
                    Vector2 position = new Vector2(other.Position.x, other.Position.z);
                    Rect footprint = new Rect(position - footprintSize * 0.5f, footprintSize);
                    Require(!entrance.Overlaps(footprint),
                        "A facility or tree obstructs a reserved city entrance.");
                }
                for (int j = 0; j < plants.Placements.Count; j++)
                {
                    TerrainPlantPlacement plant = plants.Placements[j];
                    Vector2 point = new Vector2(plant.Position.x, plant.Position.z);
                    Vector2 nearest = new Vector2(Mathf.Clamp(point.x, entrance.xMin, entrance.xMax),
                        Mathf.Clamp(point.y, entrance.yMin, entrance.yMax));
                    Require((point - nearest).sqrMagnitude + Tolerance >= plant.Radius * plant.Radius,
                        "A city's dense plant obstructs a reserved facility entrance.");
                }
            }
        }

        private static Rect Entrance(Vector2 center, Vector3 size, float yaw, Rect block)
        {
            const float EntryWidth = 2.5f;
            float halfEntry = Mathf.Min(EntryWidth, size.x) * 0.5f;
            switch (Mathf.RoundToInt(yaw / 90f) & 3)
            {
                case 0:
                    return Rect.MinMaxRect(center.x - halfEntry, center.y + size.z * 0.5f,
                        center.x + halfEntry, block.yMax);
                case 1:
                    return Rect.MinMaxRect(center.x + size.z * 0.5f, center.y - halfEntry,
                        block.xMax, center.y + halfEntry);
                case 2:
                    return Rect.MinMaxRect(center.x - halfEntry, block.yMin,
                        center.x + halfEntry, center.y - size.z * 0.5f);
                default:
                    return Rect.MinMaxRect(block.xMin, center.y - halfEntry,
                        center.x - size.z * 0.5f, center.y + halfEntry);
            }
        }

        private static void ValidateClearCircle(TerrainMapData map, Vector2 point, float radius, int region)
        {
            int minX = Mathf.FloorToInt((point.x - radius - map.Origin.x) / map.CellSize);
            int maxX = Mathf.FloorToInt((point.x + radius - map.Origin.x) / map.CellSize);
            int minZ = Mathf.FloorToInt((point.y - radius - map.Origin.y) / map.CellSize);
            int maxZ = Mathf.FloorToInt((point.y + radius - map.Origin.y) / map.CellSize);
            Require(minX >= 0 && minZ >= 0 && maxX < map.WidthCells && maxZ < map.HeightCells,
                "A city's dense plant crosses the map boundary.");
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float left = map.Origin.x + x * map.CellSize;
                    float bottom = map.Origin.y + z * map.CellSize;
                    Vector2 nearest = new Vector2(Mathf.Clamp(point.x, left, left + map.CellSize),
                        Mathf.Clamp(point.y, bottom, bottom + map.CellSize));
                    if ((point - nearest).sqrMagnitude > radius * radius)
                        continue;
                    Require(map.GetRoadCoverage(x, z) <= 0.08f && map.GetRegion(x, z) == region,
                        "A complete plant footprint overlaps a road or another subtype.");
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  表现消费 — 在 PreviewScene 创建通用 Renderer，验证纹理、网格与释放
        // ════════════════════════════════════════════════════════════

        private static int ValidatePresentation(TerrainMapData map, TerrainStuffPlan stuff,
            TerrainPlantPlan plants, CityTerrainType type, TerrainGenerationSettings settings)
        {
            TerrainSurfaceResult surface = TerrainMeshGenerator.Generate(map, settings.ChunkCells, type);
            TerrainSceneRenderer renderer = null;
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject parent = new GameObject("City Validation Preview") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(parent, preview);
            try
            {
                Require(surface.Chunks.Count == 36 && surface.ColorMap.width == map.WidthCells &&
                        surface.ColorMap.height == map.HeightCells, "City surface dimensions are incorrect.");
                ValidateRoadColors(map, surface.ColorMap, type);
                for (int i = 0; i < surface.Chunks.Count; i++)
                    foreach (Vector3 vertex in surface.Chunks[i].Mesh.vertices)
                        Require(vertex.y == 0f, "City surface mesh is not flat.");
                Shader shader = Shader.Find("TerrainSurface/TerrainSurface");
                Require(shader != null && shader.isSupported && ShaderUtil.GetShaderMessages(shader).Length == 0,
                    "The shared terrain shader is unavailable or reports compilation messages.");
                renderer = new TerrainSceneRenderer(parent.transform, map, surface, stuff, plants,
                    type, new TerrainRenderSettings(), settings.ChunkCells);
                Require(renderer.GeneratedRoot != null && renderer.SurfaceChunkCount == 36 &&
                        renderer.PlantBatchCount > 0, "The shared renderer did not consume city plans.");
                int batches = renderer.PlantBatchCount;
                Transform generated = renderer.GeneratedRoot;
                renderer.Draw(0);
                renderer.Dispose();
                renderer.Dispose();
                Require(generated == null && surface.ColorMap == null, "City renderer did not release its owned resources.");
                for (int i = 0; i < surface.Chunks.Count; i++)
                    Require(surface.Chunks[i].Mesh == null, "City renderer leaked a surface mesh.");
                return batches;
            }
            finally
            {
                renderer?.Dispose();
                surface.Dispose();
                UnityEngine.Object.DestroyImmediate(parent);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static void ValidateRoadColors(TerrainMapData map, Texture2D colors, CityTerrainType type)
        {
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    if (map.GetRoadCoverage(x, z) != 1f)
                        continue;
                    Color expected = type.RoadColor(map.GetRoadType(x, z));
                    Color actual = colors.GetPixel(x, z);
                    Require(Mathf.Abs(expected.r - actual.r) < 0.005f &&
                            Mathf.Abs(expected.g - actual.g) < 0.005f &&
                            Mathf.Abs(expected.b - actual.b) < 0.005f,
                        "City road colors were not consumed by the shared surface generator.");
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  确定性与临时资源 — 对比完整输出，始终销毁无持久资产的测试方案
        // ════════════════════════════════════════════════════════════

        private static void CompareMaps(TerrainMapData first, TerrainMapData second)
        {
            Require(first.WidthCells == second.WidthCells && first.HeightCells == second.HeightCells &&
                    first.CellSize == second.CellSize && first.Origin == second.Origin &&
                    first.Seed == second.Seed &&
                    first.Sites.Count == second.Sites.Count && first.RegionGroups.Count == second.RegionGroups.Count,
                "Repeated city map dimensions or metadata counts differ.");
            for (int i = 0; i < first.Sites.Count; i++)
            {
                TerrainSite a = first.Sites[i];
                TerrainSite b = second.Sites[i];
                Require(a.Id == b.Id && a.Region == b.Region && a.Center == b.Center && a.Radius == b.Radius,
                    "City site metadata is not deterministic.");
            }
            for (int i = 0; i < first.RegionGroups.Count; i++)
            {
                TerrainRegionGroup a = first.RegionGroups[i];
                TerrainRegionGroup b = second.RegionGroups[i];
                Require(a.Id == b.Id && a.Region == b.Region && a.Center == b.Center && a.CellCount == b.CellCount,
                    "City group metadata is not deterministic.");
            }
            for (int z = 0; z < first.HeightCells; z++)
                for (int x = 0; x < first.WidthCells; x++)
                    Require(first.GetRegion(x, z) == second.GetRegion(x, z) &&
                            first.GetOwnerSiteId(x, z) == second.GetOwnerSiteId(x, z) &&
                            first.GetRegionGroupId(x, z) == second.GetRegionGroupId(x, z) &&
                            first.GetRoadType(x, z) == second.GetRoadType(x, z) &&
                            first.GetRoadCoverage(x, z) == second.GetRoadCoverage(x, z) &&
                            first.GetHeight(x, z) == second.GetHeight(x, z),
                        "City map generation is not deterministic.");
        }

        private static bool HasDifferentRegions(TerrainMapData first, TerrainMapData second)
        {
            for (int z = 0; z < first.HeightCells; z++)
                for (int x = 0; x < first.WidthCells; x++)
                    if (first.GetRegion(x, z) != second.GetRegion(x, z))
                        return true;
            return false;
        }

        private static void CompareStuff(TerrainStuffPlan first, TerrainStuffPlan second)
        {
            Require(first.Placements.Count == second.Placements.Count, "Repeated city stuff counts differ.");
            for (int i = 0; i < first.Placements.Count; i++)
            {
                TerrainStuffPlacement a = first.Placements[i];
                TerrainStuffPlacement b = second.Placements[i];
                Require(a.Type == b.Type && a.Position == b.Position && a.Yaw == b.Yaw && a.Radius == b.Radius,
                    "City stuff generation is not deterministic.");
            }
        }

        private static void ComparePlants(TerrainPlantPlan first, TerrainPlantPlan second)
        {
            Require(first.Placements.Count == second.Placements.Count, "Repeated city plant counts differ.");
            for (int i = 0; i < first.Placements.Count; i++)
            {
                TerrainPlantPlacement a = first.Placements[i];
                TerrainPlantPlacement b = second.Placements[i];
                Require(a.Type == b.Type && a.Region == b.Region && a.Position == b.Position &&
                        a.Yaw == b.Yaw && a.Scale == b.Scale && a.Radius == b.Radius,
                    "City plant generation is not deterministic.");
            }
        }

        private static bool Contains(Rect outer, Rect inner) =>
            inner.xMin + Tolerance >= outer.xMin && inner.yMin + Tolerance >= outer.yMin &&
            inner.xMax <= outer.xMax + Tolerance && inner.yMax <= outer.yMax + Tolerance;

        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run city data validation outside Play Mode.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void RequireRejected(Action action, string message)
        {
            bool rejected = false;
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Require(rejected, message);
        }

        private readonly struct MapCounts
        {
            public readonly int AsphaltCells;
            public readonly int SidewalkCells;
            public readonly int Subtypes;

            public MapCounts(int asphalt, int sidewalk, int subtypes)
            {
                AsphaltCells = asphalt;
                SidewalkCells = sidewalk;
                Subtypes = subtypes;
            }
        }

        private sealed class CityFixture : IDisposable
        {
            public CityTerrainType Type { get; } = ScriptableObject.CreateInstance<CityTerrainType>();

            public TerrainMapData Generate(TerrainGenerationSettings settings) =>
                TerrainDataGenerator.Generate(settings, Type);

            public void Dispose() => UnityEngine.Object.DestroyImmediate(Type);
        }
    }
}
