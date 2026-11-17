// ════════════════════════════════════════════════════════════
//  TerrainVisualSmoke — 在 Play Mode 生成一块远离现有场景的地形并架设相机
//  仅用于截图验证，不参与 Unity 编译。
// ════════════════════════════════════════════════════════════

using UnityEngine;
using Mine.Terrain;

public static class Script
{
    public static void Main()
    {
        Vector3 origin = new Vector3(2000f, 0f, 2000f);

        var managerObject = new GameObject("Terrain Visual Smoke");
        var manager = managerObject.AddComponent<TerrainManager>();
        manager.Settings.WidthCells = 60;
        manager.Settings.HeightCells = 60;
        manager.Settings.CellSize = 1f;
        manager.Settings.Origin = origin;
        manager.Settings.Provider = TerrainProviderKind.Noise;
        manager.Settings.HeightScale = 6f;
        manager.Settings.NoiseScale = 0.05f;
        manager.Settings.NoiseOctaves = 3;
        manager.Settings.Seed = 7;
        manager.Generate();

        var cameraObject = new GameObject("Terrain Visual Smoke Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.depth = 1000f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.45f, 0.60f, 0.80f);
        camera.fieldOfView = 55f;
        camera.transform.position = origin + new Vector3(0f, 45f, -45f);
        camera.transform.LookAt(origin + new Vector3(30f, 0f, 30f));

        var lightObject = new GameObject("Terrain Visual Smoke Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        Debug.Log("Terrain visual smoke setup complete.");
    }
}
