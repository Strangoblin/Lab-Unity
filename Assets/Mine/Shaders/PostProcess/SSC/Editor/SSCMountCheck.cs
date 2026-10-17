using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 手动挂载检查：在 batch mode 下临时挂载 SSCFeature、渲染一帧并写出结果。
/// 编辑器中误调用时直接返回，不关闭 Editor。
/// </summary>
public static class SSCMountCheck
{
    const string ShaderPath = "Assets/Mine/Shaders/PostProcess/SSC/SSC.shader";

    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("[SSCMountCheck] Batch mode only; use -batchmode -executeMethod SSCMountCheck.Run.");
            return;
        }

        var sb = new StringBuilder();
        string resultPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tmp", "ssc-mount-result.txt"));
        GameObject go = null;
        RenderTexture rt = null;
        UniversalAdditionalCameraData camData = null;
        ScriptableRendererData rd = null;
        SSCFeature feature = null;
        bool added = false;

        try
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            sb.AppendLine("shader=" + (shader != null ? shader.name : "NULL"));

            feature = ScriptableObject.CreateInstance<SSCFeature>();
            feature.settings.sscShader = shader;
            feature.Create();
            sb.AppendLine("featureActive=" + feature.isActive);

            var passField = typeof(SSCFeature).GetField("sscPass", BindingFlags.NonPublic | BindingFlags.Instance);
            var pass = passField != null ? passField.GetValue(feature) : null;
            sb.AppendLine("pass=" + (pass != null));
            if (pass != null)
            {
                var matField = pass.GetType().GetField("_material", BindingFlags.NonPublic | BindingFlags.Instance);
                var mat = matField != null ? matField.GetValue(pass) as Material : null;
                sb.AppendLine("material=" + (mat != null) + ":" + (mat != null && mat.shader != null ? mat.shader.name : "NULL"));
            }

            sb.AppendLine("graphics=" + SystemInfo.graphicsDeviceType);

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) urp = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            sb.AppendLine("urp=" + (urp != null));

            if (urp != null)
            {
                var so = new SerializedObject(urp);
                var list = so.FindProperty("m_RendererDataList");
                sb.AppendLine("rendererCount=" + (list != null ? list.arraySize : -1));
                if (list != null && list.arraySize > 0)
                {
                    rd = list.GetArrayElementAtIndex(0).objectReferenceValue as ScriptableRendererData;
                    sb.AppendLine("rendererData=" + (rd != null ? rd.name : "NULL"));
                    if (rd != null)
                    {
                        rd.rendererFeatures.Add(feature);
                        rd.SetDirty();
                        added = true;
                        sb.AppendLine("featureAdded");
                    }
                }
            }

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && added)
            {
                go = new GameObject("SSCMountCheckCamera");
                var cam = go.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.2f, 0.3f, 0.4f, 1f);
                cam.transform.position = new Vector3(0f, 100f, -300f);
                cam.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                cam.farClipPlane = 2000f;
                camData = cam.GetUniversalAdditionalCameraData();
                camData.SetRenderer(0);

                rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(128, 128, 1, 1), 0, 0);
                tex.Apply();
                sb.AppendLine("pixel=" + tex.GetPixel(0, 0));
                UnityEngine.Object.DestroyImmediate(tex);
                RenderTexture.active = null;
                sb.AppendLine("render=OK");
            }
            else
            {
                sb.AppendLine("render=SKIPPED");
            }
        }
        catch (Exception e)
        {
            sb.AppendLine("EXCEPTION=" + e);
        }
        finally
        {
            try
            {
                if (added && rd != null && feature != null)
                {
                    rd.rendererFeatures.Remove(feature);
                    rd.SetDirty();
                }
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (feature != null) UnityEngine.Object.DestroyImmediate(feature);
            }
            catch (Exception e2)
            {
                sb.AppendLine("CLEANUP_EXCEPTION=" + e2.Message);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
        File.WriteAllText(resultPath, sb.ToString());
        UnityEngine.Debug.Log("[SSCMountCheck]\n" + sb);
        EditorApplication.Exit(0);
    }
}
