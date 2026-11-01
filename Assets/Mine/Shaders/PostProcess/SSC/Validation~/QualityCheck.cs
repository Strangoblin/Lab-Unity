using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public class Script
{
    public static object Main()
    {
        string root = "tmp/sessions/" + DateTime.UtcNow.ToString("yyyy-MM-dd") + "-ssc-quality-" + Guid.NewGuid().ToString("N").Substring(0,8) + "/output";
        string phase = "reconstructed";
        Shader linearShader = null;
        Directory.CreateDirectory(root);
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        SSCFeature feature = null;
        foreach (var f in renderer.rendererFeatures) if (f is SSCFeature) feature = (SSCFeature)f;
        if (feature == null) throw new Exception("SSC missing");
        string settings = JsonUtility.ToJson(feature.settings);
        var go = new GameObject("SSC quality capture");
        var camera = go.AddComponent<Camera>();
        var previous = RenderTexture.active;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        var results = new System.Collections.Generic.List<string>();
        try
        {
            camera.CopyFrom(Camera.main);
            camera.cameraType = CameraType.Game;
            camera.rect = new Rect(0,0,1,1);
            camera.aspect = 960f/540f;
            camera.allowDynamicResolution = false;
            camera.farClipPlane = 2000;
            camera.transform.position = new Vector3(0, 25, -90);
            camera.transform.LookAt(new Vector3(0, 50, 0));
            var data = camera.GetUniversalAdditionalCameraData();
            data.SetRenderer(0);
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthTexture = true;
            feature.settings.speed = 0;
            feature.settings.jitter = 0;
            feature.settings.showSSC = false;
            cube.transform.position = new Vector3(2.18f,32.09f,-60);
            cube.transform.localScale = new Vector3(2.18f,21.82f,2.18f);
            cube.transform.rotation = Quaternion.Euler(0,0,-26);
            mat.SetColor("_BaseColor", new Color(0.2f,0.2f,0.2f,1));
            cube.GetComponent<Renderer>().sharedMaterial = mat;
            // 固定积分、颜色、RGBA格式与中心遮挡，仅替换重建函数作为控制组。
            string source = File.ReadAllText("Assets/Mine/Shaders/PostProcess/SSC/SSC.shader");
            int function = source.IndexOf("float4 ReconstructCloud(");
            if(function < 0) throw new Exception("ReconstructCloud reference missing");
            int start = source.IndexOf('{', function);
            int end = start + 1, level = 1;
            while(level > 0 && end < source.Length)
            {
                if(source[end] == '{') level++;
                if(source[end] == '}') level--;
                end++;
            }
            string linearBody = "{ return SAMPLE_TEXTURE2D_X_LOD(_SSCTex, sampler_LinearClamp, UnityStereoTransformScreenSpaceTex(uv), 0); }";
            string linearSource = source.Substring(0,start) + linearBody + source.Substring(end);
            linearSource = linearSource.Replace("Shader \"PostProcess/SSC\"", "Shader \"Hidden/SSC/LinearValidation\"");
            linearShader = ShaderUtil.CreateShaderAsset(linearSource);
            Shader reconstructedShader = feature.settings.sscShader;
            foreach(string filter in new []{"linear","reconstructed"})
            {
            phase = filter;
            feature.settings.sscShader = filter == "linear" ? linearShader : reconstructedShader;
            feature.Create();
            foreach (bool foreground in new []{false,true})
            {
                cube.SetActive(foreground);
                foreach (int shift in new []{0,1,2,3,4})
                {
                    feature.settings.downsample = shift;
                    string path = Path.Combine(root, phase + (foreground ? "-edge-" : "-sky-") + shift + ".png");
                    var rt = new RenderTexture(960,540,24,RenderTextureFormat.ARGB32);
                    var tex = new Texture2D(960,540,TextureFormat.RGBA32,false,false);
                    try
                    {
                        rt.Create();
                        var request = new UniversalRenderPipeline.SingleCameraRequest {destination=rt};
                        if (!RenderPipeline.SupportsRenderRequest(camera,request)) throw new Exception("request unsupported");
                        RenderPipeline.SubmitRenderRequest(camera,request);
                        RenderTexture.active=rt;
                        tex.ReadPixels(new Rect(0,0,960,540),0,0);
                        tex.Apply();
                        File.WriteAllBytes(path,tex.EncodeToPNG());
                        results.Add(path);
                    }
                    finally { RenderTexture.active=previous; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex); }
                }
            }
            }
            var shader = feature.settings.sscShader;
            foreach (string keyword in new []{"SSC_RAY_COUNT_32","SSC_RAY_COUNT_64","SSC_RAY_COUNT_128","SSC_RAY_COUNT_256"})
            {
                var m = new Material(shader); m.EnableKeyword(keyword);
                for (int pass=0;pass<m.passCount;pass++) ShaderUtil.CompilePass(m,pass,true);
                UnityEngine.Object.DestroyImmediate(m);
            }
            var messages = ShaderUtil.GetShaderMessages(shader);
            foreach(var msg in messages) results.Add(msg.severity + ": " + msg.message);
            return results;
        }
        finally
        {
            JsonUtility.FromJsonOverwrite(settings,feature.settings);
            feature.Create();
            if(linearShader != null) UnityEngine.Object.DestroyImmediate(linearShader);
            UnityEngine.Object.DestroyImmediate(cube);
            UnityEngine.Object.DestroyImmediate(mat);
            UnityEngine.Object.DestroyImmediate(go);
            RenderTexture.active=previous;
        }
    }
}
