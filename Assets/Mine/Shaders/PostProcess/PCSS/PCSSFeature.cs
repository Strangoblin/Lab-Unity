using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// PCSS 软阴影渲染管线：PSSM 级联 Atlas（级联数随性能档）+ Blocker Search + Penumbra 估算
/// + 变核 PCF + 双边模糊 + 时域累积。
/// 在 URP Renderer 的 Renderer Features 中添加，配合 PCSSTemplate.shader 使用。
/// </summary>
///
/// <remarks>
/// 时域累积（重投影/clamp/混合）由 Assets/Mine/Special/HLSL/TemporalFunction.hlsl 提供，
/// 本脚本负责每相机历史 RT 的 ping-pong、上一帧 VP 矩阵与失效判定。
/// 仅相机重投影（深度 + 上一帧 VP），不依赖 ScriptableRenderPassInput.Motion。
///
/// 参数分三层：
/// - Technical：几何、投影与深度偏移，为正确性（漏光 / 痤疮 / 覆盖范围）而调
/// - Performance：纯成本旋钮，不需要微调，划档位即可（一档同时决定 Atlas 分辨率、
///   采样数、级联数与覆盖距离）
/// - Artistic：每个参数对应一个观感维度
/// 原时域钳制、深度/法线置信度、PCF 偏移乘数等实现细节已内化为 PCSSFunction.hlsl 的常量。
///
/// Atlas 分辨率必须同时下发给 shader（`_PCSS_AtlasParams`）：C# 侧用它分配 RT、烘焙
/// 级联投影、设 viewport，shader 侧用它做全部 texel↔UV 换算。两边少一边，非默认档下
/// 阴影就会整体错位——而默认档下数值恰好正确，所以这类「参数半接线」不会被察觉。
/// </remarks>
public class PCSSFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        // ════════════════════ 技术性 ════════════════════
        // 调这些是为了正确性（漏光 / 痤疮 / 覆盖范围），不是为了观感，也不是为了帧率。

        [Header("Technical · Resources")]
        public ComputeShader pcssComputeShader;

        [Header("Technical · Bias & Split")]
        [Tooltip("PSSM 混合因子：越接近 1 越按对数切分（近处 texel 更密），越接近 0 越均匀")]
        [Range(0f, 1f)] public float pssmLambda = 0.75f;
        [Range(0f, 2f)] public float depthBias  = 0.1f;
        [Range(0f, 2f)] public float normalBias = 0.0f;

        // ════════════════════ 性能 ════════════════════
        // 纯成本旋钮：对帧率影响大、不需要微调，划档位即可。
        // 一档同时决定 Atlas 分辨率、采样数、级联数与阴影覆盖距离。
        // cascadeCount 上限为 4（HLSL 的 _CascadeLightVP[4]），档位表天然满足。

        [Header("Performance")]
        [Tooltip("Low    1024 /  8 采样 / 2 级联 / 30m\n" +
                 "Medium 2048 / 16 采样 / 3 级联 / 40m\n" +
                 "High   2048 / 32 采样 / 4 级联 / 50m")]
        public Performance performance = Performance.High;

        public enum Performance { Low, Medium, High }

        /// <summary>采样档。同时是 PCSS_LOW / PCSS_MEDIUM 关键词的依据（两者都未定义 = High）。</summary>
        public enum Quality { Low, Medium, High }

        /// <summary>
        /// 性能档展开结果。2×2 Atlas 布局固定，故 tileRes = atlasRes / 2。
        /// 两个 pass 各自展开同一个纯函数，因此不存在跨 pass 状态不同步的可能。
        /// </summary>
        public readonly struct PerformanceTier
        {
            public readonly int     atlasRes;
            public readonly int     tileRes;
            public readonly int     cascadeCount;
            public readonly float   shadowDistance;
            public readonly Quality samples;

            public PerformanceTier(int atlasRes, int cascadeCount, float shadowDistance, Quality samples)
            {
                this.atlasRes       = atlasRes;
                this.tileRes        = atlasRes / 2;
                this.cascadeCount   = cascadeCount;
                this.shadowDistance = shadowDistance;
                this.samples        = samples;
            }
        }

        // 档位表 —— High 即历史默认配置（2048 / 32 采样 / 4 级联 / 50m），故默认档画面不变。
        static readonly PerformanceTier[] k_Tiers =
        {
            new PerformanceTier(1024, 2, 30f, Quality.Low),
            new PerformanceTier(2048, 3, 40f, Quality.Medium),
            new PerformanceTier(2048, 4, 50f, Quality.High),
        };

        public static PerformanceTier GetTier(Performance p) => k_Tiers[(int)p];

        // ════════════════════ 美术性 ════════════════════
        // 一个参数控制一个观感维度。

        [Header("Artistic")]
        [Tooltip("半影尺度：0 = 硬边，越大阴影越软")]
        [Range(0f, 1f)] public float softness = 1.0f;

        [Tooltip("双边保边模糊强度：0 = 关闭")]
        [Range(0f, 5f)] public float blur = 1.0f;

        [Tooltip("时域收敛：0 = 关闭（逐帧噪点），越高越稳但几何变化响应越慢")]
        [Range(0f, 1f)] public float temporal = 0.9f;

        [Header("Debug")]
        public bool showShadowMap = true;
        public TemporalDebug temporalDebug = TemporalDebug.Off;
        public enum TemporalDebug { Off, Reprojection, Confidence, HistoryUV }

        internal static readonly int ShadowCacheTexID = Shader.PropertyToID("_PCSS_ShadowCacheTex");
        internal static readonly int LightDirectionID  = Shader.PropertyToID("_LightDirection");
        internal static readonly int DepthBiasID       = Shader.PropertyToID("_ShadowDepthBias");
        internal static readonly int NormalBiasID      = Shader.PropertyToID("_ShadowNormalBias");

        // SetCompute{Float,Int,Matrix}Param 无 (shader, kernel, string, value) 重载，只能走 nameID
        internal static readonly int FrameIndexID    = Shader.PropertyToID("_FrameIndex");
        internal static readonly int PrevViewProjID  = Shader.PropertyToID("_PrevViewProj");
        internal static readonly int TemporalBlendID = Shader.PropertyToID("_TemporalBlend");
        internal static readonly int TemporalResetID = Shader.PropertyToID("_TemporalReset");
        internal static readonly int TemporalDebugID = Shader.PropertyToID("_TemporalDebug");
    }

    // ════════════════════════════════════════════════════════════
    //  CustomShadowCasterPass — 级联 Atlas 渲染
    // ════════════════════════════════════════════════════════════
    class CustomShadowCasterPass : ScriptableRenderPass
    {
        Settings m_S;

        RenderTexture m_ShadowRT;
        RTHandle      m_ShadowHandle;
        int           m_AtlasRes, m_TileRes;

        // 级联数据（供 PCSSPass 读取）
        public Matrix4x4[] CascadeViewProj;
        public Vector4     CascadeSplits;
        public Vector4     CascadeHalfWidths;
        public Vector4     CascadeZDistances;
        public Vector4[]   CascadeOffsets;
        public int         CascadeCount;
        public RTHandle    shadowHandle => m_ShadowHandle;
        public RenderTexture shadowRT => m_ShadowRT;

        class RasterPassData
        {
            public Matrix4x4[] cascadeView;
            public Matrix4x4[] cascadeProj;
            public Matrix4x4   camView, camProj;
            public RendererListHandle[] rendererLists;
            public int tileRes;
        }

        public CustomShadowCasterPass(Settings s)
        {
            m_S = s;
            renderPassEvent = RenderPassEvent.AfterRenderingShadows;
            profilingSampler = new ProfilingSampler("PCSS Caster");
            CascadeViewProj = new Matrix4x4[8];
            CascadeOffsets  = new Vector4[8];
        }

        void EnsureRT(int atlasRes, int tileRes)
        {
            if (m_ShadowRT != null && m_AtlasRes == atlasRes && m_TileRes == tileRes)
                return;

            m_ShadowRT?.Release();
            m_ShadowHandle?.Release();

            m_ShadowRT = new RenderTexture(atlasRes, atlasRes, 0, RenderTextureFormat.RFloat);
            m_ShadowRT.filterMode = FilterMode.Point;
            m_ShadowRT.wrapMode = TextureWrapMode.Clamp;
            m_ShadowRT.Create();
            m_ShadowHandle = RTHandles.Alloc(m_ShadowRT);
            m_AtlasRes = atlasRes;
            m_TileRes  = tileRes;
        }

        // ════════════════════════════════════════════════════════════
        //  球体包围盒 + 光源正交投影（接受级联 near/far）
        // ════════════════════════════════════════════════════════════
        void ComputeShadowProjection(
            Camera cam, Light light, float cascadeNear, float cascadeFar,
            int tileRes, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out Vector3 camPos)
        {
            Transform t = cam.transform;
            Vector3 fwd = t.forward;
            float fov = cam.fieldOfView * Mathf.Deg2Rad;
            float aspect = cam.aspect;

            // ── 固定包围盒半径（Unity 做法：由 cascade 距离 + FOV 决定，不依赖当帧 frustum 角点）──
            // 旋转时 frustum 角点变化 → halfW/H 变化 → 投影 scale 飘移 → 抖动。
            // 改用 cascadeFar 处的视锥对角线作为固定半径，仅在穿越 split/FOV 变更时才变。
            float halfFovV = Mathf.Tan(fov * 0.5f);
            float halfFovH = halfFovV * aspect;
            float frustumDiag = cascadeFar * Mathf.Sqrt(halfFovV * halfFovV + halfFovH * halfFovH);
            float halfExtent = frustumDiag + 1f;

            // ── 角点仍用于确定光空间中心和深度范围 ──
            float camNear = cam.nearClipPlane;
            float nearH = halfFovV * camNear;
            float nearW = nearH * aspect;
            Vector3 rgt = t.right, up = t.up;
            Vector3 dBL = fwd * camNear + rgt * (-nearW) + up * (-nearH);
            Vector3 dTR = fwd * camNear + rgt * ( nearW) + up * ( nearH);
            Vector3 dBR = fwd * camNear + rgt * ( nearW) + up * (-nearH);
            Vector3 dTL = fwd * camNear + rgt * (-nearW) + up * ( nearH);
            float sN = cascadeNear / camNear, sF = cascadeFar / camNear;
            Vector3[] corners = {
                t.position + dBL * sN, t.position + dTR * sN,
                t.position + dBR * sN, t.position + dTL * sN,
                t.position + dBL * sF, t.position + dTR * sF,
                t.position + dBR * sF, t.position + dTL * sF
            };

            Vector3 lightDir = light.transform.forward;
            Vector3 lightRgt = Vector3.Cross(lightDir, Vector3.up).normalized;
            if (lightRgt.sqrMagnitude < 0.001f)
                lightRgt = Vector3.Cross(lightDir, Vector3.forward).normalized;
            Vector3 lightUp = Vector3.Cross(lightRgt, lightDir).normalized;

            float camR = Vector3.Dot(t.position, lightRgt);
            float camU = Vector3.Dot(t.position, lightUp);
            float minD = float.MaxValue, maxD = float.MinValue;
            foreach (var c in corners)
            {
                float d = Vector3.Dot(c, lightDir);
                minD = Mathf.Min(minD, d); maxD = Mathf.Max(maxD, d);
            }

            float midD     = (minD + maxD) * 0.5f;  // frustom 几何中心（含所有可见物体）
            float backDist = cascadeFar * 8f;
            float zNear    = 0.1f;
            float zFar     = backDist * 2f;
            float halfW    = halfExtent;
            float halfH    = halfExtent;
            // ── 量化整个正交投影边界（texel-aligned ortho bounds）──
            // Floor 左边界、Ceil 右边界 → 向外扩 → 确保原范围被包含。
            Vector3 vX = -lightRgt, vY = lightUp, vZ = -lightDir;
            float worldPerTexel = halfW * 2f / tileRes;
            // 光空间 R 轴
            float bl = camR - halfW, br = camR + halfW;
            bl = Mathf.Floor(bl / worldPerTexel) * worldPerTexel;
            br = Mathf.Ceil (br / worldPerTexel) * worldPerTexel;
            float snapR = (bl + br) * 0.5f, snapHW = Mathf.Max((br - bl) * 0.5f, halfW);
            // 光空间 U 轴
            float bb = camU - halfH, bt = camU + halfH;
            bb = Mathf.Floor(bb / worldPerTexel) * worldPerTexel;
            bt = Mathf.Ceil (bt / worldPerTexel) * worldPerTexel;
            float snapU = (bb + bt) * 0.5f, snapHH = Mathf.Max((bt - bb) * 0.5f, halfH);
            Vector3 center = lightRgt * snapR + lightUp * snapU + lightDir * midD;

            Vector3 shadowCamPos = center - lightDir * backDist;
            camPos = shadowCamPos;
            viewMatrix = new Matrix4x4(
                new Vector4(vX.x, vY.x, vZ.x, 0),
                new Vector4(vX.y, vY.y, vZ.y, 0),
                new Vector4(vX.z, vY.z, vZ.z, 0),
                new Vector4(-Vector3.Dot(vX, shadowCamPos), -Vector3.Dot(vY, shadowCamPos), -Vector3.Dot(vZ, shadowCamPos), 1));
            projMatrix = Matrix4x4.Ortho(-snapHW, snapHW, -snapHH, snapHH, zNear, zFar);
        }

        // ── PSSM Split ──
        float[] ComputePSSMSplits(float nearP, float farP, int count, float lambda)
        {
            var splits = new float[count];
            for (int i = 0; i < count; i++)
            {
                float p = (i + 1f) / count;
                float logSplit = nearP * Mathf.Pow(farP / nearP, p);
                float uniSplit = nearP + (farP - nearP) * p;
                splits[i] = lambda * logSplit + (1f - lambda) * uniSplit;
            }
            return splits;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            Light mainLight = RenderSettings.sun;
            if (mainLight == null) return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            Camera cam = cameraData.camera;

            Settings.PerformanceTier tier = Settings.GetTier(m_S.performance);
            int cascadeCount = tier.cascadeCount;
            int tileRes      = tier.tileRes;
            int atlasRes     = tier.atlasRes;
            EnsureRT(atlasRes, tileRes);

            float shadowDist = tier.shadowDistance;

            // ── PSSM split ──
            float[] splits = ComputePSSMSplits(cam.nearClipPlane, shadowDist, cascadeCount, m_S.pssmLambda);
            CascadeCount = cascadeCount;

            // ── 每级联计算投影，同时找最宽级联做 culling ──
            var cascadeView  = new Matrix4x4[cascadeCount];
            var cascadeProj  = new Matrix4x4[cascadeCount];
            var cascadeCamPos = new Vector3[cascadeCount];
            var cascadeHalfW  = new float[cascadeCount];
            var cascadeZDist  = new float[cascadeCount];
            for (int ci = 0; ci < cascadeCount; ci++)
            {
                float cn = ci == 0 ? cam.nearClipPlane : splits[ci - 1];
                float cf = splits[ci];

                ComputeShadowProjection(cam, mainLight, cn, cf, tileRes,
                    out cascadeView[ci], out cascadeProj[ci], out cascadeCamPos[ci]);
            }

            // ── 存储级联数据 ──
            // 参考 Unity ShadowUtils.GetShadowTransform：
            // 1) reversed-Z 平台反转投影矩阵 z 分量
            // 2) 把 [-1,1]→[0,1] 的 scale-bias 烘焙进矩阵 → shader 只需除 w
            var scaleBias = Matrix4x4.identity;
            scaleBias.m00 = 0.5f; scaleBias.m11 = 0.5f; scaleBias.m22 = 0.5f;
            scaleBias.m03 = 0.5f; scaleBias.m13 = 0.5f; scaleBias.m23 = 0.5f;

            CascadeViewProj = new Matrix4x4[cascadeCount];
            CascadeOffsets  = new Vector4[cascadeCount];
            for (int ci = 0; ci < cascadeCount; ci++)
            {
                var proj = cascadeProj[ci];
                if (SystemInfo.usesReversedZBuffer)
                {
                    proj.m20 = -proj.m20; proj.m21 = -proj.m21;
                    proj.m22 = -proj.m22; proj.m23 = -proj.m23;
                }
                CascadeViewProj[ci] = scaleBias * (proj * cascadeView[ci]);
                int col = ci % 2, row = ci / 2;
                CascadeOffsets[ci] = new Vector4(
                    col * 0.5f, row * 0.5f, 0.5f, 0f);
            }
            // halfW 用固定参照（参考文章 _CascadeShadowSplitSpheres[i].w）
            // zDist 从投影矩阵提取（与旋转基本无关）
            float fovHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float diagFactor = Mathf.Sqrt(fovHalf * fovHalf * (1f + cam.aspect * cam.aspect));
            for (int ci = 0; ci < cascadeCount; ci++)
            {
                cascadeHalfW[ci] = splits[ci] * diagFactor + 1f;
                cascadeZDist[ci] = -2f / cascadeProj[ci].m22;
            }
            CascadeHalfWidths = new Vector4(
                cascadeHalfW.Length > 0 ? cascadeHalfW[0] : 0,
                cascadeHalfW.Length > 1 ? cascadeHalfW[1] : 0,
                cascadeHalfW.Length > 2 ? cascadeHalfW[2] : 0,
                cascadeHalfW.Length > 3 ? cascadeHalfW[3] : 0);
            CascadeZDistances = new Vector4(
                cascadeZDist.Length > 0 ? cascadeZDist[0] : 0,
                cascadeZDist.Length > 1 ? cascadeZDist[1] : 0,
                cascadeZDist.Length > 2 ? cascadeZDist[2] : 0,
                cascadeZDist.Length > 3 ? cascadeZDist[3] : 0);

            CascadeSplits = new Vector4(
                cascadeCount > 0 ? splits[0] : 0f,
                cascadeCount > 1 ? splits[1] : 0f,
                cascadeCount > 2 ? splits[2] : 0f,
                cascadeCount > 3 ? splits[3] : 0f);

            // ── 设置阴影偏移（全局，两种模式通用）──
            Shader.SetGlobalVector(Settings.LightDirectionID, mainLight.transform.forward);
            Shader.SetGlobalFloat(Settings.DepthBiasID, m_S.depthBias);
            Shader.SetGlobalFloat(Settings.NormalBiasID, m_S.normalBias);

            TextureHandle shadowTH = graph.ImportTexture(m_ShadowHandle);

            RenderTextureDescriptor depthDesc = new RenderTextureDescriptor(
                atlasRes, atlasRes, RenderTextureFormat.Depth, 16, 0);
            TextureHandle depthTH = UniversalRenderer.CreateRenderGraphTexture(
                graph, depthDesc, "_PCSS_ShadowDepth", false);

            // ── 光源视角逐级联 Culling ──
            CullContextData cullCtx = frameData.Get<CullContextData>();
            var sorting = new SortingSettings(cam);
            // 原生 pass：物体 shader 需含 LightMode=CustomShadowCaster pass，SRP Batcher 有效
            var drawSettings = new DrawingSettings(new ShaderTagId("CustomShadowCaster"), sorting);
            var filterSettings = new FilteringSettings(RenderQueueRange.opaque);
            var rlList = new RendererListHandle[cascadeCount];
            for (int ci = 0; ci < cascadeCount; ci++)
            {
                cam.TryGetCullingParameters(false, out var cp);
                cp.cullingMatrix = cascadeProj[ci] * cascadeView[ci];
                cp.isOrthographic = true;
                cp.origin = cascadeCamPos[ci];
                var planes = GeometryUtility.CalculateFrustumPlanes(cp.cullingMatrix);
                int n = Mathf.Min(planes.Length, ScriptableCullingParameters.maximumCullingPlaneCount);
                for (int i = 0; i < n; i++) cp.SetCullingPlane(i, planes[i]);
                cp.cullingPlaneCount = n;
                var lightCull = cullCtx.Cull(ref cp);
                rlList[ci] = graph.CreateRendererList(new RendererListParams(lightCull, drawSettings, filterSettings));
            }

            Matrix4x4 camView = cam.worldToCameraMatrix;
            Matrix4x4 camProj = cam.projectionMatrix;

            using (var builder = graph.AddRasterRenderPass<RasterPassData>(
                "PCSS Cascade", out var pd, profilingSampler))
            {
                pd.cascadeView = cascadeView;
                pd.cascadeProj = cascadeProj;
                pd.camView     = camView;
                pd.camProj     = camProj;
                pd.rendererLists = rlList;
                pd.tileRes     = tileRes;

                builder.SetRenderAttachment(shadowTH, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depthTH, AccessFlags.Write);
                foreach (var rh in rlList)
                    builder.UseRendererList(rh);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetGlobalTextureAfterPass(shadowTH, Settings.ShadowCacheTexID);

                builder.SetRenderFunc((RasterPassData data, RasterGraphContext ctx) =>
                {
                    ctx.cmd.ClearRenderTarget(RTClearFlags.Color | RTClearFlags.Depth,
                        Color.black, 1f, 0);

                    int count = data.cascadeView.Length;
                    for (int ci = 0; ci < count; ci++)
                    {
                        int col = ci % 2, row = ci / 2;
                        int vpX = col * data.tileRes;
                        int vpY = row * data.tileRes;
                        ctx.cmd.SetViewport(new Rect(vpX, vpY, data.tileRes, data.tileRes));
                        ctx.cmd.SetViewProjectionMatrices(data.cascadeView[ci], data.cascadeProj[ci]);
                        ctx.cmd.DrawRendererList(data.rendererLists[ci]);
                    }

                    ctx.cmd.SetViewProjectionMatrices(data.camView, data.camProj);
                });
            }
        }

        public void Release()
        {
            m_ShadowHandle?.Release();
            m_ShadowRT?.Release();
            m_ShadowRT = null; m_ShadowHandle = null;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  PCSSPass — 屏幕空间阴影比较 + 时域累积
    // ════════════════════════════════════════════════════════════
    class PCSSPass : ScriptableRenderPass
    {
        // 光源转向超过此角度时阴影整体变化，历史失效
        const float k_LightResetAngle = 5f;
        // 历史帧索引取模，避免 float 尾数长时间运行后相位冻结
        const int   k_FrameIndexModulo = 1024;
        // 未刷新超过此帧数的相机历史予以回收
        const int   k_HistoryStaleFrames = 120;

        Settings m_S;
        ComputeShader m_CS;
        int m_CSKernel, m_CSTemporalKernel, m_CSBlurHKernel, m_CSBlurVKernel;
        CustomShadowCasterPass m_Caster;

        RenderTexture m_SoftShadowRT;
        RTHandle      m_SoftShadowHandle;
        RenderTexture m_BlurTempRT;
        RTHandle      m_BlurTempHandle;
        RenderTexture m_TemporalRT;
        RTHandle      m_TemporalHandle;
        int           m_RTWidth, m_RTHeight;

        // ── 每相机历史状态（GameView / SceneView 必须隔离，否则互相污染）──
        class CameraHistory
        {
            public RenderTexture rtA, rtB;
            public RTHandle      handleA, handleB;
            public int width, height;
            public int frameIndex;
            public Matrix4x4 prevViewProj;
            public bool hasPrevViewProj;
            public Vector3 prevLightDir;
            public bool hasPrevLightDir;
            public bool reset;
            public int lastUseFrame;

            public RenderTexture readRT      => frameIndex % 2 == 0 ? rtA : rtB;
            public RenderTexture writeRT     => frameIndex % 2 == 0 ? rtB : rtA;
            public RTHandle      readHandle  => frameIndex % 2 == 0 ? handleA : handleB;
            public RTHandle      writeHandle => frameIndex % 2 == 0 ? handleB : handleA;
        }

        readonly Dictionary<int, CameraHistory> m_Histories = new Dictionary<int, CameraHistory>();
        int m_HistoryClock;

        class PassData
        {
            public ComputeShader cs;
            public int csKernel, csTemporalKernel, csBlurHKernel, csBlurVKernel;
            public RenderTexture softShadowRT, blurTempRT, temporalRT, shadowCacheRT;
            public RenderTexture historyReadRT, historyWriteRT;
            public RenderTexture blurSourceRT;
            public TextureHandle source, softShadowTH, temporalTH;
            public TextureHandle historyReadTH, historyWriteTH;
            public TextureHandle finalTH;
            public bool useTemporal;
            public Matrix4x4[] cascadeVP;
            public Vector4 splits;
            public Vector4[] cascadeOffsets;
            public int cascadeCount;
            public Vector4 cascadeHalfWidths;
            public Vector4 cascadeZDistances;
            public Settings.Quality quality;
            public float softness;
            public Vector4 lightDirection;
            public Vector4 screenSize;
            public Vector4 atlasParams;
            public Vector4 frustumRay0, frustumRay1, frustumRay2, frustumRay3;
            public Vector4 zBufferParams;
            public Vector4 worldSpaceCameraPos;
            public float blur;
            public bool showShadowMap;
            public Matrix4x4 prevViewProj;
            public float frameIndex, temporalBlend, temporalReset;
            public int temporalDebug;
        }

        public PCSSPass(Settings s, ComputeShader cs, CustomShadowCasterPass caster)
        {
            m_S = s; m_CS = cs; m_Caster = caster;
            if (m_CS != null)
            {
                m_CSKernel         = m_CS.FindKernel("PCSS_Main");
                m_CSTemporalKernel = m_CS.FindKernel("PCSS_Temporal");
                m_CSBlurHKernel    = m_CS.FindKernel("PCSS_BlurH");
                m_CSBlurVKernel    = m_CS.FindKernel("PCSS_BlurV");
            }
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            profilingSampler = new ProfilingSampler("PCSS Screen Shadow");
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        // ════════════════════════════════════════════════════════════
        //  RT 生命周期 — 屏幕尺寸相关（跨相机共享，单帧内用完即弃）
        // ════════════════════════════════════════════════════════════

        static RenderTexture NewScreenRT(int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf);
            rt.enableRandomWrite = true;
            rt.filterMode = FilterMode.Point;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.Create();
            return rt;
        }

        void EnsureRTs(int width, int height)
        {
            if (m_SoftShadowRT != null && m_RTWidth == width && m_RTHeight == height)
                return;

            m_SoftShadowRT?.Release(); m_SoftShadowHandle?.Release();
            m_BlurTempRT?.Release();   m_BlurTempHandle?.Release();
            m_TemporalRT?.Release();   m_TemporalHandle?.Release();

            m_SoftShadowRT = NewScreenRT(width, height);
            m_SoftShadowHandle = RTHandles.Alloc(m_SoftShadowRT);

            m_BlurTempRT = NewScreenRT(width, height);
            m_BlurTempHandle = RTHandles.Alloc(m_BlurTempRT);

            m_TemporalRT = NewScreenRT(width, height);
            m_TemporalHandle = RTHandles.Alloc(m_TemporalRT);

            m_RTWidth = width; m_RTHeight = height;
        }

        // ════════════════════════════════════════════════════════════
        //  每相机历史 RT — 尺寸随相机目标变化时重建并重置
        // ════════════════════════════════════════════════════════════

        static void ReleaseHistory(CameraHistory h)
        {
            h.handleA?.Release(); h.handleB?.Release();
            h.rtA?.Release();     h.rtB?.Release();
            h.handleA = null; h.handleB = null; h.rtA = null; h.rtB = null;
        }

        CameraHistory EnsureHistory(int cameraId, int width, int height)
        {
            if (!m_Histories.TryGetValue(cameraId, out var h))
            {
                h = new CameraHistory();
                m_Histories[cameraId] = h;
            }

            if (h.handleA == null || h.width != width || h.height != height)
            {
                ReleaseHistory(h);
                h.rtA = NewScreenRT(width, height);
                h.rtB = NewScreenRT(width, height);
                h.handleA = RTHandles.Alloc(h.rtA);
                h.handleB = RTHandles.Alloc(h.rtB);
                h.width = width; h.height = height;
                h.frameIndex = 0;
                h.hasPrevViewProj = false;
                h.hasPrevLightDir = false;
                h.reset = true;
            }
            return h;
        }

        /// 回收长期未刷新的相机历史（Scene View 重建等），避免 RT 泄漏
        void PruneStaleHistories()
        {
            if (m_Histories.Count <= 1) return;

            List<int> stale = null;
            foreach (var kv in m_Histories)
            {
                if (m_HistoryClock - kv.Value.lastUseFrame <= k_HistoryStaleFrames) continue;
                stale ??= new List<int>();
                stale.Add(kv.Key);
            }
            if (stale == null) return;

            foreach (int id in stale)
            {
                ReleaseHistory(m_Histories[id]);
                m_Histories.Remove(id);
            }
        }

        /// 用 ViewportToWorldPoint 预计算 4 条远平面角射线，避免 compute shader 传矩阵
        static void ComputeFrustumRays(Camera cam, out Vector4 r0, out Vector4 r1, out Vector4 r2, out Vector4 r3)
        {
            Vector3 p = cam.transform.position;
            float far = cam.farClipPlane;
            r0 = cam.ViewportToWorldPoint(new Vector3(0, 0, far)) - p;
            r1 = cam.ViewportToWorldPoint(new Vector3(1, 0, far)) - p;
            r2 = cam.ViewportToWorldPoint(new Vector3(0, 1, far)) - p;
            r3 = cam.ViewportToWorldPoint(new Vector3(1, 1, far)) - p;
        }

        public void Release()
        {
            m_SoftShadowHandle?.Release(); m_SoftShadowRT?.Release();
            m_BlurTempHandle?.Release();   m_BlurTempRT?.Release();
            m_TemporalHandle?.Release();   m_TemporalRT?.Release();
            m_SoftShadowRT = null; m_SoftShadowHandle = null;
            m_BlurTempRT = null;   m_BlurTempHandle = null;
            m_TemporalRT = null;   m_TemporalHandle = null;

            foreach (var kv in m_Histories) ReleaseHistory(kv.Value);
            m_Histories.Clear();
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            if (m_CS == null) return;

            Light sun = RenderSettings.sun;
            if (sun == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData   cameraData   = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid()) return;

            int width  = cameraData.cameraTargetDescriptor.width;
            int height = cameraData.cameraTargetDescriptor.height;
            EnsureRTs(width, height);

            m_HistoryClock++;
            CameraHistory hist = EnsureHistory(cameraData.camera.GetInstanceID(), width, height);
            hist.lastUseFrame = m_HistoryClock;
            PruneStaleHistories();

            // ── 光源转动 → shadow map 整体变化，历史失效 ──
            Vector3 lightDir = sun.transform.forward;
            if (hist.hasPrevLightDir && Vector3.Angle(hist.prevLightDir, lightDir) > k_LightResetAngle)
                hist.reset = true;
            hist.prevLightDir = lightDir;
            hist.hasPrevLightDir = true;

            // 上一帧 VP（Unity/GL 约定，非线性化矩阵），与 FrustumRays 同源
            Matrix4x4 curViewProj = cameraData.camera.projectionMatrix
                                  * cameraData.camera.worldToCameraMatrix;

            bool useTemporal = m_CSTemporalKernel >= 0;
            bool useBlur     = m_S.blur > 0f;

            TextureHandle softShadowTH = graph.ImportTexture(m_SoftShadowHandle);
            TextureHandle blurTempTH   = graph.ImportTexture(m_BlurTempHandle);
            TextureHandle temporalTH   = graph.ImportTexture(m_TemporalHandle);
            TextureHandle shadowTH     = graph.ImportTexture(m_Caster.shadowHandle);

            TextureHandle historyReadTH  = graph.ImportTexture(hist.readHandle);
            TextureHandle historyWriteTH = graph.ImportTexture(hist.writeHandle);

            // 时域关闭时下行模糊直接吃当帧信号
            TextureHandle blurSourceTH = useTemporal ? temporalTH : softShadowTH;
            // 模糊关闭时最后一个写入的 RT 才是最终结果
            TextureHandle finalTH = useBlur ? softShadowTH : blurSourceTH;

            // 档位展开与 Caster pass 同源（同一个纯函数），不会不同步
            Settings.PerformanceTier tier = Settings.GetTier(m_S.performance);
            Vector4 atlasParams = new Vector4(tier.tileRes, 1f / tier.tileRes,
                                              tier.atlasRes, 1f / tier.atlasRes);

            using (var builder = graph.AddUnsafePass<PassData>("PCSS", out var pd, profilingSampler))
            {
                pd.cs               = m_CS;
                pd.csKernel         = m_CSKernel;
                pd.csTemporalKernel = m_CSTemporalKernel;
                pd.csBlurHKernel    = m_CSBlurHKernel;
                pd.csBlurVKernel    = m_CSBlurVKernel;
                pd.softShadowRT     = m_SoftShadowRT;
                pd.blurTempRT       = m_BlurTempRT;
                pd.temporalRT       = m_TemporalRT;
                pd.shadowCacheRT    = m_Caster.shadowRT;
                pd.historyReadRT    = hist.readRT;
                pd.historyWriteRT   = hist.writeRT;
                pd.blurSourceRT     = useTemporal ? m_TemporalRT : m_SoftShadowRT;
                pd.source           = source;
                pd.softShadowTH     = softShadowTH;
                pd.temporalTH       = temporalTH;
                pd.historyReadTH    = historyReadTH;
                pd.historyWriteTH   = historyWriteTH;
                pd.finalTH          = finalTH;
                pd.useTemporal      = useTemporal;
                pd.cascadeVP        = m_Caster.CascadeViewProj;
                pd.splits           = m_Caster.CascadeSplits;
                pd.cascadeOffsets   = m_Caster.CascadeOffsets;
                pd.cascadeCount     = m_Caster.CascadeCount;
                pd.cascadeHalfWidths  = m_Caster.CascadeHalfWidths;
                pd.cascadeZDistances  = m_Caster.CascadeZDistances;
                pd.quality          = tier.samples;
                pd.softness         = m_S.softness;
                pd.screenSize = new Vector4(width, height, 1f / width, 1f / height);
                pd.atlasParams = atlasParams;
                ComputeFrustumRays(cameraData.camera, out pd.frustumRay0, out pd.frustumRay1, out pd.frustumRay2, out pd.frustumRay3);
                pd.zBufferParams = Shader.GetGlobalVector("_ZBufferParams");
                Vector3 camPos = cameraData.camera.transform.position;
                pd.worldSpaceCameraPos = new Vector4(camPos.x, camPos.y, camPos.z, 0);
                Vector3 lightDirCS = -lightDir;
                pd.lightDirection = new Vector4(lightDirCS.x, lightDirCS.y, lightDirCS.z, 0);
                pd.blur          = m_S.blur;
                pd.showShadowMap = m_S.showShadowMap;

                pd.prevViewProj = hist.hasPrevViewProj ? hist.prevViewProj : curViewProj;

                pd.frameIndex    = hist.frameIndex;
                pd.temporalBlend = m_S.temporal;
                pd.temporalReset = hist.reset ? 1f : 0f;
                pd.temporalDebug = (int)m_S.temporalDebug;

                builder.UseTexture(source,         AccessFlags.ReadWrite);
                builder.UseTexture(softShadowTH,   AccessFlags.ReadWrite);
                builder.UseTexture(blurTempTH,     AccessFlags.ReadWrite);
                builder.UseTexture(temporalTH,     AccessFlags.ReadWrite);
                builder.UseTexture(shadowTH,       AccessFlags.Read);
                builder.UseTexture(historyReadTH,  AccessFlags.Read);
                builder.UseTexture(historyWriteTH, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext ctx) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);

                    // ── Compute Shader Dispatch ──
                    int kernel = data.csKernel;

                    cmd.SetComputeTextureParam(data.cs, kernel, "_PCSS_SoftShadow", data.softShadowRT);
                    cmd.SetComputeTextureParam(data.cs, kernel, "_PCSS_ShadowCacheTex", data.shadowCacheRT);
                    cmd.SetComputeVectorParam(data.cs, "_ScreenSize", data.screenSize);
                    // Atlas 尺寸：shader 侧全部 texel↔UV 换算的唯一来源
                    cmd.SetComputeVectorParam(data.cs, "_PCSS_AtlasParams", data.atlasParams);
                    cmd.SetComputeVectorParam(data.cs, "_FrustumRay0", data.frustumRay0);
                    cmd.SetComputeVectorParam(data.cs, "_FrustumRay1", data.frustumRay1);
                    cmd.SetComputeVectorParam(data.cs, "_FrustumRay2", data.frustumRay2);
                    cmd.SetComputeVectorParam(data.cs, "_FrustumRay3", data.frustumRay3);
                    cmd.SetComputeVectorParam(data.cs, "_ZBufferParams", data.zBufferParams);
                    cmd.SetComputeVectorParam(data.cs, "_WorldSpaceCameraPos", data.worldSpaceCameraPos);
                    cmd.SetComputeMatrixArrayParam(data.cs, "_CascadeLightVP", data.cascadeVP);
                    cmd.SetComputeVectorArrayParam(data.cs, "_CascadeAtlasOffset", data.cascadeOffsets);
                    cmd.SetComputeIntParam(data.cs, "_CascadeCount", data.cascadeCount);
                    cmd.SetComputeVectorParam(data.cs, "_CascadeSplits", data.splits);
                    cmd.SetComputeVectorParam(data.cs, "_CascadeHalfWidth", data.cascadeHalfWidths);
                    cmd.SetComputeVectorParam(data.cs, "_CascadeZDistance", data.cascadeZDistances);
                    // 质量档位关键词
                    data.cs.DisableKeyword("PCSS_LOW");
                    data.cs.DisableKeyword("PCSS_MEDIUM");
                    if      (data.quality == Settings.Quality.Low)    data.cs.EnableKeyword("PCSS_LOW");
                    else if (data.quality == Settings.Quality.Medium) data.cs.EnableKeyword("PCSS_MEDIUM");

                    cmd.SetComputeFloatParam(data.cs, "_PCSS_Softness", data.softness);
                    cmd.SetComputeVectorParam(data.cs, "_LightDirection", data.lightDirection);
                    // _FrameIndex 是全局常量，PCSS_Main 与 PCSS_Temporal 共用
                    cmd.SetComputeFloatParam(data.cs, Settings.FrameIndexID, data.frameIndex);

                    int tgX = (width + 7) / 8;
                    int tgY = (height + 7) / 8;
                    cmd.DispatchCompute(data.cs, kernel, tgX, tgY, 1);

                    // ── 时域累积：重投影 + clamp + 混合（仅 penumbra 像素）──
                    // temporal = 0 时不跳过 dispatch——lerp 权重为 0 已等价于关闭，
                    // 且历史仍需每帧写入，否则调高 temporal 的首帧拿到的是陈旧内容。
                    if (data.useTemporal)
                    {
                        int tk = data.csTemporalKernel;
                        cmd.SetComputeTextureParam(data.cs, tk, "_PCSS_CurrentTex", data.softShadowRT);
                        cmd.SetComputeTextureParam(data.cs, tk, "_PCSS_HistoryInput", data.historyReadRT);
                        cmd.SetComputeTextureParam(data.cs, tk, "_PCSS_TemporalOut", data.temporalRT);
                        cmd.SetComputeTextureParam(data.cs, tk, "_PCSS_HistoryOutput", data.historyWriteRT);
                        cmd.SetComputeMatrixParam(data.cs, Settings.PrevViewProjID, data.prevViewProj);
                        cmd.SetComputeFloatParam(data.cs, Settings.TemporalBlendID, data.temporalBlend);
                        cmd.SetComputeFloatParam(data.cs, Settings.TemporalResetID, data.temporalReset);
                        cmd.SetComputeIntParam(data.cs, Settings.TemporalDebugID, data.temporalDebug);
                        cmd.DispatchCompute(data.cs, tk, tgX, tgY, 1);
                    }

                    // ── 双边保边模糊（Compute Shader，仅 penumbra 像素）──
                    if (data.blur > 0f)
                    {
                        cmd.SetComputeFloatParam(data.cs, "_BlurScale", data.blur);
                        cmd.SetComputeVectorParam(data.cs, "_ScreenSize", data.screenSize);
                        cmd.SetComputeVectorParam(data.cs, "_ZBufferParams", data.zBufferParams);

                        // BlurH: 读 blurSourceRT → 写 blurTempRT
                        cmd.SetComputeTextureParam(data.cs, data.csBlurHKernel, "_PCSS_BlurInput", data.blurSourceRT);
                        cmd.SetComputeTextureParam(data.cs, data.csBlurHKernel, "_PCSS_BlurOutput", data.blurTempRT);
                        cmd.DispatchCompute(data.cs, data.csBlurHKernel, tgX, tgY, 1);

                        // BlurV: 读 blurTempRT → 写 softShadowRT
                        cmd.SetComputeTextureParam(data.cs, data.csBlurVKernel, "_PCSS_BlurInput", data.blurTempRT);
                        cmd.SetComputeTextureParam(data.cs, data.csBlurVKernel, "_PCSS_BlurOutput", data.softShadowRT);
                        cmd.DispatchCompute(data.cs, data.csBlurVKernel, tgX, tgY, 1);
                    }

                    // ── 叠加到屏幕 ──
                    if (data.showShadowMap)
                        Blitter.BlitCameraTexture(cmd, data.finalTH, data.source);
                });
            }

            // ── 帧末推进相机历史状态（PassData 已按值快照，此处修改不影响本帧）──
            hist.prevViewProj    = curViewProj;
            hist.hasPrevViewProj = true;
            hist.frameIndex      = (hist.frameIndex + 1) % k_FrameIndexModulo;
            hist.reset           = false;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Feature 入口
    // ════════════════════════════════════════════════════════════

    public Settings settings = new();
    CustomShadowCasterPass m_CasterPass;
    PCSSPass               m_PCSSPass;

    public override void Create()
    {
        m_CasterPass?.Release();
        m_PCSSPass?.Release();
        m_CasterPass = new CustomShadowCasterPass(settings);
        m_PCSSPass   = new PCSSPass(settings, settings.pcssComputeShader, m_CasterPass);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_CasterPass == null) return;
        renderer.EnqueuePass(m_CasterPass);
        renderer.EnqueuePass(m_PCSSPass);
    }

    protected override void Dispose(bool disposing)
    {
        m_PCSSPass?.Release();
        m_CasterPass?.Release();
        m_CasterPass = null;
        m_PCSSPass = null;
    }
}
