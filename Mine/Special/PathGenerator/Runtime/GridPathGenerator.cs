using System;
using System.Collections.Generic;
using UnityEngine;
using CelToon.PathGenerator.Contracts;

namespace CelToon.PathGenerator.Runtime
{
    /// <summary>Bounded grid search with an injected obstacle world; no actor or animation dependency.</summary>
    public sealed class GridPathGenerator
    {
        private readonly float cellSize;
        private readonly float clearance;
        private readonly INavigationWorld world;
        public GridPathGenerator(INavigationWorld world, float cellSize = .5f, float clearance = .3f)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.cellSize = Mathf.Max(.25f, cellSize);
            this.clearance = Mathf.Max(.1f, clearance);
        }
        public bool TryGenerate(Vector3 start, Vector3 end, out RouteWaypoint[] waypoints)
        {
            if (!PathValidation.IsFinite(start) || !PathValidation.IsFinite(end)) { waypoints = null; return false; }
            if (!world.IsSegmentBlocked(start, end))
            {
                waypoints = new[] { new RouteWaypoint(start, 0f), new RouteWaypoint(end, Vector3.Distance(start, end)) };
                return true;
            }
            float grid = Mathf.Max(0.25f, cellSize);
            float originX = Mathf.Min(start.x, end.x) - 3f, originZ = Mathf.Min(start.z, end.z) - 3f;
            int width = Mathf.CeilToInt((Mathf.Abs(end.x - start.x) + 6f) / grid) + 1;
            int height = Mathf.CeilToInt((Mathf.Abs(end.z - start.z) + 6f) / grid) + 1;
            if (width > 96 || height > 96) { waypoints = null; return false; }
            int count = width * height;
            var costs = new float[count]; var parents = new int[count]; var closed = new bool[count]; var occupancy = new byte[count];
            for (int i = 0; i < count; i++) { costs[i] = float.PositiveInfinity; parents[i] = -1; }
            int sx = Mathf.Clamp(Mathf.RoundToInt((start.x - originX) / grid), 0, width - 1);
            int sz = Mathf.Clamp(Mathf.RoundToInt((start.z - originZ) / grid), 0, height - 1);
            int ex = Mathf.Clamp(Mathf.RoundToInt((end.x - originX) / grid), 0, width - 1);
            int ez = Mathf.Clamp(Mathf.RoundToInt((end.z - originZ) / grid), 0, height - 1);
            int source = sz * width + sx, goal = ez * width + ex;
            var open = new List<int> { source }; costs[source] = 0f;
            while (open.Count > 0)
            {
                int bestPosition = 0; float bestScore = float.PositiveInfinity;
                for (int i = 0; i < open.Count; i++)
                {
                    int cell = open[i];
                    float score = costs[cell] + Vector2.Distance(new Vector2(cell % width, cell / width), new Vector2(ex, ez));
                    if (score < bestScore) { bestScore = score; bestPosition = i; }
                }
                int current = open[bestPosition]; open.RemoveAt(bestPosition);
                if (current == goal) break;
                closed[current] = true;
                int cx = current % width, cz = current / width;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= width || nz >= height) continue;
                        int next = nz * width + nx;
                        if (closed[next] || Blocked(nx, nz, width, grid, originX, originZ, occupancy)) continue;
                        if (dx != 0 && dz != 0 &&
                            (Blocked(cx + dx, cz, width, grid, originX, originZ, occupancy) ||
                             Blocked(cx, cz + dz, width, grid, originX, originZ, occupancy))) continue;
                        float newCost = costs[current] + (dx == 0 || dz == 0 ? 1f : 1.41421356f);
                        if (newCost >= costs[next]) continue;
                        costs[next] = newCost; parents[next] = current;
                        if (!open.Contains(next)) open.Add(next);
                    }
            }
            if (source != goal && parents[goal] < 0) { waypoints = null; return false; }
            var cells = new List<int>();
            for (int node = goal; node >= 0; node = parents[node]) cells.Add(node);
            cells.Reverse();
            var positions = new List<Vector3> { start };
            int previousDx = 0, previousDz = 0;
            for (int i = 1; i < cells.Count; i++)
            {
                int dx = cells[i] % width - cells[i - 1] % width;
                int dz = cells[i] / width - cells[i - 1] / width;
                if (i > 1 && (dx != previousDx || dz != previousDz))
                {
                    int turn = cells[i - 1];
                    positions.Add(new Vector3(originX + (turn % width) * grid, start.y, originZ + (turn / width) * grid));
                }
                previousDx = dx; previousDz = dz;
            }
            positions.Add(end);
            waypoints = new RouteWaypoint[positions.Count];
            float distance = 0f;
            for (int i = 0; i < positions.Count; i++)
            {
                if (i > 0) distance += Vector3.Distance(positions[i - 1], positions[i]);
                waypoints[i] = new RouteWaypoint(positions[i], distance);
            }
            return true;
        }

        private bool Blocked(int x, int z, int width, float grid, float originX, float originZ, byte[] occupancy)
        {
            int index = z * width + x;
            if (occupancy[index] == 0)
            {
                Vector3 center = new Vector3(originX + x * grid, 0.6f, originZ + z * grid);
                float half = Mathf.Max(0.1f, clearance);
                occupancy[index] = world.IsCellBlocked(center, new Vector3(half, 0.48f, half)) ? (byte)2 : (byte)1;
            }
            return occupancy[index] == 2;
        }
    }
}
