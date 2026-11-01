// ════════════════════════════════════════════════════════════════
//  Screen Mask Renderer — frame-local scalar masks for screen-space effects.
// ════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Mine.Rendering
{
    /// <summary>
    /// Records visible opaque object masks and creates shared R8 coverage descriptors.
    /// </summary>
    /// <remarks>
    /// Coverage is stored in R, with zero excluded and one fully selected.
    /// The caller owns shader tags, materials and any published global texture ID.
    /// Object shaders sample the current camera depth; no depth attachment is written.
    /// Handles belong only to the supplied graph and camera frame.
    /// </remarks>
    public static class ScreenMaskRenderer
    {
        sealed class ObjectMaskData
        {
            public RendererListHandle rendererList;
        }

        static readonly int CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");

        // ════════════════════════════════════════════════════════════
        //  Descriptor — retain source dimensions and XR layout for scalar coverage.
        // ════════════════════════════════════════════════════════════
        public static TextureDesc CreateDescriptor(RenderGraph graph, TextureHandle sizeReference,
            string name, bool clear = true)
        {
            TextureDesc descriptor = graph.GetTextureDesc(sizeReference);
            descriptor.name = name;
            descriptor.colorFormat = GraphicsFormat.R8_UNorm;
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.clearBuffer = clear;
            descriptor.clearColor = Color.clear;
            return descriptor;
        }

        // ════════════════════════════════════════════════════════════
        //  Objects — draw material passes or override solid geometry into coverage.
        // ════════════════════════════════════════════════════════════
        public static TextureHandle RecordObjects(RenderGraph graph, ContextContainer frameData,
            TextureHandle sizeReference, TextureHandle depth, int layerMask, ShaderTagId[] shaderTags,
            Material overrideMaterial, int overridePassIndex, string name, int outputGlobalID = -1)
        {
            if (shaderTags == null || shaderTags.Length == 0)
                throw new System.ArgumentException("At least one shader tag is required.", nameof(shaderTags));

            UniversalRenderingData rendering = frameData.Get<UniversalRenderingData>();
            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            UniversalLightData lights = frameData.Get<UniversalLightData>();
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                shaderTags[0], rendering, camera, lights, camera.defaultOpaqueSortFlags);
            for (int i = 1; i < shaderTags.Length; i++)
                drawing.SetShaderPassName(i, shaderTags[i]);
            if (overrideMaterial != null)
            {
                drawing.overrideMaterial = overrideMaterial;
                drawing.overrideMaterialPassIndex = overridePassIndex;
            }
            FilteringSettings filtering = new(RenderQueueRange.opaque,
                rendering.opaqueLayerMask.value & layerMask);
            RendererListHandle rendererList = graph.CreateRendererList(
                new RendererListParams(rendering.cullResults, drawing, filtering));

            TextureHandle mask = graph.CreateTexture(CreateDescriptor(graph, sizeReference, name));
            using (var builder = graph.AddRasterRenderPass<ObjectMaskData>(name, out var data))
            {
                data.rendererList = rendererList;
                builder.UseRendererList(rendererList);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.UseGlobalTexture(CameraDepthTextureID, AccessFlags.Read);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                if (outputGlobalID != -1)
                    builder.SetGlobalTextureAfterPass(mask, outputGlobalID);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ObjectMaskData pass, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(pass.rendererList);
                });
            }
            return mask;
        }
    }
}
