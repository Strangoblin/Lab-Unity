// ═══════════════════════════════════════════════════════════════
//  LayeredGroundSurface — 单网格多层地面（数组驱动 + 纯 alpha 叠层）
//
//  · 层由 _layers 数组定义，每层一组共用 BRDF 的参数
//    （颜色 / 阴影色 / 光滑度 / 金属度），增删即生效；
//  · 每个 texel 存每层一个 alpha，和为 1：权重在一张 Texture2DArray 里，
//    一层一片，层数可以是任意 N（≤ MaxLayers）；
//  · 笔刷每落一笔就是把当前权重往目标层 lerp 一次：
//        weights = lerp(weights, oneHot(layer), alpha)
//    等价于「总颜色 = lerp(当前颜色, 该层颜色, alpha)」，后刷的层自然盖在
//    先刷的层上面；没有任何高度/顺序状态。几何不动。
//
//  笔画数据是稀疏 tile，随场景序列化；层数变化时 tile 自动扩/缩通道。
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.Interaction
{
    /// <summary>该层吃哪一路交互场。与 InteractionManager 的各子集一一对应。</summary>
    public enum LayeredGroundInteractionSource : byte
    {
        None = 0,
        Grass = 1,   // _InteractionGrassTex：压弯量
        Snow = 2,    // _InteractionSnowTex：压痕深度
        Sand = 3     // _InteractionSandTex：有符号沙面高度
    }

    /// <summary>一层的 BRDF 参数 + 交互绑定。所有层共用同一个着色函数，这里只存输入。</summary>
    [Serializable]
    public sealed class LayeredGroundLayerSettings
    {
        public string Name = "Layer";
        public Color BaseColor = Color.white;
        public Color ShadowTint = Color.gray;
        [Range(0f, 1f)] public float Smoothness = 0.1f;
        [Range(0f, 1f)] public float Metallic;

        [Header("Interaction")]
        [Tooltip("绑定到哪一路交互场；None = 这一层不响应交互。")]
        public LayeredGroundInteractionSource Interaction = LayeredGroundInteractionSource.None;
        [Tooltip("只用于法线/视差：假定位移了这么多米时的坡度。")]
        [Range(0f, 0.6f)] public float ImprintDepth = 0.22f;
        [Range(0f, 4f)] public float NormalStrength = 1f;
        [Tooltip("压痕处的明暗变化强度（雪/草变暗，沙坑变亮）。")]
        [Range(0f, 1f)] public float Darkening = 0.18f;

        public LayeredGroundLayerSettings() { }

        public LayeredGroundLayerSettings(string name, Color baseColor, Color shadowTint,
            float smoothness, float metallic = 0f,
            LayeredGroundInteractionSource interaction = LayeredGroundInteractionSource.None,
            float imprintDepth = 0.22f, float normalStrength = 1f, float darkening = 0.18f)
        {
            Name = name;
            BaseColor = baseColor;
            ShadowTint = shadowTint;
            Smoothness = smoothness;
            Metallic = metallic;
            Interaction = interaction;
            ImprintDepth = imprintDepth;
            NormalStrength = normalStrength;
            Darkening = darkening;
        }
    }

    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class LayeredGroundSurface : MonoBehaviour
    {
        /// <summary>与 LayeredGround.shader 的 LAYER_MAX 保持一致。</summary>
        public const int MaxLayers = 8;

        private const int Resolution = LayeredGroundPaintData.Resolution;

        [SerializeField] private Material _sourceMaterial;
        [SerializeField] private Mesh _mesh;
        [SerializeField, Min(0.1f)] private float _size = 12f;
        [SerializeField] private int _surfaceLayer = 6;
        [SerializeField] private List<LayeredGroundLayerSettings> _layers = new List<LayeredGroundLayerSettings>();
        // 笔画数据不画进 Inspector：tile 列表随绘制增长，每帧展开它会拖慢编辑器。
        [SerializeField, HideInInspector] private LayeredGroundPaintData _paint = new LayeredGroundPaintData();

        private MeshRenderer _renderer;
        private Texture2DArray _layerWeights;
        private Color32[][] _layerPixels;
        private float[] _sample;
        private MaterialPropertyBlock _block;

        private readonly Vector4[] _colorBuffer = new Vector4[MaxLayers];
        private readonly Vector4[] _shadowBuffer = new Vector4[MaxLayers];
        private readonly float[] _smoothnessBuffer = new float[MaxLayers];
        private readonly float[] _metallicBuffer = new float[MaxLayers];
        // 交互绑定：0 none / 1 grass / 2 snow / 3 sand，其余三项是每层的落差参数。
        private readonly float[] _interactionBuffer = new float[MaxLayers];
        private readonly float[] _imprintDepthBuffer = new float[MaxLayers];
        private readonly float[] _normalStrengthBuffer = new float[MaxLayers];
        private readonly float[] _darkeningBuffer = new float[MaxLayers];

        public Mesh Mesh => _mesh;
        public float Size => Mathf.Max(_size, 0.1f);
        public IReadOnlyList<LayeredGroundLayerSettings> Layers => _layers;
        public LayeredGroundPaintData PaintData => _paint;

        // ════════════════════════════════════════════════════════════
        //  生命周期
        // ════════════════════════════════════════════════════════════

        private void OnEnable() => Initialize();

        private void OnValidate()
        {
            if (isActiveAndEnabled)
                Initialize();
        }

        /// <summary>幂等地建立渲染器、纹理与属性块；构建脚本也会直接调用。</summary>
        public void Initialize()
        {
            if (_paint == null)
                _paint = new LayeredGroundPaintData();
            EnsureDefaultLayers();
            EnsureRenderer();
            SyncLayers();
            Rebuild();
        }

        /// <summary>用给定的一组层替换现有配置（层数、参数立即生效）。</summary>
        public void SetLayers(params LayeredGroundLayerSettings[] layers)
        {
            if (_layers == null)
                _layers = new List<LayeredGroundLayerSettings>();
            _layers.Clear();
            if (layers != null)
                foreach (LayeredGroundLayerSettings layer in layers)
                    if (layer != null)
                        _layers.Add(layer);
            Initialize();
        }

        private void EnsureDefaultLayers()
        {
            if (_layers == null)
                _layers = new List<LayeredGroundLayerSettings>();
            if (_layers.Count > 0)
                return;
            _layers.Add(new LayeredGroundLayerSettings("Grass",
                new Color(0.22f, 0.30f, 0.13f), new Color(0.09f, 0.14f, 0.07f), 0.08f,
                0f, LayeredGroundInteractionSource.Grass, 0.10f, 0.5f, 0.10f));
            _layers.Add(new LayeredGroundLayerSettings("Sand",
                new Color(0.80f, 0.66f, 0.40f), new Color(0.42f, 0.33f, 0.20f), 0.18f,
                0f, LayeredGroundInteractionSource.Sand, 0.30f, 1.0f, 0.12f));
            _layers.Add(new LayeredGroundLayerSettings("Snow",
                new Color(0.93f, 0.96f, 1.00f), new Color(0.47f, 0.61f, 0.82f), 0.35f,
                0f, LayeredGroundInteractionSource.Snow, 0.22f, 0.9f, 0.18f));
        }

        private void EnsureRenderer()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null)
                filter = gameObject.AddComponent<MeshFilter>();
            if (filter.sharedMesh != _mesh)
                filter.sharedMesh = _mesh;

            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null)
                _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = true;

            // 交互相机按层裁剪，表面必须落在 _surfaceLayer 上，否则深度参照和交互都看不到它。
            if (gameObject.layer != _surfaceLayer)
                gameObject.layer = _surfaceLayer;

            if (_renderer.sharedMaterial == null && _sourceMaterial != null)
                _renderer.sharedMaterial = _sourceMaterial;
        }

        /// <summary>层数组 → 笔画数据通道数 → 权重纹理深度，三者保持一致。</summary>
        private void SyncLayers()
        {
            for (int i = _layers.Count - 1; i >= 0; i--)
                if (_layers[i] == null)
                    _layers.RemoveAt(i);

            if (_layers.Count == 0)
                EnsureDefaultLayers();

            if (_layers.Count > MaxLayers)
            {
                Debug.LogWarning($"[LayeredGroundSurface] 最多支持 {MaxLayers} 层，多余的已忽略。", this);
                _layers.RemoveRange(MaxLayers, _layers.Count - MaxLayers);
            }

            _paint.EnsureLayerCount(_layers.Count);

            if (_sample == null || _sample.Length != _layers.Count)
                _sample = new float[_layers.Count];

            if (_layerWeights == null || _layerWeights.depth != _layers.Count)
            {
                if (_layerWeights != null)
                {
                    if (Application.isPlaying) Destroy(_layerWeights);
                    else DestroyImmediate(_layerWeights);
                }

                _layerWeights = new Texture2DArray(Resolution, Resolution, _layers.Count,
                    TextureFormat.R8, false, false)
                {
                    name = "Layered Ground Weights",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0,
                    hideFlags = HideFlags.DontSave
                };
            }

            // CPU 侧的像素缓冲与纹理分开判断：域重载 / 纹理复用都可能让两者不同步，
            // 一旦错位 Paint 会静默返回 false（笔刷看起来完全没反应）。
            if (_layerPixels == null || _layerPixels.Length != _layers.Count ||
                _layerPixels[0] == null || _layerPixels[0].Length != Resolution * Resolution)
            {
                _layerPixels = new Color32[_layers.Count][];
                for (int i = 0; i < _layerPixels.Length; i++)
                    _layerPixels[i] = new Color32[Resolution * Resolution];
            }
        }

        // ════════════════════════════════════════════════════════════
        //  纹理重建 — 笔画数据 → 每层一片权重
        // ════════════════════════════════════════════════════════════

        public void Rebuild()
        {
            if (_layerWeights == null || _layerPixels == null || _sample == null)
                return;

            int layerCount = _layers.Count;
            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                _paint.Sample(x, z, _sample);
                int i = x + z * Resolution;
                for (int layer = 0; layer < layerCount; layer++)
                    _layerPixels[layer][i] = ToWeight(_sample[layer]);
            }

            UploadWeights();
        }

        private void UploadWeights()
        {
            for (int layer = 0; layer < _layers.Count; layer++)
                _layerWeights.SetPixels32(_layerPixels[layer], layer);
            _layerWeights.Apply(false, false);
            ApplyProperties();
        }

        private void ApplyProperties()
        {
            if (_renderer == null || _layerWeights == null)
                return;

            _block ??= new MaterialPropertyBlock();
            int count = _layers.Count;
            for (int i = 0; i < count; i++)
            {
                LayeredGroundLayerSettings layer = _layers[i];
                _colorBuffer[i] = layer.BaseColor;
                _shadowBuffer[i] = layer.ShadowTint;
                _smoothnessBuffer[i] = layer.Smoothness;
                _metallicBuffer[i] = layer.Metallic;
                _interactionBuffer[i] = (float)layer.Interaction;
                _imprintDepthBuffer[i] = layer.ImprintDepth;
                _normalStrengthBuffer[i] = layer.NormalStrength;
                _darkeningBuffer[i] = layer.Darkening;
            }

            _block.SetTexture("_LayerWeights", _layerWeights);
            _block.SetFloat("_LayerCount", count);
            _block.SetVectorArray("_LayerBaseColor", _colorBuffer);
            _block.SetVectorArray("_LayerShadowTint", _shadowBuffer);
            _block.SetFloatArray("_LayerSmoothness", _smoothnessBuffer);
            _block.SetFloatArray("_LayerMetallic", _metallicBuffer);
            _block.SetFloatArray("_LayerInteraction", _interactionBuffer);
            _block.SetFloatArray("_LayerImprintDepth", _imprintDepthBuffer);
            _block.SetFloatArray("_LayerNormalStrength", _normalStrengthBuffer);
            _block.SetFloatArray("_LayerDarkening", _darkeningBuffer);
            _renderer.SetPropertyBlock(_block);
        }

        // ════════════════════════════════════════════════════════════
        //  绘制 — 世界空间圆形笔刷，一次落笔就是一次 alpha 合成
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 在世界坐标处落一笔：把权重往 layerIndex 推 alpha 比例。
        /// alpha 为 1 时该层完全覆盖；多次落笔按时间顺序自然叠加。
        /// 返回是否真的改动了数据。
        /// </summary>
        public bool Paint(Vector3 worldPoint, float radius, int layerIndex, float alpha)
        {
            if (_mesh == null || radius <= 0f || alpha <= 0f)
                return false;
            if (!EnsureState())
                return false;
            if (layerIndex < 0 || layerIndex >= _layers.Count)
                return false;
            if (!IsFinite(worldPoint) || !IsFinite(radius))
                return false;

            float size = Size;
            Vector3 origin = transform.position - new Vector3(size * 0.5f, 0f, size * 0.5f);
            float u = (worldPoint.x - origin.x) / size;
            float v = (worldPoint.z - origin.z) / size;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
                return false;

            int last = Resolution - 1;
            float centerX = u * last;
            float centerZ = v * last;
            float texelRadius = radius / size * last;
            if (texelRadius < 1f)
                texelRadius = 1f;

            int minX = Mathf.Max(0, Mathf.CeilToInt(centerX - texelRadius));
            int maxX = Mathf.Min(last, Mathf.FloorToInt(centerX + texelRadius));
            int minZ = Mathf.Max(0, Mathf.CeilToInt(centerZ - texelRadius));
            int maxZ = Mathf.Min(last, Mathf.FloorToInt(centerZ + texelRadius));
            if (minX > maxX || minZ > maxZ)
                return false;

            int layerCount = _layers.Count;
            bool changed = false;
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - centerX;
                float dz = z - centerZ;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance < texelRadius)
                {
                    float falloff = 1f - distance / texelRadius;
                    falloff = falloff * falloff * (3f - 2f * falloff);   // smoothstep 衰减
                    changed |= _paint.PaintTexel(x, z, layerIndex, falloff * alpha, _sample);
                }
                else
                {
                    _paint.Sample(x, z, _sample);
                }

                int i = x + z * Resolution;
                for (int layer = 0; layer < layerCount; layer++)
                    _layerPixels[layer][i] = ToWeight(_sample[layer]);
            }

            if (changed)
            {
                UploadWeights();
            }
            return changed;
        }

        // ════════════════════════════════════════════════════════════
        //  工具
        // ════════════════════════════════════════════════════════════

        /// <summary>渲染状态没建好（域重载、纹理重建后）时自愈一次，避免笔刷静默失效。</summary>
        private bool EnsureState()
        {
            if (IsReady())
                return true;
            Initialize();
            return IsReady();
        }

        private bool IsReady() =>
            _layerWeights != null && _layerPixels != null &&
            _layerPixels.Length == _layers.Count && _layerPixels[0] != null &&
            _sample != null && _sample.Length == _layers.Count;

        private static Color32 ToWeight(float value) => new Color32(ToByte(value), 0, 0, 0);

        private static byte ToByte(float value) =>
            (byte)(Mathf.Clamp01(value) * 255f + 0.5f);

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// 稀疏 tile 存储：默认全部落在第 0 层，只保存被画过的 16×16 块。
    /// 每个 texel 存 N 个 alpha（和为 1），N 随 LayeredGroundSurface 的层数组变化。
    /// </summary>
    [Serializable]
    public sealed class LayeredGroundPaintData : ISerializationCallbackReceiver
    {
        public const int Resolution = 256;
        private const int Last = Resolution - 1;
        private const int TileSize = 16;
        private const int TilesPerSide = Resolution / TileSize;
        private const int TileTexels = TileSize * TileSize;

        [Serializable]
        private sealed class Tile
        {
            public int Key;
            public float[] Weights;   // TileTexels × LayerCount
        }

        [SerializeField] private List<Tile> _tiles = new List<Tile>();
        [SerializeField] private int _layerCount = 3;
        [NonSerialized] private Dictionary<int, Tile> _lookup;

        public bool IsEmpty => _tiles == null || _tiles.Count == 0;
        public int PaintedTileCount => _tiles?.Count ?? 0;
        public int LayerCount => _layerCount;

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize() => _lookup = null;

        public void Invalidate() => _lookup = null;

        /// <summary>写入 buffer，长度即当前层数；无覆盖时第 0 层为 1。</summary>
        public void Sample(int x, int z, float[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
                return;

            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = i == 0 ? 1f : 0f;

            if (x < 0 || x > Last || z < 0 || z > Last || IsEmpty)
                return;
            if (!GetLookup().TryGetValue(TileKey(x, z), out Tile tile) ||
                tile.Weights == null || tile.Weights.Length == 0)
                return;

            int stride = tile.Weights.Length / TileTexels;
            if (stride <= 0)
                return;

            int baseIndex = TileIndex(x, z) * stride;
            int count = Mathf.Min(buffer.Length, stride);
            for (int c = 0; c < count; c++)
                buffer[c] = tile.Weights[baseIndex + c];
        }

        /// <summary>
        /// 一次 alpha 合成：把权重往 layer 推 alpha 比例，返回是否变化并带回最终值。
        /// alpha=1 时结果为该层的 one-hot，即完全覆盖之前的内容。
        /// </summary>
        public bool PaintTexel(int x, int z, int layer, float alpha, float[] weights)
        {
            if (x < 0 || x > Last || z < 0 || z > Last || alpha <= 0f)
                return false;
            if (weights == null || layer < 0 || layer >= weights.Length)
                return false;

            Sample(x, z, weights);

            float t = Mathf.Clamp01(alpha);
            bool changed = false;
            for (int c = 0; c < weights.Length; c++)
            {
                float target = c == layer ? 1f : 0f;
                float value = Mathf.Lerp(weights[c], target, t);
                if (Mathf.Abs(value - weights[c]) > 1e-4f)
                    changed = true;
                weights[c] = value;
            }

            if (!changed)
                return false;

            Tile tile = GetOrCreateTile(x, z);
            int stride = weights.Length;
            int baseIndex = TileIndex(x, z) * stride;
            for (int c = 0; c < stride; c++)
                tile.Weights[baseIndex + c] = weights[c];
            return true;
        }

        /// <summary>层数变化时同步每个 tile 的通道数：增层补 0，删层重新归一化。</summary>
        public void EnsureLayerCount(int count)
        {
            if (count < 1)
                count = 1;
            if (_tiles != null)
            {
                foreach (Tile tile in _tiles)
                {
                    if (tile?.Weights == null)
                        continue;

                    int oldCount = tile.Weights.Length / TileTexels;
                    if (oldCount == count)
                        continue;

                    var resized = new float[TileTexels * count];
                    int copy = Mathf.Min(oldCount, count);
                    for (int texel = 0; texel < TileTexels; texel++)
                    {
                        int oldBase = texel * oldCount;
                        int newBase = texel * count;
                        float sum = 0f;
                        for (int c = 0; c < copy; c++)
                        {
                            resized[newBase + c] = tile.Weights[oldBase + c];
                            sum += resized[newBase + c];
                        }

                        if (count < oldCount)
                        {
                            // 删掉的层留下的权重按比例分回剩余层；全是 0 就退到第 0 层。
                            if (sum > 1e-4f)
                                for (int c = 0; c < count; c++)
                                    resized[newBase + c] /= sum;
                            else
                                resized[newBase] = 1f;
                        }
                        else if (oldCount <= 0)
                        {
                            resized[newBase] = 1f;
                        }
                    }

                    tile.Weights = resized;
                }
            }
            _layerCount = count;
        }

        private Tile GetOrCreateTile(int x, int z)
        {
            Dictionary<int, Tile> lookup = GetLookup();
            int key = TileKey(x, z);
            if (lookup.TryGetValue(key, out Tile tile) && tile != null &&
                tile.Weights != null && tile.Weights.Length == TileTexels * _layerCount)
                return tile;

            tile = new Tile
            {
                Key = key,
                Weights = new float[TileTexels * _layerCount]
            };
            for (int texel = 0; texel < TileTexels; texel++)
                tile.Weights[texel * _layerCount] = 1f;

            if (_tiles == null)
                _tiles = new List<Tile>();
            _tiles.Add(tile);
            lookup[key] = tile;
            return tile;
        }

        private Dictionary<int, Tile> GetLookup()
        {
            if (_lookup != null)
                return _lookup;
            _lookup = new Dictionary<int, Tile>();
            if (_tiles != null)
                foreach (Tile tile in _tiles)
                    if (tile != null)
                        _lookup[tile.Key] = tile;
            return _lookup;
        }

        private static int TileKey(int x, int z) => x / TileSize + z / TileSize * TilesPerSide;
        private static int TileIndex(int x, int z) => x % TileSize + z % TileSize * TileSize;
    }
}
