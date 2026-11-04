using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.Grass
{
    /// <summary>A deterministic terrain grid with one continuous surface and configurable logical chunks.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ProceduralGrassTerrain : GrassTerrainSource
    {
        public const float TerrainSize = 100f;
        private const int TotalCells = 40;
        private const float CellSize = TerrainSize / TotalCells;
        private const float MinChunkSize = 0.1f;
        private const float MaxChunkSize = 10000f;
        private const float DefaultChunkSize = 20f;

        [SerializeField] private Material _groundMaterial;
        [SerializeField, Min(MinChunkSize)] private float _chunkSize = DefaultChunkSize;

        private float[,] _heights;
        private GrassTerrainLayer[,] _layers;
        private GameObject _root;
        private Mesh _mesh;
        private Material _ownedMaterial;
        private Vector3 _origin;
        private float _builtChunkSize;
        private bool _invalidTransformReported;
        private int _dataVersion;

        public float ChunkSize { get => SafeChunkSize(_chunkSize); set => _chunkSize = SafeChunkSize(value); }
        public override Vector3 Origin => _origin;
        public override int ChunkCountX => Mathf.CeilToInt(TerrainSize / ChunkSize);
        public override int ChunkCountZ => Mathf.CeilToInt(TerrainSize / ChunkSize);
        public override float ChunkWidth => ChunkSize;
        public override float Width => TerrainSize;
        public override float Depth => TerrainSize;
        public override bool HasData => _heights != null && _layers != null && _mesh != null;
        public int SurfaceMeshCount => _mesh != null ? 1 : 0;
        public int SurfaceChunkCount => SurfaceMeshCount;
        public override int DataVersion => _dataVersion;

        public override void Initialize()
        {
            float chunkSize = ChunkSize;
            Vector3 expectedPosition = _origin + new Vector3(TerrainSize * 0.5f, 0f, TerrainSize * 0.5f);
            bool validTransform = Quaternion.Angle(transform.rotation, Quaternion.identity) < 0.01f &&
                (transform.lossyScale - Vector3.one).sqrMagnitude < 0.000001f;
            if (_heights != null && (!validTransform || _root == null || _mesh == null ||
                (transform.position - expectedPosition).sqrMagnitude > 0.000001f))
                Release();
            if (!validTransform)
            {
                if (!_invalidTransformReported)
                    Debug.LogError("Grass terrain requires identity world rotation and unit world scale.", this);
                _invalidTransformReported = true;
                return;
            }
            _invalidTransformReported = false;
            if (_heights != null)
            {
                if (chunkSize != _builtChunkSize)
                {
                    _builtChunkSize = chunkSize;
                    _dataVersion++;
                }
                return;
            }

            _builtChunkSize = chunkSize;
            _origin = transform.position - new Vector3(TerrainSize * 0.5f, 0f, TerrainSize * 0.5f);
            _heights = new float[TotalCells + 1, TotalCells + 1];
            _layers = new GrassTerrainLayer[TotalCells, TotalCells];
            for (int z = 0; z <= TotalCells; z++)
            for (int x = 0; x <= TotalCells; x++)
                _heights[x, z] = _origin.y + (Mathf.PerlinNoise(x * 0.075f + 13f, z * 0.075f + 19f) - 0.5f) * 2f;
            for (int z = 0; z < TotalCells; z++)
            for (int x = 0; x < TotalCells; x++)
                _layers[x, z] = GrassTerrainLayer.Grass;

            Material material = _groundMaterial;
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");
                if (shader == null)
                {
                    Debug.LogError("Cannot find a ground shader for the grass terrain.", this);
                    Release();
                    return;
                }
                _ownedMaterial = new Material(shader) { name = "Grass Terrain Ground (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave };
                _ownedMaterial.color = new Color(0.34f, 0.43f, 0.23f, 1f);
                material = _ownedMaterial;
            }

            CreateSurface(material);
            _dataVersion++;
        }

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
            if (_heights == null || _layers == null || float.IsNaN(worldX) || float.IsNaN(worldZ) ||
                float.IsInfinity(worldX) || float.IsInfinity(worldZ))
                return false;
            float gridX = (worldX - _origin.x) / CellSize;
            float gridZ = (worldZ - _origin.z) / CellSize;
            if (gridX < 0f || gridZ < 0f || gridX >= TotalCells || gridZ >= TotalCells)
                return false;
            int x = Mathf.FloorToInt(gridX);
            int z = Mathf.FloorToInt(gridZ);
            float u = gridX - x;
            float v = gridZ - z;
            Vector3 p00 = Vertex(x, z);
            Vector3 p10 = Vertex(x + 1, z);
            Vector3 p01 = Vertex(x, z + 1);
            Vector3 p11 = Vertex(x + 1, z + 1);
            float height;
            if (u + v <= 1f)
            {
                height = p00.y + (p10.y - p00.y) * u + (p01.y - p00.y) * v;
                normal = Vector3.Cross(p01 - p00, p10 - p00).normalized;
            }
            else
            {
                height = p11.y + (p01.y - p11.y) * (1f - u) + (p10.y - p11.y) * (1f - v);
                normal = Vector3.Cross(p01 - p10, p11 - p10).normalized;
            }
            position = new Vector3(worldX, height, worldZ);
            layer = _layers[x, z];
            return true;
        }

        private Vector3 Vertex(int x, int z)
        {
            return new Vector3(_origin.x + x * CellSize, _heights[x, z], _origin.z + z * CellSize);
        }

        private void CreateSurface(Material material)
        {
            int side = TotalCells + 1;
            Vector3[] vertices = new Vector3[side * side];
            int[] triangles = new int[TotalCells * TotalCells * 6];
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
                vertices[x + z * side] = Vertex(x, z) - _origin;
            int index = 0;
            for (int z = 0; z < TotalCells; z++)
            for (int x = 0; x < TotalCells; x++)
            {
                int i = x + z * side;
                triangles[index++] = i;
                triangles[index++] = i + side;
                triangles[index++] = i + 1;
                triangles[index++] = i + 1;
                triangles[index++] = i + side;
                triangles[index++] = i + side + 1;
            }
            _mesh = new Mesh { name = "Grass Terrain Surface", hideFlags = HideFlags.DontSave };
            _mesh.vertices = vertices;
            _mesh.triangles = triangles;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            _root = new GameObject("Grass Terrain Surface (Runtime)") { hideFlags = HideFlags.DontSave };
            _root.transform.SetParent(transform, false);
            _root.transform.position = _origin;
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = _root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static float SafeChunkSize(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? DefaultChunkSize : Mathf.Clamp(value, MinChunkSize, MaxChunkSize);

        private void OnValidate() => _chunkSize = SafeChunkSize(_chunkSize);

        private void OnDisable() => Release();

        private void Release()
        {
            if (_root != null)
                DestroyOwned(_root);
            _root = null;
            if (_mesh != null)
                DestroyOwned(_mesh);
            _mesh = null;
            if (_ownedMaterial != null)
                DestroyOwned(_ownedMaterial);
            _ownedMaterial = null;
            _heights = null;
            _layers = null;
        }

        private static void DestroyOwned(Object value)
        {
            if (Application.isPlaying)
                Destroy(value);
            else
                DestroyImmediate(value);
        }
    }
}
