// ════════════════════════════════════════════════════════════
//  TerrainRoutePlanner — 最小连通路网与避水的代价寻路
// ════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mine.Terrain
{
    /// <summary>
    /// 仅连接陆地特征地点；A* 绕开水域后以通视检测简化折线。
    /// </summary>
    public static class TerrainRoutePlanner
    {
        private readonly struct SiteEdge
        {
            public readonly int Start;
            public readonly int End;
            public readonly float Length;

            public SiteEdge(int start, int end, float length)
            {
                Start = start;
                End = end;
                Length = length;
            }
        }

        private readonly struct PathNode
        {
            public readonly int Index;
            public readonly float Priority;

            public PathNode(int index, float priority)
            {
                Index = index;
                Priority = priority;
            }
        }

        public static void CreateRoutes(TerrainMapData map, TerrainGenerationSettings settings)
        {
            if (map == null || settings == null)
                throw new ArgumentNullException(map == null ? nameof(map) : nameof(settings));

            int village = FindSite(map, TerrainRegion.Village);
            int wheat = FindSite(map, TerrainRegion.WheatField);
            int forest = FindSite(map, TerrainRegion.Forest);
            SiteEdge[] edges =
            {
                Edge(map, village, wheat),
                Edge(map, village, forest),
                Edge(map, wheat, forest)
            };
            Array.Sort(edges, (a, b) => a.Length.CompareTo(b.Length));

            bool[] passable = BuildPassableCells(map, settings.RoadWidth);
            for (int i = 0; i < 2; i++)
            {
                Vector2 start = map.Sites[edges[i].Start].Center;
                Vector2 end = map.Sites[edges[i].End].Center;
                map.AddRoute(new TerrainRoute(FindRoute(map, passable, start, end)));
            }
        }

        // ════════════════════════════════════════════════════════════
        //  地点图 — 三条候选边中取最短两条构成最小生成树
        // ════════════════════════════════════════════════════════════

        private static int FindSite(TerrainMapData map, TerrainRegion region)
        {
            for (int i = 0; i < map.Sites.Count; i++)
                if (map.Sites[i].Region == region)
                    return i;
            throw new InvalidOperationException($"Missing terrain site: {region}.");
        }

        private static SiteEdge Edge(TerrainMapData map, int start, int end)
        {
            return new SiteEdge(start, end,
                Vector2.Distance(map.Sites[start].Center, map.Sites[end].Center));
        }

        // ════════════════════════════════════════════════════════════
        //  可通行格 — 将水域按道路半径膨胀，避免栅格化后压进岸线
        // ════════════════════════════════════════════════════════════

        private static bool[] BuildPassableCells(TerrainMapData map, float roadWidth)
        {
            int width = map.WidthCells;
            int height = map.HeightCells;
            bool[] passable = new bool[width * height];
            float clearance = roadWidth * 0.5f + Mathf.Max(map.CellSize, 0.5f) + map.CellSize;
            int range = Mathf.CeilToInt(clearance / map.CellSize);

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool clear = true;
                    for (int dz = -range; dz <= range && clear; dz++)
                    {
                        for (int dx = -range; dx <= range; dx++)
                        {
                            if ((dx * dx + dz * dz) * map.CellSize * map.CellSize > clearance * clearance)
                                continue;
                            int sx = x + dx;
                            int sz = z + dz;
                            if (sx < 0 || sz < 0 || sx >= width || sz >= height)
                                continue;
                            if (map.GetRegion(sx, sz) != TerrainRegion.Water)
                                continue;
                            clear = false;
                            break;
                        }
                    }
                    passable[x + z * width] = clear;
                }
            }

            return passable;
        }

        // ════════════════════════════════════════════════════════════
        //  A* — 区域代价决定路径，欧氏下界保证搜索不高估
        // ════════════════════════════════════════════════════════════

        private static Vector2[] FindRoute(
            TerrainMapData map,
            bool[] passable,
            Vector2 start,
            Vector2 end)
        {
            int width = map.WidthCells;
            int startIndex = CellIndex(map, start);
            int endIndex = CellIndex(map, end);
            float[] cost = new float[passable.Length];
            int[] previous = new int[passable.Length];
            bool[] closed = new bool[passable.Length];
            for (int i = 0; i < cost.Length; i++)
            {
                cost[i] = float.PositiveInfinity;
                previous[i] = -1;
            }

            PathHeap open = new PathHeap();
            cost[startIndex] = 0f;
            open.Push(new PathNode(startIndex, Heuristic(map, startIndex, endIndex)));

            while (open.Count > 0)
            {
                int current = open.Pop().Index;
                if (closed[current])
                    continue;
                if (current == endIndex)
                    return SmoothPath(map, passable, previous, startIndex, endIndex, start, end);
                closed[current] = true;

                int x = current % width;
                int z = current / width;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;
                        int nx = x + dx;
                        int nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= width || nz >= map.HeightCells)
                            continue;
                        int next = nx + nz * width;
                        if (!passable[next] || closed[next])
                            continue;
                        if (dx != 0 && dz != 0 &&
                            (!passable[nx + z * width] || !passable[x + nz * width]))
                            continue;

                        float step = (dx == 0 || dz == 0 ? 1f : 1.41421356f) * map.CellSize;
                        float candidate = cost[current] + step * RegionCost(map.GetRegion(nx, nz));
                        if (candidate >= cost[next])
                            continue;
                        cost[next] = candidate;
                        previous[next] = current;
                        open.Push(new PathNode(next, candidate + Heuristic(map, next, endIndex)));
                    }
                }
            }

            throw new InvalidOperationException("No land route could connect the selected terrain sites.");
        }

        private static float RegionCost(TerrainRegion region)
        {
            switch (region)
            {
                case TerrainRegion.Forest: return 1.8f;
                case TerrainRegion.WheatField: return 1.3f;
                default: return 1f;
            }
        }

        private static float Heuristic(TerrainMapData map, int from, int to)
        {
            int width = map.WidthCells;
            return Vector2.Distance(
                new Vector2(from % width, from / width),
                new Vector2(to % width, to / width)) * map.CellSize;
        }

        private static int CellIndex(TerrainMapData map, Vector2 point)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((point.x - map.Origin.x) / map.CellSize),
                0, map.WidthCells - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((point.y - map.Origin.y) / map.CellSize),
                0, map.HeightCells - 1);
            return x + z * map.WidthCells;
        }

        // ════════════════════════════════════════════════════════════
        //  路线简化 — 仅在膨胀后的可通行域内拉直折线
        // ════════════════════════════════════════════════════════════

        private static Vector2[] SmoothPath(
            TerrainMapData map,
            bool[] passable,
            int[] previous,
            int startIndex,
            int endIndex,
            Vector2 start,
            Vector2 end)
        {
            List<int> cells = new List<int>();
            for (int node = endIndex; node != -1; node = previous[node])
                cells.Add(node);
            cells.Reverse();
            if (cells[0] != startIndex)
                throw new InvalidOperationException("Route reconstruction did not reach its start site.");

            List<Vector2> points = new List<Vector2> { start };
            int anchor = 0;
            while (anchor < cells.Count - 1)
            {
                int furthest = anchor + 1;
                Vector2 anchorPoint = map.CellCenter(cells[anchor] % map.WidthCells,
                    cells[anchor] / map.WidthCells);
                for (int test = cells.Count - 1; test > anchor + 1; test--)
                {
                    Vector2 target = map.CellCenter(cells[test] % map.WidthCells,
                        cells[test] / map.WidthCells);
                    if (!LineClear(map, passable, anchorPoint, target))
                        continue;
                    furthest = test;
                    break;
                }
                points.Add(map.CellCenter(cells[furthest] % map.WidthCells,
                    cells[furthest] / map.WidthCells));
                anchor = furthest;
            }
            points.Add(end);
            return points.ToArray();
        }

        private static bool LineClear(
            TerrainMapData map,
            bool[] passable,
            Vector2 start,
            Vector2 end)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(start, end) / (map.CellSize * 0.5f));
            for (int i = 0; i <= steps; i++)
                if (!passable[CellIndex(map, Vector2.Lerp(start, end, i / (float)steps))])
                    return false;
            return true;
        }

        private sealed class PathHeap
        {
            private readonly List<PathNode> _nodes = new List<PathNode>();

            public int Count => _nodes.Count;

            public void Push(PathNode node)
            {
                _nodes.Add(node);
                int index = _nodes.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Before(node, _nodes[parent]))
                        break;
                    _nodes[index] = _nodes[parent];
                    index = parent;
                }
                _nodes[index] = node;
            }

            public PathNode Pop()
            {
                PathNode result = _nodes[0];
                int lastIndex = _nodes.Count - 1;
                PathNode last = _nodes[lastIndex];
                _nodes.RemoveAt(lastIndex);
                if (lastIndex == 0)
                    return result;

                int index = 0;
                while (index * 2 + 1 < _nodes.Count)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < _nodes.Count && Before(_nodes[child + 1], _nodes[child]))
                        child++;
                    if (!Before(_nodes[child], last))
                        break;
                    _nodes[index] = _nodes[child];
                    index = child;
                }
                _nodes[index] = last;
                return result;
            }

            private static bool Before(PathNode a, PathNode b)
            {
                return a.Priority < b.Priority ||
                       (a.Priority == b.Priority && a.Index < b.Index);
            }
        }
    }
}
