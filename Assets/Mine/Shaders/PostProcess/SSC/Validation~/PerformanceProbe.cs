using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public class Script
{
    [Serializable] public class Block { public string mode; public int round; public double[] submit; public double[] sync; }
    [Serializable] public class Report
    {
        public string device, api, unity, scene, pose, settings, metric, sourceHash;
        public int width=1920, height=1080, warmup=8, samples=12;
        public bool sceneDirtyBefore, sceneDirtyAfter, restored;
        public string samplesFile="samples.csv";
    }
    public static object Main()
    {
        string root="tmp/sessions/"+DateTime.UtcNow.ToString("yyyy-MM-dd")+"-ssc-cost-"+Guid.NewGuid().ToString("N").Substring(0,8)+"/output";
        Directory.CreateDirectory(root);
        string csv=Path.Combine(root,"samples.csv");
        if(File.Exists(csv))throw new Exception("Avoid overwriting prior raw samples");
        File.WriteAllText(csv,"round,mode,sample,submit_ms,sync_ms\n");
        var rd=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        SSCFeature f=null; foreach(var x in rd.rendererFeatures)if(x is SSCFeature)f=(SSCFeature)x;
        if(f==null||!f.isActive)throw new Exception("Active SSC required");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        string originalSettings=JsonUtility.ToJson(f.settings);
        // Historical .995-only probe: use the pinned pre-optimization source.
        string source=File.ReadAllText("Assets/Mine/Shaders/PostProcess/SSC/Validation~/Fixtures/SSC-before-optimization.shader.txt");
        string marker="if (tCurrent >= tFar || tCurrent >= tScene)";
        if(!source.Contains(marker))throw new Exception("Early-exit patch marker missing");
        string earlySource=source.Replace("Shader \"PostProcess/SSC\"","Shader \"Hidden/SSC/EarlyExitProfile\"")
            .Replace(marker,"if (tCurrent >= tFar || tCurrent >= tScene || Density >= 0.995)");
        var earlyShader=ShaderUtil.CreateShaderAsset(earlySource);
        var baselineShader=ShaderUtil.CreateShaderAsset(source.Replace("Shader \"PostProcess/SSC\"","Shader \"Hidden/SSC/LegacyCostBaseline\""));
        var go=new GameObject("SSC profile camera"); var camera=go.AddComponent<Camera>();
        var rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGBHalf);
        var pixel=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
        var previous=RenderTexture.active;
        var report=new Report {
            device=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),unity=Application.unityVersion,
            scene=scene.path,settings=originalSettings,sceneDirtyBefore=scene.isDirty,
            metric="GPU-synchronized SubmitRenderRequest + 1x1 ReadPixels wall ms; not pure GPU or GameView FPS"
        };
        using(var sha=System.Security.Cryptography.SHA256.Create())report.sourceHash=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(source))).Replace("-","");
        try
        {
            var view=SceneView.lastActiveSceneView;
            camera.CopyFrom(view!=null?view.camera:Camera.main);
            camera.cameraType=CameraType.Game;camera.rect=new Rect(0,0,1,1);camera.aspect=1920f/1080;
            camera.allowDynamicResolution=false;
            var reference=view!=null?view.camera:Camera.main;
            camera.transform.SetPositionAndRotation(reference.transform.position,reference.transform.rotation);
            report.pose=camera.transform.position+" rotation="+camera.transform.eulerAngles+" fov="+camera.fieldOfView;
            var data=camera.GetUniversalAdditionalCameraData();data.SetRenderer(0);data.renderPostProcessing=false;
            data.antialiasing=AntialiasingMode.None;data.requiresDepthTexture=true;
            rt.Create(); var request=new UniversalRenderPipeline.SingleCameraRequest {destination=rt};
            if(!RenderPipeline.SupportsRenderRequest(camera,request))throw new Exception("Unsupported render request");
            string[] modes={"current-q64","early-q64","current-h64","early-h64","current-q128","early-q128"};
            for(int round=0;round<3;round++)
            {
                for(int index=0;index<modes.Length;index++)
                {
                    int slot=round%2==0?index:modes.Length-1-index;
                    string mode=modes[slot];
                    f.settings.sscShader=mode.StartsWith("early")?earlyShader:baselineShader;
                    f.settings.rayCount=mode.EndsWith("128")?SSCFeature.RayCount.COUNT128:SSCFeature.RayCount.COUNT64;
                    f.settings.downsample=mode.Contains("-h")?1:2;
                    f.settings.speed=0;f.settings.jitter=0;f.settings.showSSC=false;f.Create();
                    for(int warm=0;warm<8;warm++)
                    {
                        RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=rt;
                        pixel.ReadPixels(new Rect(0,0,1,1),0,0,false);pixel.Apply(false,false);
                    }
                    if(round==0 && (mode=="current-q64"||mode=="early-q64"))
                    {
                        var image=new Texture2D(1920,1080,TextureFormat.RGBA32,false,false);
                        image.ReadPixels(new Rect(0,0,1920,1080),0,0,false);image.Apply();
                        File.WriteAllBytes(Path.Combine(root,mode+".png"),image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                    }
                    var block=new Block {mode=mode,round=round,submit=new double[12],sync=new double[12]};
                    var timer=new Stopwatch();
                    for(int sample=0;sample<12;sample++)
                    {
                        timer.Restart();RenderPipeline.SubmitRenderRequest(camera,request);
                        block.submit[sample]=timer.Elapsed.TotalMilliseconds;
                        RenderTexture.active=rt;pixel.ReadPixels(new Rect(0,0,1,1),0,0,false);pixel.Apply(false,false);
                        block.sync[sample]=timer.Elapsed.TotalMilliseconds;timer.Stop();
                    }
                    var rows=new System.Text.StringBuilder();
                    for(int sample=0;sample<12;sample++)rows.AppendLine(round+","+mode+","+sample+","+block.submit[sample].ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+block.sync[sample].ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                    File.AppendAllText(csv,rows.ToString());
                    File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(report,true));
                }
            }
            return Path.GetFullPath(Path.Combine(root,"profile.json"));
        }
        finally
        {
            JsonUtility.FromJsonOverwrite(originalSettings,f.settings);f.Create();
            RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(pixel);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(earlyShader);UnityEngine.Object.DestroyImmediate(baselineShader);
            report.sceneDirtyAfter=scene.isDirty;report.restored=JsonUtility.ToJson(f.settings)==originalSettings;
            File.WriteAllText(Path.Combine(root,"profile.json"),JsonUtility.ToJson(report,true));
        }
    }
}