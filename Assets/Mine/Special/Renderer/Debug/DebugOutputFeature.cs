using UnityEngine;
using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Mine.RenderingDebug
{
    // ════════════════════════════════════════════════════════════
    //  DebugOutputFeature — 将绑定材质或 Shader 直接覆盖到屏幕
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// 通用全屏 Shader 调试 RendererFeature。Feature 只负责创建真实 RenderGraph 临时 RT、
    /// 执行绑定 Shader 的全屏 Blit，并把结果写回相机颜色目标。
    /// </summary>
    public sealed class DebugOutputFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("优先使用此材质，保留纹理、参数和关键字。")]
            public Material debugMaterial;

            [Tooltip("未指定材质时，使用此 Shader 的默认参数。")]
            public Shader debugShader;

            [Tooltip("在 Pass 0 之后执行的材质 Pass；-1 表示仅复制输出。")]
            [Min(-1)] public int additionalPassIndex = -1;

            [Tooltip("被测 Shader 需要场景深度时开启。")]
            public bool requireDepth;

            [Header("Debug")]
            public bool debug;
        }

        private sealed class ShaderPass : ScriptableRenderPass
        {
            private sealed class RenderPassData
            {
                public Material material;
                public TextureHandle source;
                public TextureHandle output;
            }

            private sealed class OutputPassData
            {
                public TextureHandle source;
                public TextureHandle destination;
                public Material material;
                public int passIndex;
            }

            private Material _material;
            private int _additionalPassIndex = -1;
            private bool _requireDepth;

            public ShaderPass(Material material)
            {
                _material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }

            public void SetMaterial(Material material, int additionalPassIndex, bool requireDepth)
            {
                _material = material;
                _additionalPassIndex = material != null && additionalPassIndex >= 0
                    && additionalPassIndex < material.passCount ? additionalPassIndex : -1;
                _requireDepth = requireDepth;
                ConfigureInput(requireDepth ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                TextureHandle destination = resourceData.activeColorTexture;

                if (_material == null || !destination.IsValid() || resourceData.isActiveTargetBackBuffer)
                    return;

                RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;

                TextureHandle output = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    descriptor,
                    "_DebugShaderOutputRT",
                    false);

                using (var builder = renderGraph.AddUnsafePass<RenderPassData>(
                    "Debug Output/Shader",
                    out var passData))
                {
                    passData.material = _material;
                    passData.source = destination;
                    passData.output = output;

                    builder.UseTexture(destination, AccessFlags.Read);
                    if (_requireDepth && resourceData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                    builder.UseTexture(output, AccessFlags.Write);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc((RenderPassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        Blitter.BlitCameraTexture(
                            commandBuffer,
                            data.source,
                            data.output,
                            data.material,
                            0);
                    });
                }

                using (var builder = renderGraph.AddUnsafePass<OutputPassData>(
                    "Debug Output/Screen",
                    out var passData))
                {
                    passData.source = output;
                    passData.destination = destination;
                    passData.material = _material;
                    passData.passIndex = _additionalPassIndex;

                    builder.UseTexture(output, AccessFlags.Read);
                    builder.UseTexture(destination, AccessFlags.Write);
                    if (_requireDepth && resourceData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc((OutputPassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        if (data.passIndex >= 0)
                            Blitter.BlitCameraTexture(commandBuffer, data.source, data.destination,
                                data.material, data.passIndex);
                        else
                            Blitter.BlitCameraTexture(commandBuffer, data.source, data.destination);
                    });
                }
            }
        }

        public Settings settings = new Settings();

        private ShaderPass _pass;
        private Material _ownedMaterial;

        public override void Create()
        {
            ReleaseResources();

            _pass = new ShaderPass(null);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!settings.debug || _pass == null)
                return;

            Material material = settings.debugMaterial;
            if (material != null || settings.debugShader == null ||
                (_ownedMaterial != null && _ownedMaterial.shader != settings.debugShader))
                ReleaseOwnedMaterial();

            if (material == null && settings.debugShader != null)
            {
                if (_ownedMaterial == null)
                    _ownedMaterial = CoreUtils.CreateEngineMaterial(settings.debugShader);
                material = _ownedMaterial;
            }

            _pass.SetMaterial(material, settings.additionalPassIndex, settings.requireDepth);
            if (material != null)
                renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ReleaseResources();

            base.Dispose(disposing);
        }

        private void ReleaseResources()
        {
            _pass = null;
            ReleaseOwnedMaterial();
        }

        private void ReleaseOwnedMaterial()
        {
            if (_ownedMaterial != null)
            {
                CoreUtils.Destroy(_ownedMaterial);
                _ownedMaterial = null;
            }
        }
    }
}
