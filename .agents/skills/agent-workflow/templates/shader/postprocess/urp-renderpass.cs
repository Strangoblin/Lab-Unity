// ═══════════════════════════════════════════════════════════════
//  Unity 6 URP RenderGraph RenderPass 模板
//
//  基于 Unity 官方 ScriptableRenderPass + RenderGraph API
//  参考: com.unity.render-pipelines.universal/Runtime/Passes/
//
//  使用: 复制 → 全局替换 YourEffect → 按 ⚠️ 处定制 → 在 Feature.AddRenderPasses() 注册
// ═══════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// ⚠️ 类名与下面的 k_PassName 都是占位符（YourEffect），拷贝后全局替换
public class YourEffectPass : ScriptableRenderPass
{
    const string k_PassName = "YourEffect";
    readonly Settings m_Settings;
    Material m_Material;

    // ═══ PassData — RenderGraph 要求独立的 class ═══
    class PassData
    {
        public Material material;
        public TextureHandle source;
        // ⚠️ public TextureHandle extraTex;
    }

    public YourEffectPass(Settings settings)
    {
        m_Settings = settings;
        m_Material = CoreUtils.CreateEngineMaterial(settings.shader);
        // 默认值只作构造回退；实际事件每帧由 AddRenderPasses 按 settings.passEvent 覆盖
        renderPassEvent = settings.passEvent;
        // ⚠️ 可选: AfterRenderingOpaques, AfterRenderingSkybox,
        //         AfterRenderingPostProcessing, BeforeRenderingPostProcessing
    }

    // ═══ Unity 6 主入口 — RenderGraph（不用旧版 Execute）═══
    public override void RecordRenderGraph(RenderGraph renderGraph,
                                           ContextContainer frameData)
    {
        // 1. 获取 camera color
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        TextureHandle cameraColor = resourceData.activeColorTexture;

        // 2. 单 Pass 不需要描述符 —— 直接以 cameraColor 为附件即可。
        //    （需要临时 RT 时才建描述符，见文末多 pass 模板）

        // 3. 添加 raster pass
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                   k_PassName, out var passData))
        {
            passData.material = m_Material;
            passData.source = cameraColor;

            // 声明纹理使用
            builder.UseTexture(cameraColor, AccessFlags.Read);
            builder.SetRenderAttachment(cameraColor, 0, AccessFlags.Write);
            // ⚠️ 如需额外纹理: builder.UseTexture(extraTex, AccessFlags.Read);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                // ⚠️ pass index 对应 shader 中的 Pass 顺序
                Blitter.BlitTexture(ctx.cmd, data.source,
                    Vector2.one, data.material, 0);
            });
        }
    }

    // ═══ 多 pass 模板（需创建临时 RT）═══
    // public override void RecordRenderGraph(RenderGraph renderGraph,
    //                                        ContextContainer frameData)
    // {
    //     UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
    //     TextureHandle cameraColor = resourceData.activeColorTexture;
    //
    //     // 临时 RT：从 cameraColor 取描述符，去掉深度后建图内纹理
    //     TextureDesc desc = renderGraph.GetTextureDesc(cameraColor);
    //     desc.name = "YourEffectTemp";
    //     desc.clearBuffer = false;
    //     desc.depthBufferBits = DepthBits.None;
    //     TextureHandle tempRT = renderGraph.CreateTexture(desc);
    //
    //     // Pass 0: cameraColor → tempRT
    //     using (var builder = renderGraph.AddRasterRenderPass<PassData>(...))
    //     {
    //         ...builder.UseTexture(cameraColor, AccessFlags.Read);
    //         ...builder.SetRenderAttachment(tempRT, 0, AccessFlags.Write);
    //         ...Blitter.BlitTexture(ctx.cmd, source, Vector2.one, material, 0);
    //     }
    //
    //     // Pass 1: tempRT → cameraColor
    //     using (var builder = renderGraph.AddRasterRenderPass<PassData>(...))
    //     {
    //         ...builder.UseTexture(tempRT, AccessFlags.Read);
    //         ...builder.SetRenderAttachment(cameraColor, 0, AccessFlags.Write);
    //         ...Blitter.BlitTexture(ctx.cmd, source, Vector2.one, material, 1);
    //     }
    // }
}

// ═══ Feature 侧 — Settings 与入队（含 Events）═══
// public class YourEffectFeature : ScriptableRendererFeature
// {
//     [System.Serializable]
//     public class Settings
//     {
//         [Header("Resources")]
//         public Shader shader;
//
//         // ... 参数按 Technical → Performance → Artistic 分组 ...
//
//         [Header("Event")]
//         [Tooltip("该 Pass 在帧内的位置；按它读写哪些纹理来决定。")]
//         public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingTransparents;
//
//         [Header("Debug")]
//         public bool debug;
//     }
//
//     public Settings settings = new Settings();
//     YourEffectPass m_Pass;
//
//     public override void Create() => m_Pass = settings.shader != null
//         ? new YourEffectPass(settings) : null;
//
//     public override void AddRenderPasses(ScriptableRenderer renderer,
//                                          ref RenderingData renderingData)
//     {
//         if (m_Pass == null) return;
//         // 运行时改面板立即生效：事件按帧覆盖，而不是只在构造时读一次
//         m_Pass.renderPassEvent = settings.passEvent;
//         renderer.EnqueuePass(m_Pass);
//     }
// }
