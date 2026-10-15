#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public class Noise3DGeneratorWindow : EditorWindow
{
    private int size = 32;
    private float scale = 4f;
    private bool seamless = true;

    private string outputPath = "Assets/Textures/Noise3D.asset";
    private Texture3D noiseTexture;

    private int previewAxis = 2; // 0:X,1:Y,2:Z
    private int previewSlice = 0;
    private Texture2D preview2D;

    [MenuItem("Tools/3D Noise Generator...")]
    public static void ShowWindow()
    {
        var window = GetWindow<Noise3DGeneratorWindow>(false, "3D Noise Generator", true);
        window.minSize = new Vector2(360, 340);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("3D Noise Settings", EditorStyles.boldLabel);
        size = EditorGUILayout.IntSlider("Size", Mathf.Clamp(size, 4, 256), 4, 256);
        scale = EditorGUILayout.FloatField("Scale", Mathf.Max(0.0001f, scale));
        seamless = EditorGUILayout.Toggle("Seamless", seamless);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        outputPath = EditorGUILayout.TextField("Asset Path", outputPath);
        if (GUILayout.Button("Browse...", GUILayout.MaxWidth(90)))
        {
            string directory = "Assets";
            string filename = "Noise3D.asset";
            if (!string.IsNullOrEmpty(outputPath))
            {
                directory = Path.GetDirectoryName(outputPath)?.Replace('\\', '/') ?? "Assets";
                var fn = Path.GetFileName(outputPath);
                if (!string.IsNullOrEmpty(fn)) filename = fn;
            }

            string newPath = EditorUtility.SaveFilePanelInProject(
                "保存 Texture3D",
                filename,
                "asset",
                "选择保存 Texture3D 的位置",
                directory
            );
            if (!string.IsNullOrEmpty(newPath))
            {
                outputPath = newPath;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate 3D Noise"))
        {
            GenerateNoise();
        }

        using (new EditorGUI.DisabledScope(noiseTexture == null))
        {
            if (GUILayout.Button("Save Texture3D"))
            {
                SaveNoise();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        DrawPreviewGUI();
    }

    private void DrawPreviewGUI()
    {
        using (new EditorGUI.DisabledScope(noiseTexture == null))
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            previewAxis = GUILayout.Toolbar(previewAxis, new[] { "X", "Y", "Z" });
            EditorGUILayout.EndHorizontal();

            int maxSlice = Mathf.Max(0, size - 1);
            previewSlice = EditorGUILayout.IntSlider("Slice", Mathf.Clamp(previewSlice, 0, maxSlice), 0, maxSlice);

            if (noiseTexture != null)
            {
                UpdatePreviewTexture();
                if (preview2D != null)
                {
                    float w = EditorGUIUtility.currentViewWidth - 40;
                    float h = w;
                    Rect r = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(true));
                    if (Event.current.type == EventType.Repaint)
                    {
                        GUI.DrawTexture(r, preview2D, ScaleMode.ScaleToFit, false);
                    }
                }
            }
        }
    }

    private void UpdatePreviewTexture()
    {
        if (noiseTexture == null) return;
        if (preview2D == null || preview2D.width != size || preview2D.height != size)
        {
            if (preview2D != null) DestroyImmediate(preview2D);
            preview2D = new Texture2D(size, size, TextureFormat.R8, false) { filterMode = FilterMode.Point };
        }

        Color[] colors = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float v = SampleSlice(x, y);
                colors[x + y * size] = new Color(v, v, v, 1);
            }
        }
        preview2D.SetPixels(colors);
        preview2D.Apply(false, false);
    }

    private float SampleSlice(int x, int y)
    {
        // noiseTexture is RFloat encoded via SetPixels with noise in .r
        // Convert coordinates based on axis/slice
        int xi = 0, yi = 0, zi = 0;
        switch (previewAxis)
        {
            case 0: // X slice
                xi = previewSlice; yi = y; zi = x; break;
            case 1: // Y slice
                xi = x; yi = previewSlice; zi = y; break;
            default: // Z slice
                xi = x; yi = y; zi = previewSlice; break;
        }
        float wx = (float)xi / size;
        float wy = (float)yi / size;
        float wz = (float)zi / size;
        return seamless ? Perlin3DPeriodic(wx, wy, wz, scale, size) : Perlin3D(wx, wy, wz, scale);
    }

    private void GenerateNoise()
    {
        noiseTexture = new Texture3D(size, size, size, TextureFormat.RFloat, false);
        noiseTexture.wrapMode = TextureWrapMode.Repeat;
        noiseTexture.filterMode = FilterMode.Bilinear;

        Color[] colors = new Color[size * size * size];
        for (int z = 0; z < size; z++)
        {
            float wz = (float)z / size;
            for (int y = 0; y < size; y++)
            {
                float wy = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    float wx = (float)x / size;
                    float noiseValue = seamless ?
                        Perlin3DPeriodic(wx, wy, wz, scale, size) :
                        Perlin3D(wx, wy, wz, scale);
                    colors[x + y * size + z * size * size] = new Color(noiseValue, 0, 0, 1);
                }
            }
        }
        noiseTexture.SetPixels(colors);
        noiseTexture.Apply();

        Repaint();
        Debug.Log($"3D Noise Generated! Size: {size}");
    }

    // --- True 3D Perlin noise implementation ---
    // Permutation table (standard 256 values duplicated)
    private static readonly int[] perm = new int[512]
    {
        151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,
        190, 6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,56,87,174,
        20,125,136,171,168, 68,175,74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,
        230,220,105,92,41,55,46,245,40,244,102,143,54, 65,25,63,161, 1,216,80,73,209,76,132,187,208, 89,
        18,169,200,196,135,130,116,188,159,86,164,100,109,198,173,186, 3,64,52,217,226,250,124,123,5,202,
        38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,119,248,
        152, 2,44,154,163, 70,221,153,101,155,167, 43,172,9,129,22,39,253, 19,98,108,110,79,113,224,232,
        178,185,112,104,218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,241, 81,51,145,235,249,
        14,239,107,49,192,214, 31,181,199,106,157,184, 84,204,176,115,121,50,45,127,  4,150,254,138,236,
        205, 93,222,114, 67,29,24,72,243,141,128,195,78,66,215,61,156,180,
        // duplicate
        151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,
        190, 6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,56,87,174,
        20,125,136,171,168, 68,175,74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,
        230,220,105,92,41,55,46,245,40,244,102,143,54, 65,25,63,161, 1,216,80,73,209,76,132,187,208, 89,
        18,169,200,196,135,130,116,188,159,86,164,100,109,198,173,186, 3,64,52,217,226,250,124,123,5,202,
        38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,119,248,
        152, 2,44,154,163, 70,221,153,101,155,167, 43,172,9,129,22,39,253, 19,98,108,110,79,113,224,232,
        178,185,112,104,218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,241, 81,51,145,235,249,
        14,239,107,49,192,214, 31,181,199,106,157,184, 84,204,176,115,121,50,45,127,  4,150,254,138,236,
        205, 93,222,114, 67,29,24,72,243,141,128,195,78,66,215,61,156,180
    };

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Grad(int hash, float x, float y, float z)
    {
        int h = hash & 15;
        float u = h < 8 ? x : y;
        float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
        float res = ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        return res;
    }

    // Non-periodic Perlin3D
    private static float Perlin3D(float x, float y, float z, float s)
    {
        x *= s; y *= s; z *= s;
        int X = Mathf.FloorToInt(x) & 255;
        int Y = Mathf.FloorToInt(y) & 255;
        int Z = Mathf.FloorToInt(z) & 255;
        float xf = x - Mathf.Floor(x);
        float yf = y - Mathf.Floor(y);
        float zf = z - Mathf.Floor(z);
        float u = Fade(xf);
        float v = Fade(yf);
        float w = Fade(zf);
        int A = perm[X] + Y; int AA = perm[A] + Z; int AB = perm[A + 1] + Z;
        int B = perm[X + 1] + Y; int BA = perm[B] + Z; int BB = perm[B + 1] + Z;
        float x1 = Lerp(Grad(perm[AA], xf, yf, zf), Grad(perm[BA], xf - 1, yf, zf), u);
        float x2 = Lerp(Grad(perm[AB], xf, yf - 1, zf), Grad(perm[BB], xf - 1, yf - 1, zf), u);
        float y1 = Lerp(x1, x2, v);
        float x3 = Lerp(Grad(perm[AA + 1], xf, yf, zf - 1), Grad(perm[BA + 1], xf - 1, yf, zf - 1), u);
        float x4 = Lerp(Grad(perm[AB + 1], xf, yf - 1, zf - 1), Grad(perm[BB + 1], xf - 1, yf - 1, zf - 1), u);
        float y2 = Lerp(x3, x4, v);
        float val = Lerp(y1, y2, w); // range ~[-1,1]
        return val * 0.5f + 0.5f; // remap to [0,1]
    }

    // Periodic (tileable) Perlin3D: period ensures wrapping at lattice boundaries
    private static float Perlin3DPeriodic(float x, float y, float z, float s, int period)
    {
        x *= s; y *= s; z *= s;
        // enforce period by modulo on integer lattice indices
        int X0 = Mathf.FloorToInt(x) % period; if (X0 < 0) X0 += period;
        int Y0 = Mathf.FloorToInt(y) % period; if (Y0 < 0) Y0 += period;
        int Z0 = Mathf.FloorToInt(z) % period; if (Z0 < 0) Z0 += period;
        float xf = x - Mathf.Floor(x);
        float yf = y - Mathf.Floor(y);
        float zf = z - Mathf.Floor(z);
        float u = Fade(xf);
        float v = Fade(yf);
        float w = Fade(zf);
        // wrap indices within period then map through perm
        int X1 = (X0 + 1) % period;
        int Y1 = (Y0 + 1) % period;
        int Z1 = (Z0 + 1) % period;
        // Hash combine (simple): use perm with mask
        int AA = perm[(perm[(perm[X0 & 255] + Y0) & 255] + Z0) & 255];
        int AB = perm[(perm[(perm[X0 & 255] + Y0) & 255] + Z1) & 255];
        int BA = perm[(perm[(perm[X1 & 255] + Y0) & 255] + Z0) & 255];
        int BB = perm[(perm[(perm[X1 & 255] + Y0) & 255] + Z1) & 255];
        int AA1 = perm[(perm[(perm[X0 & 255] + Y1) & 255] + Z0) & 255];
        int AB1 = perm[(perm[(perm[X0 & 255] + Y1) & 255] + Z1) & 255];
        int BA1 = perm[(perm[(perm[X1 & 255] + Y1) & 255] + Z0) & 255];
        int BB1 = perm[(perm[(perm[X1 & 255] + Y1) & 255] + Z1) & 255];
        float x1 = Lerp(Grad(AA, xf, yf, zf), Grad(BA, xf - 1, yf, zf), u);
        float x2 = Lerp(Grad(AB, xf, yf, zf - 1), Grad(BB, xf - 1, yf, zf - 1), u);
        float y1 = Lerp(x1, x2, w);
        float x3 = Lerp(Grad(AA1, xf, yf - 1, zf), Grad(BA1, xf - 1, yf - 1, zf), u);
        float x4 = Lerp(Grad(AB1, xf, yf - 1, zf - 1), Grad(BB1, xf - 1, yf - 1, zf - 1), u);
        float y2 = Lerp(x3, x4, w);
        float val = Lerp(y1, y2, v);
        return val * 0.5f + 0.5f;
    }

    private void SaveNoise()
    {
        if (noiseTexture == null)
        {
            Debug.LogWarning("Noise texture is null. Generate first.");
            return;
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            Debug.LogWarning("Output path is empty. Set a valid project-relative path like 'Assets/Textures/Noise3D.asset'.");
            return;
        }

        outputPath = outputPath.Replace('\\', '/');
        string dir = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(dir) || !outputPath.StartsWith("Assets"))
        {
            Debug.LogError("Output path must be inside project, e.g., 'Assets/Textures/Noise3D.asset'.");
            return;
        }

        EnsureFolders(dir);

        var assetCopy = Object.Instantiate(noiseTexture);
        AssetDatabase.DeleteAsset(outputPath);
        AssetDatabase.CreateAsset(assetCopy, outputPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"3D Noise saved to: {outputPath}");
    }

    private static void EnsureFolders(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return;
        fullPath = fullPath.Replace('\\', '/');
        if (!fullPath.StartsWith("Assets")) return;

        string[] parts = fullPath.Split('/');
        string current = parts[0]; // Assets
        for (int i = 1; i < parts.Length; i++)
        {
            string next = parts[i];
            if (string.IsNullOrEmpty(next)) continue;
            string combined = current + "/" + next;
            if (!AssetDatabase.IsValidFolder(combined))
            {
                AssetDatabase.CreateFolder(current, next);
            }
            current = combined;
        }
    }
}
#endif
