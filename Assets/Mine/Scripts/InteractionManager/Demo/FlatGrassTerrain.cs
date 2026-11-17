using Mine.Grass;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.Interaction
{
    /// <summary>
    /// A flat rectangular grass source for the interaction demo.
    ///
    /// <see cref="ProceduralGrassTerrain"/> is a fixed 100×100 noisy terrain; interaction
    /// surfaces need a known flat reference plane, so this source exposes a plain grid
    /// of one surface category and a configurable ground layer.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class FlatGrassTerrain : GrassTerrainSource
    {
        private const float MinSize = 0.1f;
        private const int MinSubdivisions = 1;

        [SerializeField] private Vector2 _size = new Vector2(12f, 12f);
        [SerializeField, Min(MinSize)] private float _chunkSize = 6f;
        [SerializeField] private Material _groundMaterial;
        [Tooltip("Layer of the generated ground surface. The interaction camera must render it for depth references.")]
        [SerializeField] private int _surfaceLayer = 6;
        [SerializeField, Min(MinSubdivisions)] private int _surfaceSubdivisions = 8;
        [SerializeField] private Color _fallbackGroundColor = new Color(0.24f, 0.33f, 0.15f, 1f);
        [Tooltip("关掉后只留草叶的采样源（TrySample），不再生成可见地面——" +
                 "把地面交给 LayeredGroundSurface 时用。")]
        [SerializeField] private bool _renderGround = true;

        private GameObject _root;
        private Mesh _mesh;
        private Material _ownedMaterial;
        private Vector3 _origin;
        private Vector2 _builtSize;
        private Vector3 _builtPosition;
        private float _builtChunkSize;
        private int _builtLayer;
        private int _builtSubdivisions;
        private Material _builtMaterial;
        private bool _builtRenderGround;
        private int _dataVersion;

        public override Vector3 Origin => _origin;
        public override int ChunkCountX => Mathf.CeilToInt(Width / ChunkWidth);
        public override int ChunkCountZ => Mathf.CeilToInt(Depth / ChunkWidth);
        public override float ChunkWidth => Mathf.Max(ChunkSize, MinSize);
        public override float Width => Mathf.Max(_size.x, MinSize);
        public override float Depth => Mathf.Max(_size.y, MinSize);
        public override bool HasData => _root != null && _mesh != null;
        public override int DataVersion => _dataVersion;

        public float ChunkSize { get => Mathf.Max(_chunkSize, MinSize); set => _chunkSize = Mathf.Max(value, MinSize); }
        public int SurfaceLayer { get => _surfaceLayer; set => _surfaceLayer = value; }

        public override void Initialize()
        {
            Vector3 position = transform.position;
            bool needsRebuild = !HasData
                || _builtSize != _size
                || _builtPosition != position
                || _builtChunkSize != ChunkSize
                || _builtLayer != _surfaceLayer
                || _builtSubdivisions != _surfaceSubdivisions
                || _builtMaterial != _groundMaterial
                || _builtRenderGround != _renderGround;

            if (!needsRebuild)
                return;

            Release();

            _builtSize = _size;
            _builtPosition = position;
            _builtChunkSize = ChunkSize;
            _builtLayer = _surfaceLayer;
            _builtSubdivisions = Mathf.Max(_surfaceSubdivisions, MinSubdivisions);
            _builtMaterial = _groundMaterial;
            _builtRenderGround = _renderGround;

            _origin = position - new Vector3(Width * 0.5f, 0f, Depth * 0.5f);

            Material material = _groundMaterial;
            if (material == null && _renderGround)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null)
                {
                    Debug.LogError("Cannot find a ground shader for the flat grass terrain.", this);
                    return;
                }
                _ownedMaterial = new Material(shader) { name = "Flat Grass Ground (Runtime)", hideFlags = HideFlags.DontSave };
                _ownedMaterial.color = _fallbackGroundColor;
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

            if (!HasData || float.IsNaN(worldX) || float.IsNaN(worldZ) ||
                float.IsInfinity(worldX) || float.IsInfinity(worldZ))
                return false;

            if (worldX < _origin.x || worldZ < _origin.z ||
                worldX > _origin.x + Width || worldZ > _origin.z + Depth)
                return false;

            position = new Vector3(worldX, _origin.y, worldZ);
            layer = GrassTerrainLayer.Grass;
            return true;
        }

        private void CreateSurface(Material material)
        {
            int subdivisions = _builtSubdivisions;
            int side = subdivisions + 1;
            Vector3[] vertices = new Vector3[side * side];
            Vector2[] uvs = new Vector2[side * side];
            Vector3[] normals = new Vector3[side * side];
            int[] triangles = new int[subdivisions * subdivisions * 6];

            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                int i = x + z * side;
                float u = x / (float)subdivisions;
                float v = z / (float)subdivisions;
                vertices[i] = new Vector3(u * Width, 0f, v * Depth);
                uvs[i] = new Vector2(u, v);
                normals[i] = Vector3.up;
            }

            int index = 0;
            for (int z = 0; z < subdivisions; z++)
            for (int x = 0; x < subdivisions; x++)
            {
                int i = x + z * side;
                triangles[index++] = i;
                triangles[index++] = i + side;
                triangles[index++] = i + 1;
                triangles[index++] = i + 1;
                triangles[index++] = i + side;
                triangles[index++] = i + side + 1;
            }

            _mesh = new Mesh { name = "Flat Grass Surface", hideFlags = HideFlags.DontSave };
            _mesh.vertices = vertices;
            _mesh.uv = uvs;
            _mesh.normals = normals;
            _mesh.triangles = triangles;
            _mesh.RecalculateBounds();

            _root = new GameObject("Flat Grass Surface (Runtime)") { hideFlags = HideFlags.DontSave };
            _root.layer = _surfaceLayer;
            _root.transform.SetParent(transform, false);
            _root.transform.position = _origin;
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            if (!_renderGround)
                return;   // 只要采样源：草地由 LayeredGroundSurface 画
            MeshRenderer renderer = _root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void OnDisable() => Release();

        private void Release()
        {
            if (_root != null) DestroyOwned(_root);
            _root = null;
            if (_mesh != null) DestroyOwned(_mesh);
            _mesh = null;
            if (_ownedMaterial != null) DestroyOwned(_ownedMaterial);
            _ownedMaterial = null;
        }

        private static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else                       DestroyImmediate(value);
        }
    }
}
