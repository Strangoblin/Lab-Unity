using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class NormalSmoother
{
    public enum StorageMode
    {
        VertexColors,
        VertexNormals
    }

    public struct Settings
    {
        public float angleThreshold;
        public float positionEpsilon;
        public StorageMode storageMode;
    }

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
                hash = (hash * 397) ^ y;
                hash = (hash * 397) ^ z;
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
                hash = (hash * 397) ^ positionGroupIndex;
                hash = (hash * 397) ^ clusterIndex;
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

        settings.angleThreshold = Mathf.Clamp(settings.angleThreshold, 0f, 180f);
        settings.positionEpsilon = Mathf.Max(1e-6f, settings.positionEpsilon);

        Vector3[] sourceVertices = sourceMesh.vertices;
        int vertexCount = sourceVertices.Length;

        Mesh outputMesh = new Mesh
        {
            name = string.IsNullOrEmpty(sourceMesh.name) ? "SmoothedMesh" : sourceMesh.name + "_Smoothed"
        };

        if (vertexCount == 0)
        {
            outputMesh.indexFormat = sourceMesh.indexFormat;
            outputMesh.subMeshCount = Mathf.Max(1, sourceMesh.subMeshCount);
            return outputMesh;
        }

        Vector3[] sourceNormals = sourceMesh.normals;
        Vector4[] sourceTangents = sourceMesh.tangents;
        Color[] sourceColors = sourceMesh.colors;
        BoneWeight[] sourceBoneWeights = sourceMesh.boneWeights;
        bool hasSourceNormals = sourceNormals != null && sourceNormals.Length == vertexCount;
        bool hasTangents = sourceTangents != null && sourceTangents.Length == vertexCount;
        bool hasSourceColors = sourceColors != null && sourceColors.Length == vertexCount;
        bool hasBoneWeights = sourceBoneWeights != null && sourceBoneWeights.Length == vertexCount;

        List<Vector4>[] sourceUVs = new List<Vector4>[8];
        bool[] hasUvChannel = new bool[8];
        for (int channel = 0; channel < 8; channel++)
        {
            sourceUVs[channel] = new List<Vector4>(vertexCount);
            sourceMesh.GetUVs(channel, sourceUVs[channel]);
            hasUvChannel[channel] = sourceUVs[channel].Count == vertexCount;
            if (!hasUvChannel[channel])
            {
                sourceUVs[channel].Clear();
            }
        }

        int subMeshCount = Mathf.Max(1, sourceMesh.subMeshCount);
        List<int[]> sourceTriangles = new List<int[]>(subMeshCount);
        List<int> faceStartBySubMesh = new List<int>(subMeshCount);
        List<CornerData> corners = new List<CornerData>();
        Dictionary<PositionKey, List<int>> positionGroups = new Dictionary<PositionKey, List<int>>();
        int faceIndex = 0;

        for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
        {
            faceStartBySubMesh.Add(faceIndex);
            int[] triangles = sourceMesh.GetTriangles(subMesh);
            sourceTriangles.Add(triangles);

            for (int index = 0; index < triangles.Length; index += 3)
            {
                int i0 = triangles[index];
                int i1 = triangles[index + 1];
                int i2 = triangles[index + 2];

                Vector3 p0 = sourceVertices[i0];
                Vector3 p1 = sourceVertices[i1];
                Vector3 p2 = sourceVertices[i2];

                Vector3 faceVector = Vector3.Cross(p1 - p0, p2 - p0);
                float faceMagnitude = faceVector.magnitude;
                Vector3 faceNormal = faceMagnitude > 1e-8f ? faceVector / faceMagnitude : Vector3.up;
                float area = faceMagnitude * 0.5f;

                AddCorner(corners, positionGroups, i0, faceNormal, area, sourceVertices, settings.positionEpsilon);
                AddCorner(corners, positionGroups, i1, faceNormal, area, sourceVertices, settings.positionEpsilon);
                AddCorner(corners, positionGroups, i2, faceNormal, area, sourceVertices, settings.positionEpsilon);
                faceIndex++;
            }
        }

        List<Dictionary<int, Vector3>> clusterNormalsByGroup = new List<Dictionary<int, Vector3>>(positionGroups.Count);
        float cosThreshold = Mathf.Cos(settings.angleThreshold * Mathf.Deg2Rad);
        int groupIndex = 0;

        foreach (var group in positionGroups)
        {
            List<int> cornerIndices = group.Value;
            bool[] visited = new bool[cornerIndices.Count];
            Dictionary<int, Vector3> clusterNormals = new Dictionary<int, Vector3>();
            int clusterIndex = 0;

            for (int start = 0; start < cornerIndices.Count; start++)
            {
                if (visited[start])
                {
                    continue;
                }

                Queue<int> queue = new Queue<int>();
                List<int> clusterMembers = new List<int>();
                visited[start] = true;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int currentCornerIndex = cornerIndices[current];
                    clusterMembers.Add(currentCornerIndex);

                    for (int other = 0; other < cornerIndices.Count; other++)
                    {
                        if (visited[other])
                        {
                            continue;
                        }

                        int otherCornerIndex = cornerIndices[other];
                        if (Vector3.Dot(corners[currentCornerIndex].faceNormal, corners[otherCornerIndex].faceNormal) >= cosThreshold)
                        {
                            visited[other] = true;
                            queue.Enqueue(other);
                        }
                    }
                }

                Vector3 normalSum = Vector3.zero;
                for (int i = 0; i < clusterMembers.Count; i++)
                {
                    CornerData corner = corners[clusterMembers[i]];
                    float weight = Mathf.Max(1e-6f, corner.area);
                    normalSum += corner.faceNormal * weight;
                }

                Vector3 smoothedNormal = normalSum.sqrMagnitude > 1e-12f
                    ? normalSum.normalized
                    : corners[clusterMembers[0]].faceNormal;

                clusterNormals[clusterIndex] = smoothedNormal;

                for (int i = 0; i < clusterMembers.Count; i++)
                {
                    int cornerIndex = clusterMembers[i];
                    CornerData corner = corners[cornerIndex];
                    corner.positionGroupIndex = groupIndex;
                    corner.clusterIndex = clusterIndex;
                    corners[cornerIndex] = corner;
                }

                clusterIndex++;
            }

            clusterNormalsByGroup.Add(clusterNormals);
            groupIndex++;
        }

        Dictionary<OutputVertexKey, int> outputVertexMap = new Dictionary<OutputVertexKey, int>();
        List<Vector3> outputVertices = new List<Vector3>();
        bool writeSmoothedNormals = settings.storageMode == StorageMode.VertexNormals || !hasSourceNormals;
        bool writeEncodedColors = settings.storageMode == StorageMode.VertexColors;
        List<Vector3> outputNormals = new List<Vector3>();
        List<Vector4> outputTangents = hasTangents ? new List<Vector4>() : null;
        List<Color> outputColors = (writeEncodedColors || hasSourceColors) ? new List<Color>() : null;
        List<BoneWeight> outputBoneWeights = hasBoneWeights ? new List<BoneWeight>() : null;
        List<Vector4>[] outputUVs = new List<Vector4>[8];
        List<int>[] outputTrianglesBySubMesh = new List<int>[subMeshCount];
        for (int channel = 0; channel < 8; channel++)
        {
            outputUVs[channel] = hasUvChannel[channel] ? new List<Vector4>() : null;
        }
        for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
        {
            outputTrianglesBySubMesh[subMesh] = new List<int>(sourceTriangles[subMesh].Length);
        }

        int GetOrCreateOutputVertex(int sourceVertexIndex, int positionGroupIndex, int clusterIndex, Vector3 smoothedNormal)
        {
            OutputVertexKey key = new OutputVertexKey(sourceVertexIndex, positionGroupIndex, clusterIndex);
            if (outputVertexMap.TryGetValue(key, out int outputIndex))
            {
                return outputIndex;
            }

            outputIndex = outputVertices.Count;
            outputVertexMap[key] = outputIndex;

            outputVertices.Add(sourceVertices[sourceVertexIndex]);
            if (writeSmoothedNormals)
            {
                outputNormals.Add(smoothedNormal);
            }
            else if (hasSourceNormals)
            {
                outputNormals.Add(sourceNormals[sourceVertexIndex]);
            }

            if (hasTangents)
            {
                outputTangents.Add(sourceTangents[sourceVertexIndex]);
            }

            if (outputColors != null)
            {
                if (writeEncodedColors)
                {
                    outputColors.Add(EncodeNormal(smoothedNormal));
                }
                else if (hasSourceColors)
                {
                    outputColors.Add(sourceColors[sourceVertexIndex]);
                }
            }

            if (hasBoneWeights)
            {
                outputBoneWeights.Add(sourceBoneWeights[sourceVertexIndex]);
            }

            for (int channel = 0; channel < 8; channel++)
            {
                if (outputUVs[channel] != null)
                {
                    outputUVs[channel].Add(sourceUVs[channel][sourceVertexIndex]);
                }
            }

            return outputIndex;
        }

        for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
        {
            int[] triangles = sourceTriangles[subMesh];
            int faceStart = faceStartBySubMesh[subMesh];

            for (int index = 0; index < triangles.Length; index += 3)
            {
                int globalFaceIndex = faceStart + index / 3;
                int cornerBase = globalFaceIndex * 3;

                for (int localCorner = 0; localCorner < 3; localCorner++)
                {
                    int cornerIndex = cornerBase + localCorner;
                    CornerData corner = corners[cornerIndex];
                    Vector3 smoothedNormal = clusterNormalsByGroup[corner.positionGroupIndex][corner.clusterIndex];
                    int outputVertexIndex = GetOrCreateOutputVertex(corner.sourceVertexIndex, corner.positionGroupIndex, corner.clusterIndex, smoothedNormal);
                    outputTrianglesBySubMesh[subMesh].Add(outputVertexIndex);
                }
            }
        }

        outputMesh.indexFormat = outputVertices.Count > 65535 ? IndexFormat.UInt32 : sourceMesh.indexFormat;
        outputMesh.SetVertices(outputVertices);
        if (outputNormals.Count > 0)
        {
            outputMesh.SetNormals(outputNormals);
        }

        if (hasTangents)
        {
            outputMesh.SetTangents(outputTangents);
        }

        if (outputColors != null)
        {
            outputMesh.SetColors(outputColors);
        }

        if (hasBoneWeights)
        {
            outputMesh.boneWeights = outputBoneWeights.ToArray();
            outputMesh.bindposes = sourceMesh.bindposes;
        }

        for (int channel = 0; channel < 8; channel++)
        {
            if (outputUVs[channel] != null)
            {
                outputMesh.SetUVs(channel, outputUVs[channel]);
            }
        }

        outputMesh.subMeshCount = subMeshCount;
        for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
        {
            outputMesh.SetTriangles(outputTrianglesBySubMesh[subMesh], subMesh, true);
        }
        outputMesh.RecalculateBounds();
        return outputMesh;
    }

    private static void AddCorner(
        List<CornerData> corners,
        Dictionary<PositionKey, List<int>> positionGroups,
        int sourceVertexIndex,
        Vector3 faceNormal,
        float area,
        Vector3[] sourceVertices,
        float positionEpsilon)
    {
        int cornerIndex = corners.Count;
        corners.Add(new CornerData
        {
            sourceVertexIndex = sourceVertexIndex,
            faceNormal = faceNormal,
            area = area,
            positionGroupIndex = -1,
            clusterIndex = -1
        });

        PositionKey key = GetPositionKey(sourceVertices[sourceVertexIndex], positionEpsilon);
        if (!positionGroups.TryGetValue(key, out List<int> groupCorners))
        {
            groupCorners = new List<int>();
            positionGroups.Add(key, groupCorners);
        }

        groupCorners.Add(cornerIndex);
    }

    private static PositionKey GetPositionKey(Vector3 position, float positionEpsilon)
    {
        float inv = 1f / positionEpsilon;
        return new PositionKey(
            Mathf.RoundToInt(position.x * inv),
            Mathf.RoundToInt(position.y * inv),
            Mathf.RoundToInt(position.z * inv));
    }

    private static Color EncodeNormal(Vector3 normal)
    {
        Vector3 encoded = normal.normalized * 0.5f + Vector3.one * 0.5f;
        return new Color(encoded.x, encoded.y, encoded.z, 1f);
    }
}