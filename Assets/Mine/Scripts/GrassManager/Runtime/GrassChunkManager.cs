using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.Grass
{
    /// <summary>Generates and draws the 3x3 neighborhood of a camera on the terrain data.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class GrassChunkManager : MonoBehaviour
    {
        private const int MaxInstancesPerChunk = 4000;
        [SerializeField] private GrassTerrainSource _terrain;
        [SerializeField] private Camera _camera;
        [SerializeField] private Mesh _grassMesh;
        [SerializeField] private Material _grassMaterial;
        [SerializeField] private GrassTerrainLayer _terrainLayers = GrassTerrainLayer.Grass;
        [SerializeField, Tooltip("Local Euler rotation of the grass mesh before aligning it to the terrain.")]
        private Vector3 _meshRotation;
        // The allocation cap is applied per chunk when width and density would exceed it.
        [SerializeField, Range(0f, 10f)] private float _density = 0.8f;
        [SerializeField, Range(0f, 10f)] private float _size = 1f;
        [SerializeField, HideInInspector] private GrassHeightPaintData _heightPaint = new GrassHeightPaintData();

        private readonly Dictionary<Vector2Int, GrassChunk> _chunks = new Dictionary<Vector2Int, GrassChunk>();
        private Material _runtimeMaterial;
        private float _builtDensity = float.NaN;
        private float _builtSize = float.NaN;
        private int _builtTerrainVersion = -1;
        private GrassTerrainLayer _builtTerrainLayers;
        private Vector3 _builtMeshRotation;
        private GrassTerrainSource _activeTerrain;
        private Mesh _activeMesh;
        private Material _activeMaterial;
        private bool _limitReported;
        private bool _paintDataInvalidated;
        private readonly Plane[] _frustumPlanes = new Plane[6];

        public int ActiveChunkCount => _chunks.Count;
        public int VisibleInstanceCount
        {
            get
            {
                int count = 0;
                foreach (GrassChunk chunk in _chunks.Values)
                    count += chunk.VisibleCount;
                return count;
            }
        }
        public int ActiveInstanceCount
        {
            get
            {
                int count = 0;
                foreach (GrassChunk chunk in _chunks.Values)
                    count += chunk.Count;
                return count;
            }
        }
        public float Density { get => _density; set => _density = SafeParameter(value); }
        public float Size { get => _size; set => _size = SafeParameter(value); }
        public GrassTerrainLayer TerrainLayers { get => _terrainLayers; set => _terrainLayers = value; }
        public Vector3 MeshRotation { get => _meshRotation; set => _meshRotation = value; }
        public GrassTerrainSource Terrain => _terrain;
        public float HeightPaintMinimumBrushRadius
        {
            get
            {
                if (_terrain == null || _terrain.Width <= 0f || _terrain.Depth <= 0f ||
                    float.IsNaN(_terrain.Width) || float.IsNaN(_terrain.Depth) ||
                    float.IsInfinity(_terrain.Width) || float.IsInfinity(_terrain.Depth))
                    return 0.1f;
                double x = (double)_terrain.Width / (GrassHeightPaintData.Resolution - 1);
                double z = (double)_terrain.Depth / (GrassHeightPaintData.Resolution - 1);
                return Mathf.Max(0.1f, (float)(Math.Sqrt(x * x + z * z) * 0.501));
            }
        }
        public bool IsChunkActive(int x, int z) => _chunks.ContainsKey(new Vector2Int(x, z));

        /// <summary>Returns the painted height multiplier at a world position; unpainted terrain is 1.</summary>
        public float SamplePaintedHeight(Vector3 position)
        {
            if (_terrain == null || _heightPaint == null || !_terrain.HasData)
                return 1f;
            return _heightPaint.Sample(position, _terrain.Origin, _terrain.Width, _terrain.Depth);
        }

        /// <summary>Applies a smooth round brush in world XZ and invalidates only intersecting grass chunks.</summary>
        public bool PaintHeight(Vector3 center, float radius, float strength)
        {
            if (_terrain == null || !_terrain.isActiveAndEnabled)
                return false;
            _terrain.Initialize();
            if (!_terrain.HasData)
                return false;
            if (_heightPaint == null)
                _heightPaint = new GrassHeightPaintData();
            if (!_heightPaint.Paint(center, radius, strength, _terrain.Origin, _terrain.Width, _terrain.Depth))
                return false;

            List<Vector2Int> affected = new List<Vector2Int>();
            foreach (Vector2Int coordinate in _chunks.Keys)
            {
                float minX = _terrain.Origin.x + coordinate.x * _terrain.ChunkWidth;
                float minZ = _terrain.Origin.z + coordinate.y * _terrain.ChunkWidth;
                float maxX = Mathf.Min(minX + _terrain.ChunkWidth, _terrain.Origin.x + _terrain.Width);
                float maxZ = Mathf.Min(minZ + _terrain.ChunkWidth, _terrain.Origin.z + _terrain.Depth);
                // Bilinear sampling can affect points one data-grid cell beyond the brush.
                float marginX = _heightPaint.CellWidth(_terrain.Width);
                float marginZ = _heightPaint.CellWidth(_terrain.Depth);
                if (center.x + radius + marginX >= minX && center.x - radius - marginX <= maxX &&
                    center.z + radius + marginZ >= minZ && center.z - radius - marginZ <= maxZ)
                    affected.Add(coordinate);
            }
            foreach (Vector2Int coordinate in affected)
            {
                _chunks[coordinate].Dispose();
                _chunks.Remove(coordinate);
            }
            return true;
        }

        public void Configure(GrassTerrainSource terrain, Camera targetCamera, Mesh mesh, Material material)
        {
            _terrain = terrain;
            _camera = targetCamera;
            _grassMesh = mesh;
            _grassMaterial = material;
            Release();
        }

        public void Rebuild()
        {
            ClearChunks();
            _builtDensity = float.NaN;
            _builtSize = float.NaN;
            if (isActiveAndEnabled && UpdateChunks())
                RefreshVisibility();
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginContextRendering += BeginContextRendering;
        }

        private void Update()
        {
            if (UpdateChunks())
                RefreshVisibility();
        }

        private void BeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!isActiveAndEnabled || !UpdateChunks())
                return;
            RefreshVisibility();
            foreach (Camera camera in cameras)
            {
                if (camera != _camera && camera.cameraType != CameraType.SceneView)
                    continue;
                foreach (GrassChunk chunk in _chunks.Values)
                    chunk.Draw(_grassMesh, _runtimeMaterial, camera, gameObject.layer);
            }
        }

        private void RefreshVisibility()
        {
            if (_camera == null)
                return;
            GeometryUtility.CalculateFrustumPlanes(_camera, _frustumPlanes);
            foreach (GrassChunk chunk in _chunks.Values)
                chunk.Cull(_frustumPlanes, _camera.transform.position, _terrain.ChunkWidth);
        }

        private bool UpdateChunks()
        {
            if (_terrain == null || !_terrain.isActiveAndEnabled || _grassMesh == null || _grassMaterial == null)
            {
                Release();
                return false;
            }
            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
            {
                Release();
                return false;
            }

            if (_activeTerrain != _terrain || _activeMesh != _grassMesh || _activeMaterial != _grassMaterial)
            {
                Release();
                _activeTerrain = _terrain;
                _activeMesh = _grassMesh;
                _activeMaterial = _grassMaterial;
            }

            _terrain.Initialize();
            if (!_terrain.HasData || float.IsNaN(_terrain.ChunkWidth) || float.IsInfinity(_terrain.ChunkWidth) ||
                _terrain.ChunkWidth <= 0f || _terrain.ChunkCountX <= 0 || _terrain.ChunkCountZ <= 0 ||
                float.IsNaN(_terrain.Width) || float.IsInfinity(_terrain.Width) || _terrain.Width <= 0f ||
                float.IsNaN(_terrain.Depth) || float.IsInfinity(_terrain.Depth) || _terrain.Depth <= 0f)
            {
                ClearChunks();
                return false;
            }
            if (_runtimeMaterial == null)
            {
                _runtimeMaterial = new Material(_grassMaterial) { name = "Grass Chunk Material (Runtime)", enableInstancing = true,
                    hideFlags = HideFlags.HideAndDontSave };
            }
            float density = SafeParameter(_density);
            float size = SafeParameter(_size);
            if (_paintDataInvalidated)
            {
                ClearChunks();
                _paintDataInvalidated = false;
            }
            double largestWidth = Math.Min(_terrain.ChunkWidth, _terrain.Width);
            double largestDepth = Math.Min(_terrain.ChunkWidth, _terrain.Depth);
            if (density * largestWidth * largestDepth > MaxInstancesPerChunk)
            {
                if (!_limitReported)
                    Debug.LogWarning($"Grass density and chunk width exceed the {MaxInstancesPerChunk} instance limit per chunk; generation is capped, reducing the effective density.", this);
                _limitReported = true;
            }
            else
                _limitReported = false;
            if (_builtDensity != density || _builtSize != size || _builtTerrainVersion != _terrain.DataVersion ||
                _builtTerrainLayers != _terrainLayers || _builtMeshRotation != _meshRotation)
            {
                ClearChunks();
                _builtDensity = density;
                _builtSize = size;
                _builtTerrainVersion = _terrain.DataVersion;
                _builtTerrainLayers = _terrainLayers;
                _builtMeshRotation = _meshRotation;
            }

            Vector3 relative = _camera.transform.position - _terrain.Origin;
            int centerX = Mathf.FloorToInt(relative.x / _terrain.ChunkWidth);
            int centerZ = Mathf.FloorToInt(relative.z / _terrain.ChunkWidth);
            List<Vector2Int> remove = new List<Vector2Int>();
            foreach (Vector2Int key in _chunks.Keys)
                if (Mathf.Abs(key.x - centerX) > 1 || Mathf.Abs(key.y - centerZ) > 1)
                    remove.Add(key);
            foreach (Vector2Int key in remove)
            {
                _chunks[key].Dispose();
                _chunks.Remove(key);
            }

            for (int z = centerZ - 1; z <= centerZ + 1; z++)
            for (int x = centerX - 1; x <= centerX + 1; x++)
            {
                if (x < 0 || z < 0 || x >= _terrain.ChunkCountX || z >= _terrain.ChunkCountZ)
                    continue;
                Vector2Int key = new Vector2Int(x, z);
                if (!_chunks.ContainsKey(key))
                    _chunks.Add(key, CreateChunk(key, density, size));
            }
            return true;
        }

        private GrassChunk CreateChunk(Vector2Int coordinate, float density, float size)
        {
            float chunkWidth = _terrain.ChunkWidth;
            float offsetX = coordinate.x * chunkWidth;
            float offsetZ = coordinate.y * chunkWidth;
            float width = Mathf.Max(0f, Mathf.Min(chunkWidth, _terrain.Width - offsetX));
            float depth = Mathf.Max(0f, Mathf.Min(chunkWidth, _terrain.Depth - offsetZ));
            System.Random random = new System.Random(unchecked(73129 ^ coordinate.x * 73856093 ^ coordinate.y * 19349663));
            float expected = (float)Math.Min((double)density * width * depth, MaxInstancesPerChunk);
            int count = Mathf.FloorToInt(expected);
            if (random.NextDouble() < expected - count)
                count++;
            Matrix4x4[] matrices = new Matrix4x4[count];
            GrassProperties[] grass = new GrassProperties[count];
            uint[] indices = new uint[count];
            float minX = _terrain.Origin.x + offsetX;
            float minZ = _terrain.Origin.z + offsetZ;
            float minHeight = float.PositiveInfinity;
            float maxHeight = float.NegativeInfinity;
            int accepted = 0;
            Quaternion meshRotation = Quaternion.Euler(_meshRotation);
            for (int i = 0; i < count; i++)
            {
                float x = minX + (float)random.NextDouble() * width;
                float z = minZ + (float)random.NextDouble() * depth;
                if (!_terrain.TrySample(x, z, out Vector3 position, out Vector3 normal, out GrassTerrainLayer layer) ||
                    (layer & _terrainLayers) == GrassTerrainLayer.None)
                    continue;
                minHeight = Mathf.Min(minHeight, position.y);
                maxHeight = Mathf.Max(maxHeight, position.y);
                Quaternion alignment = Quaternion.FromToRotation(Vector3.up, normal);
                float scale = Mathf.Lerp(0.8f, 1.2f, (float)random.NextDouble()) * size;
                float height = SamplePaintedHeight(position);
                matrices[accepted] = Matrix4x4.TRS(position, alignment, new Vector3(scale, scale * height, scale)) *
                    Matrix4x4.Rotate(meshRotation);
                grass[accepted] = new GrassProperties { Normal = normal, Height = height };
                indices[accepted] = (uint)accepted;
                accepted++;
            }
            if (accepted != count)
            {
                Array.Resize(ref matrices, accepted);
                Array.Resize(ref grass, accepted);
                Array.Resize(ref indices, accepted);
                count = accepted;
            }
            // Conservatively include the rotated/scaled mesh in the chunk bounds.
            float radius = (_grassMesh.bounds.center.magnitude + _grassMesh.bounds.extents.magnitude) * size * 1.2f;
            if (count == 0)
                minHeight = maxHeight = _terrain.Origin.y;
            Bounds bounds = new Bounds(new Vector3(minX + width * 0.5f, (minHeight + maxHeight) * 0.5f,
                minZ + depth * 0.5f), new Vector3(width + 2f * radius,
                Mathf.Max(0.01f, maxHeight - minHeight + 2f * radius), depth + 2f * radius));
            return new GrassChunk(_grassMesh, matrices, grass, indices, bounds);
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= BeginContextRendering;
            Release();
        }

        // Unity calls this after serialized data changes, including Undo/Redo.
        private void OnValidate()
        {
            _paintDataInvalidated = true;
            _heightPaint?.InvalidateCache();
        }

        private void OnDrawGizmos()
        {
            Camera camera = _camera != null ? _camera : Camera.main;
            if (camera == null)
                return;
            var near = new Vector3[4];
            var far = new Vector3[4];
            camera.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), camera.nearClipPlane,
                Camera.MonoOrStereoscopicEye.Mono, near);
            camera.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), camera.farClipPlane,
                Camera.MonoOrStereoscopicEye.Mono, far);
            Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.8f);
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                Vector3 nearPoint = camera.transform.TransformPoint(near[i]);
                Vector3 farPoint = camera.transform.TransformPoint(far[i]);
                Gizmos.DrawLine(nearPoint, camera.transform.TransformPoint(near[next]));
                Gizmos.DrawLine(farPoint, camera.transform.TransformPoint(far[next]));
                Gizmos.DrawLine(nearPoint, farPoint);
            }

            // The distance test is a circle on the XZ plane centered on the camera.
            if (_terrain != null && _terrain.ChunkWidth > 0f &&
                !float.IsNaN(_terrain.ChunkWidth) && !float.IsInfinity(_terrain.ChunkWidth))
            {
                Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.9f);
                const int segments = 64;
                Vector3 center = camera.transform.position;
                Vector3 previous = center + Vector3.right * _terrain.ChunkWidth;
                for (int i = 1; i <= segments; i++)
                {
                    float angle = 2f * Mathf.PI * i / segments;
                    Vector3 next = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _terrain.ChunkWidth;
                    Gizmos.DrawLine(previous, next);
                    previous = next;
                }
            }
        }

        private void ClearChunks()
        {
            foreach (GrassChunk chunk in _chunks.Values)
                chunk.Dispose();
            _chunks.Clear();
        }

        private void Release()
        {
            ClearChunks();
            if (_runtimeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_runtimeMaterial);
                else
                    DestroyImmediate(_runtimeMaterial);
            }
            _runtimeMaterial = null;
            _builtDensity = float.NaN;
            _builtSize = float.NaN;
            _builtTerrainVersion = -1;
            _activeTerrain = null;
            _activeMesh = null;
            _activeMaterial = null;
        }

        private static float SafeParameter(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, 0f, 10f);

        private struct GrassProperties
        {
            public Vector3 Normal;
            public float Height;
        }

        private sealed class GrassChunk : IDisposable
        {
            private ComputeBuffer _matrices;
            private ComputeBuffer _grass;
            private ComputeBuffer _indices;
            private ComputeBuffer _arguments;
            private readonly MaterialPropertyBlock _properties;
            private readonly Bounds _bounds;
            private readonly Bounds[] _instanceBounds;
            private readonly Vector2[] _instancePositions;
            private readonly bool[] _hasHeight;
            private readonly uint[] _visibleIndices;
            private readonly uint[] _drawArguments;
            public int Count { get; }
            public int VisibleCount { get; private set; }

            public GrassChunk(Mesh mesh, Matrix4x4[] matrices, GrassProperties[] grass, uint[] indices, Bounds bounds)
            {
                Count = matrices.Length;
                _bounds = bounds;
                _instanceBounds = new Bounds[Count];
                _instancePositions = new Vector2[Count];
                _hasHeight = new bool[Count];
                _visibleIndices = new uint[Count];
                _drawArguments = new uint[] { mesh.GetIndexCount(0), 0, mesh.GetIndexStart(0),
                    (uint)mesh.GetBaseVertex(0), 0 };
                if (Count == 0)
                    return;
                try
                {
                    Bounds meshBounds = mesh.bounds;
                    for (int i = 0; i < Count; i++)
                    {
                        Matrix4x4 matrix = matrices[i];
                        _hasHeight[i] = grass[i].Height > 0f;
                        _instancePositions[i] = new Vector2(matrix.m03, matrix.m23);
                        Vector3 extents = meshBounds.extents;
                        Vector3 worldExtents = new Vector3(
                            Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y + Mathf.Abs(matrix.m02) * extents.z,
                            Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z,
                            Mathf.Abs(matrix.m20) * extents.x + Mathf.Abs(matrix.m21) * extents.y + Mathf.Abs(matrix.m22) * extents.z);
                        _instanceBounds[i] = new Bounds(matrix.MultiplyPoint3x4(meshBounds.center), worldExtents * 2f);
                    }
                    _matrices = new ComputeBuffer(Count, sizeof(float) * 16);
                    _grass = new ComputeBuffer(Count, sizeof(float) * 4);
                    _indices = new ComputeBuffer(Count, sizeof(uint));
                    _arguments = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
                    _matrices.SetData(matrices);
                    _grass.SetData(grass);
                    _indices.SetData(indices);
                    _arguments.SetData(_drawArguments);
                    _properties = new MaterialPropertyBlock();
                    _properties.SetBuffer("_MeshProperties", _matrices);
                    _properties.SetBuffer("_GrassProperties", _grass);
                    _properties.SetBuffer("_ClipProperties", _indices);
                    _properties.SetTexture("_WindNoiseTexture", Texture2D.blackTexture);
                    _properties.SetTexture("_InteractionTexture", Texture2D.blackTexture);
                    _properties.SetVector("_WindDirection", Vector3.right);
                    _properties.SetVector("_InteractionDirection", Vector3.up);
                    _properties.SetFloat("_WindScale", 1f);
                    _properties.SetFloat("_WindStrength", 0f);
                    _properties.SetFloat("_GroundColorBlend", 0f);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Cull(Plane[] planes, Vector3 cameraPosition, float radius)
            {
                if (Count == 0)
                    return;
                int visible = 0;
                if (GeometryUtility.TestPlanesAABB(planes, _bounds))
                {
                    double radiusSquared = (double)radius * radius;
                    for (int i = 0; i < Count; i++)
                    {
                        double dx = (double)_instancePositions[i].x - cameraPosition.x;
                        double dz = (double)_instancePositions[i].y - cameraPosition.z;
                        if (_hasHeight[i] && dx * dx + dz * dz <= radiusSquared &&
                            GeometryUtility.TestPlanesAABB(planes, _instanceBounds[i]))
                            _visibleIndices[visible++] = (uint)i;
                    }
                }
                VisibleCount = visible;
                if (visible > 0)
                    _indices.SetData(_visibleIndices, 0, 0, visible);
                _drawArguments[1] = (uint)visible;
                _arguments.SetData(_drawArguments);
            }

            public void Draw(Mesh mesh, Material material, Camera camera, int layer)
            {
                if (VisibleCount == 0)
                    return;
                Graphics.DrawMeshInstancedIndirect(mesh, 0, material, _bounds, _arguments, 0, _properties,
                    ShadowCastingMode.Off, false, layer, camera);
            }

            public void Dispose()
            {
                _matrices?.Release();
                _grass?.Release();
                _indices?.Release();
                _arguments?.Release();
                _matrices = null;
                _grass = null;
                _indices = null;
                _arguments = null;
            }
        }
    }

    /// <summary>Serialized, terrain-relative height overrides independent of generated instances and chunks.</summary>
    [Serializable]
    internal sealed class GrassHeightPaintData : ISerializationCallbackReceiver
    {
        public const int Resolution = 256;
        private const int Last = Resolution - 1;
        private const int TileSize = 16;
        private const int TilesPerSide = Resolution / TileSize;

        [Serializable]
        private sealed class Tile
        {
            public int Key;
            public float[] Values;
        }

        [SerializeField] private List<Tile> _tiles = new List<Tile>();
        [NonSerialized] private Dictionary<int, Tile> _tileLookup;

        public float CellWidth(float extent) => extent / Last;
        public void InvalidateCache() => _tileLookup = null;
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() => InvalidateCache();

        public float Sample(Vector3 position, Vector3 origin, float width, float depth)
        {
            if (_tiles == null || _tiles.Count == 0 || !ValidExtent(width, depth))
                return 1f;
            float u = (position.x - origin.x) / width;
            float v = (position.z - origin.z) / depth;
            if (!Finite(u) || !Finite(v) || u < 0f || u > 1f || v < 0f || v > 1f)
                return 1f;
            float x = u * Last;
            float z = v * Last;
            int x0 = Mathf.FloorToInt(x);
            int z0 = Mathf.FloorToInt(z);
            int x1 = Mathf.Min(x0 + 1, Last);
            int z1 = Mathf.Min(z0 + 1, Last);
            Dictionary<int, Tile> lookup = GetLookup();
            float a = Mathf.Lerp(Read(lookup, x0, z0), Read(lookup, x1, z0), x - x0);
            float b = Mathf.Lerp(Read(lookup, x0, z1), Read(lookup, x1, z1), x - x0);
            return Mathf.Clamp01(Mathf.Lerp(a, b, z - z0));
        }

        public bool Paint(Vector3 center, float radius, float strength, Vector3 origin, float width, float depth)
        {
            if (!ValidExtent(width, depth) || !Finite(center.x) || !Finite(center.z) ||
                !Finite(radius) || !Finite(strength) || radius <= 0f || Mathf.Approximately(strength, 0f))
                return false;
            if (center.x + radius < origin.x || center.x - radius > origin.x + width ||
                center.z + radius < origin.z || center.z - radius > origin.z + depth)
                return false;
            if ((_tiles == null || _tiles.Count == 0) && strength > 0f)
                return false;

            int minX = Mathf.CeilToInt(Mathf.Clamp01((center.x - radius - origin.x) / width) * Last);
            int maxX = Mathf.FloorToInt(Mathf.Clamp01((center.x + radius - origin.x) / width) * Last);
            int minZ = Mathf.CeilToInt(Mathf.Clamp01((center.z - radius - origin.z) / depth) * Last);
            int maxZ = Mathf.FloorToInt(Mathf.Clamp01((center.z + radius - origin.z) / depth) * Last);
            bool changed = false;
            Dictionary<int, Tile> lookup = GetLookup();
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                float dx = origin.x + width * x / Last - center.x;
                float dz = origin.z + depth * z / Last - center.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance >= radius)
                    continue;
                float u = 1f - distance / radius;
                float weight = u * u * (3f - 2f * u);
                int key = TileKey(x, z);
                int index = TileIndex(x, z);
                lookup.TryGetValue(key, out Tile tile);
                float oldValue = tile != null && tile.Values != null && tile.Values.Length == TileSize * TileSize
                    ? tile.Values[index] : 1f;
                float newValue = Mathf.Clamp01(oldValue + strength * weight);
                if (newValue == oldValue)
                    continue;
                if (tile == null)
                {
                    tile = new Tile { Key = key };
                    if (_tiles == null)
                        _tiles = new List<Tile>();
                    _tiles.Add(tile);
                    lookup.Add(key, tile);
                }
                if (tile.Values == null || tile.Values.Length != TileSize * TileSize)
                {
                    tile.Values = new float[TileSize * TileSize];
                    for (int i = 0; i < tile.Values.Length; i++)
                        tile.Values[i] = 1f;
                }
                tile.Values[index] = newValue;
                changed = true;
            }
            return changed;
        }

        private Dictionary<int, Tile> GetLookup()
        {
            if (_tileLookup != null)
                return _tileLookup;
            _tileLookup = new Dictionary<int, Tile>();
            if (_tiles != null)
                foreach (Tile tile in _tiles)
                    if (tile != null)
                        _tileLookup[tile.Key] = tile;
            return _tileLookup;
        }

        private static float Read(Dictionary<int, Tile> lookup, int x, int z)
        {
            if (!lookup.TryGetValue(TileKey(x, z), out Tile tile) || tile.Values == null ||
                tile.Values.Length != TileSize * TileSize)
                return 1f;
            return tile.Values[TileIndex(x, z)];
        }

        private static int TileKey(int x, int z) => x / TileSize + z / TileSize * TilesPerSide;
        private static int TileIndex(int x, int z) => x % TileSize + z % TileSize * TileSize;

        private static bool ValidExtent(float width, float depth) =>
            Finite(width) && Finite(depth) && width > 0f && depth > 0f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
