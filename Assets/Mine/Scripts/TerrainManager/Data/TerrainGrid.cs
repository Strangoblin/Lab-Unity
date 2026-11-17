// ════════════════════════════════════════════════════════════
//  TerrainGrid — 地形数据层的唯一权威网格
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// XZ 规则网格：高度存放在顶点，地表层存放在格，采样与渲染共用同一套三角剖分。
    /// </summary>
    /// <remarks>
    /// 每个格沿 (0,0) → (1,1) 对角线切成两个三角形：
    /// u >= v 时使用 (00,11,10)，否则使用 (00,01,11)。
    /// 网格渲染与 <see cref="TrySample"/> 必须保持一致，否则草与交互会落在错误的面上。
    /// </remarks>
    public sealed class TerrainGrid
    {
        private readonly float[] _heights;
        private readonly TerrainSurfaceLayer[] _layers;

        public int WidthCells { get; }
        public int HeightCells { get; }
        public float CellSize { get; }
        public Vector3 Origin { get; }

        public float MinHeight { get; private set; }
        public float MaxHeight { get; private set; }
        public float WorldWidth => WidthCells * CellSize;
        public float WorldDepth => HeightCells * CellSize;
        public int VertexCount => _heights.Length;
        public int CellCount => _layers.Length;

        public TerrainGrid(int widthCells, int heightCells, float cellSize, Vector3 origin)
        {
            if (widthCells < 1 || heightCells < 1)
                throw new ArgumentException("Terrain grid needs at least one cell on each axis.");
            if (!IsFinite(cellSize) || cellSize <= 0f)
                throw new ArgumentException("Terrain grid cell size must be finite and positive.");
            if (!IsFinite(origin.x) || !IsFinite(origin.y) || !IsFinite(origin.z))
                throw new ArgumentException("Terrain grid origin must be finite.");

            WidthCells = widthCells;
            HeightCells = heightCells;
            CellSize = cellSize;
            Origin = origin;
            _heights = new float[(widthCells + 1) * (heightCells + 1)];
            _layers = new TerrainSurfaceLayer[widthCells * heightCells];
        }

        public Bounds Bounds
        {
            get
            {
                float height = Mathf.Max(MaxHeight - MinHeight, 0.01f);
                Vector3 center = Origin + new Vector3(
                    WorldWidth * 0.5f,
                    (MinHeight + MaxHeight) * 0.5f,
                    WorldDepth * 0.5f);
                return new Bounds(center, new Vector3(WorldWidth, height, WorldDepth));
            }
        }

        // ════════════════════════════════════════════════════════════
        //  顶点与格访问 — 数据层只在这里读写
        // ════════════════════════════════════════════════════════════

        public float GetHeight(int x, int z)
        {
            RequireVertex(x, z);
            return _heights[z * (WidthCells + 1) + x];
        }

        public void SetHeight(int x, int z, float height)
        {
            RequireVertex(x, z);
            if (!IsFinite(height))
                throw new ArgumentException("Terrain height must be finite.");
            _heights[z * (WidthCells + 1) + x] = height;
            MinHeight = Mathf.Min(MinHeight, height);
            MaxHeight = Mathf.Max(MaxHeight, height);
        }

        public TerrainSurfaceLayer GetLayer(int x, int z)
        {
            RequireCell(x, z);
            return _layers[z * WidthCells + x];
        }

        public void SetLayer(int x, int z, TerrainSurfaceLayer layer)
        {
            RequireCell(x, z);
            _layers[z * WidthCells + x] = layer;
        }

        public Vector3 GetVertexPosition(int x, int z)
        {
            return Origin + new Vector3(x * CellSize, GetHeight(x, z), z * CellSize);
        }

        public void RecalculateHeightRange()
        {
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int i = 0; i < _heights.Length; i++)
            {
                float height = _heights[i];
                min = Mathf.Min(min, height);
                max = Mathf.Max(max, height);
            }

            MinHeight = _heights.Length > 0 ? min : 0f;
            MaxHeight = _heights.Length > 0 ? max : 0f;
        }

        /// <summary>
        /// 平滑顶点法线，用于网格着色；采样法线仍使用实际三角面。
        /// </summary>
        public Vector3 ComputeVertexNormal(int x, int z)
        {
            RequireVertex(x, z);

            float left = GetHeight(Mathf.Max(x - 1, 0), z);
            float right = GetHeight(Mathf.Min(x + 1, WidthCells), z);
            float down = GetHeight(x, Mathf.Max(z - 1, 0));
            float up = GetHeight(x, Mathf.Min(z + 1, HeightCells));

            float xSpan = x == 0 || x == WidthCells ? CellSize : CellSize * 2f;
            float zSpan = z == 0 || z == HeightCells ? CellSize : CellSize * 2f;
            return new Vector3(-(right - left) / xSpan, 1f,
                -(up - down) / zSpan).normalized;
        }

        // ════════════════════════════════════════════════════════════
        //  世界空间采样 — 网格与消费者的统一入口
        // ════════════════════════════════════════════════════════════

        public bool Contains(float worldX, float worldZ)
        {
            if (!IsFinite(worldX) || !IsFinite(worldZ))
                return false;
            float x = (worldX - Origin.x) / CellSize;
            float z = (worldZ - Origin.z) / CellSize;
            return x >= 0f && x <= WidthCells && z >= 0f && z <= HeightCells;
        }

        public bool TrySample(float worldX, float worldZ, out TerrainSample sample)
        {
            sample = default;
            if (!IsFinite(worldX) || !IsFinite(worldZ))
                return false;

            float gridX = (worldX - Origin.x) / CellSize;
            float gridZ = (worldZ - Origin.z) / CellSize;
            if (gridX < 0f || gridX > WidthCells || gridZ < 0f || gridZ > HeightCells)
                return false;

            int cellX = Mathf.Min(Mathf.FloorToInt(gridX), WidthCells - 1);
            int cellZ = Mathf.Min(Mathf.FloorToInt(gridZ), HeightCells - 1);
            float u = gridX - cellX;
            float v = gridZ - cellZ;

            Vector3 p00 = GetVertexPosition(cellX, cellZ);
            Vector3 p10 = GetVertexPosition(cellX + 1, cellZ);
            Vector3 p01 = GetVertexPosition(cellX, cellZ + 1);
            Vector3 p11 = GetVertexPosition(cellX + 1, cellZ + 1);

            float height;
            Vector3 normal;
            if (u >= v)
            {
                height = p00.y * (1f - u) + p11.y * v + p10.y * (u - v);
                normal = Vector3.Cross(p11 - p00, p10 - p00);
            }
            else
            {
                height = p00.y * (1f - v) + p11.y * u + p01.y * (v - u);
                normal = Vector3.Cross(p01 - p00, p11 - p00);
            }

            if (normal.y < 0f)
                normal = -normal;

            sample = new TerrainSample(
                new Vector3(worldX, height, worldZ),
                normal.normalized,
                GetLayer(cellX, cellZ),
                height - Origin.y);
            return true;
        }

        // ════════════════════════════════════════════════════════════
        //  内部断言 — 数据写入越界时尽早失败
        // ════════════════════════════════════════════════════════════

        private void RequireVertex(int x, int z)
        {
            if (x < 0 || x > WidthCells || z < 0 || z > HeightCells)
                throw new ArgumentOutOfRangeException($"Vertex ({x}, {z}) is outside the terrain grid.");
        }

        private void RequireCell(int x, int z)
        {
            if (x < 0 || x >= WidthCells || z < 0 || z >= HeightCells)
                throw new ArgumentOutOfRangeException($"Cell ({x}, {z}) is outside the terrain grid.");
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
