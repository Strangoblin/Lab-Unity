// ════════════════════════════════════════════════════════════
//  TerrainSurfaceInstance — 标记渲染层生成的子物体
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Mine.Terrain
{
    /// <summary>
    /// 标记 <see cref="TerrainSurfaceRenderer"/> 生成的子物体。
    /// </summary>
    /// <remarks>
    /// 生成物是 DontSave 的运行时对象，脚本域重载后入口持有的引用会丢失；
    /// 入口重建时靠这个标记回收残留，避免场景里堆积同名地表。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TerrainSurfaceInstance : MonoBehaviour
    {
    }
}
