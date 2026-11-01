// ═══════════════════════════════════════════════════════════════
//  ScreenSpaceDepthPyramid — lazy, frame-local RenderGraph depth hierarchy service.
// ═══════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;

namespace Mine.ScreenSpaceTrace
{
    public static class ScreenSpaceDepthPyramid
    {
        public struct Request
        {
            public TextureHandle Source;
            public ComputeShader Shader;
            public int SourceRevision;
            public int Width;
            public int Height;
            public Vector4 SourceUvScaleBias;
            public Vector4 ZBufferParams;
            public float NearPlane;
            public float FarPlane;
            public bool LinearDepth;
            public bool Orthographic;
            public bool ReversedZ;
        }

        public readonly struct Result
        {
            public readonly TextureHandle Texture;
            public readonly int Width;
            public readonly int Height;
            public readonly int MipCount;

            internal Result(TextureHandle texture, int width, int height, int mipCount)
            {
                Texture = texture;
                Width = width;
                Height = height;
                MipCount = mipCount;
            }
        }

        private sealed class FrameCache : ContextItem
        {
            public readonly List<CacheEntry> Entries = new();
            public FrameCache() { }
            public override void Reset() => Entries.Clear();
        }

        private struct CacheEntry
        {
            public RenderGraph Graph;
            public Request Request;
            public Result Result;
        }

        private sealed class CopyPassData
        {
            public Request Request;
            public TextureHandle Destination;
            public int Kernel;
        }

        private sealed class ReducePassData
        {
            public ComputeShader Shader;
            public TextureHandle Texture;
            public int Kernel;
            public int Mip;
            public int Width;
            public int Height;
            public float FarPlane;
        }

        public static bool TryGet(RenderGraph graph, ContextContainer frameData, Request request,
            out Result result, out string reason)
        {
            result = default;
            reason = Validate(graph, frameData, request);
            if (reason != null)
                return false;
            FrameCache cache = frameData.GetOrCreate<FrameCache>();
            foreach (CacheEntry entry in cache.Entries)
            {
                if (entry.Graph == graph && SameRequest(entry.Request, request))
                {
                    result = entry.Result;
                    return true;
                }
            }
            int mipCount = 1;
            for (int size = Mathf.Max(request.Width, request.Height); size > 1; size >>= 1)
                mipCount++;
            TextureHandle texture = graph.CreateTexture(new TextureDesc(request.Width, request.Height)
            {
                name = "Screen Trace Eye Depth Pyramid",
                format = GraphicsFormat.R32_SFloat,
                dimension = TextureDimension.Tex2D,
                slices = 1,
                msaaSamples = MSAASamples.None,
                useMipMap = true,
                autoGenerateMips = false,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            });
            RecordCopy(graph, request, texture);
            for (int mip = 1; mip < mipCount; mip++)
                RecordReduce(graph, request, texture, mip);
            result = new Result(texture, request.Width, request.Height, mipCount);
            cache.Entries.Add(new CacheEntry { Graph = graph, Request = request, Result = result });
            return true;
        }

