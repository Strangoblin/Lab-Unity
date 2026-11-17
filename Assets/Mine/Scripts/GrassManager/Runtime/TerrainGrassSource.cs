// ════════════════════════════════════════════════════════════
//  TerrainGrassSource — 把 TerrainManager 接到草的采样契约上
// ════════════════════════════════════════════════════════════

using UnityEngine;
using Mine.Terrain;

namespace Mine.Grass
{
    /// <summary>
    /// 把 <see cref="TerrainManager"/> 的数据层适配成 <see cref="GrassTerrainSource"/>。
    /// </summary>
    /// <remarks>
    /// 只有「草怎么取地表」这一层是本组件的职责：地形高度、法线与地表层全部来自
    /// <see cref="TerrainManager"/>，本组件不持有任何地形数据。
    /// 逻辑地块的边长属于草的生成策略，因此在这里独立配置，改它只重建草，不改地形。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TerrainGrassSource : GrassTerrainSource
    {
        private const float MinChunkSize = 0.1f;
        private const float MaxChunkSize = 10000f;
        private const float DefaultChunkSize = 20f;

        [SerializeField] private TerrainManager _terrain;
        [SerializeField, Min(MinChunkSize)] private float _chunkSize = DefaultChunkSize;

        private int _version;
        private int _seenTerrainVersion = -1;
        private float _seenChunkSize = float.NaN;

        public TerrainManager Terrain => _terrain;
        public float ChunkSize { get => SafeChunkSize(_chunkSize); set => _chunkSize = SafeChunkSize(value); }

        public override Vector3 Origin
        {
            get
            {
                TerrainGrid grid = Grid;
                return grid != null ? grid.Origin : Vector3.zero;
            }
        }

        public override int ChunkCountX => CountChunks(AxisExtent(Grid, true));
        public override int ChunkCountZ => CountChunks(AxisExtent(Grid, false));
        public override float ChunkWidth => ChunkSize;
        public override float Width => AxisExtent(Grid, true);
        public override float Depth => AxisExtent(Grid, false);
        public override int DataVersion => _version;
        public override bool HasData => _terrain != null && _terrain.HasData && Grid != null;

        private TerrainGrid Grid => _terrain != null ? _terrain.CurrentGrid : null;

        // ════════════════════════════════════════════════════════════
        //  生命周期 — 地形数据与地块划分的版本合并
        // ════════════════════════════════════════════════════════════

        public void Configure(TerrainManager terrain)
        {
            _terrain = terrain;
            _seenTerrainVersion = -1;
            _seenChunkSize = float.NaN;
            _version++;
        }

        /// <summary>
        /// 按需生成地形，并把「地形版本」与「地块边长」合并成一个消费方可见的版本号。
        /// </summary>
        public override void Initialize()
        {
            if (_terrain == null)
                return;

            _terrain.Initialize();

            int terrainVersion = _terrain.DataVersion;
            float chunkSize = ChunkSize;
            if (terrainVersion == _seenTerrainVersion && Mathf.Approximately(chunkSize, _seenChunkSize))
                return;

            _seenTerrainVersion = terrainVersion;
            _seenChunkSize = chunkSize;
            _version++;
        }

        // ════════════════════════════════════════════════════════════
        //  采样 — 位置与法线直接沿用数据层，地表层做类别映射
        // ════════════════════════════════════════════════════════════

        public override bool TrySample(float worldX, float worldZ, out Vector3 position, out Vector3 normal)
        {
            return TrySample(worldX, worldZ, out position, out normal, out _);
        }

        public override bool TrySample(float worldX, float worldZ, out Vector3 position, out Vector3 normal,
            out GrassTerrainLayer layer)
        {
            position = default;
            normal = Vector3.up;
            layer = GrassTerrainLayer.None;

            if (_terrain == null || !_terrain.TrySample(worldX, worldZ, out TerrainSample sample))
                return false;

            position = sample.Position;
            normal = sample.Normal;
            layer = ToGrassLayer(sample.Layer);
            return true;
        }

        private static GrassTerrainLayer ToGrassLayer(TerrainSurfaceLayer layer)
        {
            switch (layer)
            {
                case TerrainSurfaceLayer.Grass: return GrassTerrainLayer.Grass;
                case TerrainSurfaceLayer.Dirt: return GrassTerrainLayer.Dirt;
                case TerrainSurfaceLayer.Rock: return GrassTerrainLayer.Rock;
                case TerrainSurfaceLayer.Sand: return GrassTerrainLayer.Sand;
                case TerrainSurfaceLayer.Snow: return GrassTerrainLayer.Snow;
                default: return GrassTerrainLayer.None;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  地表颜色 — 直接借用地形渲染层的颜色图，与网格共用世界 UV 映射
        // ════════════════════════════════════════════════════════════

        public override bool TryGetGroundColor(out GrassGroundColorSource source)
        {
            TerrainGrid grid = Grid;
            Texture colorMap = _terrain != null ? _terrain.SurfaceColorMap : null;
            if (grid == null || colorMap == null)
            {
                source = default;
                return false;
            }

            source = new GrassGroundColorSource(colorMap,
                new Vector2(grid.Origin.x, grid.Origin.z),
                new Vector2(grid.WorldWidth, grid.WorldDepth));
            return true;
        }

        // ════════════════════════════════════════════════════════════
        //  范围换算 — 地块数向上取整，世界范围取数据层真实尺寸
        // ════════════════════════════════════════════════════════════

        private int CountChunks(float extent)
        {
            if (extent <= 0f)
                return 0;
            return Mathf.Max(1, Mathf.CeilToInt(extent / ChunkSize));
        }

        private static float AxisExtent(TerrainGrid grid, bool horizontal)
        {
            if (grid == null)
                return 0f;
            return horizontal ? grid.WorldWidth : grid.WorldDepth;
        }

        private static float SafeChunkSize(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? DefaultChunkSize : Mathf.Clamp(value, MinChunkSize, MaxChunkSize);

        private void OnValidate() => _chunkSize = SafeChunkSize(_chunkSize);
    }
}
