// ════════════════════════════════════════════════════════════
//  TerrainCoreChecks — 数据层、网格层与渲染层的自检
//  通过 unityctl script execute -f 运行，不参与 Unity 编译。
// ════════════════════════════════════════════════════════════

using System;
using UnityEngine;
using Mine.Terrain;

public static class Script
{
    public static void Main()
    {
        CheckFlatProvider();
        CheckNoiseProviderAndSampling();
        CheckMesh();
        CheckRenderer();
        CheckManager();
        Debug.Log("Terrain core validation passed.");
    }

    private static void CheckFlatProvider()
    {
        var settings = new TerrainSettings
        {
            WidthCells = 8,
            HeightCells = 6,
            CellSize = 2f,
            Origin = new Vector3(10f, 3f, -4f),
            Provider = TerrainProviderKind.Flat,
            DefaultLayer = TerrainSurfaceLayer.Sand
        };
        settings.Validate();

        var grid = new TerrainGrid(settings.WidthCells, settings.HeightCells,
            settings.CellSize, settings.Origin);
        new FlatTerrainProvider().Generate(grid, settings);

        for (int z = 0; z <= grid.HeightCells; z++)
            for (int x = 0; x <= grid.WidthCells; x++)
                Check(Mathf.Approximately(grid.GetHeight(x, z), 0f), "Flat height is not zero.");

        for (int z = 0; z < grid.HeightCells; z++)
            for (int x = 0; x < grid.WidthCells; x++)
                Check(grid.GetLayer(x, z) == TerrainSurfaceLayer.Sand, "Flat layer mismatch.");

        Check(grid.TrySample(10f, -4f, out TerrainSample sample), "Origin sample failed.");
        Check(Mathf.Approximately(sample.Position.y, 3f), "Origin sample height mismatch.");
        Check(sample.Normal.y > 0.999f, "Flat normal is not up.");
        Check(sample.Layer == TerrainSurfaceLayer.Sand, "Origin sample layer mismatch.");
        Check(!grid.TrySample(9.99f, -4f, out _), "Outside sample was accepted.");
    }

    private static void CheckNoiseProviderAndSampling()
    {
        var settings = new TerrainSettings
        {
            WidthCells = 16,
            HeightCells = 16,
            CellSize = 1f,
            Origin = new Vector3(-5f, 2f, 7f),
            Provider = TerrainProviderKind.Noise,
            Seed = 123,
            HeightScale = 4f,
            NoiseScale = 0.1f,
            NoiseOctaves = 3,
            DefaultLayer = TerrainSurfaceLayer.Grass
        };
        settings.Validate();

        var grid = new TerrainGrid(settings.WidthCells, settings.HeightCells,
            settings.CellSize, settings.Origin);
        new NoiseTerrainProvider().Generate(grid, settings);

        Check(grid.MaxHeight > grid.MinHeight, "Noise terrain is flat.");
        for (int z = 0; z <= grid.HeightCells; z++)
        {
            for (int x = 0; x <= grid.WidthCells; x++)
            {
                float worldX = grid.Origin.x + x * grid.CellSize;
                float worldZ = grid.Origin.z + z * grid.CellSize;
                Check(grid.TrySample(worldX, worldZ, out TerrainSample sample),
                    "Vertex sample failed.");
                Check(Mathf.Abs(sample.Position.y -
                    (grid.Origin.y + grid.GetHeight(x, z))) < 1e-4f,
                    "Vertex sample height does not match the grid.");
            }
        }

        for (int z = 0; z < grid.HeightCells; z++)
        {
            for (int x = 0; x < grid.WidthCells; x++)
            {
                float worldX = grid.Origin.x + (x + 0.5f) * grid.CellSize;
                float worldZ = grid.Origin.z + (z + 0.5f) * grid.CellSize;
                Check(grid.TrySample(worldX, worldZ, out TerrainSample sample),
                    "Cell sample failed.");
                Check(sample.Layer == grid.GetLayer(x, z), "Cell sample layer mismatch.");
            }
        }
    }

    private static void CheckMesh()
    {
        var grid = new TerrainGrid(4, 3, 2f, new Vector3(1f, 0f, 2f));
        for (int z = 0; z <= grid.HeightCells; z++)
            for (int x = 0; x <= grid.WidthCells; x++)
                grid.SetHeight(x, z, x * 0.25f - z * 0.1f);
        grid.RecalculateHeightRange();

        Mesh mesh = TerrainMeshBuilder.Build(grid);
        try
        {
            Check(mesh.vertexCount == grid.VertexCount, "Mesh vertex count mismatch.");
            Check(mesh.triangles.Length == grid.CellCount * 6, "Mesh triangle count mismatch.");
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 normal = Vector3.Cross(
                    vertices[triangles[i + 1]] - vertices[triangles[i]],
                    vertices[triangles[i + 2]] - vertices[triangles[i]]);
                Check(normal.y > 0f, "Mesh triangle winding is not facing up.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static void CheckRenderer()
    {
        var grid = new TerrainGrid(4, 4, 1f, Vector3.zero);
        for (int z = 0; z <= grid.HeightCells; z++)
            for (int x = 0; x <= grid.WidthCells; x++)
                grid.SetHeight(x, z, 0f);
        for (int z = 0; z < grid.HeightCells; z++)
            for (int x = 0; x < grid.WidthCells; x++)
                grid.SetLayer(x, z, TerrainSurfaceLayer.Grass);
        grid.RecalculateHeightRange();

        var parent = new GameObject("TerrainValidationRoot");
        try
        {
            var renderer = new TerrainSurfaceRenderer(parent.transform, grid, null, true, true);
            Check(renderer.Root != null, "Renderer root was not created.");
            Check(renderer.Mesh != null, "Renderer mesh was not created.");
            Check(renderer.Root.GetComponent<MeshFilter>()?.sharedMesh == renderer.Mesh,
                "Mesh filter is not bound to the renderer mesh.");
            MeshRenderer meshRenderer = renderer.Root.GetComponent<MeshRenderer>();
            Check(meshRenderer != null && meshRenderer.sharedMaterial != null,
                "Mesh renderer or material is missing.");
            Check(meshRenderer.sharedMaterial.GetTexture("_ColorMap") != null,
                "Color map was not assigned.");
            renderer.Dispose();
            Check(renderer.Root == null && renderer.Mesh == null,
                "Renderer did not release its resources.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(parent);
        }
    }

    private static void CheckManager()
    {
        var go = new GameObject("TerrainValidationManager");
        try
        {
            var manager = go.AddComponent<TerrainManager>();
            manager.Settings.WidthCells = 8;
            manager.Settings.HeightCells = 6;
            manager.Settings.CellSize = 1f;
            manager.Settings.Origin = Vector3.zero;
            manager.Settings.Provider = TerrainProviderKind.Flat;

            manager.Generate();
            Check(manager.HasData, "Manager did not publish data.");
            Check(manager.DataVersion == 1, "Manager first version mismatch.");
            Check(manager.GeneratedRoot != null, "Manager did not create a render root.");
            Check(manager.TrySample(0f, 0f, out TerrainSample sample) &&
                Mathf.Approximately(sample.Position.y, 0f), "Manager sample failed.");

            manager.Generate();
            Check(manager.DataVersion == 2, "Manager version did not advance.");
            manager.Release();
            Check(!manager.HasData && manager.GeneratedRoot == null,
                "Manager release did not clear data and render resources.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"[TerrainCoreChecks] {message}");
    }
}
