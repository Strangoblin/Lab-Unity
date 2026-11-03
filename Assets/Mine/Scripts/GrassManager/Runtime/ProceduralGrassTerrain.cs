using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.Grass
{
    /// <summary>A small, deterministic terrain data source and its matching 5x5 surface.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ProceduralGrassTerrain : GrassTerrainSource
    {
        public const int ChunkCount = 5;
        public const int CellsPerChunk = 8;
        public const float ChunkSize = 20f;
        private const int TotalCells = ChunkCount * CellsPerChunk;
        private const float CellSize = ChunkSize / CellsPerChunk;

        [SerializeField] private Material _groundMaterial;

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private float[,] _heights;
        private GameObject _root;
        private Material _ownedMaterial;
        private Vector3 _origin;
        private bool _invalidTransformReported;
        private int _dataVersion;

        public override Vector3 Origin => _origin;
        public override int ChunkCountX => ChunkCount;
        public override int ChunkCountZ => ChunkCount;
        public override float ChunkWidth => ChunkSize;
        public override bool HasData => _heights != null && _meshes.Count == ChunkCount * ChunkCount;
        public int SurfaceChunkCount => _meshes.Count;
        public override int DataVersion => _dataVersion;

        public override void Initialize()
        {
            Vector3 expectedPosition = _origin + new Vector3(ChunkCount * ChunkSize * 0.5f, 0f,
                ChunkCount * ChunkSize * 0.5f);
            bool validTransform = Quaternion.Angle(transform.rotation, Quaternion.identity) < 0.01f &&
                (transform.lossyScale - Vector3.one).sqrMagnitude < 0.000001f;
            if (_heights != null && (!validTransform || (transform.position - expectedPosition).sqrMagnitude > 0.000001f))
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
                return;

            // The grid is in world coordinates; the generated surface and sampling use it together.
            _origin = transform.position - new Vector3(ChunkCount * ChunkSize * 0.5f, 0f,
                ChunkCount * ChunkSize * 0.5f);
            _heights = new float[TotalCells + 1, TotalCells + 1];
            for (int z = 0; z <= TotalCells; z++)
            for (int x = 0; x <= TotalCells; x++)
                _heights[x, z] = _origin.y + (Mathf.PerlinNoise(x * 0.075f + 13f, z * 0.075f + 19f) - 0.5f) * 2f;

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

            _root = new GameObject("Grass Terrain Chunks (Runtime)") { hideFlags = HideFlags.DontSave };
            _root.transform.SetParent(transform, false);
            for (int z = 0; z < ChunkCount; z++)
            for (int x = 0; x < ChunkCount; x++)
                CreateChunk(x, z, material);
            _dataVersion++;
        }

        public override bool TrySample(float worldX, float worldZ, out Vector3 position, out Vector3 normal)
        {
            position = default;
            normal = Vector3.up;
            if (_heights == null)
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
            return true;
        }

        private Vector3 Vertex(int x, int z) => new Vector3(_origin.x + x * CellSize,
            _heights[x, z], _origin.z + z * CellSize);

        private void CreateChunk(int chunkX, int chunkZ, Material material)
        {
            int side = CellsPerChunk + 1;
            Vector3[] vertices = new Vector3[side * side];
            int[] triangles = new int[CellsPerChunk * CellsPerChunk * 6];
            Vector3 chunkOrigin = new Vector3(_origin.x + chunkX * ChunkSize, 0f,
                _origin.z + chunkZ * ChunkSize);
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
                vertices[x + z * side] = Vertex(chunkX * CellsPerChunk + x, chunkZ * CellsPerChunk + z) - chunkOrigin;
            int index = 0;
            for (int z = 0; z < CellsPerChunk; z++)
            for (int x = 0; x < CellsPerChunk; x++)
            {
                int i = x + z * side;
                triangles[index++] = i;
                triangles[index++] = i + side;
                triangles[index++] = i + 1;
                triangles[index++] = i + 1;
                triangles[index++] = i + side;
                triangles[index++] = i + side + 1;
            }
            Mesh mesh = new Mesh { name = $"Grass Terrain {chunkX},{chunkZ}", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _meshes.Add(mesh);
            GameObject chunk = new GameObject(mesh.name) { hideFlags = HideFlags.DontSave };
            chunk.transform.SetParent(_root.transform, false);
            chunk.transform.position = chunkOrigin;
            chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = chunk.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void OnDisable() => Release();

        private void Release()
        {
            if (_root != null)
                DestroyOwned(_root);
            _root = null;
            foreach (Mesh mesh in _meshes)
                DestroyOwned(mesh);
            _meshes.Clear();
            if (_ownedMaterial != null)
                DestroyOwned(_ownedMaterial);
            _ownedMaterial = null;
            _heights = null;
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
