// ════════════════════════════════════════════════════════════
//  TerrainManager — 编排可替换地图方案与通用表现生命周期
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;

namespace Mine.Terrain
{
    /// <summary>
    /// 场景入口只持有共享配置，完整生成成功后才替换当前地图和表现资源。
    /// </summary>
    public sealed class TerrainManager : MonoBehaviour
    {
        [SerializeField] private TerrainGenerationSettings _settings = new TerrainGenerationSettings();
        [SerializeField] private TerrainTypeDefinition _terrainType;
        [SerializeField] private TerrainRenderSettings _renderSettings = new TerrainRenderSettings();
        [SerializeField] private bool _generateOnStart = true;

        private TerrainSceneRenderer _renderer;

        public TerrainTypeDefinition TerrainType => _terrainType;
        public TerrainMapData CurrentMap { get; private set; }
        public TerrainStuffPlan CurrentStuff { get; private set; }
        public TerrainPlantPlan CurrentPlants { get; private set; }
        public int SurfaceChunkCount => _renderer?.SurfaceChunkCount ?? 0;
        public int PlantBatchCount => _renderer?.PlantBatchCount ?? 0;
        public Transform GeneratedRoot => _renderer?.GeneratedRoot;

        // ════════════════════════════════════════════════════════════
        //  生命周期 — 场景持有表现实例，禁用或退出时释放
        // ════════════════════════════════════════════════════════════

        private void Start()
        {
            if (_generateOnStart)
                Generate();
        }

        private void Update() => _renderer?.Draw(gameObject.layer);

        private void OnDisable() => Release();

        private void OnDestroy() => Release();

        [ContextMenu("Generate Terrain Experiment")]
        public void Generate()
        {
            if (_terrainType == null)
                throw new InvalidOperationException("Assign a Terrain Type asset before generating a map.");
            if (_settings == null || _renderSettings == null)
                throw new InvalidOperationException("Terrain map or render settings are missing.");
            _renderSettings.Validate();

            TerrainSurfaceResult surface = null;
            TerrainSceneRenderer renderer = null;
            TerrainMapData map;
            TerrainStuffPlan stuff;
            TerrainPlantPlan plants;
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            long dataMilliseconds;
            long meshMilliseconds;
            long stuffMilliseconds;
            long plantMilliseconds;

            try
            {
                map = TerrainDataGenerator.Generate(_settings, _terrainType);
                dataMilliseconds = timer.ElapsedMilliseconds;
                surface = TerrainMeshGenerator.Generate(map, _settings.ChunkCells,
                    _terrainType, _renderSettings.ColorBlendWidth);
                meshMilliseconds = timer.ElapsedMilliseconds - dataMilliseconds;
                stuff = _terrainType.DistributeStuff(map);
                stuffMilliseconds = timer.ElapsedMilliseconds - dataMilliseconds - meshMilliseconds;
                plants = _terrainType.DistributePlants(map, stuff);
                plantMilliseconds = timer.ElapsedMilliseconds - dataMilliseconds -
                    meshMilliseconds - stuffMilliseconds;
                renderer = new TerrainSceneRenderer(transform, map, surface, stuff, plants,
                    _terrainType, _renderSettings, _settings.ChunkCells);
            }
            catch
            {
                if (renderer != null)
                    renderer.Dispose();
                else
                    surface?.Dispose();
                throw;
            }

            Release();
            _renderer = renderer;
            CurrentMap = map;
            CurrentStuff = stuff;
            CurrentPlants = plants;

            Debug.Log($"Terrain experiment generated ({_terrainType.name}): " +
                      $"{map.WidthCells}x{map.HeightCells} cells, " +
                      $"{SurfaceChunkCount} chunks, {stuff.Placements.Count} structures, " +
                      $"{plants.Placements.Count} plants in {PlantBatchCount} batches; " +
                      $"data {dataMilliseconds} ms, mesh {meshMilliseconds} ms, " +
                      $"structures {stuffMilliseconds} ms, plants {plantMilliseconds} ms, " +
                      $"total {timer.ElapsedMilliseconds} ms.", this);
        }

        private void Release()
        {
            _renderer?.Dispose();
            _renderer = null;
            CurrentMap = null;
            CurrentStuff = null;
            CurrentPlants = null;
        }
    }
}
