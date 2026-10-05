// ════════════════════════════════════════════════════════════
//  TerrainDataNoise — 对已生成的区域、归属与道路执行统一坐标扰动
// ════════════════════════════════════════════════════════════

using UnityEngine;

namespace Mine.Terrain
{
    /// <summary>
    /// 统一重采样完整地图数据，避免地区边界与道路使用不同的噪声坐标。
    /// </summary>
    public static class TerrainDataNoise
    {
        public static void Apply(TerrainMapData map, TerrainGenerationSettings settings)
        {
            float shortSide = Mathf.Min(settings.Width, settings.Height);
            Vector2 frequencies = new Vector2(settings.BroadWarpCycles / shortSide,
                settings.DetailWarpCycles / shortSide);
            Vector2 amplitudes = new Vector2(settings.BroadWarpDistance,
                settings.DetailWarpDistance) * (shortSide / 300f);
            if (amplitudes.x <= 0f && amplitudes.y <= 0f)
                return;

            int width = map.WidthCells;
            int height = map.HeightCells;
            int count = width * height;
            TerrainRegion[] regions = new TerrainRegion[count];
            int[] owners = new int[count];
            TerrainRoadType[] roadTypes = new TerrainRoadType[count];
            float[] roadCoverage = new float[count];
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = x + z * width;
                    regions[index] = map.GetRegion(x, z);
                    owners[index] = map.GetOwnerSiteId(x, z);
                    roadTypes[index] = map.GetRoadType(x, z);
                    roadCoverage[index] = map.GetRoadCoverage(x, z);
                }
            }

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector2 point = map.CellCenter(x, z);
                    float weight = SiteCoreWarpWeight(map, point);
                    Vector2 warped = Vector2.Lerp(point,
                        WarpPoint(point, map.Seed, frequencies, amplitudes), weight);
                    float sampleX = Mathf.Clamp((warped.x - map.Origin.x) / map.CellSize - 0.5f,
                        0f, width - 1f);
                    float sampleZ = Mathf.Clamp((warped.y - map.Origin.y) / map.CellSize - 0.5f,
                        0f, height - 1f);
                    int nearestX = Mathf.RoundToInt(sampleX);
                    int nearestZ = Mathf.RoundToInt(sampleZ);
                    int nearest = nearestX + nearestZ * width;
                    TerrainRegion region = regions[nearest];
                    map.SetRegion(x, z, region, owners[nearest]);
                    float coverage = SampleCoverage(roadCoverage, width, height,
                        sampleX, sampleZ, out int strongest);
                    TerrainRoadType type = coverage > 0f ? roadTypes[strongest] : TerrainRoadType.None;
                    map.SetRoad(x, z, coverage, type);
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  道路重采样 — 连续覆盖度与主导路面类型保持一致
        // ════════════════════════════════════════════════════════════

        private static float SampleCoverage(float[] values, int width, int height,
            float x, float z, out int strongest)
        {
            int x0 = Mathf.FloorToInt(x);
            int z0 = Mathf.FloorToInt(z);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int z1 = Mathf.Min(z0 + 1, height - 1);
            float tx = x - x0;
            float tz = z - z0;
            int i00 = x0 + z0 * width;
            int i10 = x1 + z0 * width;
            int i01 = x0 + z1 * width;
            int i11 = x1 + z1 * width;
            float w00 = (1f - tx) * (1f - tz);
            float w10 = tx * (1f - tz);
            float w01 = (1f - tx) * tz;
            float w11 = tx * tz;
            float c00 = values[i00] * w00;
            float c10 = values[i10] * w10;
            float c01 = values[i01] * w01;
            float c11 = values[i11] * w11;
            strongest = i00;
            float best = c00;
            if (c10 > best) { best = c10; strongest = i10; }
            if (c01 > best) { best = c01; strongest = i01; }
            if (c11 > best) strongest = i11;
            return c00 + c10 + c01 + c11;
        }

        private static Vector2 WarpPoint(Vector2 point, int seed,
            Vector2 frequencies, Vector2 amplitudes)
        {
            float broadX = Mathf.PerlinNoise(point.x * frequencies.x + seed * 0.017f,
                point.y * frequencies.x + seed * 0.029f);
            float broadY = Mathf.PerlinNoise(point.x * frequencies.x + seed * 0.043f + 31.7f,
                point.y * frequencies.x + seed * 0.011f + 47.3f);
            float detailX = Mathf.PerlinNoise(point.x * frequencies.y + seed * 0.067f + 71.9f,
                point.y * frequencies.y + seed * 0.053f + 13.1f);
            float detailY = Mathf.PerlinNoise(point.x * frequencies.y + seed * 0.037f + 19.3f,
                point.y * frequencies.y + seed * 0.079f + 83.7f);
            return point + new Vector2(broadX - 0.5f, broadY - 0.5f) * (amplitudes.x * 2f) +
                new Vector2(detailX - 0.5f, detailY - 0.5f) * (amplitudes.y * 2f);
        }

        private static float SiteCoreWarpWeight(TerrainMapData map, Vector2 point)
        {
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < map.Sites.Count; i++)
            {
                TerrainSite site = map.Sites[i];
                float distance = (point - site.Center).sqrMagnitude /
                    (site.Radius * site.Radius);
                nearest = Mathf.Min(nearest, distance);
            }
            return Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.12f * 0.12f, 0.4f * 0.4f, nearest));
        }
    }
}
