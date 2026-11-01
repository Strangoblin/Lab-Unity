using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Mine.HDRMaps;
using Object = UnityEngine.Object;

// Diagnostic only: prints every expected/actual probe even on mismatch, never
// changes the production sky/capture code or writes a scene/asset.
public class Script
{
    [Serializable] public sealed class Probe
    {
        public string name, face;
        public Vector3 direction, delta;
        public Color expected, actual;
        public float error;
    }
    [Serializable] public sealed class Report
    {
        public string suite = "HDRMapCaptureGradientChecks", graphicsDevice, error;
        public float maxError;
        public string[] faceOrder = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };
        public float[] faceMaxErrors = new float[6];
        public bool matchesIndependentDirections, originalSceneUnchanged, renderStateRestored;
        public List<Probe> probes = new List<Probe>();
        // String details survive unityctl's Roslyn transient-type serialization
        // even on Editor versions that omit List<user-defined nested class>.
        public List<string> details = new List<string>();
        public List<string> shaderMessages = new List<string>();
        public List<string> unityErrors = new List<string>();
    }
    public static object Main(string[] args)
    {
        var report = new Report { graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName };
        Scene originalScene = SceneManager.GetActiveScene();
        bool originalDirty = originalScene.isDirty;
        int originalCount = SceneManager.sceneCount;
        RenderTexture originalRT = RenderTexture.active;
        bool originalSRGB = GL.sRGBWrite;
        Scene preview = default(Scene);
        var owned = new List<Object>();
        Application.LogCallback listener = (message, trace, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                report.unityErrors.Add(type + ": " + message);
        };
        Application.logMessageReceived += listener;
        try
        {
            preview = EditorSceneManager.NewPreviewScene();
            string source = @"Shader ""Hidden/Mine/HDRMapCaptureGradientDiagnostic"" {
Properties { _Origin(""Origin"",Vector)=(0,0,0,0) }
SubShader { Tags { ""RenderPipeline""=""UniversalPipeline"" ""RenderType""=""Opaque"" } Pass {
Tags { ""LightMode""=""UniversalForwardOnly"" } Cull Off ZWrite On ZTest LEqual
HLSLPROGRAM
#pragma target 3.5
#pragma vertex Vert
#pragma fragment Frag
#include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
CBUFFER_START(UnityPerMaterial) float4 _Origin; CBUFFER_END
struct A { float4 positionOS:POSITION; };
struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; };
V Vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.positionWS=TransformObjectToWorld(i.positionOS.xyz); return o; }
float4 Frag(V i):SV_Target { return float4(normalize(i.positionWS-_Origin.xyz)*.5+.5,1); }
ENDHLSL
} } Fallback Off }";
            Shader shader = ShaderUtil.CreateShaderAsset(source, true);
            owned.Add(shader);
            shader.hideFlags = HideFlags.HideAndDontSave;
            Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            owned.Add(material);
            material.SetVector("_Origin", Vector4.zero);
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            owned.Add(sphere);
            sphere.hideFlags = HideFlags.HideAndDontSave;
            sphere.name = "Real geometry directional gradient, inside sphere";
            SceneManager.MoveGameObjectToScene(sphere, preview);
            sphere.layer = 0;
            sphere.transform.position = Vector3.zero;
            sphere.transform.localScale = Vector3.one * 20f;
            sphere.GetComponent<Renderer>().sharedMaterial = material;
            GameObject cameraGO = new GameObject("HDR gradient diagnostic camera") { hideFlags = HideFlags.HideAndDontSave };
            owned.Add(cameraGO);
            SceneManager.MoveGameObjectToScene(cameraGO, preview);
            Camera camera = cameraGO.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = preview;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
            camera.allowHDR = true;
            camera.allowMSAA = false;
            UniversalAdditionalCameraData data = cameraGO.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.volumeLayerMask = 0;
            data.antialiasing = AntialiasingMode.None;
            data.allowXRRendering = false;
            data.allowHDROutput = false;
            Texture2D panorama = HDRMapCapture.Bake(camera, Vector3.zero, Quaternion.identity, 100f, 1, false, 65);
            owned.Add(panorama);
            Vector3[] directions = {
                new Vector3(1,.2f,.3f),new Vector3(-1,.2f,.3f),
                new Vector3(.3f,1,.2f),new Vector3(.3f,-1,.2f),
                new Vector3(.3f,.2f,1),new Vector3(.3f,.2f,-1),
                new Vector3(.01f,1,.1f),new Vector3(.01f,1,-.1f),
                new Vector3(.01f,-1,.1f),new Vector3(.01f,-1,-.1f)
            };
            for (int i = 0; i < directions.Length; i++)
            {
                Vector3 d = directions[i].normalized;
                float u = Mathf.Atan2(d.x, d.z) / (2f * Mathf.PI) + .5f;
                float v = Mathf.Asin(d.y) / Mathf.PI + .5f;
                Color actual = panorama.GetPixelBilinear(u, v);
                Color expected = new Color(d.x * .5f + .5f, d.y * .5f + .5f, d.z * .5f + .5f, 1f);
                Vector3 delta = new Vector3(actual.r - expected.r, actual.g - expected.g, actual.b - expected.b);
                float error = Mathf.Max(Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)), Mathf.Abs(delta.z));
                int face = Face(d);
                report.maxError = Mathf.Max(report.maxError, error);
                report.faceMaxErrors[face] = Mathf.Max(report.faceMaxErrors[face], error);
                report.probes.Add(new Probe { name = "direction_" + i, direction = d, face = report.faceOrder[face],
                    expected = expected, actual = actual, delta = delta, error = error });
                report.details.Add("probe=" + i + " face=" + report.faceOrder[face] + " direction=" + d.ToString("F6")
                    + " expected=" + expected.ToString("F6") + " actual=" + actual.ToString("F6")
                    + " delta=" + delta.ToString("F6") + " maxError=" + error.ToString("R"));
            }
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                report.shaderMessages.Add(message.severity + ": " + message.message + " @ " + message.file + ":" + message.line);
            report.matchesIndependentDirections = report.maxError < .025f;
        }
        catch (Exception exception) { report.error = exception.ToString(); }
        finally
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            Application.logMessageReceived -= listener;
        }
        report.originalSceneUnchanged = SceneManager.GetActiveScene() == originalScene && originalScene.isDirty == originalDirty
            && SceneManager.sceneCount == originalCount;
        report.renderStateRestored = RenderTexture.active == originalRT && GL.sRGBWrite == originalSRGB;
        string json = JsonUtility.ToJson(report, true);
        if (args != null && args.Length > 0)
        {
            Directory.CreateDirectory(args[0]);
            File.WriteAllText(Path.Combine(args[0], "capture-gradient-diagnostic.json"), json);
        }
        return json;
    }
    private static int Face(Vector3 d)
    {
        float x = Mathf.Abs(d.x), y = Mathf.Abs(d.y), z = Mathf.Abs(d.z);
        return x > y && x > z ? (d.x >= 0 ? 0 : 1) : y > z ? (d.y >= 0 ? 2 : 3) : (d.z >= 0 ? 4 : 5);
    }
}
