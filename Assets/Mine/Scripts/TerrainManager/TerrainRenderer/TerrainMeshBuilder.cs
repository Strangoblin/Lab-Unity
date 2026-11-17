// ════════════════════════════════════════════════════════════
//  TerrainMeshBuilder — 把 TerrainGrid 转为单块规则网格
// ════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;

namespace Mine.Terrain
{
    /// <summary>
    /// 当前里程碑使用整块网格；后续可替换为分块、LOD 或 GPU 驱动网格。
    /// </summary>
    public static class TerrainMeshBuilder
    {
        public static Mesh Build(TerrainGrid grid)
        {
            int verticesX = grid.WidthCells + 1;
            int verticesZ = grid.HeightCells + 1;
            int vertexCount = verticesX * verticesZ;

            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];

            for (int z = 0; z < verticesZ; z++)
            {
                for (int x = 0; x < verticesX; x++)
                {
                    int index = z * verticesX + x;
                    vertices[index] = new Vector3(
                        x * grid.CellSize,
                        grid.GetHeight(x, z),
                        z * grid.CellSize);
                    normals[index] = grid.ComputeVertexNormal(x, z);
                    uvs[index] = new Vector2(
                        x / (float)grid.WidthCells,
                        z / (float)grid.HeightCells);
                }
            }

            var triangles = new int[grid.CellCount * 6];
            int cursor = 0;
            for (int z = 0; z < grid.HeightCells; z++)
            {
                for (int x = 0; x < grid.WidthCells; x++)
                {
                    int i00 = z * verticesX + x;
                    int i10 = i00 + 1;
                    int i01 = i00 + verticesX;
                    int i11 = i01 + 1;

                    // 与 TerrainGrid.TrySample 共用 (00,11) 对角线。
                    triangles[cursor++] = i00;
                    triangles[cursor++] = i11;
                    triangles[cursor++] = i10;
                    triangles[cursor++] = i00;
                    triangles[cursor++] = i01;
                    triangles[cursor++] = i11;
                }
            }

            var mesh = new Mesh { name = "Terrain Surface" };
            if (vertexCount > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
