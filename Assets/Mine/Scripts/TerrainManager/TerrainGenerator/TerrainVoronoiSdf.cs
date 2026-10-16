// ════════════════════════════════════════════════════════════
//  TerrainVoronoiSdf — 胞边几何距离与未归一化的种子距离差
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Mine.Terrain
{
    /// <summary>
    /// 提供地区所需的胞边几何距离与道路所需的种子距离差。
    /// </summary>
    public static class TerrainVoronoiSdf
    {
        public static float DistanceToEdge(TerrainMapData map, Vector2 point,
            int owner, out int neighbor)
        {
            neighbor = -1;
            if (owner < 0)
                return float.PositiveInfinity;

            Vector2 center = map.Sites[owner].Center;
            float ownDistance = (point - center).sqrMagnitude;
            float best = float.PositiveInfinity;
            for (int i = 0; i < map.Sites.Count; i++)
            {
                if (i == owner)
                    continue;
                Vector2 other = map.Sites[i].Center;
                float separation = Vector2.Distance(center, other);
                float distance = ((point - other).sqrMagnitude - ownDistance) /
                    (2f * separation);
                if (distance >= best)
                    continue;
                best = distance;
                neighbor = i;
            }
            return best;
        }

        public static float DistanceGap(TerrainMapData map, Vector2 point,
            int owner, out int neighbor, float cornerSoftness = 0f)
        {
            neighbor = -1;
            if (owner < 0)
                return float.PositiveInfinity;

            float ownDistance = Vector2.Distance(point, map.Sites[owner].Center);
            float nextDistance = float.PositiveInfinity;
            float thirdDistance = float.PositiveInfinity;
            for (int i = 0; i < map.Sites.Count; i++)
            {
                if (i == owner)
                    continue;
                float distance = Vector2.Distance(point, map.Sites[i].Center);
                if (distance < nextDistance)
                {
                    thirdDistance = nextDistance;
                    nextDistance = distance;
                    neighbor = i;
                }
                else if (distance < thirdDistance)
                {
                    thirdDistance = distance;
                }
            }
            if (neighbor < 0)
                return float.PositiveInfinity;
            float gap = (nextDistance - ownDistance) * 0.5f;
            if (cornerSoftness <= 0f || float.IsPositiveInfinity(thirdDistance))
                return gap;
            float nextGap = (thirdDistance - ownDistance) * 0.5f;
            return gap - cornerSoftness * Mathf.Log(1f +
                Mathf.Exp((gap - nextGap) / cornerSoftness));
        }
    }
}
