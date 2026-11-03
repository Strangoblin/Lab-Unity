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
        // Cap per-chunk allocations: 10 plants/m² means at most 36,000 plants in 3x3 chunks.
        [SerializeField, Range(0f, 10f)] private float _density = 0.8f;
        [SerializeField, Range(0f, 10f)] private float _size = 1f;

        private readonly Dictionary<Vector2Int, GrassChunk> _chunks = new Dictionary<Vector2Int, GrassChunk>();
        private Material _runtimeMaterial;
        private float _builtDensity = float.NaN;
        private float _builtSize = float.NaN;
        private int _builtTerrainVersion = -1;
        private GrassTerrainSource _activeTerrain;
        private Mesh _activeMesh;
        private Material _activeMaterial;
        private bool _limitReported;
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
        public bool IsChunkActive(int x, int z) => _chunks.ContainsKey(new Vector2Int(x, z));

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
                chunk.Cull(_frustumPlanes);
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
                _terrain.ChunkWidth <= 0f || _terrain.ChunkCountX <= 0 || _terrain.ChunkCountZ <= 0)
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
            if ((double)density * _terrain.ChunkWidth * _terrain.ChunkWidth > MaxInstancesPerChunk)
            {
                ClearChunks();
                if (!_limitReported)
                    Debug.LogError($"Grass density exceeds the {MaxInstancesPerChunk} instance limit per chunk.", this);
                _limitReported = true;
                return false;
            }
            _limitReported = false;
            if (_builtDensity != density || _builtSize != size || _builtTerrainVersion != _terrain.DataVersion)
            {
                ClearChunks();
                _builtDensity = density;
                _builtSize = size;
                _builtTerrainVersion = _terrain.DataVersion;
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
            float width = _terrain.ChunkWidth;
            float area = width * width;
            System.Random random = new System.Random(unchecked(73129 ^ coordinate.x * 73856093 ^ coordinate.y * 19349663));
            float expected = density * area;
            int count = Mathf.FloorToInt(expected);
            if (random.NextDouble() < expected - count)
                count++;
            Matrix4x4[] matrices = new Matrix4x4[count];
            GrassProperties[] grass = new GrassProperties[count];
            uint[] indices = new uint[count];
            float minX = _terrain.Origin.x + coordinate.x * width;
            float minZ = _terrain.Origin.z + coordinate.y * width;
            float minHeight = float.PositiveInfinity;
            float maxHeight = float.NegativeInfinity;
            int accepted = 0;
            for (int i = 0; i < count; i++)
            {
                float x = minX + (float)random.NextDouble() * width;
                float z = minZ + (float)random.NextDouble() * width;
                if (!_terrain.TrySample(x, z, out Vector3 position, out Vector3 normal))
                    continue;
                minHeight = Mathf.Min(minHeight, position.y);
                maxHeight = Mathf.Max(maxHeight, position.y);
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal);
                float scale = Mathf.Lerp(0.8f, 1.2f, (float)random.NextDouble()) * size;
                matrices[accepted] = Matrix4x4.TRS(position, rotation, Vector3.one * scale);
                grass[accepted] = new GrassProperties { Normal = normal, Height = 1f };
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
            // Conservatively include the rotated/scaled mesh, with no per-instance culling.
            float radius = (_grassMesh.bounds.center.magnitude + _grassMesh.bounds.extents.magnitude) * size * 1.2f;
            if (count == 0)
                minHeight = maxHeight = _terrain.Origin.y;
            Bounds bounds = new Bounds(new Vector3(minX + width * 0.5f, (minHeight + maxHeight) * 0.5f,
                minZ + width * 0.5f), new Vector3(width + 2f * radius,
                Mathf.Max(0.01f, maxHeight - minHeight + 2f * radius), width + 2f * radius));
            return new GrassChunk(_grassMesh, matrices, grass, indices, bounds);
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= BeginContextRendering;
            Release();
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
            private readonly uint[] _visibleIndices;
            private readonly uint[] _drawArguments;
            public int Count { get; }
            public int VisibleCount { get; private set; }

            public GrassChunk(Mesh mesh, Matrix4x4[] matrices, GrassProperties[] grass, uint[] indices, Bounds bounds)
            {
                Count = matrices.Length;
                _bounds = bounds;
                _instanceBounds = new Bounds[Count];
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

            public void Cull(Plane[] planes)
            {
                if (Count == 0)
                    return;
                int visible = 0;
                if (GeometryUtility.TestPlanesAABB(planes, _bounds))
                {
                    for (int i = 0; i < Count; i++)
                        if (GeometryUtility.TestPlanesAABB(planes, _instanceBounds[i]))
                            _visibleIndices[visible++] = (uint)i;
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
}
