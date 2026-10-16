using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;

/// <summary>Connected-Editor contract checks; fixtures are owned by this runner.</summary>
public static class Script
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    public static object Main()
    {
        _checks = 0;
        string folder = "Assets/Mine/Scripts/NoiseGenerator/__ProductionValidation_" + Guid.NewGuid().ToString("N");
        var windows = new List<EditorWindow>();
        var sources = new List<UnityEngine.Object>();
        try
        {
            Check(AssetAccess.IsValidAssetPath(folder + "/x.asset"), "valid path");
            Check(!AssetAccess.IsValidAssetPath("Assets/../escape.asset"), "parent traversal rejected");
            Check(!AssetAccess.IsValidAssetPath("Packages/x.asset"), "outside Assets rejected");

            var input2D = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            sources.Add(input2D);
            var colors2D = new Color[64];
            for (int i = 0; i < colors2D.Length; i++) colors2D[i] = new Color(0.9f, 0.2f, 0.1f, 1f);
            input2D.SetPixels(colors2D);
            input2D.Apply();
            string path2D = folder + "/sentinel2d.asset";
            var saved2D = (Texture2D)AssetAccess.Save(input2D, path2D);
            Check(saved2D != input2D && EditorUtility.IsPersistent(saved2D), "save clones transient source");

            var input3D = new Texture3D(8, 6, 4, TextureFormat.RGBA32, false);
            sources.Add(input3D);
            var colors3D = new Color[8 * 6 * 4];
            for (int i = 0; i < colors3D.Length; i++) colors3D[i] = new Color(0.1f + 0.8f * i / (colors3D.Length - 1), 0f, 0f, 1f);
            input3D.SetPixels(colors3D);
            input3D.Apply();
            string path3D = folder + "/sentinel3d.asset";
            var saved3D = (Texture3D)AssetAccess.Save(input3D, path3D);

            var noise = ScriptableObject.CreateInstance<NoiseGeneratorWindow>();
            windows.Add(noise);
            SetMode(noise, "Texture2D");
            SetPath(noise, path2D);
            InvokeAction(noise, "LoadFromAsset");
            InvokeNoise(noise, "UpdatePackedPreviewTexture");
            Check(Current(noise) == saved2D, "loaded 2D survives preview refresh with mismatched dimensions");
            for (int i = 0; i < 4; i++) InvokeNoise(noise, "UpdateChannelPreview", i);
            InvokeNoise(noise, "UpdatePackedPreviewTexture");
            Check(Current(noise) == saved2D && Math.Abs(saved2D.GetPixel(0, 0).r - 0.9f) < 0.005f, "loaded 2D survives settings preview");

            InvokeAction(noise, "Bake");
            var generated2D = Current(noise) as Texture2D;
            Check(generated2D != null && generated2D.width == 32 && !EditorUtility.IsPersistent(generated2D), "Generate explicitly replaces loaded 2D");
            SetPath(noise, folder + "/generated2d.asset");
            InvokeAction(noise, "SaveToAsset");
            var savedGenerated = Current(noise);
            Check(generated2D == null && EditorUtility.IsPersistent(savedGenerated), "save releases owned source and keeps persistent clone");
            InvokeNoise(noise, "UpdatePackedPreviewTexture");
            Check(Current(noise) == savedGenerated, "saved 2D survives preview refresh");

            SetMode(noise, "Texture3D");
            SetPath(noise, path3D);
            InvokeAction(noise, "LoadFromAsset");
            var expectedDimensions = new[] { new Vector2Int(4, 6), new Vector2Int(8, 4), new Vector2Int(8, 6) };
            var slices = (int[])typeof(NoiseGeneratorWindow).GetField("_previewSlices3D", Flags).GetValue(noise);
            for (int axis = 0; axis < 3; axis++)
            {
                slices[axis] = 1;
                InvokeNoise(noise, "UpdateSlicePreview3D", axis);
                var previews = (Texture2D[])typeof(NoiseGeneratorWindow).GetField("_sliceTextures3D", Flags).GetValue(noise);
                Check(previews[axis].width == expectedDimensions[axis].x && previews[axis].height == expectedDimensions[axis].y, "noncubic dimensions axis " + axis);
                float actual = (float)InvokeNoise(noise, "Sample3DSlice", axis, 1, 2, 1, 8);
                float expected = axis == 0 ? saved3D.GetPixel(1, 2, 1).r : axis == 1 ? saved3D.GetPixel(1, 1, 2).r : saved3D.GetPixel(1, 2, 1).r;
                Check(Math.Abs(actual - expected) < 0.005f, "actual asset pixels axis " + axis);
                float displayed = previews[axis].GetPixel(1, 2).r;
                Check(Math.Abs(displayed - expected) < 0.005f, "displayed slice pixels axis " + axis);
            }

            var replacement = new Texture3D(8, 6, 4, TextureFormat.RGBA32, false);
            sources.Add(replacement);
            var replacementColors = new Color[colors3D.Length];
            for (int i = 0; i < replacementColors.Length; i++) replacementColors[i] = Color.white;
            replacement.SetPixels(replacementColors);
            replacement.Apply();
            noise.GetType().GetMethod("SetAsset", Flags).Invoke(noise, new object[] { replacement, true });
            InvokeNoise(noise, "UpdateSlicePreview3D", 2);
            var rebuilt = (Texture2D[])typeof(NoiseGeneratorWindow).GetField("_sliceTextures3D", Flags).GetValue(noise);
            Check(rebuilt[2].GetPixel(1, 2).r > 0.99f, "same-size source switch invalidates cached slices");

            var unreadable = new Texture3D(4, 4, 4, TextureFormat.RGBA32, false);
            sources.Add(unreadable);
            unreadable.Apply(false, true);
            noise.GetType().GetMethod("SetAsset", Flags).Invoke(noise, new object[] { unreadable, true });
            Check(replacement == null, "replacing owned 3D releases previous source");
            InvokeNoise(noise, "UpdateSlicePreview3D", 2);
            var blocked = (Texture2D[])typeof(NoiseGeneratorWindow).GetField("_sliceTextures3D", Flags).GetValue(noise);
            Check(blocked[2] == null, "unreadable asset has no fabricated slice");

            var curve = ScriptableObject.CreateInstance<CurveAsset>();
            sources.Add(curve);
            string curvePath = folder + "/curve.asset";
            var savedCurve = AssetAccess.Save(curve, curvePath);
            var types = new[] { typeof(CurveGeneratorWindow), typeof(NoiseGeneratorWindow), typeof(FGDLutBakerWindow), typeof(Mine.SSSLutBaker.SSSLutBakerWindow), typeof(InteriorMapBakerWindow) };
            foreach (Type type in types)
            {
                Check(type.BaseType == typeof(EditorWindow), type.Name + " is an independent EditorWindow");
                var window = (EditorWindow)ScriptableObject.CreateInstance(type);
                windows.Add(window);
                if (type == typeof(NoiseGeneratorWindow)) SetMode(window, "Texture2D");
                string path = type == typeof(CurveGeneratorWindow) ? curvePath : path2D;
                UnityEngine.Object expected = type == typeof(CurveGeneratorWindow) ? savedCurve : saved2D;
                SetPath(window, path);
                InvokeAction(window, "LoadFromAsset");
                Check(Current(window) == expected, type.Name + " Load identity");
                string guid = AssetDatabase.AssetPathToGUID(path);
                int instanceId = expected.GetInstanceID();
                InvokeAction(window, "SaveToAsset");
                Check(Current(window).GetInstanceID() == instanceId && AssetDatabase.AssetPathToGUID(path) == guid, type.Name + " self-save keeps GUID and identity");
                UnityEngine.Object.DestroyImmediate(window);
                Check(expected != null && AssetAccess.Load(path) == expected, type.Name + " close preserves loaded asset");
            }

            UnityEngine.Object.DestroyImmediate(noise);
            Check(unreadable == null && saved2D != null && saved3D != null, "close releases only owned assets");
            return "PASS " + _checks + " checks; runner-owned fixture folder cleaned in finally";
        }
        finally
        {
            foreach (EditorWindow window in windows) if (window != null) UnityEngine.Object.DestroyImmediate(window);
            foreach (UnityEngine.Object source in sources) if (source != null) UnityEngine.Object.DestroyImmediate(source);
            if (AssetDatabase.IsValidFolder(folder) && !AssetDatabase.DeleteAsset(folder)) throw new Exception("Fixture cleanup failed: " + folder);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _checks++;
    }

    private static UnityEngine.Object Current(EditorWindow window)
    {
        return (UnityEngine.Object)window.GetType().GetProperty("Asset", Flags).GetValue(window);
    }

    private static void SetPath(EditorWindow window, string path)
    {
        window.GetType().GetProperty("AssetPath", Flags).SetValue(window, path);
    }

    private static void SetMode(EditorWindow window, string modeName)
    {
        FieldInfo mode = typeof(NoiseGeneratorWindow).GetField("_outputMode", Flags);
        mode.SetValue(window, Enum.Parse(mode.FieldType, modeName));
    }

    private static object InvokeAction(EditorWindow window, string name)
    {
        return window.GetType().GetMethod(name, Flags).Invoke(window, null);
    }

    private static object InvokeNoise(EditorWindow window, string name, params object[] args)
    {
        return typeof(NoiseGeneratorWindow).GetMethod(name, Flags).Invoke(window, args);
    }

    private static class AssetAccess
    {
        private static readonly Type Files = typeof(NoiseGeneratorWindow).GetNestedType("AssetFiles", BindingFlags.NonPublic);

        public static bool IsValidAssetPath(string path)
        {
            return (bool)Files.GetMethod("IsValidAssetPath", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { path });
        }

        public static UnityEngine.Object Save(UnityEngine.Object source, string path)
        {
            return (UnityEngine.Object)Files.GetMethod("Save", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { source, path });
        }

        public static UnityEngine.Object Load(string path)
        {
            return (UnityEngine.Object)Files.GetMethod("Load", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { path });
        }
    }
}
