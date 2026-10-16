// ════════════════════════════════════════════════════════════
//  TerrainGenerationSettings — 所有地图方案共享的网格与随机种子配置
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 地图尺度、网格分块、原点与种子；具体方案参数由 TerrainType 独立维护。
    /// </summary>
    [Serializable]
    public sealed class TerrainGenerationSettings
    {
        [SerializeField, Min(32)] private int _width = 300;
        [SerializeField, Min(32)] private int _height = 300;
        [SerializeField, Min(0.25f)] private float _cellSize = 1f;
        [SerializeField, Min(8)] private int _chunkCells = 50;
        [SerializeField] private Vector2 _origin = Vector2.zero;
        [SerializeField] private int _seed = 42;

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

        public void Validate()
        {
            if (_width < 32 || _height < 32 || !IsFinite(_cellSize) ||
                _cellSize < 0.25f || _chunkCells < 8 ||
                !IsFinite(_origin.x) || !IsFinite(_origin.y))
                throw new ArgumentException("Terrain size, cell size, chunk size, or origin is invalid.");
            float widthRatio = _width / _cellSize;
            float heightRatio = _height / _cellSize;
            double widthCells = Math.Round(widthRatio);
            double heightCells = Math.Round(heightRatio);
            if (widthCells < 1d || heightCells < 1d)
                throw new ArgumentException("Terrain must contain at least one cell on each axis.");
            if (widthCells > int.MaxValue || heightCells > int.MaxValue ||
                widthCells * heightCells > int.MaxValue)
                throw new ArgumentException("Terrain cell count exceeds the supported integer range.");
            if (Math.Abs(widthRatio - widthCells) > 0.001d ||
                Math.Abs(heightRatio - heightCells) > 0.001d)
                throw new ArgumentException("Terrain width and height must be divisible by cell size.");
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
