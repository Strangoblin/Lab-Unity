// ═══════════════════════════════════════════════════════════════
//  SurfaceDitherTextureGenerator — 生成 3D 分形 Bayer 图案与亮度 ramp
//
//  菜单：Tools/Surface Dither/Generate Assets
//  图案结构：8×8 基础点，每层多一个点，共 64 层；每点 16×16 texel。
//  生成结果：SurfaceDitherPattern8x8.asset、SurfaceDitherRamp.asset、
//  SurfaceDither.mat（已绑定前两者）。
// ═══════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Mine.SurfaceDither
{
    public static class SurfaceDitherTextureGenerator
    {
        private const int k_Recursion = 3;
        private const int k_TexelsPerDot = 16;
        private const int k_BrightnessBucketCount = 256;
        private const string k_AssetFolder = "Assets/Mine/Shaders/Render/SurfaceDither";
        private const string k_PatternPath = k_AssetFolder + "/SurfaceDitherPattern8x8.asset";
        private const string k_RampPath = k_AssetFolder + "/SurfaceDitherRamp.asset";
        private const string k_WhitePath = k_AssetFolder + "/SurfaceDitherWhite.asset";
        private const string k_MaterialPath = k_AssetFolder + "/SurfaceDither.mat";
        private const string k_ShaderName = "Render/SurfaceDither";

        // ════════════════════════════════════════════════════════════
        //  菜单入口 — 生成纹理与示例材质
        // ════════════════════════════════════════════════════════════
        [MenuItem("Tools/Surface Dither/Generate Assets")]
        public static void GenerateAssets()
        {
            Shader shader = Shader.Find(k_ShaderName);
            if (shader == null)
            {
                Debug.LogError($"SurfaceDither: 找不到 Shader \"{k_ShaderName}\"，请先等待编译完成。");
                return;
            }

            try
            {
                int dotsPerSide = 1 << k_Recursion;
                int size = k_TexelsPerDot * dotsPerSide;
                int layers = dotsPerSide * dotsPerSide;

                EditorUtility.DisplayProgressBar("Surface Dither", "Bayer points", 0.05f);
                Vector2[] points = BuildBayerPoints(k_Recursion);

                EditorUtility.DisplayProgressBar("Surface Dither", "Pattern texture", 0.1f);
                Texture3D pattern = GetOrCreatePatternTexture(size, layers);
                if (pattern == null)
                {
                    return;
                }

                float[] lookupRamp = FillPatternTexture(pattern, points, size, layers);

                EditorUtility.DisplayProgressBar("Surface Dither", "Brightness ramp", 0.9f);
                Texture2D ramp = GetOrCreateRampTexture(size);
                if (ramp == null)
                {
                    return;
                }

                FillRampTexture(ramp, lookupRamp);
                SetLinearColorSpace(pattern);
                SetLinearColorSpace(ramp);

                Texture2D white = GetOrCreateWhiteTexture();
                if (white == null)
                {
                    return;
                }

                EditorUtility.DisplayProgressBar("Surface Dither", "Material", 0.95f);
                Material material = GetOrCreateMaterial(shader, pattern, ramp, white);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"SurfaceDither: generated 8x8 pattern, ramp and material at {k_AssetFolder}.");
                Selection.activeObject = material;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Bayer 点集 — 递归四象限构造，保留原始点顺序
        // ════════════════════════════════════════════════════════════
        private static Vector2[] BuildBayerPoints(int recursion)
        {
            List<Vector2> points = new List<Vector2>
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.0f),
                new Vector2(0.0f, 0.5f)
            };

            for (int level = 0; level < recursion - 1; level++)
            {
                int count = points.Count;
                float offset = Mathf.Pow(0.5f, level + 1);
                for (int corner = 1; corner < 4; corner++)
                {
                    for (int index = 0; index < count; index++)
                    {
                        points.Add(points[index] + points[corner] * offset);
                    }
                }
            }

            return points.ToArray();
        }

        // ════════════════════════════════════════════════════════════
        //  Pattern — 每层增加一个点，写入径向渐变，并统计亮度直方图
        // ════════════════════════════════════════════════════════════
        private static float[] FillPatternTexture(
            Texture3D texture,
            Vector2[] points,
            int size,
            int layers)
        {
            Color[] pixels = new Color[size * size * layers];
            int[] brightnessBuckets = new int[k_BrightnessBucketCount];
            float invRes = 1.0f / size;
            float invDotRadiusScale = 1.0f / 2.4f;

            for (int z = 0; z < layers; z++)
            {
                int dotCount = z + 1;
                float dotArea = 0.5f / dotCount;
                float dotRadius = Mathf.Sqrt(dotArea / Mathf.PI);
                int layerOffset = z * size * size;

                for (int y = 0; y < size; y++)
                {
                    float pointY = (y + 0.5f) * invRes;
                    int rowOffset = layerOffset + y * size;

                    for (int x = 0; x < size; x++)
                    {
                        float pointX = (x + 0.5f) * invRes;
                        float bestSquared = float.MaxValue;

                        for (int i = 0; i < dotCount; i++)
                        {
                            float deltaX = pointX - points[i].x;
                            float deltaY = pointY - points[i].y;
                            deltaX -= Mathf.Floor(deltaX + 0.5f);
                            deltaY -= Mathf.Floor(deltaY + 0.5f);
                            float squared = deltaX * deltaX + deltaY * deltaY;
                            if (squared < bestSquared)
                            {
                                bestSquared = squared;
                            }
                        }

                        float normalized = Mathf.Sqrt(bestSquared) / dotRadius * invDotRadiusScale;
                        float value = Mathf.Clamp01(1.0f - normalized);
                        pixels[rowOffset + x] = new Color(value, value, value, 1.0f);

                        int bucket = Mathf.Clamp(
                            (int)(value * k_BrightnessBucketCount),
                            0,
                            k_BrightnessBucketCount - 1);
                        brightnessBuckets[bucket]++;
                    }
                }

                EditorUtility.DisplayProgressBar(
                    "Surface Dither",
                    "Pattern texture",
                    0.1f + 0.78f * (z + 1) / layers);
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            EditorUtility.SetDirty(texture);
            return BuildLookupRamp(brightnessBuckets, pixels.Length, size);
        }

        // ════════════════════════════════════════════════════════════
        //  Lookup — 由覆盖率直方图反查目标亮度对应的阈值
        // ════════════════════════════════════════════════════════════
        private static float[] BuildLookupRamp(int[] brightnessBuckets, int pixelCount, int size)
        {
            float[] brightnessRamp = new float[brightnessBuckets.Length + 1];
            int cumulative = 0;

            for (int i = 0; i < brightnessBuckets.Length; i++)
            {
                cumulative += brightnessBuckets[brightnessBuckets.Length - 1 - i];
                brightnessRamp[i + 1] = cumulative / (float)pixelCount;
            }

            float[] lookupRamp = new float[size];
            float lowerBrightness = 0.0f;
            int higherIndex = 1;
            float higherBrightness = brightnessRamp[1];

            for (int i = 0; i < size; i++)
            {
                float desiredBrightness = i / (float)(size - 1);
                while (higherIndex < brightnessRamp.Length - 1
                    && higherBrightness < desiredBrightness)
                {
                    higherIndex++;
                    higherBrightness = brightnessRamp[higherIndex];
                }

                float interpolation = Mathf.InverseLerp(
                    lowerBrightness,
                    higherBrightness,
                    desiredBrightness);
                lookupRamp[i] = (higherIndex - 1 + interpolation)
                    / (brightnessRamp.Length - 1);
            }

            return lookupRamp;
        }

        // ════════════════════════════════════════════════════════════
        //  Ramp — 写入线性 R8 亮度查找纹理
        // ════════════════════════════════════════════════════════════
        private static void FillRampTexture(Texture2D ramp, float[] lookupRamp)
        {
            Color[] pixels = new Color[lookupRamp.Length];
            for (int i = 0; i < lookupRamp.Length; i++)
            {
                pixels[i] = new Color(lookupRamp[i], lookupRamp[i], lookupRamp[i], 1.0f);
            }

            ramp.SetPixels(pixels);
            ramp.Apply(false, false);
            EditorUtility.SetDirty(ramp);
        }

        // ════════════════════════════════════════════════════════════
        //  资源创建 — 已存在时原位更新，避免删除用户资产
        // ════════════════════════════════════════════════════════════
        private static Texture3D GetOrCreatePatternTexture(int size, int layers)
        {
            Texture3D texture = AssetDatabase.LoadAssetAtPath<Texture3D>(k_PatternPath);
            if (texture == null)
            {
                texture = new Texture3D(size, size, layers, TextureFormat.R8, false, true)
                {
                    name = "SurfaceDitherPattern8x8",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 0
                };
                AssetDatabase.CreateAsset(texture, k_PatternPath);
                return texture;
            }

            if (texture.width != size || texture.height != size || texture.depth != layers)
            {
                Debug.LogError(
                    $"SurfaceDither: {k_PatternPath} 尺寸不匹配，期望 {size}x{size}x{layers}，"
                    + $"实际 {texture.width}x{texture.height}x{texture.depth}。请删除该资产后重试。");
                return null;
            }

            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 0;
            return texture;
        }

        private static Texture2D GetOrCreateRampTexture(int size)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(k_RampPath);
            if (texture == null)
            {
                texture = new Texture2D(size, 1, TextureFormat.R8, false, true)
                {
                    name = "SurfaceDitherRamp",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 0
                };
                AssetDatabase.CreateAsset(texture, k_RampPath);
                return texture;
            }

            if (texture.width != size || texture.height != 1)
            {
                Debug.LogError(
                    $"SurfaceDither: {k_RampPath} 尺寸不匹配，期望 {size}x1，"
                    + $"实际 {texture.width}x{texture.height}。请删除该资产后重试。");
                return null;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 0;
            return texture;
        }

        private static Texture2D GetOrCreateWhiteTexture()
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(k_WhitePath);
            if (texture == null)
            {
                texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true)
                {
                    name = "SurfaceDitherWhite",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point,
                    anisoLevel = 0
                };
                texture.SetPixel(0, 0, Color.white);
                texture.Apply(false, false);
                AssetDatabase.CreateAsset(texture, k_WhitePath);
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.anisoLevel = 0;
            SetLinearColorSpace(texture);
            return texture;
        }

        private static void SetLinearColorSpace(Texture texture)
        {
            SerializedObject serialized = new SerializedObject(texture);
            SerializedProperty colorSpace = serialized.FindProperty("m_ColorSpace");
            if (colorSpace != null)
            {
                colorSpace.intValue = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static Material GetOrCreateMaterial(
            Shader shader,
            Texture3D pattern,
            Texture2D ramp,
            Texture2D white)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "SurfaceDither"
                };
                AssetDatabase.CreateAsset(material, k_MaterialPath);
            }

            material.shader = shader;
            material.SetTexture("_MainTex", white);
            material.SetTexture("_DitherTex", pattern);
            material.SetTexture("_DitherRampTex", ramp);
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_DarkColor", new Color(0.02f, 0.03f, 0.04f, 1.0f));
            material.SetColor("_LightColor", new Color(0.92f, 0.90f, 0.84f, 1.0f));
            material.SetFloat("_AlphaClip", 0.0f);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetFloat("_AmbientStrength", 1.0f);
            material.SetFloat("_MainLightStrength", 1.0f);
            material.SetFloat("_ReceiveShadows", 1.0f);
            material.SetFloat("_InputExposure", 1.0f);
            material.SetFloat("_InputOffset", 0.0f);
            material.SetFloat("_InverseDots", 0.0f);
            material.SetFloat("_CoordinateMode", 0.0f);
            material.SetFloat("_DitherTiling", 1.0f);
            material.SetFloat("_DitherScale", 5.0f);
            material.SetFloat("_DotSizeVariability", 0.0f);
            material.SetFloat("_DotContrast", 1.0f);
            material.SetFloat("_StretchSmoothness", 1.0f);
            material.SetFloat("_QuantizeLayers", 0.0f);
            material.SetFloat("_RadialCompensation", 0.0f);
            material.SetFloat("_DebugFractal", 0.0f);

            material.EnableKeyword("_RECEIVE_SHADOWS");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_INVERSE_DOTS");
            material.DisableKeyword("_QUANTIZE_LAYERS");
            material.DisableKeyword("_RADIAL_COMPENSATION");
            material.DisableKeyword("_DEBUG_FRACTAL");

            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
