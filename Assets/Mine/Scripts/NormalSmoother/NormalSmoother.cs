// ════════════════════════════════════════════════════════════
//  NormalSmoother — 按角度聚类重建网格，烘焙平滑法线。
//  同一位置的面按法线夹角分组，组内做面积加权平均。
// ════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.NormalSmoother
{
    /// <summary>
    /// 无状态网格法线平滑服务：按位置合并顶点，按面法线夹角聚类后重建网格。
    /// 结果写入顶点法线或顶点色，供硬表面与卡通着色采样。
    /// </summary>
    ///
    /// <remarks>
    /// 源网格必须开启 Read/Write；返回值是新建网格，所有权归调用方。
    /// 顶点色模式下法线编码为 <c>n * 0.5 + 0.5</c>，alpha 恒为 1。
    /// </remarks>
    public static class NormalSmoother
    {
        public enum StorageMode
        {
            VertexColors,
            VertexNormals
        }

        /// <summary>平滑参数；<see cref="CreateSmoothedMesh"/> 内部会夹紧到合法范围。</summary>
        [Serializable]
        public struct Settings
        {
            [Range(0f, 180f)] public float angleThreshold;
            [Min(1e-6f)] public float positionEpsilon;
            public StorageMode storageMode;
        }

        // ════════════════════════════════════════════════════════════
        //  常量 — 数值下限与顶点格式边界
        // ════════════════════════════════════════════════════════════

        private const int   k_UvChannelCount     = 8;
        private const int   k_UInt16VertexLimit  = 65535;
        private const float k_MinPositionEpsilon = 1e-6f;
        private const float k_MinFaceMagnitude   = 1e-8f;
        private const float k_MinAreaWeight      = 1e-6f;
        private const float k_MinNormalSumSquare = 1e-12f;
        private const float k_NormalEncodeScale  = 0.5f;
        private const int   k_HashMultiplier     = 397;

        // ════════════════════════════════════════════════════════════
        //  公开 API — 由源网格生成平滑法线网格
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 按 <paramref name="settings"/> 聚类面法线，返回新的平滑网格；不可读网格返回 null。
        /// </summary>
        public static Mesh CreateSmoothedMesh(Mesh sourceMesh, Settings settings)
        {
            if (sourceMesh == null)
            {
                return null;
            }

            if (!sourceMesh.isReadable)
            {
                Debug.LogWarning($"Mesh '{sourceMesh.name}' is not readable. Enable Read/Write on the importer before smoothing.");
                return null;
            }

            settings = NormalizeSettings(settings);

            Vector3[] sourceVertices = sourceMesh.vertices;
            Mesh outputMesh = new Mesh
            {
                name = BuildMeshName(sourceMesh)
            };

            if (sourceVertices.Length == 0)
            {
                outputMesh.indexFormat = sourceMesh.indexFormat;
                outputMesh.subMeshCount = Mathf.Max(1, sourceMesh.subMeshCount);
                return outputMesh;
            }

            SourceMeshData source = ReadMeshData(sourceMesh, sourceVertices);
            List<CornerData> corners = BuildCorners(source);
            Dictionary<PositionKey, List<int>> positionGroups = GroupCornersByPosition(corners, source.vertices, settings.positionEpsilon);
            List<Dictionary<int, Vector3>> clusterNormalsByGroup = BuildClusters(corners, positionGroups, settings.angleThreshold);
            WriteOutputMesh(outputMesh, source, corners, clusterNormalsByGroup, settings);

            return outputMesh;
        }

        // ════════════════════════════════════════════════════════════
        //  源数据读取 — 顶点、通道与子网格三角形
        // ════════════════════════════════════════════════════════════

        private static SourceMeshData ReadMeshData(Mesh mesh, Vector3[] vertices)
        {
            int vertexCount = vertices.Length;
            int subMeshCount = Mathf.Max(1, mesh.subMeshCount);

            SourceMeshData data = new SourceMeshData
            {
                vertices           = vertices,
                normals            = mesh.normals,
                tangents           = mesh.tangents,
                colors             = mesh.colors,
                bindposes          = mesh.bindposes,
                indexFormat        = mesh.indexFormat,
                boneWeights        = mesh.boneWeights,
                subMeshCount       = subMeshCount,
                triangles          = new List<int[]>(subMeshCount),
                faceStartBySubMesh = new List<int>(subMeshCount),
                uvs                = new List<Vector4>[k_UvChannelCount],
                hasUvChannel       = new bool[k_UvChannelCount]
            };

            data.hasNormals     = data.normals != null && data.normals.Length == vertexCount;
            data.hasTangents    = data.tangents != null && data.tangents.Length == vertexCount;
            data.hasColors      = data.colors != null && data.colors.Length == vertexCount;
            data.hasBoneWeights = data.boneWeights != null && data.boneWeights.Length == vertexCount;

            for (int channel = 0; channel < k_UvChannelCount; channel++)
            {
                data.uvs[channel] = new List<Vector4>(vertexCount);
                mesh.GetUVs(channel, data.uvs[channel]);
                data.hasUvChannel[channel] = data.uvs[channel].Count == vertexCount;
                if (!data.hasUvChannel[channel])
                {
                    data.uvs[channel].Clear();
                }
            }

            int faceIndex = 0;
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                data.faceStartBySubMesh.Add(faceIndex);
                int[] triangles = mesh.GetTriangles(subMesh);
                data.triangles.Add(triangles);
                faceIndex += triangles.Length / 3;
            }

            return data;
        }

        // ════════════════════════════════════════════════════════════
        //  拐角构建 — 每个三角形顶点记录所在面的法线与面积
        // ════════════════════════════════════════════════════════════

        private static List<CornerData> BuildCorners(SourceMeshData source)
        {
            int faceCount = 0;
            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                faceCount += source.triangles[subMesh].Length / 3;
            }

            List<CornerData> corners = new List<CornerData>(faceCount * 3);
            Vector3[] vertices = source.vertices;

            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                int[] triangles = source.triangles[subMesh];

                for (int index = 0; index < triangles.Length; index += 3)
                {
                    int i0 = triangles[index];
                    int i1 = triangles[index + 1];
                    int i2 = triangles[index + 2];

                    Vector3 p0 = vertices[i0];
                    Vector3 p1 = vertices[i1];
                    Vector3 p2 = vertices[i2];

                    Vector3 faceVector = Vector3.Cross(p1 - p0, p2 - p0);
                    float faceMagnitude = faceVector.magnitude;
                    Vector3 faceNormal = faceMagnitude > k_MinFaceMagnitude ? faceVector / faceMagnitude : Vector3.up;
                    float area = faceMagnitude * 0.5f;

                    corners.Add(CreateCorner(i0, faceNormal, area));
                    corners.Add(CreateCorner(i1, faceNormal, area));
                    corners.Add(CreateCorner(i2, faceNormal, area));
                }
            }

            return corners;
        }

        private static CornerData CreateCorner(int sourceVertexIndex, Vector3 faceNormal, float area)
        {
            return new CornerData
            {
                sourceVertexIndex = sourceVertexIndex,
                faceNormal        = faceNormal,
                area              = area,
                positionGroupIndex = -1,
                clusterIndex       = -1
            };
        }

        // ════════════════════════════════════════════════════════════
        //  位置分组 — 量化坐标把同位置拐角归为一组
        // ════════════════════════════════════════════════════════════

        private static Dictionary<PositionKey, List<int>> GroupCornersByPosition(
            List<CornerData> corners,
            Vector3[] vertices,
            float positionEpsilon)
        {
            Dictionary<PositionKey, List<int>> positionGroups = new Dictionary<PositionKey, List<int>>();

            for (int cornerIndex = 0; cornerIndex < corners.Count; cornerIndex++)
            {
                CornerData corner = corners[cornerIndex];
                PositionKey key = GetPositionKey(vertices[corner.sourceVertexIndex], positionEpsilon);

                if (!positionGroups.TryGetValue(key, out List<int> groupCorners))
                {
                    groupCorners = new List<int>();
                    positionGroups.Add(key, groupCorners);
                }

                groupCorners.Add(cornerIndex);
            }

            return positionGroups;
        }

        private static PositionKey GetPositionKey(Vector3 position, float positionEpsilon)
        {
            float inv = 1f / positionEpsilon;
            return new PositionKey(
                Mathf.RoundToInt(position.x * inv),
                Mathf.RoundToInt(position.y * inv),
                Mathf.RoundToInt(position.z * inv));
        }

        // ════════════════════════════════════════════════════════════
        //  法线聚类 — 组内按夹角连通，簇法线为面积加权平均
        // ════════════════════════════════════════════════════════════

        private static List<Dictionary<int, Vector3>> BuildClusters(
            List<CornerData> corners,
            Dictionary<PositionKey, List<int>> positionGroups,
            float angleThreshold)
        {
            List<Dictionary<int, Vector3>> clusterNormalsByGroup = new List<Dictionary<int, Vector3>>(positionGroups.Count);
            float cosThreshold = Mathf.Cos(angleThreshold * Mathf.Deg2Rad);
            int groupIndex = 0;

            foreach (KeyValuePair<PositionKey, List<int>> group in positionGroups)
            {
                List<int> cornerIndices = group.Value;
                bool[] visited = new bool[cornerIndices.Count];
                Dictionary<int, Vector3> clusterNormals = new Dictionary<int, Vector3>();

                for (int start = 0; start < cornerIndices.Count; start++)
                {
                    if (visited[start])
                    {
                        continue;
                    }

                    int clusterIndex = clusterNormals.Count;
                    List<int> clusterMembers = CollectClusterMembers(corners, cornerIndices, visited, start, cosThreshold);
                    Vector3 smoothedNormal = AverageClusterNormal(corners, clusterMembers);
                    clusterNormals[clusterIndex] = smoothedNormal;

                    for (int i = 0; i < clusterMembers.Count; i++)
                    {
                        int cornerIndex = clusterMembers[i];
                        CornerData corner = corners[cornerIndex];
                        corner.positionGroupIndex = groupIndex;
                        corner.clusterIndex = clusterIndex;
                        corners[cornerIndex] = corner;
                    }
                }

                clusterNormalsByGroup.Add(clusterNormals);
                groupIndex++;
            }

            return clusterNormalsByGroup;
        }

        private static List<int> CollectClusterMembers(
            List<CornerData> corners,
            List<int> cornerIndices,
            bool[] visited,
            int start,
            float cosThreshold)
        {
            List<int> clusterMembers = new List<int>();
            Queue<int> queue = new Queue<int>();
            visited[start] = true;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int currentCornerIndex = cornerIndices[current];
                Vector3 currentNormal = corners[currentCornerIndex].faceNormal;
                clusterMembers.Add(currentCornerIndex);

                for (int other = 0; other < cornerIndices.Count; other++)
                {
                    if (visited[other])
                    {
                        continue;
                    }

                    int otherCornerIndex = cornerIndices[other];
                    if (Vector3.Dot(currentNormal, corners[otherCornerIndex].faceNormal) >= cosThreshold)
                    {
                        visited[other] = true;
                        queue.Enqueue(other);
                    }
                }
            }

            return clusterMembers;
        }

        private static Vector3 AverageClusterNormal(List<CornerData> corners, List<int> clusterMembers)
        {
            Vector3 normalSum = Vector3.zero;

            for (int i = 0; i < clusterMembers.Count; i++)
            {
                CornerData corner = corners[clusterMembers[i]];
                float weight = Mathf.Max(k_MinAreaWeight, corner.area);
                normalSum += corner.faceNormal * weight;
            }

            return normalSum.sqrMagnitude > k_MinNormalSumSquare
                ? normalSum.normalized
                : corners[clusterMembers[0]].faceNormal;
        }

        // ════════════════════════════════════════════════════════════
        //  输出网格 — 按 (源顶点, 位置组, 簇) 拆分并写回各通道
        // ════════════════════════════════════════════════════════════

        private static void WriteOutputMesh(
            Mesh outputMesh,
            SourceMeshData source,
            List<CornerData> corners,
            List<Dictionary<int, Vector3>> clusterNormalsByGroup,
            Settings settings)
        {
            OutputBuffers output = CreateOutputBuffers(source, settings);
            List<int>[] outputTrianglesBySubMesh = CreateOutputTriangles(source);

            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                int[] triangles = source.triangles[subMesh];
                int faceStart = source.faceStartBySubMesh[subMesh];

                for (int index = 0; index < triangles.Length; index += 3)
                {
                    int globalFaceIndex = faceStart + index / 3;
                    int cornerBase = globalFaceIndex * 3;

                    for (int localCorner = 0; localCorner < 3; localCorner++)
                    {
                        CornerData corner = corners[cornerBase + localCorner];
                        Vector3 smoothedNormal = clusterNormalsByGroup[corner.positionGroupIndex][corner.clusterIndex];
                        int outputVertexIndex = GetOrCreateOutputVertex(output, source, corner, smoothedNormal);
                        outputTrianglesBySubMesh[subMesh].Add(outputVertexIndex);
                    }
                }
            }

            ApplyOutputBuffers(outputMesh, source, output, outputTrianglesBySubMesh);
        }

        private static OutputBuffers CreateOutputBuffers(SourceMeshData source, Settings settings)
        {
            OutputBuffers output = new OutputBuffers
            {
                writeSmoothedNormals = settings.storageMode == StorageMode.VertexNormals || !source.hasNormals,
                writeEncodedColors   = settings.storageMode == StorageMode.VertexColors,
                tangents             = source.hasTangents ? new List<Vector4>() : null,
                colors               = null,
                boneWeights          = source.hasBoneWeights ? new List<BoneWeight>() : null
            };

            output.colors = (output.writeEncodedColors || source.hasColors) ? new List<Color>() : null;

            for (int channel = 0; channel < k_UvChannelCount; channel++)
            {
                output.uvs[channel] = source.hasUvChannel[channel] ? new List<Vector4>() : null;
            }

            return output;
        }

        private static List<int>[] CreateOutputTriangles(SourceMeshData source)
        {
            List<int>[] outputTrianglesBySubMesh = new List<int>[source.subMeshCount];
            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                outputTrianglesBySubMesh[subMesh] = new List<int>(source.triangles[subMesh].Length);
            }

            return outputTrianglesBySubMesh;
        }

        private static int GetOrCreateOutputVertex(
            OutputBuffers output,
            SourceMeshData source,
            CornerData corner,
            Vector3 smoothedNormal)
        {
            OutputVertexKey key = new OutputVertexKey(corner.sourceVertexIndex, corner.positionGroupIndex, corner.clusterIndex);
            if (output.vertexMap.TryGetValue(key, out int outputIndex))
            {
                return outputIndex;
            }

            outputIndex = output.vertices.Count;
            output.vertexMap[key] = outputIndex;

            int sourceVertexIndex = corner.sourceVertexIndex;
            output.vertices.Add(source.vertices[sourceVertexIndex]);

            if (output.writeSmoothedNormals)
            {
                output.normals.Add(smoothedNormal);
            }
            else if (source.hasNormals)
            {
                output.normals.Add(source.normals[sourceVertexIndex]);
            }

            if (source.hasTangents)
            {
                output.tangents.Add(source.tangents[sourceVertexIndex]);
            }

            if (output.colors != null)
            {
                if (output.writeEncodedColors)
                {
                    output.colors.Add(EncodeNormal(smoothedNormal));
                }
                else if (source.hasColors)
                {
                    output.colors.Add(source.colors[sourceVertexIndex]);
                }
            }

            if (source.hasBoneWeights)
            {
                output.boneWeights.Add(source.boneWeights[sourceVertexIndex]);
            }

            for (int channel = 0; channel < k_UvChannelCount; channel++)
            {
                if (output.uvs[channel] != null)
                {
                    output.uvs[channel].Add(source.uvs[channel][sourceVertexIndex]);
                }
            }

            return outputIndex;
        }

        private static void ApplyOutputBuffers(
            Mesh outputMesh,
            SourceMeshData source,
            OutputBuffers output,
            List<int>[] outputTrianglesBySubMesh)
        {
            outputMesh.indexFormat = output.vertices.Count > k_UInt16VertexLimit ? IndexFormat.UInt32 : source.indexFormat;
            outputMesh.SetVertices(output.vertices);

            if (output.normals.Count > 0)
            {
                outputMesh.SetNormals(output.normals);
            }

            if (source.hasTangents)
            {
                outputMesh.SetTangents(output.tangents);
            }

            if (output.colors != null)
            {
                outputMesh.SetColors(output.colors);
            }

            if (source.hasBoneWeights)
            {
                outputMesh.boneWeights = output.boneWeights.ToArray();
                outputMesh.bindposes = source.bindposes;
            }

            for (int channel = 0; channel < k_UvChannelCount; channel++)
            {
                if (output.uvs[channel] != null)
                {
                    outputMesh.SetUVs(channel, output.uvs[channel]);
                }
            }

            outputMesh.subMeshCount = source.subMeshCount;
            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                outputMesh.SetTriangles(outputTrianglesBySubMesh[subMesh], subMesh, true);
            }

            outputMesh.RecalculateBounds();
        }

        // ════════════════════════════════════════════════════════════
        //  辅助 — 参数夹紧、命名与法线编码
        // ════════════════════════════════════════════════════════════

        private static Settings NormalizeSettings(Settings settings)
        {
            settings.angleThreshold = Mathf.Clamp(settings.angleThreshold, 0f, 180f);
            settings.positionEpsilon = Mathf.Max(k_MinPositionEpsilon, settings.positionEpsilon);
            return settings;
        }

        private static string BuildMeshName(Mesh sourceMesh)
        {
            return string.IsNullOrEmpty(sourceMesh.name) ? "SmoothedMesh" : sourceMesh.name + "_Smoothed";
        }

        private static Color EncodeNormal(Vector3 normal)
        {
            Vector3 encoded = normal.normalized * k_NormalEncodeScale + Vector3.one * k_NormalEncodeScale;
            return new Color(encoded.x, encoded.y, encoded.z, 1f);
        }

        // ════════════════════════════════════════════════════════════
        //  数据容器 — 键、拐角、源网格与输出缓冲
        // ════════════════════════════════════════════════════════════

        private struct PositionKey : IEquatable<PositionKey>
        {
            private readonly int x;
            private readonly int y;
            private readonly int z;

            public PositionKey(int x, int y, int z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public bool Equals(PositionKey other)
            {
                return x == other.x && y == other.y && z == other.z;
            }

            public override bool Equals(object obj)
            {
                return obj is PositionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = x;
                    hash = (hash * k_HashMultiplier) ^ y;
                    hash = (hash * k_HashMultiplier) ^ z;
                    return hash;
                }
            }
        }

        private struct OutputVertexKey : IEquatable<OutputVertexKey>
        {
            private readonly int sourceVertexIndex;
            private readonly int positionGroupIndex;
            private readonly int clusterIndex;

            public OutputVertexKey(int sourceVertexIndex, int positionGroupIndex, int clusterIndex)
            {
                this.sourceVertexIndex = sourceVertexIndex;
                this.positionGroupIndex = positionGroupIndex;
                this.clusterIndex = clusterIndex;
            }

            public bool Equals(OutputVertexKey other)
            {
                return sourceVertexIndex == other.sourceVertexIndex
                    && positionGroupIndex == other.positionGroupIndex
                    && clusterIndex == other.clusterIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is OutputVertexKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = sourceVertexIndex;
                    hash = (hash * k_HashMultiplier) ^ positionGroupIndex;
                    hash = (hash * k_HashMultiplier) ^ clusterIndex;
                    return hash;
                }
            }
        }

        private struct CornerData
        {
            public int sourceVertexIndex;
            public Vector3 faceNormal;
            public float area;
            public int positionGroupIndex;
            public int clusterIndex;
        }

        private sealed class SourceMeshData
        {
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector4[] tangents;
            public Color[] colors;
            public BoneWeight[] boneWeights;
            public Matrix4x4[] bindposes;
            public List<Vector4>[] uvs;
            public bool[] hasUvChannel;
            public List<int[]> triangles;
            public List<int> faceStartBySubMesh;
            public int subMeshCount;
            public IndexFormat indexFormat;
            public bool hasNormals;
            public bool hasTangents;
            public bool hasColors;
            public bool hasBoneWeights;
        }

        private sealed class OutputBuffers
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector4>[] uvs = new List<Vector4>[k_UvChannelCount];
            public readonly Dictionary<OutputVertexKey, int> vertexMap = new Dictionary<OutputVertexKey, int>();
            public List<Vector4> tangents;
            public List<Color> colors;
            public List<BoneWeight> boneWeights;
            public bool writeSmoothedNormals;
            public bool writeEncodedColors;
        }
    }
}
