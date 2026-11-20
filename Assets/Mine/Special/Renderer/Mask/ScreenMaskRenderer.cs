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
    /// Records visible object masks and creates shared coverage descriptors.
    /// </summary>
    /// <remarks>
    /// Scalar callers store coverage in R. Override materials can use separate channels.
    /// The caller owns shader tags, materials and any published global texture ID.
    /// Object shaders sample the current camera depth; no depth attachment is written.
    /// Handles belong only to the supplied graph and camera frame.
    /// </remarks>
    public static class ScreenMaskRenderer
    {
        sealed class ObjectMaskData
        {
            public RendererListHandle rendererList;
            public RendererListHandle transparentList;
            public RendererListHandle transparentOccluders;
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
            Material overrideMaterial, int overridePassIndex, string name, int outputGlobalID = -1,
            int transparentOverridePassIndex = -1, int transparentOccluderPassIndex = -1,
            GraphicsFormat colorFormat = GraphicsFormat.R8_UNorm)
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

            int transparentLayers = rendering.transparentLayerMask.value;
            RendererListHandle transparentList = CreateTransparentList(graph, rendering, drawing,
                transparentLayers & layerMask, transparentOverridePassIndex);
            RendererListHandle transparentOccluders = CreateTransparentList(graph, rendering, drawing,
                transparentLayers, transparentOccluderPassIndex);

            TextureDesc descriptor = CreateDescriptor(graph, sizeReference, name);
            descriptor.colorFormat = colorFormat;
            TextureHandle mask = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<ObjectMaskData>(name, out var data))
            {
                data.rendererList = rendererList;
                data.transparentList = transparentList;
                data.transparentOccluders = transparentOccluders;
                builder.UseRendererList(rendererList);
                if (transparentList.IsValid())
                    builder.UseRendererList(transparentList);
                if (transparentOccluders.IsValid())
                    builder.UseRendererList(transparentOccluders);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.UseGlobalTexture(CameraDepthTextureID, AccessFlags.Read);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                if (outputGlobalID != -1)
                    builder.SetGlobalTextureAfterPass(mask, outputGlobalID);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ObjectMaskData pass, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(pass.rendererList);
                    if (pass.transparentList.IsValid())
                        context.cmd.DrawRendererList(pass.transparentList);
                    if (pass.transparentOccluders.IsValid())
                        context.cmd.DrawRendererList(pass.transparentOccluders);
                });
            }
            return mask;
        }

        static RendererListHandle CreateTransparentList(RenderGraph graph, UniversalRenderingData rendering,
            DrawingSettings drawing, int layerMask, int overridePassIndex)
        {
            if (overridePassIndex < 0 || layerMask == 0)
                return default;

            SortingSettings sorting = drawing.sortingSettings;
            sorting.criteria = SortingCriteria.CommonTransparent;
            drawing.sortingSettings = sorting;
            drawing.overrideMaterialPassIndex = overridePassIndex;
            FilteringSettings filtering = new(RenderQueueRange.transparent, layerMask);
            return graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));
        }
    }
}
