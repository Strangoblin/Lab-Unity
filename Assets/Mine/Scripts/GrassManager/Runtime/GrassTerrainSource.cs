using System;
using UnityEngine;

namespace Mine.Grass
{
    /// <summary>Surface categories that a vegetation rule can select.</summary>
    [Flags]
    public enum GrassTerrainLayer
    {
        None = 0,
        Grass = 1,
        Dirt = 2,
        Water = 4,
        Rock = 8,
        Sand = 16,
        Snow = 32
    }

    /// <summary>
    /// World-space color map that grass samples as its base color.
    /// </summary>
    /// <remarks>
    /// <see cref="Origin"/> and <see cref="Size"/> describe the world XZ rectangle the map
    /// covers; the map is sampled with <c>(worldXZ - Origin) / Size</c>, matching the UV
    /// layout of the terrain mesh.
    /// </remarks>
    public readonly struct GrassGroundColorSource
    {
        public readonly Texture ColorMap;
        public readonly Vector2 Origin;
        public readonly Vector2 Size;

        public GrassGroundColorSource(Texture colorMap, Vector2 origin, Vector2 size)
        {
            ColorMap = colorMap;
            Origin = origin;
            Size = size;
        }

        public bool IsValid => ColorMap != null && Size.x > 0f && Size.y > 0f;
    }

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
        /// <summary>World-space extent; the last logical chunk may be only partially filled.</summary>
        public virtual float Width => ChunkCountX * ChunkWidth;
        public virtual float Depth => ChunkCountZ * ChunkWidth;
        public abstract int DataVersion { get; }
        public abstract bool HasData { get; }
        public abstract void Initialize();
        public abstract bool TrySample(float worldX, float worldZ, out Vector3 position, out Vector3 normal);

        /// <summary>Sample surface geometry and its category. Older sources default to Grass.</summary>
        public virtual bool TrySample(float worldX, float worldZ, out Vector3 position, out Vector3 normal,
            out GrassTerrainLayer layer)
        {
            bool found = TrySample(worldX, worldZ, out position, out normal);
            layer = found ? GrassTerrainLayer.Grass : GrassTerrainLayer.None;
            return found;
        }

        /// <summary>Optional color map sampled as the grass base color; unset sources draw with white.</summary>
        public virtual bool TryGetGroundColor(out GrassGroundColorSource source)
        {
            source = default;
            return false;
        }
    }
}
