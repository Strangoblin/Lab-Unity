// ════════════════════════════════════════════════════════════
//  TerrainMapData — 俯视地图的逻辑网格、地点和道路数据契约
// ════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mine.Terrain
{
    public enum TerrainRegion
    {
        Grassland,
        Village,
        WheatField,
        Forest,
        Water
    }

    public enum TerrainRoadType
    {
        None,
        Dirt,
        Stone
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

        [Header("Features")]
        [SerializeField, Min(1f)] private float _roadWidth = 5f;
        [SerializeField, Min(0f)] private float _depressionDepth = 1f;
        [SerializeField, Min(0)] private int _houseCount = 10;
        [SerializeField, Min(0)] private int _treeCount = 90;
        [SerializeField, Min(0)] private int _grassCount = 160;
        [SerializeField, Min(0)] private int _wheatCount = 140;

        public int Width
        {
            get => _width;
            set => _width = value;
        }
        public int Height => _height;
        public float CellSize => _cellSize;
        public int ChunkCells => _chunkCells;
        public Vector2 Origin => _origin;
        public int Seed
        {
            get => _seed;
            set => _seed = value;
        }
        public float RoadWidth => _roadWidth;
        public float DepressionDepth => _depressionDepth;
        public int HouseCount => _houseCount;
        public int TreeCount => _treeCount;
        public int GrassCount => _grassCount;
        public int WheatCount => _wheatCount;

        public void Validate()
        {
            if (_width < 32 || _height < 32 || _cellSize < 0.25f || _chunkCells < 8)
                throw new ArgumentException("Terrain size, cell size, or chunk size is invalid.");
            if (_roadWidth < 1f || _depressionDepth < 0f)
                throw new ArgumentException("Terrain road width or depression depth is invalid.");
            if (_houseCount < 0 || _treeCount < 0 || _grassCount < 0 || _wheatCount < 0)
                throw new ArgumentException("Terrain content counts must not be negative.");
            if (Mathf.Abs(_width / _cellSize - Mathf.Round(_width / _cellSize)) > 0.001f ||
                Mathf.Abs(_height / _cellSize - Mathf.Round(_height / _cellSize)) > 0.001f)
                throw new ArgumentException("Terrain width and height must be divisible by cell size.");
        }
    }

    public readonly struct TerrainSite
    {
        public readonly TerrainRegion Region;
        public readonly Vector2 Center;
        public readonly float Radius;

        public TerrainSite(TerrainRegion region, Vector2 center, float radius)
        {
            Region = region;
            Center = center;
            Radius = radius;
        }
    }

    public sealed class TerrainRoute
    {
        public readonly Vector2[] Points;

        public TerrainRoute(Vector2[] points)
        {
            Points = points;
        }
    }

    /// <summary>
    /// 地形的唯一逻辑数据源；区域与道路为独立层，地表和内容生成器只读取它。
    /// </summary>
    public sealed class TerrainMapData
    {
        private readonly TerrainRegion[] _regions;
        private readonly TerrainRoadType[] _roadTypes;
        private readonly float[] _roadCoverage;
        private readonly float[] _heights;
        private readonly List<TerrainSite> _sites = new List<TerrainSite>();
        private readonly List<TerrainRoute> _routes = new List<TerrainRoute>();

        public int WidthCells { get; }
        public int HeightCells { get; }
        public float CellSize { get; }
        public Vector2 Origin { get; }
        public int Seed { get; }
        public IReadOnlyList<TerrainSite> Sites => _sites;
        public IReadOnlyList<TerrainRoute> Routes => _routes;

        internal TerrainMapData(TerrainGenerationSettings settings)
        {
            WidthCells = Mathf.RoundToInt(settings.Width / settings.CellSize);
            HeightCells = Mathf.RoundToInt(settings.Height / settings.CellSize);
            CellSize = settings.CellSize;
            Origin = settings.Origin;
            Seed = settings.Seed;

            int count = WidthCells * HeightCells;
            _regions = new TerrainRegion[count];
            _roadTypes = new TerrainRoadType[count];
            _roadCoverage = new float[count];
            _heights = new float[count];
        }

        public TerrainRegion GetRegion(int x, int z) => _regions[Index(x, z)];
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

        internal void SetRegion(int x, int z, TerrainRegion value) => _regions[Index(x, z)] = value;
        internal void SetHeight(int x, int z, float value) => _heights[Index(x, z)] = value;

        internal void SetRoad(int x, int z, float coverage, TerrainRoadType type)
        {
            int index = Index(x, z);
            _roadCoverage[index] = Mathf.Clamp01(coverage);
            _roadTypes[index] = type;
        }

        internal void AddSite(TerrainSite site) => _sites.Add(site);
        internal void AddRoute(TerrainRoute route) => _routes.Add(route);

        private int Index(int x, int z)
        {
            if (x < 0 || x >= WidthCells || z < 0 || z >= HeightCells)
                throw new ArgumentOutOfRangeException($"Terrain cell ({x}, {z}) is outside {WidthCells} x {HeightCells}.");
            return x + z * WidthCells;
        }
    }
}