        private static string Validate(RenderGraph graph, ContextContainer frameData, Request request)
        {
            if (graph == null || frameData == null || !request.Source.IsValid() || request.Shader == null)
                return "Missing graph, frame data, depth source or compute shader.";
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat,
                GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample))
                return "Compute shaders or R32_SFloat load/store are unavailable.";
            if (request.Width < 1 || request.Height < 1 || request.Width > SystemInfo.maxTextureSize
                || request.Height > SystemInfo.maxTextureSize || !float.IsFinite(request.NearPlane)
                || !float.IsFinite(request.FarPlane) || request.NearPlane <= 0f || request.FarPlane <= request.NearPlane
                || !Finite(request.SourceUvScaleBias) || request.SourceUvScaleBias.x == 0f
                || request.SourceUvScaleBias.y == 0f || !Finite(request.ZBufferParams))
                return "Invalid viewport, UV mapping or depth conversion parameters.";
            TextureDesc source = graph.GetTextureDesc(request.Source);
            if (source.dimension != TextureDimension.Tex2D || source.slices > 1 || source.msaaSamples != MSAASamples.None)
                return "Only resolved, single-slice 2D depth is supported; resolve MSAA/XR in the caller.";
            if (!request.Shader.HasKernel("CopyDepth") || !request.Shader.HasKernel("ReduceDepth"))
                return "Depth pyramid kernels are missing.";
            return null;
        }

        private static bool Finite(Vector4 value) => float.IsFinite(value.x) && float.IsFinite(value.y)
            && float.IsFinite(value.z) && float.IsFinite(value.w);

        private static bool SameRequest(Request first, Request second) => first.Source.Equals(second.Source)
            && first.Shader == second.Shader && first.SourceRevision == second.SourceRevision
            && first.Width == second.Width && first.Height == second.Height
            && first.SourceUvScaleBias.Equals(second.SourceUvScaleBias) && first.ZBufferParams.Equals(second.ZBufferParams)
            && first.NearPlane == second.NearPlane && first.FarPlane == second.FarPlane
            && first.LinearDepth == second.LinearDepth && first.Orthographic == second.Orthographic
            && first.ReversedZ == second.ReversedZ;

        private static void RecordCopy(RenderGraph graph, Request request, TextureHandle destination)
        {
            using var builder = graph.AddComputePass<CopyPassData>("Screen Trace Copy Depth", out var data);
            data.Request = request;
            data.Destination = destination;
            data.Kernel = request.Shader.FindKernel("CopyDepth");
            builder.UseTexture(request.Source, AccessFlags.Read);
            builder.UseTexture(destination, AccessFlags.Write);
            builder.SetRenderFunc((CopyPassData pass, ComputeGraphContext context) =>
            {
                Request input = pass.Request;
                context.cmd.SetComputeTextureParam(input.Shader, pass.Kernel, SourceDepthId, input.Source);
                context.cmd.SetComputeTextureParam(input.Shader, pass.Kernel, DestinationId, pass.Destination, 0);
                context.cmd.SetComputeVectorParam(input.Shader, DestinationSizeId, new Vector4(input.Width, input.Height, 0, 0));
                context.cmd.SetComputeVectorParam(input.Shader, SourceUvScaleBiasId, input.SourceUvScaleBias);
                context.cmd.SetComputeVectorParam(input.Shader, SourceZBufferParamsId, input.ZBufferParams);
                context.cmd.SetComputeVectorParam(input.Shader, DepthParamsId,
                    new Vector4(input.NearPlane, input.FarPlane, input.Orthographic ? 1 : 0, input.ReversedZ ? 1 : 0));
                context.cmd.SetComputeIntParam(input.Shader, SourceIsLinearId, input.LinearDepth ? 1 : 0);
                context.cmd.DispatchCompute(input.Shader, pass.Kernel, (input.Width + 7) / 8, (input.Height + 7) / 8, 1);
            });
        }

        private static void RecordReduce(RenderGraph graph, Request request, TextureHandle texture, int mip)
        {
            using var builder = graph.AddComputePass<ReducePassData>("Screen Trace Reduce Depth", out var data);
            data.Shader = request.Shader;
            data.Texture = texture;
            data.Kernel = request.Shader.FindKernel("ReduceDepth");
            data.Mip = mip;
            data.Width = Mathf.Max(1, request.Width >> mip);
            data.Height = Mathf.Max(1, request.Height >> mip);
            data.FarPlane = request.FarPlane;
            builder.UseTexture(texture, AccessFlags.ReadWrite);
            builder.SetRenderFunc((ReducePassData pass, ComputeGraphContext context) =>
            {
                context.cmd.SetComputeTextureParam(pass.Shader, pass.Kernel, SourcePyramidId, pass.Texture);
                context.cmd.SetComputeTextureParam(pass.Shader, pass.Kernel, DestinationId, pass.Texture, pass.Mip);
                context.cmd.SetComputeIntParam(pass.Shader, SourceMipId, pass.Mip - 1);
                context.cmd.SetComputeVectorParam(pass.Shader, DestinationSizeId, new Vector4(pass.Width, pass.Height, 0, 0));
                context.cmd.SetComputeVectorParam(pass.Shader, DepthParamsId, new Vector4(0, pass.FarPlane, 0, 0));
                context.cmd.DispatchCompute(pass.Shader, pass.Kernel, (pass.Width + 7) / 8, (pass.Height + 7) / 8, 1);
            });
        }

        private static readonly int SourceDepthId = Shader.PropertyToID("_SourceDepth");
        private static readonly int SourcePyramidId = Shader.PropertyToID("_SourcePyramid");
        private static readonly int DestinationId = Shader.PropertyToID("_Destination");
        private static readonly int DestinationSizeId = Shader.PropertyToID("_DestinationSize");
        private static readonly int SourceUvScaleBiasId = Shader.PropertyToID("_SourceUvScaleBias");
        private static readonly int SourceZBufferParamsId = Shader.PropertyToID("_SourceZBufferParams");
        private static readonly int DepthParamsId = Shader.PropertyToID("_DepthParams");
        private static readonly int SourceMipId = Shader.PropertyToID("_SourceMip");
        private static readonly int SourceIsLinearId = Shader.PropertyToID("_SourceIsLinear");
    }
}
