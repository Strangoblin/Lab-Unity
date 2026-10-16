// ════════════════════════════════════════════════════════════
//  TerrainGridRoadGenerator — 正交道路走廊与矩形街区的通用布局算子
// ════════════════════════════════════════════════════════════

using UnityEngine;
using System;
using System.Collections.Generic;

namespace Mine.Terrain
{
    /// <summary>
    /// 世界单位下的道路与街区规格，不持有城市用途或内容类型。
    /// </summary>
    [Serializable]
    public sealed class TerrainGridSettings
    {
        [SerializeField, Range(1, 12)] private int _columns = 6;
        [SerializeField, Range(1, 12)] private int _rows = 6;
        [SerializeField, Range(0, 12)] private int _mainRoadEvery = 3;
        [SerializeField, Min(0.25f)] private float _mainRoadWidth = 8f;
        [SerializeField, Min(0.25f)] private float _streetWidth = 4f;
        [SerializeField, Min(0f)] private float _sidewalkWidth = 2f;
        [SerializeField, Min(4f)] private float _minBlockSpan = 24f;

        public int Columns { get => _columns; set => _columns = value; }
        public int Rows { get => _rows; set => _rows = value; }
        public int MainRoadEvery { get => _mainRoadEvery; set => _mainRoadEvery = value; }
        public float MainRoadWidth { get => _mainRoadWidth; set => _mainRoadWidth = value; }
        public float StreetWidth { get => _streetWidth; set => _streetWidth = value; }
        public float SidewalkWidth { get => _sidewalkWidth; set => _sidewalkWidth = value; }
        public float MinBlockSpan { get => _minBlockSpan; set => _minBlockSpan = value; }

