// ════════════════════════════════════════════════════════════
//  TerrainMapData — 俯视地图的逻辑网格、地点和道路数据契约
// ════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Mine.Terrain
{
    public enum TerrainRegion
    {
        Grassland,
        Village,
        WheatField,
        Forest,
        Water,
        Meadow,
        Orchard
    }

    public enum TerrainRoadType
    {
        None,
        Dirt,
        Stone
    }

    [Serializable]
    public sealed class TerrainFeatureRule
    {
        [SerializeField] private TerrainRegion _region;
        [SerializeField, Min(1)] private int _sitesAtReferenceSize;
        [SerializeField, Min(1f)] private float _minRadiusAtReferenceSize;
        [SerializeField, Min(1f)] private float _maxRadiusAtReferenceSize;
        [SerializeField] private bool _connectRoad;

        public TerrainRegion Region => _region;
        public int SitesAtReferenceSize => _sitesAtReferenceSize;
        public float MinRadiusAtReferenceSize => _minRadiusAtReferenceSize;
        public float MaxRadiusAtReferenceSize => _maxRadiusAtReferenceSize;
        public bool ConnectRoad => _connectRoad;

        public TerrainFeatureRule(TerrainRegion region, int sites, float minRadius, float maxRadius,
            bool connectRoad)
        {
            _region = region;
            _sitesAtReferenceSize = sites;
            _minRadiusAtReferenceSize = minRadius;
            _maxRadiusAtReferenceSize = maxRadius;
            _connectRoad = connectRoad;
        }
    }

    [Serializable]
    public sealed class TerrainGenerationSettings
    {
        [Header("Map")]
        [SerializeField, Min(32)] private int _width = 300;
        [SerializeField, Min(32)] private int _height = 300;
        [SerializeField, Min(0.25f)] private float _cellSize = 1f;
        [SerializeField, Min(8)] private int _chunkCells = 50;
        [SerializeField] private Vector2 _origin = Vector2.zero;
        [SerializeField] private int _seed = 42;

        [Header("Final Data Warp (300-unit reference)")]
        [SerializeField, Range(3f, 9f), Tooltip("Broad noise cycles across the map's short side.")]
        private float _broadWarpCycles = 7f;
        [SerializeField, Range(0f, 20f), Tooltip("Broad displacement in world units on a 300-unit map.")]
        private float _broadWarpDistance = 18f;
        [SerializeField, Range(8f, 24f), Tooltip("Detail noise cycles across the map's short side.")]
        private float _detailWarpCycles = 20f;
        [SerializeField, Range(0f, 6f), Tooltip("Detail displacement in world units on a 300-unit map.")]
        private float _detailWarpDistance = 5f;

        [Header("Features")]
        [SerializeField, HideInInspector, Range(1f, 60f), Tooltip("Expansion from each seed's center distance gap on a 300-unit map; larger values cover more of the cell.")]
        private float _voronoiRegionWidth = 22f;
        [SerializeField, HideInInspector, Range(0f, 8f), Tooltip("Smooths Voronoi region corners without changing nearest-site ownership; zero keeps the original distance gap.")]
        private float _voronoiRegionSoftness = 3f;
        [SerializeField, HideInInspector, Range(1f, 20f), Tooltip("Nearest/second-nearest seed distance-gap threshold for variable-width Voronoi roads.")]
        private float _voronoiRoadWidth = 5f;
        [FormerlySerializedAs("_voronoiWaistStrength")]
        [SerializeField, HideInInspector, Range(0f, 8f), Tooltip("Smooths the distance field near Voronoi road junctions; zero restores the original distance gap.")]
        private float _voronoiCornerSoftness = 3f;
        [SerializeField, Min(0f)] private float _depressionDepth = 1f;
        [SerializeField, Min(0)] private int _houseCount = 10;
        [SerializeField, Min(0)] private int _treeCount = 90;
        [SerializeField, HideInInspector, Min(0)] private int _grassCount = 160;
        [SerializeField, HideInInspector, Min(0)] private int _wheatCount = 140;
        [SerializeField] private TerrainFeatureRule[] _featureRules =
        {
            new TerrainFeatureRule(TerrainRegion.Village, 3, 17f, 22f, true),
            new TerrainFeatureRule(TerrainRegion.WheatField, 7, 22f, 32f, true),
            new TerrainFeatureRule(TerrainRegion.Forest, 8, 23f, 32f, true),
            new TerrainFeatureRule(TerrainRegion.Water, 4, 13f, 19f, false),
            new TerrainFeatureRule(TerrainRegion.Meadow, 3, 15f, 23f, false),
            new TerrainFeatureRule(TerrainRegion.Orchard, 3, 16f, 23f, true)
        };

        public int Width
        {
            get => _width;
            set => _width = value;
        }
        public int Height
        {
            get => _height;
            set => _height = value;
        }
        public float CellSize => _cellSize;
        public int ChunkCells => _chunkCells;
        public Vector2 Origin => _origin;
        public int Seed
        {
            get => _seed;
            set => _seed = value;
        }
        public float BroadWarpCycles
        {
            get => _broadWarpCycles;
            set => _broadWarpCycles = value;
        }
        public float BroadWarpDistance
        {
            get => _broadWarpDistance;
            set => _broadWarpDistance = value;
        }
        public float DetailWarpCycles
        {
            get => _detailWarpCycles;
            set => _detailWarpCycles = value;
        }
        public float DetailWarpDistance
        {
            get => _detailWarpDistance;
            set => _detailWarpDistance = value;
        }
        public float VoronoiRegionWidth
        {
            get => _voronoiRegionWidth;
            set => _voronoiRegionWidth = value;
        }
        public float VoronoiRegionSoftness
        {
            get => _voronoiRegionSoftness;
            set => _voronoiRegionSoftness = value;
        }
        public float VoronoiRoadWidth
        {
            get => _voronoiRoadWidth;
            set => _voronoiRoadWidth = value;
        }
        public float VoronoiCornerSoftness
        {
            get => _voronoiCornerSoftness;
            set => _voronoiCornerSoftness = value;
        }
        public float DepressionDepth => _depressionDepth;
        public int HouseCount => _houseCount;
        public int TreeCount => _treeCount;
        public int GrassCount => _grassCount;
        public int WheatCount => _wheatCount;
        public TerrainFeatureRule[] FeatureRules
        {
            get => _featureRules;
            set => _featureRules = value;
        }

        public void Validate()
        {
            if (_width < 32 || _height < 32 || _cellSize < 0.25f || _chunkCells < 8)
                throw new ArgumentException("Terrain size, cell size, or chunk size is invalid.");
            if (!(_broadWarpCycles >= 3f && _broadWarpCycles <= 9f) ||
                !(_broadWarpDistance >= 0f && _broadWarpDistance <= 20f) ||
                !(_detailWarpCycles >= 8f && _detailWarpCycles <= 24f) ||
                !(_detailWarpDistance >= 0f && _detailWarpDistance <= 6f))
                throw new ArgumentException("Terrain region warp settings are invalid.");
            if (_voronoiRegionWidth < 1f || _voronoiRegionWidth > 60f ||
                _voronoiRegionSoftness < 0f || _voronoiRegionSoftness > 8f ||
                _voronoiRoadWidth < 1f || _voronoiRoadWidth > 20f ||
                _voronoiCornerSoftness < 0f || _voronoiCornerSoftness > 8f ||
                _depressionDepth < 0f)
                throw new ArgumentException("Terrain SDF width or depression depth is invalid.");
            if (_houseCount < 0 || _treeCount < 0 || _grassCount < 0 || _wheatCount < 0)
                throw new ArgumentException("Terrain content counts must not be negative.");
            if (_featureRules == null || _featureRules.Length == 0)
                throw new ArgumentException("Terrain feature rules must not be empty.");
            bool[] seen = new bool[Enum.GetValues(typeof(TerrainRegion)).Length];
            for (int i = 0; i < _featureRules.Length; i++)
            {
                TerrainFeatureRule rule = _featureRules[i];
                if (rule == null || (int)rule.Region < 0 ||
                    (int)rule.Region >= seen.Length || seen[(int)rule.Region] ||
                    rule.SitesAtReferenceSize < 1 || rule.MinRadiusAtReferenceSize <= 0f ||
                    rule.MaxRadiusAtReferenceSize < rule.MinRadiusAtReferenceSize)
                    throw new ArgumentException($"Invalid or duplicate terrain feature rule at index {i}.");
                seen[(int)rule.Region] = true;
            }
            if (seen[(int)TerrainRegion.Grassland] && _featureRules.Length == 1)
                throw new ArgumentException("At least one non-grassland feature rule is required.");
            if (Mathf.Abs(_width / _cellSize - Mathf.Round(_width / _cellSize)) > 0.001f ||
                Mathf.Abs(_height / _cellSize - Mathf.Round(_height / _cellSize)) > 0.001f)
                throw new ArgumentException("Terrain width and height must be divisible by cell size.");
        }
    }

    public readonly struct TerrainSite
    {
        public readonly int Id;
        public readonly TerrainRegion Region;
        public readonly Vector2 Center;
        public readonly float Radius;
        public readonly bool ConnectRoad;

        public TerrainSite(int id, TerrainRegion region, Vector2 center, float radius, bool connectRoad)
        {
            Id = id;
            Region = region;
            Center = center;
            Radius = radius;
            ConnectRoad = connectRoad;
        }
    }

    public readonly struct TerrainRegionGroup
    {
        public readonly int Id;
        public readonly TerrainRegion Region;
        public readonly int CellCount;
        public readonly Vector2 Center;
        public readonly int AnchorSiteId;

        public TerrainRegionGroup(int id, TerrainRegion region, int cellCount,
            Vector2 center, int anchorSiteId)
        {
            Id = id;
            Region = region;
            CellCount = cellCount;
            Center = center;
            AnchorSiteId = anchorSiteId;
        }
    }

    /// <summary>
    /// 地形的唯一逻辑数据源；区域与道路为独立层，地表和内容生成器只读取它。
    /// </summary>
    public sealed class TerrainMapData
    {
        private readonly TerrainRegion[] _regions;
        private readonly int[] _ownerSiteIds;
        private readonly int[] _regionGroupIds;
        private readonly TerrainRoadType[] _roadTypes;
        private readonly float[] _roadCoverage;
        private readonly float[] _heights;
        private readonly List<TerrainSite> _sites = new List<TerrainSite>();
        private readonly List<TerrainRegionGroup> _regionGroups = new List<TerrainRegionGroup>();

        public int WidthCells { get; }
        public int HeightCells { get; }
        public float CellSize { get; }
        public Vector2 Origin { get; }
        public int Seed { get; }
        public IReadOnlyList<TerrainSite> Sites => _sites;
        public IReadOnlyList<TerrainRegionGroup> RegionGroups => _regionGroups;

        internal TerrainMapData(TerrainGenerationSettings settings)
        {
            WidthCells = Mathf.RoundToInt(settings.Width / settings.CellSize);
            HeightCells = Mathf.RoundToInt(settings.Height / settings.CellSize);
            CellSize = settings.CellSize;
            Origin = settings.Origin;
            Seed = settings.Seed;

            int count = WidthCells * HeightCells;
            _regions = new TerrainRegion[count];
            _ownerSiteIds = new int[count];
            _regionGroupIds = new int[count];
            _roadTypes = new TerrainRoadType[count];
            _roadCoverage = new float[count];
            _heights = new float[count];
            for (int i = 0; i < count; i++)
            {
                _ownerSiteIds[i] = -1;
                _regionGroupIds[i] = -1;
            }
        }

        public TerrainRegion GetRegion(int x, int z) => _regions[Index(x, z)];
        public int GetOwnerSiteId(int x, int z) => _ownerSiteIds[Index(x, z)];
        public int GetRegionGroupId(int x, int z) => _regionGroupIds[Index(x, z)];
        public TerrainRoadType GetRoadType(int x, int z) => _roadTypes[Index(x, z)];
        public float GetRoadCoverage(int x, int z) => _roadCoverage[Index(x, z)];
        public float GetHeight(int x, int z) => _heights[Index(x, z)];

        public Vector2 CellCenter(int x, int z)
        {
            Index(x, z);
            return Origin + new Vector2((x + 0.5f) * CellSize, (z + 0.5f) * CellSize);
        }

        public float SampleHeightAtVertex(int x, int z)
        {
            float sum = 0f;
            for (int dz = -1; dz <= 0; dz++)
            {
                for (int dx = -1; dx <= 0; dx++)
                {
                    int sx = Mathf.Clamp(x + dx, 0, WidthCells - 1);
                    int sz = Mathf.Clamp(z + dz, 0, HeightCells - 1);
                    sum += GetHeight(sx, sz);
                }
            }
            return sum * 0.25f;
        }

        internal void SetRegion(int x, int z, TerrainRegion value, int ownerSiteId)
        {
            int index = Index(x, z);
            _regions[index] = value;
            _ownerSiteIds[index] = ownerSiteId;
        }
        internal void SetRegionGroup(int x, int z, int groupId) =>
            _regionGroupIds[Index(x, z)] = groupId;
        internal void SetHeight(int x, int z, float value) => _heights[Index(x, z)] = value;

        internal void SetRoad(int x, int z, float coverage, TerrainRoadType type)
        {
            int index = Index(x, z);
            _roadCoverage[index] = Mathf.Clamp01(coverage);
            _roadTypes[index] = type;
        }

        internal void AddSite(TerrainSite site) => _sites.Add(site);
        internal void AddRegionGroup(TerrainRegionGroup group) => _regionGroups.Add(group);

        private int Index(int x, int z)
        {
            if (x < 0 || x >= WidthCells || z < 0 || z >= HeightCells)
                throw new ArgumentOutOfRangeException($"Terrain cell ({x}, {z}) is outside {WidthCells} x {HeightCells}.");
            return x + z * WidthCells;
        }
    }
}
