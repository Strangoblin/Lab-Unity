// ════════════════════════════════════════════════════════════
//  TerrainRoutePlanner — 最小连通路网与避水的代价寻路
// ════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mine.Terrain
{
    /// <summary>
    /// 连接需要道路的地点；最小连通骨架补充环路，A* 绕水后保留受控弯曲。
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
            List<int> landSites = new List<int>();
            for (int i = 0; i < map.Sites.Count; i++)
                if (map.Sites[i].ConnectRoad)
                    landSites.Add(i);
            if (landSites.Count < 2)
                return;

            List<SiteEdge> edges = BuildRoadGraph(map, landSites,
                Mathf.Min(settings.Width, settings.Height), settings.LoopRatio);
            bool[] passable = BuildPassableCells(map, settings.RoadWidth);
            float[] traversalCosts = BuildTraversalCosts(map);
            for (int i = 0; i < edges.Count; i++)
            {
                Vector2 start = map.Sites[edges[i].Start].Center;
                Vector2 end = map.Sites[edges[i].End].Center;
                map.AddRoute(new TerrainRoute(BuildRoute(map, passable, traversalCosts,
                    start, end, settings.RoadBend, edges[i])));
            }
        }

        // ════════════════════════════════════════════════════════════
        //  地点图 — 最小生成树保证连通，过长绕行处补少量环路
        // ════════════════════════════════════════════════════════════

        private static List<SiteEdge> BuildRoadGraph(TerrainMapData map, List<int> sites,
            float shortSide, float loopRatio)
        {
            List<SiteEdge> candidates = new List<SiteEdge>();
            for (int i = 0; i < sites.Count; i++)
                for (int j = i + 1; j < sites.Count; j++)
                    candidates.Add(Edge(map, sites[i], sites[j]));
            candidates.Sort((a, b) => a.Length.CompareTo(b.Length));

            int[] parents = new int[map.Sites.Count];
            for (int i = 0; i < parents.Length; i++)
                parents[i] = i;
            List<SiteEdge> selected = new List<SiteEdge>();
            for (int i = 0; i < candidates.Count && selected.Count < sites.Count - 1; i++)
            {
                SiteEdge edge = candidates[i];
                int startRoot = FindRoot(parents, edge.Start);
                int endRoot = FindRoot(parents, edge.End);
                if (startRoot == endRoot)
                    continue;
                parents[startRoot] = endRoot;
                selected.Add(edge);
            }

            int loopLimit = sites.Count >= 4 ? Mathf.RoundToInt(sites.Count * loopRatio) : 0;
            for (int i = 0; i < candidates.Count && loopLimit > 0; i++)
            {
                SiteEdge edge = candidates[i];
                if (edge.Length > shortSide * 0.45f || ContainsEdge(selected, edge))
                    continue;
                if (GraphDistance(selected, sites, edge.Start, edge.End) < edge.Length * 1.6f)
                    continue;
                selected.Add(edge);
                loopLimit--;
            }
            return selected;
        }

        private static int FindRoot(int[] parents, int node)
        {
            while (parents[node] != node)
                node = parents[node];
            return node;
        }

        private static bool ContainsEdge(List<SiteEdge> edges, SiteEdge candidate)
        {
            for (int i = 0; i < edges.Count; i++)
                if (edges[i].Start == candidate.Start && edges[i].End == candidate.End)
                    return true;
            return false;
        }

        private static float GraphDistance(List<SiteEdge> edges, List<int> sites, int start, int end)
        {
            float[] distances = new float[sites.Count];
            bool[] visited = new bool[sites.Count];
            for (int i = 0; i < distances.Length; i++)
                distances[i] = float.PositiveInfinity;
            distances[sites.IndexOf(start)] = 0f;
            for (int step = 0; step < sites.Count; step++)
            {
                int best = -1;
                for (int i = 0; i < sites.Count; i++)
                    if (!visited[i] && (best < 0 || distances[i] < distances[best]))
                        best = i;
                if (best < 0 || float.IsPositiveInfinity(distances[best]))
                    break;
                if (sites[best] == end)
                    return distances[best];
                visited[best] = true;
                for (int i = 0; i < edges.Count; i++)
                {
                    SiteEdge edge = edges[i];
                    int neighbor = edge.Start == sites[best] ? edge.End :
                        edge.End == sites[best] ? edge.Start : -1;
                    if (neighbor < 0)
                        continue;
                    int neighborIndex = sites.IndexOf(neighbor);
                    distances[neighborIndex] = Mathf.Min(distances[neighborIndex],
                        distances[best] + edge.Length);
                }
            }
            return float.PositiveInfinity;
        }

        private static SiteEdge Edge(TerrainMapData map, int start, int end)
        {
            return new SiteEdge(start, end,
                Vector2.Distance(map.Sites[start].Center, map.Sites[end].Center));
        }

        private static Vector2[] BuildRoute(TerrainMapData map, bool[] passable,
            float[] traversalCosts, Vector2 start, Vector2 end, float bendRatio, SiteEdge edge)
        {
            if (!TryFindBend(map, passable, start, end, bendRatio, edge, out Vector2 bend))
                return FindRoute(map, passable, traversalCosts, start, end);
            try
            {
                Vector2[] first = FindRoute(map, passable, traversalCosts, start, bend);
                Vector2[] second = FindRoute(map, passable, traversalCosts, bend, end);
                Vector2[] result = new Vector2[first.Length + second.Length - 1];
                Array.Copy(first, result, first.Length);
                Array.Copy(second, 1, result, first.Length, second.Length - 1);
                return result;
            }
            catch (InvalidOperationException)
            {
                return FindRoute(map, passable, traversalCosts, start, end);
            }
        }

        private static bool TryFindBend(TerrainMapData map, bool[] passable,
            Vector2 start, Vector2 end, float bendRatio, SiteEdge edge, out Vector2 bend)
        {
            bend = Vector2.zero;
            Vector2 direction = end - start;
            float length = direction.magnitude;
            if (bendRatio <= 0f || length < 35f)
                return false;
            Vector2 normal = new Vector2(-direction.y, direction.x) / length;
            int hash = unchecked(map.Seed * 73856093 ^ edge.Start * 19349663 ^ edge.End * 83492791);
            float primarySign = (hash & 1) == 0 ? 1f : -1f;
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? primarySign : -primarySign;
                for (int scale = 0; scale < 3; scale++)
                {
                    float offset = length * bendRatio * (1f - scale * 0.25f);
                    Vector2 candidate = (start + end) * 0.5f + normal * (offset * sign);
                    if (candidate.x < map.Origin.x + map.CellSize ||
                        candidate.y < map.Origin.y + map.CellSize ||
                        candidate.x > map.Origin.x + map.WidthCells * map.CellSize - map.CellSize ||
                        candidate.y > map.Origin.y + map.HeightCells * map.CellSize - map.CellSize)
                        continue;
                    int index = CellIndex(map, candidate);
                    if (!passable[index])
                        continue;
                    bend = map.CellCenter(index % map.WidthCells, index / map.WidthCells);
                    return true;
                }
            }
            return false;
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
        //  A* — 区域代价与低频噪声构成地形走廊，欧氏下界保证搜索不高估
        // ════════════════════════════════════════════════════════════

        private static Vector2[] FindRoute(
            TerrainMapData map,
            bool[] passable,
            float[] traversalCosts,
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
                        float candidate = cost[current] + step * traversalCosts[next];
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
                case TerrainRegion.Orchard: return 1.5f;
                case TerrainRegion.Meadow: return 1.1f;
                default: return 1f;
            }
        }

        private static float[] BuildTraversalCosts(TerrainMapData map)
        {
            float[] costs = new float[map.WidthCells * map.HeightCells];
            for (int z = 0; z < map.HeightCells; z++)
            {
                for (int x = 0; x < map.WidthCells; x++)
                {
                    Vector2 point = map.CellCenter(x, z);
                    float noise = Mathf.PerlinNoise(point.x * 0.018f + map.Seed * 0.17f,
                        point.y * 0.018f + map.Seed * 0.31f);
                    costs[x + z * map.WidthCells] = RegionCost(map.GetRegion(x, z)) *
                        (0.7f + noise * 0.6f);
                }
            }
            return costs;
        }

        private static float Heuristic(TerrainMapData map, int from, int to)
        {
            int width = map.WidthCells;
            return Vector2.Distance(
                new Vector2(from % width, from / width),
                new Vector2(to % width, to / width)) * map.CellSize * 0.7f;
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
        //  路线简化 — 保留地形走廊的弯曲并安全圆角化
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
                int maxTest = Mathf.Min(cells.Count - 1,
                    anchor + Mathf.Max(2, Mathf.CeilToInt(30f / map.CellSize)));
                for (int test = maxTest; test > anchor + 1; test--)
                {
                    Vector2 target = map.CellCenter(cells[test] % map.WidthCells,
                        cells[test] / map.WidthCells);
                    if (!LineClear(map, passable, anchorPoint, target) ||
                        !FollowsCorridor(map, cells, anchor, test, anchorPoint, target))
                        continue;
                    furthest = test;
                    break;
                }
                points.Add(map.CellCenter(cells[furthest] % map.WidthCells,
                    cells[furthest] / map.WidthCells));
                anchor = furthest;
            }
            points.Add(end);
            List<Vector2> rounded = RoundCorners(points);
            for (int i = 1; i < rounded.Count; i++)
                if (!LineClear(map, passable, rounded[i - 1], rounded[i]))
                    return points.ToArray();
            return rounded.ToArray();
        }

        private static bool FollowsCorridor(TerrainMapData map, List<int> cells,
            int start, int end, Vector2 segmentStart, Vector2 segmentEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float lengthSquared = segment.sqrMagnitude;
            float tolerance = Mathf.Max(map.CellSize * 1.5f, 2f);
            for (int i = start + 1; i < end; i++)
            {
                Vector2 point = map.CellCenter(cells[i] % map.WidthCells,
                    cells[i] / map.WidthCells);
                float t = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / lengthSquared);
                if (Vector2.Distance(point, segmentStart + segment * t) > tolerance)
                    return false;
            }
            return true;
        }

        private static List<Vector2> RoundCorners(List<Vector2> points)
        {
            if (points.Count < 3)
                return points;
            List<Vector2> rounded = new List<Vector2> { points[0] };
            for (int i = 1; i < points.Count - 1; i++)
            {
                rounded.Add(Vector2.Lerp(points[i - 1], points[i], 0.75f));
                rounded.Add(Vector2.Lerp(points[i], points[i + 1], 0.25f));
            }
            rounded.Add(points[points.Count - 1]);
            return rounded;
        }

        private static bool LineClear(
            TerrainMapData map,
            bool[] passable,
            Vector2 start,
            Vector2 end)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(start, end) / (map.CellSize * 0.5f));
            if (steps == 0)
                return passable[CellIndex(map, start)];
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
