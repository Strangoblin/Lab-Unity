// ════════════════════════════════════════════════════════════════
//  SSGI Feature — shared opaque input and one lighting composite.
// ════════════════════════════════════════════════════════════════
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class SSGIFeature : ScriptableRendererFeature
{
    public enum DebugMode { Off, AO, DiffuseGI, SpecularGI }

    [Serializable]
    public class Settings
    {
        [Header("Resources")]
        public Shader compositeShader;
        public Shader temporalShader;

        [Header("Technical · Modules")]
        public AOFeature.Settings ao = new();
        public DiffuseGIFeature.Settings diffuseGI = new();
        public SpecularGIFeature.Settings specularGI = new();

        [Header("Performance")]
        public SSGIPerformanceControls performance = new();

        [Header("Intensity")]
        public SSGIIntensityControls intensity = new();

        [Header("Temporal")]
        public SSGITemporalControls temporal = new();

        [Header("Artistic")]
        public SSGIArtisticControls artistic = new();

        [Header("Debug")]
        public DebugMode debug = DebugMode.Off;
    }

    sealed class SSGIPass : ScriptableRenderPass
    {
        sealed class CompositeData
        {
            public Material material;
            public TextureHandle scene;
            public TextureHandle ao;
            public TextureHandle diffuse;
            public TextureHandle specular;
            public Vector4 parameters;
            public Vector4 albedo;
            public Vector4 flags;
            public float roughness;
            public float debug;
        }

        static readonly int AOTextureID = Shader.PropertyToID("_SSGIAOTexture");
        static readonly int DiffuseTextureID = Shader.PropertyToID("_SSGIDiffuseTexture");
        static readonly int SpecularTextureID = Shader.PropertyToID("_SSGISpecularTexture");
        static readonly int ParamsID = Shader.PropertyToID("_SSGIParams");
        static readonly int AlbedoID = Shader.PropertyToID("_SSGIReceiverAlbedo");
        static readonly int ModuleFlagsID = Shader.PropertyToID("_SSGIModuleFlags");
        static readonly int RoughnessID = Shader.PropertyToID("_SSGIRoughness");
        static readonly int DebugID = Shader.PropertyToID("_SSGIDebugMode");

        readonly Settings _settings;
        readonly Material _compositeMaterial;
        readonly Material _aoMaterial;
        readonly Material _diffuseMaterial;
        readonly AOFeature.AOPass _aoPass;
        readonly DiffuseGIFeature.DiffuseGIPass _diffusePass;
        readonly SpecularGIFeature.SpecularGIPass _specularPass;
        readonly SSGITemporalFilter _temporal;

        public SSGIPass(Settings settings)
        {
            _settings = settings;
            _compositeMaterial = CoreUtils.CreateEngineMaterial(settings.compositeShader);
            _temporal = new SSGITemporalFilter(settings.temporalShader);
            if (settings.ao.shader != null)
            {
                _aoMaterial = CoreUtils.CreateEngineMaterial(settings.ao.shader);
                _aoPass = new AOFeature.AOPass(_aoMaterial, settings.ao);
            }
            if (settings.diffuseGI.shader != null)
            {
                _diffuseMaterial = CoreUtils.CreateEngineMaterial(settings.diffuseGI.shader);
                _diffusePass = new DiffuseGIFeature.DiffuseGIPass(_diffuseMaterial, settings.diffuseGI);
            }
            if (settings.specularGI.shader != null)
                _specularPass = new SpecularGIFeature.SpecularGIPass(settings.specularGI.shader, settings.specularGI);

            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Color
                | ScriptableRenderPassInput.Depth
                | ScriptableRenderPassInput.Normal
                | ScriptableRenderPassInput.Motion);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid()
                || !resources.cameraDepthTexture.IsValid() || !resources.cameraNormalsTexture.IsValid())
                return;

            TextureHandle scene = resources.activeColorTexture;
            _temporal.BeginFrame(frameData);
            int frameIndex = _temporal.FrameIndex;
            TextureHandle ao = _settings.intensity.ao > 0f && _aoPass != null
                ? _aoPass.RecordIntegrated(graph, frameData, scene, frameIndex,
                    _settings.performance.ao, _settings.artistic.aoFalloff) : TextureHandle.nullHandle;
            TextureHandle diffuse = _settings.intensity.diffuseGI > 0f && _diffusePass != null
                ? _diffusePass.RecordIntegrated(graph, frameData, scene, frameIndex,
                    _settings.performance.diffuseGI, _settings.artistic.diffuseDistanceFalloff,
                    _settings.artistic.receiverAlbedo) : TextureHandle.nullHandle;
            TextureHandle specular = _settings.intensity.specularGI > 0f && _specularPass != null
                ? _specularPass.RecordIntegrated(graph, frameData, scene, frameIndex,
                    _settings.performance.specularGI, _settings.artistic.specularRoughness) : TextureHandle.nullHandle;

            if (ao.IsValid())
                ao = _temporal.Resolve(graph, frameData, ao,
                    SSGITemporalFilter.Signal.AO, _settings.temporal.ao);
            if (diffuse.IsValid())
                diffuse = _temporal.Resolve(graph, frameData, diffuse,
                    SSGITemporalFilter.Signal.DiffuseGI, _settings.temporal.diffuseGI);
            if (specular.IsValid())
                specular = _temporal.Resolve(graph, frameData, specular,
                    SSGITemporalFilter.Signal.SpecularGI, _settings.temporal.specularGI);
            _temporal.CompleteFrame(graph, frameData);

            if (!ao.IsValid() && !diffuse.IsValid() && !specular.IsValid())
                return;

            TextureDesc descriptor = graph.GetTextureDesc(scene);
            descriptor.name = "SSGI.Composite";
            descriptor.clearBuffer = false;
            TextureHandle output = graph.CreateTexture(descriptor);

            using (var builder = graph.AddRasterRenderPass<CompositeData>("SSGI.Composite", out var data))
            {
                data.material = _compositeMaterial;
                data.scene = scene;
                data.ao = ao;
                data.diffuse = diffuse;
                data.specular = specular;
                data.parameters = new Vector4(
                    Mathf.Clamp(_settings.intensity.ao, 0f, 4f),
                    Mathf.Clamp(_settings.intensity.diffuseGI, 0f, 4f),
                    Mathf.Clamp01(_settings.intensity.specularGI),
                    Mathf.Clamp01(_settings.artistic.sceneAO));
                Color albedo = _settings.artistic.receiverAlbedo.linear;
                data.albedo = new Vector4(Mathf.Clamp01(albedo.r), Mathf.Clamp01(albedo.g), Mathf.Clamp01(albedo.b), 1f);
                data.flags = new Vector4(ao.IsValid() ? 1f : 0f, diffuse.IsValid() ? 1f : 0f,
                    specular.IsValid() ? 1f : 0f, 0f);
                data.roughness = Mathf.Clamp01(_settings.artistic.specularRoughness);
                data.debug = (float)_settings.debug;

                builder.UseTexture(scene, AccessFlags.Read);
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                if (ao.IsValid())
                    builder.UseTexture(ao, AccessFlags.Read);
                if (diffuse.IsValid())
                    builder.UseTexture(diffuse, AccessFlags.Read);
                if (specular.IsValid())
                    builder.UseTexture(specular, AccessFlags.Read);
                builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((CompositeData pass, RasterGraphContext context) =>
                {
                    pass.material.SetVector(ParamsID, pass.parameters);
                    pass.material.SetVector(AlbedoID, pass.albedo);
                    pass.material.SetVector(ModuleFlagsID, pass.flags);
                    pass.material.SetFloat(RoughnessID, pass.roughness);
                    pass.material.SetFloat(DebugID, pass.debug);
                    if (pass.ao.IsValid())
                        context.cmd.SetGlobalTexture(AOTextureID, pass.ao);
                    if (pass.diffuse.IsValid())
                        context.cmd.SetGlobalTexture(DiffuseTextureID, pass.diffuse);
                    if (pass.specular.IsValid())
                        context.cmd.SetGlobalTexture(SpecularTextureID, pass.specular);
                    Blitter.BlitTexture(context.cmd, pass.scene, new Vector4(1f, 1f, 0f, 0f), pass.material, 0);
                });
            }

            resources.cameraColor = output;
        }

        public void Release()
        {
            _aoPass?.Release();
            _diffusePass?.Release();
            _specularPass?.Release();
            _temporal.Release();
            CoreUtils.Destroy(_aoMaterial);
            CoreUtils.Destroy(_diffuseMaterial);
            CoreUtils.Destroy(_compositeMaterial);
        }
    }

    public Settings settings = new();
    SSGIPass _pass;

    public override void Create()
    {
        _pass?.Release();
        _pass = settings.compositeShader != null ? new SSGIPass(settings) : null;
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Release();
        _pass = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        CameraData camera = renderingData.cameraData;
        if (_pass == null || camera.renderType == CameraRenderType.Overlay
            || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
            return;
        renderer.EnqueuePass(_pass);
    }
}
