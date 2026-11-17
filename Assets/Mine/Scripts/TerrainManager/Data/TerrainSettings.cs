// ════════════════════════════════════════════════════════════
//  TerrainSettings — 数据网格与默认提供者的共享配置
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    public enum TerrainProviderKind
    {
        Flat = 0,
        Noise = 1
    }

    /// <summary>
    /// 网格尺度、原点、种子与提供者参数；所有字段共同决定一次数据生成。
    /// </summary>
    [Serializable]
    public sealed class TerrainSettings
    {
        [Header("Grid")]
        [SerializeField, Min(1)] private int _widthCells = 100;
        [SerializeField, Min(1)] private int _heightCells = 100;
        [SerializeField, Min(0.1f)] private float _cellSize = 1f;
        [SerializeField] private Vector3 _origin = Vector3.zero;
        [SerializeField] private int _seed = 42;

        [Header("Provider")]
        [SerializeField] private TerrainProviderKind _provider = TerrainProviderKind.Noise;
        [SerializeField] private TerrainSurfaceLayer _defaultLayer = TerrainSurfaceLayer.Grass;
        [SerializeField, Min(0f)] private float _heightScale = 6f;
        [SerializeField, Min(0.0001f)] private float _noiseScale = 0.025f;
        [SerializeField, Range(1, 6)] private int _noiseOctaves = 3;

        public int WidthCells
        {
            get => _widthCells;
            set => _widthCells = value;
        }

        public int HeightCells
        {
            get => _heightCells;
            set => _heightCells = value;
        }

        public float CellSize
        {
            get => _cellSize;
            set => _cellSize = value;
        }

        public Vector3 Origin
        {
            get => _origin;
            set => _origin = value;
        }

        public int Seed
        {
            get => _seed;
            set => _seed = value;
        }

        public TerrainProviderKind Provider
        {
            get => _provider;
            set => _provider = value;
        }

        public TerrainSurfaceLayer DefaultLayer
        {
            get => _defaultLayer;
            set => _defaultLayer = value;
        }

        public float HeightScale
        {
            get => _heightScale;
            set => _heightScale = value;
        }

        public float NoiseScale
        {
            get => _noiseScale;
            set => _noiseScale = value;
        }

        public int NoiseOctaves
        {
            get => _noiseOctaves;
            set => _noiseOctaves = value;
        }

        public float WorldWidth => _widthCells * _cellSize;
        public float WorldDepth => _heightCells * _cellSize;
        public int VertexCount => (_widthCells + 1) * (_heightCells + 1);
        public int CellCount => _widthCells * _heightCells;

        // ════════════════════════════════════════════════════════════
        //  校验 — 生成前拒绝不可分配的网格与无效参数
        // ════════════════════════════════════════════════════════════

        public void Validate()
        {
            if (!TryValidate(out string error))
                throw new ArgumentException(error);
        }

        public bool TryValidate(out string error)
        {
            if (_widthCells < 1 || _heightCells < 1)
            {
                error = "Terrain must contain at least one cell on each axis.";
                return false;
            }

            if (!IsFinite(_cellSize) || _cellSize <= 0f)
            {
                error = "Terrain cell size must be a finite positive value.";
                return false;
            }

            if (!IsFinite(_origin.x) || !IsFinite(_origin.y) || !IsFinite(_origin.z))
            {
                error = "Terrain origin must be finite.";
                return false;
            }

            if (!IsFinite(_heightScale) || _heightScale < 0f)
            {
                error = "Terrain height scale must be a finite non-negative value.";
                return false;
            }

            if (_provider == TerrainProviderKind.Noise &&
                (!IsFinite(_noiseScale) || _noiseScale <= 0f))
            {
                error = "Terrain noise scale must be a finite positive value.";
                return false;
            }

            if (_provider == TerrainProviderKind.Noise && _noiseOctaves < 1)
            {
                error = "Terrain noise octaves must be at least one.";
                return false;
            }

            long vertices = (long)(_widthCells + 1) * (_heightCells + 1);
            long cells = (long)_widthCells * _heightCells;
            if (vertices > int.MaxValue || cells > int.MaxValue)
            {
                error = "Terrain cell or vertex count exceeds the supported range.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