        public void Validate()
        {
            if (_columns < 1 || _columns > 12 || _rows < 1 || _rows > 12 ||
                _mainRoadEvery < 0 || _mainRoadEvery > 12)
                throw new ArgumentException("Grid counts or major-road interval are invalid.");
            if (!IsFinite(_mainRoadWidth) || !IsFinite(_streetWidth) ||
                !IsFinite(_sidewalkWidth) || !IsFinite(_minBlockSpan) ||
                _mainRoadWidth < _streetWidth || _streetWidth < 0.25f ||
                _sidewalkWidth < 0f || _minBlockSpan < 4f)
                throw new ArgumentException("Grid widths or minimum block span are invalid.");
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// 一条贯穿地图的道路带，Min/Max 包含两侧人行走廊，CarMin/CarMax 为车行部分。
    /// </summary>
    public readonly struct TerrainGridRoadBand
    {
        public readonly float Min;
        public readonly float Max;
        public readonly float CarMin;
        public readonly float CarMax;

        public TerrainGridRoadBand(float min, float max, float carMin, float carMax)
        {
            Min = min;
            Max = max;
            CarMin = carMin;
            CarMax = carMax;
        }
    }

    /// <summary>
    /// 不可变正交布局；街区为世界 XZ 矩形，按 z 行、x 列连续编号。
    /// </summary>
    public sealed class TerrainGridRoadLayout
    {
        public int Columns { get; }
        public int Rows { get; }
        public Rect Bounds { get; }
        public IReadOnlyList<Rect> Blocks { get; }
        public IReadOnlyList<TerrainGridRoadBand> XRoads { get; }
        public IReadOnlyList<TerrainGridRoadBand> ZRoads { get; }

        internal TerrainGridRoadLayout(int columns, int rows, Rect bounds,
            Rect[] blocks, TerrainGridRoadBand[] xRoads, TerrainGridRoadBand[] zRoads)
        {
            Columns = columns;
            Rows = rows;
            Bounds = bounds;
            Blocks = Array.AsReadOnly(blocks);
            XRoads = Array.AsReadOnly(xRoads);
            ZRoads = Array.AsReadOnly(zRoads);
        }

        public bool TrySampleRoad(Vector2 point, out bool carriageway)
        {
            carriageway = false;
            if (!Bounds.Contains(point))
                return false;
            bool corridorX = SampleAxis(XRoads, point.x, out bool carX);
            bool corridorZ = SampleAxis(ZRoads, point.y, out bool carZ);
            carriageway = carX || carZ;
            return corridorX || corridorZ;
        }

        public int FindBlockIndex(Vector2 point)
        {
            for (int i = 0; i < Blocks.Count; i++)
                if (Blocks[i].Contains(point))
                    return i;
            return -1;
        }

        private static bool SampleAxis(IReadOnlyList<TerrainGridRoadBand> roads,
            float coordinate, out bool carriageway)
        {
            carriageway = false;
            for (int i = 0; i < roads.Count; i++)
            {
                TerrainGridRoadBand band = roads[i];
                if (coordinate >= band.Min && coordinate < band.Max)
                {
                    carriageway = coordinate >= band.CarMin && coordinate < band.CarMax;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 先生成贯穿道路，再从道路间隙导出街区；小地图自动减少数量，无法容纳时明确拒绝。
    /// </summary>
    public static class TerrainGridRoadGenerator
    {
        public static TerrainGridRoadLayout Generate(TerrainGenerationSettings mapSettings,
            TerrainGridSettings settings)
        {
            if (mapSettings == null)
                throw new ArgumentNullException(nameof(mapSettings));
            mapSettings.Validate();
            ValidateSampling(settings, mapSettings.CellSize);
            return Generate(mapSettings.Width, mapSettings.Height, mapSettings.Origin, settings);
        }

        public static TerrainGridRoadLayout Generate(TerrainMapData map, TerrainGridSettings settings)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            ValidateSampling(settings, map.CellSize);
            return Generate(map.WidthCells * map.CellSize, map.HeightCells * map.CellSize,
                map.Origin, settings);
        }

        private static void ValidateSampling(TerrainGridSettings settings, float cellSize)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            if (settings.StreetWidth < cellSize ||
                (settings.SidewalkWidth > 0f && settings.SidewalkWidth < cellSize))
                throw new ArgumentException("Street and enabled sidewalk widths must be at least one map cell. " +
                    "Increase the widths or reduce the map cell size to avoid missing road samples.");
        }

        // ════════════════════════════════════════════════════════════
        //  轴线布局 — 保留完整边界道路带，街区均分道路之外的可用长度
        // ════════════════════════════════════════════════════════════

        private static TerrainGridRoadLayout Generate(float width, float height,
            Vector2 origin, TerrainGridSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            int columns = ResolveCount(width, settings.Columns, settings);
            int rows = ResolveCount(height, settings.Rows, settings);
            TerrainGridRoadBand[] xRoads = BuildAxis(origin.x, width, columns, settings);
            TerrainGridRoadBand[] zRoads = BuildAxis(origin.y, height, rows, settings);
            Rect[] blocks = new Rect[columns * rows];
            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < columns; x++)
                {
                    blocks[x + z * columns] = Rect.MinMaxRect(
                        xRoads[x].Max, zRoads[z].Max,
                        xRoads[x + 1].Min, zRoads[z + 1].Min);
                }
            }
            return new TerrainGridRoadLayout(columns, rows, new Rect(origin.x, origin.y, width, height),
                blocks, xRoads, zRoads);
        }

        private static int ResolveCount(float extent, int requested, TerrainGridSettings settings)
        {
            for (int count = requested; count >= 1; count--)
            {
                double available = extent - RoadSpan(count, settings);
                if (available / count >= settings.MinBlockSpan - 0.0001f)
                    return count;
            }
            throw new ArgumentException("Map is too small for one grid block and its boundary roads. " +
                "Increase map size or reduce road widths/minimum block span.");
        }

        private static TerrainGridRoadBand[] BuildAxis(float origin, float extent,
            int count, TerrainGridSettings settings)
        {
            TerrainGridRoadBand[] bands = new TerrainGridRoadBand[count + 1];
            double blockSpan = (extent - RoadSpan(count, settings)) / count;
            double cursor = origin;
            for (int i = 0; i <= count; i++)
            {
                float carWidth = CarWidth(i, count, settings);
                float corridorWidth = carWidth + 2f * settings.SidewalkWidth;
                float min = (float)cursor;
                float max = i == count ? origin + extent : (float)(cursor + corridorWidth);
                bands[i] = new TerrainGridRoadBand(min, max,
                    min + settings.SidewalkWidth, max - settings.SidewalkWidth);
                cursor += corridorWidth + blockSpan;
            }
            return bands;
        }

        private static double RoadSpan(int count, TerrainGridSettings settings)
        {
            double span = 0d;
            for (int i = 0; i <= count; i++)
                span += CarWidth(i, count, settings) + 2d * settings.SidewalkWidth;
            return span;
        }

        private static float CarWidth(int index, int count, TerrainGridSettings settings)
        {
            bool main = settings.MainRoadEvery > 0 &&
                (index == 0 || index == count || index % settings.MainRoadEvery == 0);
            return main ? settings.MainRoadWidth : settings.StreetWidth;
        }
    }
}
