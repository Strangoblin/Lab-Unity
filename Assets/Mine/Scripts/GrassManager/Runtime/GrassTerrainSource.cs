using UnityEngine;

namespace Mine.Grass
{
    /// <summary>
    /// World-space terrain data consumed by grass. An implementation owns its data and
    /// increments DataVersion whenever its sampling result or grid changes. TrySample
    /// returns false where there is no surface suitable for grass.
    /// </summary>
    public abstract class GrassTerrainSource : MonoBehaviour
    {
        public abstract Vector3 Origin { get; }
        public abstract int ChunkCountX { get; }
        public abstract int ChunkCountZ { get; }
        public abstract float ChunkWidth { get; }
        public abstract int DataVersion { get; }
        public abstract bool HasData { get; }
        public abstract void Initialize();
        public abstract bool TrySample(float worldX, float worldZ, out Vector3 position, out Vector3 normal);
    }
}
