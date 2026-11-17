// ════════════════════════════════════════════════════════════
//  TerrainSample — 一次地形采样的完整结果
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Mine.Terrain
{
    /// <summary>
    /// 世界空间采样结果：位置、三角面法线、地表层与局部高度。
    /// </summary>
    public readonly struct TerrainSample
    {
        public readonly Vector3 Position;
        public readonly Vector3 Normal;
        public readonly TerrainSurfaceLayer Layer;
        public readonly float Height;

        public TerrainSample(Vector3 position, Vector3 normal,
            TerrainSurfaceLayer layer, float height)
        {
            Position = position;
            Normal = normal;
            Layer = layer;
            Height = height;
        }
    }
}
