using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public class Script
{
    public static object Main()
    {
        var rd=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        SSCFeature feature=null;
        foreach(var f in rd.rendererFeatures) if(f is SSCFeature) feature=(SSCFeature)f;
        if(feature==null) throw new Exception("SSC missing");
        string settings=JsonUtility.ToJson(feature.settings);
        bool active=feature.isActive;
        var go=new GameObject("SSC HDR check");
        var cam=go.AddComponent<Camera>();
        var previous=RenderTexture.active;
        var results=new System.Collections.Generic.List<string>();
        try
        {
            cam.CopyFrom(Camera.main); cam.cameraType=CameraType.Game;
            cam.rect=new Rect(0,0,1,1); cam.aspect=320f/180f;
            cam.allowDynamicResolution=false; cam.allowHDR=true;
            cam.cullingMask=0;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(0.125f,0.25f,0.5f,1);
            cam.transform.position=new Vector3(0,25,-90); cam.transform.LookAt(new Vector3(0,50,0));
            var data=cam.GetUniversalAdditionalCameraData();
            data.SetRenderer(0); data.requiresDepthTexture=true; data.renderPostProcessing=false;
            feature.settings.speed=0; feature.settings.jitter=0; feature.settings.downsample=2;
            foreach(var format in new[]{RenderTextureFormat.RGB111110Float,RenderTextureFormat.ARGBHalf})
            {
                var rt=new RenderTexture(320,180,24,format);
                var tex=new Texture2D(320,180,TextureFormat.RGBAFloat,false,true);
                try
                {
                    rt.Create();
                    feature.SetActive(false);
                    RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                    RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,320,180),0,0);tex.Apply();
                    var baseline=tex.GetPixels();
                    feature.SetActive(true);
                    foreach(bool cloud in new[]{false,true})
                    {
                        feature.settings.height=cloud?50:10000;
                        RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                        RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,320,180),0,0);tex.Apply();
                        var pixels=tex.GetPixels();
                        double squared=0; float max=0;
                        for(int i=0;i<pixels.Length;i++)
                        {
                            var d=pixels[i]-baseline[i];
                            if(float.IsNaN(pixels[i].r)||float.IsInfinity(pixels[i].r)) throw new Exception("nonfinite");
                            max=Mathf.Max(max,Mathf.Abs(d.r),Mathf.Abs(d.g),Mathf.Abs(d.b));
                            squared+=d.r*d.r+d.g*d.g+d.b*d.b;
                        }
                        double rms=Math.Sqrt(squared/(pixels.Length*3));
                        results.Add(format+" cloud="+cloud+" rms="+rms+" max="+max);
                        results.Add("center=" + pixels[320*90+160] + " baseline=" + baseline[320*90+160]);
                        if(!cloud && rms>0.01) throw new Exception("empty clouds changed scene: " + string.Join("; ",results));
                        if(cloud && rms<0.01) throw new Exception("positive control did not render clouds");
                    }
                }
                finally{rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);}
            }
            return results;
        }
        finally
        {
            JsonUtility.FromJsonOverwrite(settings,feature.settings);
            feature.SetActive(active);
            RenderTexture.active=previous;
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}