// ════════════════════════════════════════════════════════════════
//  GrassRenderFeature — 把草的间接绘制搬进 URP 的 pass 序列
//
//  草不是场景里的 Renderer，进不了 CullResults；旧做法用
//  Graphics.DrawMeshInstancedIndirect + beginContextRendering 登记，
//  这些绘制只在 RenderSingleCamera 末尾的 context.Submit() 落地，
//  跑在整条 pass 序列之外 —— 既不进深度 / 法线 prepass，也不进后处理。
//  这里改用 ScriptableRendererFeature 在管线内直接下发：
//    AfterRenderingPrePasses — 写 _CameraDepthTexture / _CameraNormalsTexture
//    AfterRenderingOpaques   — 写相机颜色，早于天空盒与 SSGI
// ════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Mine.Grass
{
    /// <summary>一次 draw-call 所需的全部引用；由 <see cref="GrassChunkManager"/> 每帧登记。</summary>
    public struct GrassDrawRequest
    {
        public Mesh Mesh;
        public Material Material;
        public ComputeBuffer Arguments;
        public MaterialPropertyBlock Properties;
        public int Layer;
    }

    /// <summary>
    /// 一帧内的草绘制请求。渲染前由区块管理器整体重建，渲染时由 <see cref="GrassRenderFeature"/> 消费。
    /// </summary>
    public static class GrassDrawRequests
    {
        private static readonly List<GrassDrawRequest> RequestsInternal = new List<GrassDrawRequest>();

        public static IReadOnlyList<GrassDrawRequest> Requests => RequestsInternal;

        public static void Clear() => RequestsInternal.Clear();

        public static void Add(Mesh mesh, Material material, ComputeBuffer arguments,
            MaterialPropertyBlock properties, int layer)
        {
            RequestsInternal.Add(new GrassDrawRequest
            {
                Mesh = mesh,
                Material = material,
                Arguments = arguments,
                Properties = properties,
                Layer = layer,
            });
        }
    }

    /// <summary>
    /// 在管线内绘制 <see cref="GrassDrawRequests"/> 登记的草，使其拥有正常的深度、法线与后处理。
    /// </summary>
    public sealed class GrassRenderFeature : ScriptableRendererFeature
    {
        private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int CameraNormalsTextureId = Shader.PropertyToID("_CameraNormalsTexture");

        private GrassDepthNormalsPass _prepass;
        private GrassOpaquePass _opaque;

        public override void Create()
        {
            _prepass = new GrassDepthNormalsPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses,
            };
            _opaque = new GrassOpaquePass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraData cameraData = renderingData.cameraData;
            if (cameraData.renderType == CameraRenderType.Overlay)
                return;
            if (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView)
                return;
            if (GrassDrawRequests.Requests.Count == 0)
                return;

            renderer.EnqueuePass(_prepass);
            renderer.EnqueuePass(_opaque);
        }

        protected override void Dispose(bool disposing)
        {
            _prepass = null;
            _opaque = null;
        }

        // ════════════════════════════════════════════════════════════
        //  共用 — 按 pass 名解析出可下发的绘制批次
        // ════════════════════════════════════════════════════════════
        private sealed class DrawBatch
        {
            public Mesh Mesh;
            public Material Material;
            public int PassIndex;
            public ComputeBuffer Arguments;
            public MaterialPropertyBlock Properties;
        }

        private sealed class PassData
        {
            public DrawBatch[] Batches;
        }

        private static readonly HashSet<string> WarnedPasses = new HashSet<string>();

        private static DrawBatch[] Gather(int cullingMask, string passName)
        {
            IReadOnlyList<GrassDrawRequest> requests = GrassDrawRequests.Requests;
            List<DrawBatch> batches = null;
            for (int i = 0; i < requests.Count; i++)
            {
                GrassDrawRequest request = requests[i];
                if (request.Mesh == null || request.Material == null || request.Arguments == null ||
                    request.Properties == null)
                    continue;
                if (request.Layer < 0 || request.Layer > 31 ||
                    (cullingMask & (1 << request.Layer)) == 0)
                    continue;
                int passIndex = request.Material.FindPass(passName);
                if (passIndex < 0)
                {
                    if (WarnedPasses.Add(passName))
                        Debug.LogWarning($"[GrassRenderFeature] Grass material has no '{passName}' pass; " +
                                         "grass stays out of that stage.");
                    continue;
                }
                batches ??= new List<DrawBatch>(requests.Count);
                batches.Add(new DrawBatch
                {
                    Mesh = request.Mesh,
                    Material = request.Material,
                    PassIndex = passIndex,
                    Arguments = request.Arguments,
                    Properties = request.Properties,
                });
            }
            return batches?.ToArray();
        }

        private static void Draw(PassData data, RasterGraphContext context)
        {
            DrawBatch[] batches = data.Batches;
            for (int i = 0; i < batches.Length; i++)
            {
                DrawBatch batch = batches[i];
                context.cmd.DrawMeshInstancedIndirect(batch.Mesh, 0, batch.Material, batch.PassIndex,
                    batch.Arguments, 0, batch.Properties);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  深度 / 法线 prepass — 接在 URP 自己的 prepass 之后，
        //  让草与地形一样进入 _CameraDepthTexture / _CameraNormalsTexture
        // ════════════════════════════════════════════════════════════
        private sealed class GrassDepthNormalsPass : ScriptableRenderPass
        {
            public GrassDepthNormalsPass()
            {
                profilingSampler = new ProfilingSampler("Grass Depth Normals");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (!resources.cameraDepthTexture.IsValid())
                    return;

                // 没有法线需求（例如 SSGI 关闭）时 URP 只跑 DepthOnly prepass，这里跟着降级。
                bool withNormals = resources.cameraNormalsTexture.IsValid();
                DrawBatch[] batches = Gather(cameraData.camera.cullingMask,
                    withNormals ? "DepthNormals" : "DepthOnly");
                if (batches == null)
                    return;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Grass Depth Normals",
                    out PassData passData, profilingSampler))
                {
                    passData.Batches = batches;
                    if (withNormals)
                        builder.SetRenderAttachment(resources.cameraNormalsTexture, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.cameraDepthTexture, AccessFlags.ReadWrite);
                    builder.SetGlobalTextureAfterPass(resources.cameraDepthTexture, CameraDepthTextureId);
                    if (withNormals)
                        builder.SetGlobalTextureAfterPass(resources.cameraNormalsTexture, CameraNormalsTextureId);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) => Draw(data, context));
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  不透明颜色 — 在不透明物体之后、天空盒与 SSGI 之前写入
        // ════════════════════════════════════════════════════════════
        private sealed class GrassOpaquePass : ScriptableRenderPass
        {
            public GrassOpaquePass()
            {
                profilingSampler = new ProfilingSampler("Grass Opaque");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (!resources.activeColorTexture.IsValid())
                    return;

                DrawBatch[] batches = Gather(cameraData.camera.cullingMask, "Forward");
                if (batches == null)
                    return;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Grass Opaque",
                    out PassData passData, profilingSampler))
                {
                    passData.Batches = batches;
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                    if (resources.activeDepthTexture.IsValid())
                        builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) => Draw(data, context));
                }
            }
        }

        // 草不参与阴影投射。
        //
        // 这里曾经有一个 GrassShadowPass：在 AfterRenderingShadows 用
        // DrawMeshInstancedIndirect 往 URP 的主光阴影图里追加草几何，切片用
        // ShadowUtils.ExtractDirectionalLightMatrix 按 URP 内部同样的参数复算。
        // 它确实把草画进了阴影图，但始终无法与接收端对齐——Game 视图里会稳定出现
        // 一大片固定在相机视角上的错位阴影。级联数、阴影距离、投射偏移、接收淡出
        // 都只能改变它的形状和浓度，不能让它消失，说明错位发生在写入端而不是参数。
        // 追查成本已经超过收益，2026-11-12 决定放弃草的投射，只保留接收。
        //
        // 草仍然接收主光阴影：见 GrassWind.shader 的 MainLightShadow，不做额外的距离混合。
    }
}
