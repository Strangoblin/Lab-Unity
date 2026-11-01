// ═══════════════════════════════════════════════════════════════
//  SurfaceDitherValidator — Shader 消息、Pass 变体与隔离渲染检查
//
//  菜单：Tools/Surface Dither/Validate
//  批处理：-executeMethod Mine.SurfaceDither.SurfaceDitherValidator.Validate
//  输出：Validation~/near-*.png、far-*.png、results.txt
// ═══════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Mine.SurfaceDither
{
    internal static class SurfaceDitherValidator
    {
        private const string k_MaterialPath =
            "Assets/Mine/Shaders/Render/SurfaceDither/SurfaceDither.mat";
        private const string k_OutputFolder =
            "Assets/Mine/Shaders/Render/SurfaceDither/Validation~";
        private const int k_Width = 512;
        private const int k_Height = 512;

        [MenuItem("Tools/Surface Dither/Validate")]
        public static void Validate()
        {
            StringBuilder report = new StringBuilder();
            bool passed = true;

            try
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
                if (material == null || material.shader == null)
                {
                    Debug.LogError("SurfaceDither: 找不到材质或 Shader，先执行 Generate Assets。");
                    return;
                }

                Shader shader = material.shader;
                report.AppendLine($"Shader: {shader.name}");
                report.AppendLine($"Pass count: {shader.passCount}");
                passed &= CheckShaderMessages(shader, report, "import");
                passed &= CompilePasses(material, report);
                passed &= CheckShaderMessages(shader, report, "pass-compile");

                Directory.CreateDirectory(k_OutputFolder);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string nearPath = Path.Combine(k_OutputFolder, $"near-{stamp}.png");
                string farPath = Path.Combine(k_OutputFolder, $"far-{stamp}.png");

                ImageStats nearStats = RenderIsolated(material, -3.2f, nearPath, report);
                ImageStats farStats = RenderIsolated(material, -6.4f, farPath, report);

                Material brightnessMaterial = new Material(material)
                {
                    name = "SurfaceDitherBrightnessDiagnostic"
                };
                brightnessMaterial.SetFloat("_DotContrast", 0.0f);
                brightnessMaterial.SetColor("_DarkColor", Color.black);
                brightnessMaterial.SetColor("_LightColor", Color.white);
                string brightnessPath = Path.Combine(k_OutputFolder, $"brightness-{stamp}.png");
                ImageStats brightnessStats = RenderIsolated(
                    brightnessMaterial, -3.2f, brightnessPath, report);
                UnityEngine.Object.DestroyImmediate(brightnessMaterial);
                report.AppendLine($"Diagnostic input brightness: {brightnessStats.MeanLuminance:F4}");

                Material objectSpaceMaterial = new Material(material)
                {
                    name = "SurfaceDitherObjectSpace"
                };
                objectSpaceMaterial.SetFloat("_CoordinateMode", 1.0f);
                string objectSpacePath = Path.Combine(k_OutputFolder, $"object-space-{stamp}.png");
                ImageStats objectSpaceStats = RenderIsolated(
                    objectSpaceMaterial, -3.2f, objectSpacePath, report);
                UnityEngine.Object.DestroyImmediate(objectSpaceMaterial);
                report.AppendLine($"Object Space edge density: {objectSpaceStats.EdgeDensity:F4}");
                passed &= objectSpaceStats.Passed;

                passed &= nearStats.Passed;
                passed &= farStats.Passed;
                report.AppendLine($"Near edge density: {nearStats.EdgeDensity:F4}");
                report.AppendLine($"Far edge density:  {farStats.EdgeDensity:F4}");
                report.AppendLine($"Edge density ratio: {farStats.EdgeDensity / Mathf.Max(nearStats.EdgeDensity, 1e-5f):F3}");

                report.AppendLine(passed ? "RESULT: PASS" : "RESULT: FAIL");
                File.WriteAllText(Path.Combine(k_OutputFolder, "results.txt"), report.ToString());
                if (passed)
                {
                    Debug.Log("SurfaceDither validation PASS. " + Path.GetFullPath(k_OutputFolder));
                }
                else
                {
                    Debug.LogError("SurfaceDither validation FAIL. " + Path.GetFullPath(k_OutputFolder));
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"SurfaceDither validation exception: {exception}");
                report.AppendLine($"EXCEPTION: {exception}");
                File.WriteAllText(Path.Combine(k_OutputFolder, "results.txt"), report.ToString());
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Shader 消息 — 导入与逐 Pass 编译后各查一次
        // ════════════════════════════════════════════════════════════
        private static bool CheckShaderMessages(Shader shader, StringBuilder report, string stage)
        {
            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            report.AppendLine($"Shader messages ({stage}): {messages.Length}");
            for (int i = 0; i < messages.Length; i++)
            {
                ShaderMessage message = messages[i];
                string line = $"[{i}] severity={message.severity} "
                    + $"platform={message.platform} line={message.line} "
                    + $"file={message.file} message={message.message}";
                report.AppendLine("  " + line);
                Debug.Log($"[SurfaceDither][{stage}] {line}");
            }

            bool hasError = ShaderUtil.ShaderHasError(shader);
            report.AppendLine($"ShaderHasError ({stage}): {hasError}");
            return !hasError;
        }

        // ════════════════════════════════════════════════════════════
        //  Pass 编译 — 四个 Pass + 前向关键变体
        // ════════════════════════════════════════════════════════════
        private static bool CompilePasses(Material source, StringBuilder report)
        {
            bool passed = true;
            Material material = new Material(source)
            {
                name = "SurfaceDitherValidation"
            };

            try
            {
                for (int pass = 0; pass < source.shader.passCount; pass++)
                {
                    ShaderUtil.ClearShaderMessages(source.shader);
                    ShaderUtil.CompilePass(material, pass, true);
                    bool compiled = !ShaderUtil.ShaderHasError(source.shader);
                    report.AppendLine($"CompilePass {pass}: {compiled}");
                    passed &= compiled;
                }

                string[] variants =
                {
                    "_INVERSE_DOTS",
                    "_QUANTIZE_LAYERS",
                    "_RADIAL_COMPENSATION",
                    "_DEBUG_FRACTAL",
                    "_ALPHATEST_ON"
                };

                for (int i = 0; i < variants.Length; i++)
                {
                    material.EnableKeyword(variants[i]);
                    ShaderUtil.ClearShaderMessages(source.shader);
                    ShaderUtil.CompilePass(material, 0, true);
                    bool compiled = !ShaderUtil.ShaderHasError(source.shader);
                    report.AppendLine($"CompilePass Forward +{variants[i]}: {compiled}");
                    passed &= compiled;
                    material.DisableKeyword(variants[i]);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }

            return passed;
        }

        // ════════════════════════════════════════════════════════════
        //  隔离预览 — 平板立方体、Game 相机、主光与固定环境光
        // ════════════════════════════════════════════════════════════
        private static ImageStats RenderIsolated(
            Material sourceMaterial,
            float cameraZ,
            string outputPath,
            StringBuilder report)
        {
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            Material material = new Material(sourceMaterial)
            {
                name = "SurfaceDitherValidation"
            };
            AmbientMode previousAmbientMode = RenderSettings.ambientMode;
            Color previousAmbientLight = RenderSettings.ambientLight;
            Light previousSun = RenderSettings.sun;

            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f, 1.0f);

                GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(slab, previewScene);
                slab.name = "SurfaceDitherSlab";
                slab.transform.position = Vector3.zero;
                slab.transform.localScale = new Vector3(2.2f, 2.2f, 0.2f);
                slab.GetComponent<Renderer>().sharedMaterial = material;

                GameObject cameraObject = new GameObject("SurfaceDitherCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.cameraType = CameraType.Game;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.0f, 1.0f, 0.0f, 1.0f);
                camera.fieldOfView = 32.0f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 50.0f;
                camera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
                camera.allowDynamicResolution = false;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.cullingMask = ~0;
                camera.scene = previewScene;
                cameraObject.transform.position = new Vector3(0.0f, 0.0f, cameraZ);
                cameraObject.transform.LookAt(Vector3.zero);

                UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.renderType = CameraRenderType.Base;
                cameraData.renderPostProcessing = false;
                cameraData.antialiasing = AntialiasingMode.None;
                cameraData.renderShadows = false;

                GameObject lightObject = new GameObject("SurfaceDitherKeyLight");
                SceneManager.MoveGameObjectToScene(lightObject, previewScene);
                Light keyLight = lightObject.AddComponent<Light>();
                keyLight.type = LightType.Directional;
                keyLight.color = Color.white;
                keyLight.intensity = 0.0f;
                keyLight.shadows = LightShadows.None;
                lightObject.transform.rotation = Quaternion.Euler(22.0f, -28.0f, 0.0f);
                RenderSettings.sun = keyLight;

                GameObject fillObject = new GameObject("SurfaceDitherFillLight");
                SceneManager.MoveGameObjectToScene(fillObject, previewScene);
                Light fillLight = fillObject.AddComponent<Light>();
                fillLight.type = LightType.Directional;
                fillLight.color = new Color(0.55f, 0.65f, 0.8f, 1.0f);
                fillLight.intensity = 0.0f;
                fillLight.shadows = LightShadows.None;
                fillObject.transform.rotation = Quaternion.Euler(-18.0f, 145.0f, 0.0f);

                OffscreenCapture.Capture(camera, outputPath, k_Width, k_Height);
                ImageStats stats = Analyze(outputPath);
                report.AppendLine(
                    $"{Path.GetFileName(outputPath)}: nonBg={stats.NonBackground}, "
                    + $"dark={stats.Dark}, light={stats.Light}, mid={stats.Mid}, "
                    + $"mean={stats.MeanLuminance:F3}, edge={stats.EdgeDensity:F4}, "
                    + $"pass={stats.Passed}");
                return stats;
            }
            finally
            {
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                RenderSettings.sun = previousSun;
                UnityEngine.Object.DestroyImmediate(material);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  PNG 统计 — 区分绿色背景、暗点、亮点与边缘密度
        // ════════════════════════════════════════════════════════════
        private static ImageStats Analyze(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);

            try
            {
                if (!texture.LoadImage(bytes))
                {
                    throw new InvalidOperationException("Could not decode validation PNG.");
                }

                Color32[] pixels = texture.GetPixels32();
                ImageStats stats = new ImageStats();
                float luminanceSum = 0.0f;
                float previousLuminance = -1.0f;

                for (int y = 0; y < texture.height; y++)
                {
                    previousLuminance = -1.0f;
                    for (int x = 0; x < texture.width; x++)
                    {
                        Color32 pixel = pixels[y * texture.width + x];
                        if (IsBackground(pixel))
                        {
                            previousLuminance = -1.0f;
                            continue;
                        }

                        float luminance = (0.299f * pixel.r + 0.587f * pixel.g + 0.114f * pixel.b)
                            / 255.0f;
                        stats.NonBackground++;
                        luminanceSum += luminance;

                        if (luminance < 0.25f)
                        {
                            stats.Dark++;
                        }
                        else if (luminance > 0.6f)
                        {
                            stats.Light++;
                        }
                        else
                        {
                            stats.Mid++;
                        }

                        if (previousLuminance >= 0.0f)
                        {
                            stats.Pairs++;
                            if (Mathf.Abs(luminance - previousLuminance) > 0.2f)
                            {
                                stats.Transitions++;
                            }
                        }

                        previousLuminance = luminance;
                    }
                }

                stats.MeanLuminance = stats.NonBackground > 0
                    ? luminanceSum / stats.NonBackground
                    : 0.0f;
                stats.EdgeDensity = stats.Pairs > 0
                    ? stats.Transitions / (float)stats.Pairs
                    : 0.0f;
                stats.Passed = stats.NonBackground > 1000
                    && stats.Dark > 0
                    && stats.Light > 0
                    && stats.EdgeDensity > 0.02f;
                return stats;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static bool IsBackground(Color32 pixel)
        {
            return pixel.g > 220 && pixel.r < 40 && pixel.b < 40;
        }

        private sealed class ImageStats
        {
            public int NonBackground;
            public int Dark;
            public int Light;
            public int Mid;
            public int Pairs;
            public int Transitions;
            public float MeanLuminance;
            public float EdgeDensity;
            public bool Passed;
        }

        // ════════════════════════════════════════════════════════════
        //  离屏取像 — URP SingleCameraRequest → RT → PNG
        // ════════════════════════════════════════════════════════════
        private static class OffscreenCapture
        {
            public static void Capture(Camera camera, string outputPath, int width, int height)
            {
                RenderTexture previousActive = RenderTexture.active;
                RenderTexture previousTarget = camera.targetTexture;
                RenderTexture target = null;
                Texture2D image = null;

                try
                {
                    target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                    {
                        name = "SurfaceDitherValidation",
                        antiAliasing = 1,
                        useMipMap = false,
                        autoGenerateMips = false,
                        useDynamicScale = false
                    };
                    if (!target.Create())
                    {
                        throw new InvalidOperationException("Could not create validation RenderTexture.");
                    }

                    var request = new UniversalRenderPipeline.SingleCameraRequest
                    {
                        destination = target
                    };
                    if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    {
                        throw new InvalidOperationException(
                            "The active pipeline does not support URP SingleCameraRequest.");
                    }
                    RenderPipeline.SubmitRenderRequest(camera, request);

                    image = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0.0f, 0.0f, width, height), 0, 0, false);
                    image.Apply(false, false);
                    byte[] png = image.EncodeToPNG();
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
                    File.WriteAllBytes(outputPath, png);
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    RenderTexture.active = previousActive;
                    if (image != null)
                    {
                        UnityEngine.Object.DestroyImmediate(image);
                    }
                    if (target != null)
                    {
                        target.Release();
                        UnityEngine.Object.DestroyImmediate(target);
                    }
                }
            }
        }
    }
}
