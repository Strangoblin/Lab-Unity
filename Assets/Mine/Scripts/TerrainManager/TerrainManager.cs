// ════════════════════════════════════════════════════════════
//  TerrainManager — 地形数据层与渲染层的场景入口
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 生成并发布 TerrainSnapshot，同时持有渲染层的生命周期。
    /// </summary>
    /// <remarks>
    /// 消费者只依赖 Snapshot.Grid 与 Version；渲染层当前是单块默认网格，
    /// 后续的分块、深度参考层与其他程序化网格都在本入口下扩展。
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    [ExecuteAlways]
    public sealed class TerrainManager : MonoBehaviour
    {
        // ── 数据 ──────────────────────────────────────────────

        [Header("Data")]
        [SerializeField] private TerrainSettings _settings = new TerrainSettings();
        [SerializeField] private bool _generateOnStart = true;

        // ── 渲染 ──────────────────────────────────────────────

        [Header("Render")]
        [SerializeField] private Material _surfaceMaterial;
        [SerializeField] private bool _castShadows = true;
        [SerializeField] private bool _receiveShadows = true;

        private TerrainSurfaceRenderer _renderer;
        private int _nextVersion = 1;

        public TerrainSnapshot Snapshot { get; private set; }
        public TerrainGrid CurrentGrid => Snapshot?.Grid;
        public int DataVersion => Snapshot?.Version ?? 0;
        public bool HasData => Snapshot != null;
        public TerrainSettings Settings => _settings;
        public Transform GeneratedRoot => _renderer?.Root;
        /// <summary>上次成功生成的地表颜色图；与网格共用世界 XZ → UV 映射，生成期间随快照一起替换。</summary>
        public Texture2D SurfaceColorMap => _renderer?.ColorMap;

        /// <summary>
        /// 每次成功生成后发布；订阅者应在回调中读取新 Snapshot。
        /// </summary>
        public event Action<TerrainSnapshot> Rebuilt;

        // ════════════════════════════════════════════════════════════
        //  生命周期 — 生成入口与资源释放
        // ════════════════════════════════════════════════════════════

        private void Start()
        {
            if (_generateOnStart)
                Generate();
        }

        private void OnDisable() => Release();

        private void OnDestroy() => Release();

        private void OnValidate()
        {
            if (_settings == null)
                _settings = new TerrainSettings();
        }

        /// <summary>
        /// 供消费者在 Start 之前显式准备数据。
        /// </summary>
        public void Initialize()
        {
            if (!HasData)
                Generate();
        }

        [ContextMenu("Generate Terrain")]
        public void Generate()
        {
            if (_settings == null)
                throw new InvalidOperationException("Terrain settings are missing.");
            _settings.Validate();

            var grid = new TerrainGrid(_settings.WidthCells, _settings.HeightCells,
                _settings.CellSize, _settings.Origin);
            ITerrainProvider provider = TerrainProviderFactory.Create(_settings.Provider);
            provider.Generate(grid, _settings);

            var renderer = new TerrainSurfaceRenderer(transform, grid, _surfaceMaterial,
                _castShadows, _receiveShadows);
            var snapshot = new TerrainSnapshot(grid, _nextVersion++);

            _renderer?.Dispose();
            // 先建后换：新地表已就绪才回收旧引用与域重载后失去引用的残留。
            DestroyInstancesExcept(renderer.Root);
            _renderer = renderer;
            Snapshot = snapshot;
            Rebuilt?.Invoke(snapshot);

            Debug.Log($"Terrain generated: {grid.WidthCells}x{grid.HeightCells} cells, " +
                      $"{grid.VertexCount} vertices, provider {_settings.Provider}, " +
                      $"version {snapshot.Version}.", this);
        }

        [ContextMenu("Release Terrain")]
        public void Release()
        {
            _renderer?.Dispose();
            _renderer = null;
            Snapshot = null;
            DestroyInstancesExcept(null);
        }

        private void DestroyInstancesExcept(Transform keep)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child == keep || child.GetComponent<TerrainSurfaceInstance>() == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  采样入口 — 消费者无需直接持有网格
        // ════════════════════════════════════════════════════════════

        public bool TrySample(float worldX, float worldZ, out TerrainSample sample)
        {
            TerrainGrid grid = CurrentGrid;
            if (grid == null)
            {
                sample = default;
                return false;
            }

            return grid.TrySample(worldX, worldZ, out sample);
        }

        private void OnDrawGizmosSelected()
        {
            TerrainGrid grid = CurrentGrid;
            if (grid == null)
                return;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(grid.Bounds.center, grid.Bounds.size);
        }
    }
}
